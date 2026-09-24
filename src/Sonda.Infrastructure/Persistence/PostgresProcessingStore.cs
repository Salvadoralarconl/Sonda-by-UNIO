using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Metrics;
using Sonda.Domain.Processing;
using Sonda.Domain.Profiles;

namespace Sonda.Infrastructure.Persistence;

public sealed class PostgresProcessingStore(string connection, Action<PersistenceBoundary>? fault = null) : IProcessingStore
{
    internal static async Task<RuntimeRow> Lock(SondaDbContext db, ProcessingScope s, CancellationToken ct) =>
        await db.Set<RuntimeRow>().FromSqlInterpolated($"SELECT * FROM sonda.application_runtime WHERE team_id={s.TeamId} AND session_id={s.SessionId} AND application_id={s.ApplicationId} FOR UPDATE").SingleAsync(ct);

    public async Task<ProcessingReceipt> ProcessAsync(ProcessingRequest r, CancellationToken cancellationToken = default)
    {
        await using var db = SondaDbContext.Open(connection);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await ProcessInTransactionAsync(db, r, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (!result.Replayed) { fault?.Invoke(PersistenceBoundary.AfterCommit); await ObserveCommit((await db.Set<ReceiptRow>().FindAsync([r.Scope.TeamId, r.Scope.SessionId, result.Id], cancellationToken))!, cancellationToken); }
        return result;
    }

    internal async Task<ProcessingReceipt> ProcessInTransactionAsync(SondaDbContext db, ProcessingRequest r, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("An active transaction is required.");
        var s = r.Scope;
        var fingerprint = SimulationJson.Hash(r with { RequestId = Guid.Empty });
        var runtime = await Lock(db, s, ct);
        var existing = await db.Set<ReceiptRow>().SingleOrDefaultAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.RequestId == r.RequestId, ct);
        var bound = await db.Set<RequestKeyRow>().FindAsync([s.TeamId, s.SessionId, r.RequestId], ct);
        if (existing is null && bound is not null) existing = await db.Set<ReceiptRow>().FindAsync([s.TeamId, s.SessionId, bound.ReceiptId], ct);
        var evidence = await db.Set<EvidenceRow>().SingleOrDefaultAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.ProfileId == r.ProfileId && x.SourceKey == r.SourceKey && x.Generation == r.Generation && x.SourceOrdinal == r.SourceOrdinal, ct);
        if (existing is null && evidence is not null) existing = await db.Set<ReceiptRow>().SingleAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.EvidenceId == evidence.Id, ct);
        if (existing is not null)
        {
            if (existing.Fingerprint != fingerprint) throw new PersistenceConflict("Committed input identity reused with different semantic content.");
            if (bound is null)
            {
                db.Add(new RequestKeyRow { TeamId = s.TeamId, SessionId = s.SessionId, RequestId = r.RequestId, ReceiptId = existing.Id });
                await db.SaveChangesAsync(ct);
            }
            return new(existing.Id, existing.Version, ReadTrace(existing.Trace), true);
        }
        if (runtime.NextSequence != r.Sequence || r.ProcessedAt == default || r.ProcessedAt.UtcTicks < runtime.LastProcessedTicks || r.Line <= 0) throw new PersistenceConflict("Input order or processing clock conflict.");
        var lane = await db.Set<LaneRow>().FindAsync([s.TeamId, s.SessionId, r.ProfileId], ct) ?? throw new PersistenceConflict("Unknown Profile lane.");
        if (lane.ApplicationId != s.ApplicationId) throw new PersistenceConflict("Cross-application input.");
        var session = await db.Set<SessionRow>().FindAsync([s.TeamId, s.SessionId], ct);
        var version = await db.Set<VersionRow>().FindAsync([s.TeamId, r.ProfileId, lane.Version], ct);
        if (version is null || !version.Sealed) throw new PersistenceConflict("Unpublished version.");
        var profile = SimulationJson.Deserialize<Profile>(version.Snapshot);
        if (profile.Policy is not null) throw new PersistenceConflict("Revision-2 inputs require the versioned command adapter; legacy receipt replay remains supported.");
        var before = await ReadState(db, s, r.ProfileId, ct); var counter = before.IdCounter;
        var engine = ProfileInterpreter.Restore(profile, kind => $"{session!.Seed}:{profile.Id}:{kind}:{++counter:D4}", before.Interpreter);
        EntryTrace trace;
        if (lane.Blocked) trace = new EntryTrace { Line = r.Line, ProfileId = r.ProfileId, Raw = r.Raw, Disposition = "BlockedByPriorError" };
        else trace = InterpretInput.Apply(profile, engine, new SampleEntry { ProfileId = r.ProfileId, Raw = r.Raw, ProcessedAt = r.ProcessedAt }, r.Line, r.SampleDate);
        var rejected = trace.Disposition == "Quarantined";
        var after = rejected ? before.Interpreter : engine.ExportState();
        var id = Guid.NewGuid();
        evidence = new EvidenceRow { TeamId = s.TeamId, SessionId = s.SessionId, Id = Guid.NewGuid(), ProfileId = r.ProfileId, SourceKey = r.SourceKey, Generation = r.Generation, SourceOrdinal = r.SourceOrdinal, Raw = r.Raw };
        db.Add(evidence); await db.SaveChangesAsync(ct); fault?.Invoke(PersistenceBoundary.AfterEvidence);
        var receipt = new ReceiptRow { TeamId = s.TeamId, SessionId = s.SessionId, Id = id, RequestId = r.RequestId, EvidenceId = evidence.Id, ApplicationId = s.ApplicationId, ProfileId = r.ProfileId, Version = lane.Version, Sequence = r.Sequence, Line = r.Line, Fingerprint = fingerprint, InputContext = SimulationJson.Serialize(r), Trace = SimulationJson.Serialize(trace), ProcessedTicks = r.ProcessedAt.UtcTicks, ProcessedAt = r.ProcessedAt.ToUniversalTime() };
        db.Add(receipt); await db.SaveChangesAsync(ct);
        db.Add(new RequestKeyRow { TeamId = s.TeamId, SessionId = s.SessionId, RequestId = r.RequestId, ReceiptId = receipt.Id });
        if (trace.Parsed is { } parsed) db.Add(new NormalizedRow { TeamId = s.TeamId, SessionId = s.SessionId, ReceiptId = id, SourceTimestamp = parsed.Fields.GetValueOrDefault("timestamp"), Quality = parsed.TimestampQuality, EventTicks = parsed.EventAt.UtcTicks, EventAt = parsed.EventAt.ToUniversalTime(), Payload = SimulationJson.Serialize(parsed) });
        await db.SaveChangesAsync(ct);
        await PersistRuns(db, s, lane.Version, before.Interpreter, after, id, ct);
        fault?.Invoke(PersistenceBoundary.AfterRuns);
        await PersistIncidents(db, s, before.Interpreter, after, id, ct);
        var zone = TimeZoneInfo.FromSerializedString(session!.ZoneRules);
        foreach (var run in after.Runs.Where(x => x.Result is not null && !before.Interpreter.Runs.Any(old => old.Id == x.Id && old.Result is not null)))
        {
            var eventAt = run.CompletedEventAt!.Value; var processed = run.CompletedProcessedAt!.Value;
            db.Add(new FactRow { TeamId = s.TeamId, SessionId = s.SessionId, RunId = run.Id, ReceiptId = id, Scope = run.Scope.ToString(), Result = run.Result!.Value.ToString(), EventTicks = eventAt.UtcTicks, ProcessedTicks = processed.UtcTicks, EventAt = eventAt.ToUniversalTime(), ProcessedAt = processed.ToUniversalTime(), EventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(eventAt, zone).DateTime), ProcessedDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(processed, zone).DateTime) });
        }
        lane.CycleSequence = after.CycleSequence; lane.IdCounter = rejected ? before.IdCounter : counter; lane.Blocked |= rejected; lane.Revision++;
        runtime.NextSequence++; runtime.LastProcessedTicks = r.ProcessedAt.UtcTicks; runtime.Revision++;
        await db.SaveChangesAsync(ct); fault?.Invoke(PersistenceBoundary.AfterFacts); fault?.Invoke(PersistenceBoundary.BeforeCommit);
        return new(id, lane.Version, trace, false);
    }

    internal static async Task PersistRuns(SondaDbContext db, ProcessingScope s, int version, InterpreterState before, InterpreterState after, Guid receipt, CancellationToken ct)
    {
        foreach (var run in after.Runs)
        {
            var payload = SimulationJson.Serialize(run);
            var row = await db.Set<RunRow>().FindAsync([s.TeamId, s.SessionId, run.Id], ct);
            if (row is not null && row.Payload == payload) continue;
            if (row is null) { row = new RunRow { TeamId = s.TeamId, SessionId = s.SessionId, Id = run.Id, ProfileId = run.ProfileId, Version = run.PolicyContext?.ProfileVersion ?? version, Scope = run.Scope.ToString(), CycleSequence = run.CycleSequence, ParentId = run.ApplicationRunId, Identifier = run.Identifier, Attempt = run.AttemptNumber, CreatedReceipt = receipt }; db.Add(row); }
            row.Payload = payload; row.Result = run.Result?.ToString(); if (run.Result is not null) row.CompletedReceipt = receipt;
            // Parent must exist before order guard locks it.
            await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO sonda.run_evidence(team_id,session_id,run_id,receipt_id) SELECT team_id,session_id,{run.Id},id FROM sonda.processing_receipts WHERE team_id={s.TeamId} AND session_id={s.SessionId} AND line=ANY({run.EvidenceLines}) ON CONFLICT DO NOTHING", ct);
        }
    }

    internal static async Task PersistIncidents(SondaDbContext db, ProcessingScope s, InterpreterState before, InterpreterState after, Guid receipt, CancellationToken ct)
    {
        foreach (var item in after.Incidents)
        {
            var row = await db.Set<IncidentRow>().FindAsync([s.TeamId, s.SessionId, item.Id], ct);
            if (row is null)
            {
                var key = item.Problem.IncidentKey;
                var problem = await db.Set<ProblemRow>().SingleOrDefaultAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.Key == key, ct);
                if (problem is null) { problem = new ProblemRow { TeamId = s.TeamId, SessionId = s.SessionId, Id = Guid.NewGuid(), Key = key, Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) }; db.Add(problem); await db.SaveChangesAsync(ct); }
                row = new IncidentRow { TeamId = s.TeamId, SessionId = s.SessionId, Id = item.Id, ProfileId = after.ProfileId, ProblemId = problem.Id, Problem = SimulationJson.Serialize(item.Problem), RecoveryPolicy = item.RecoveryPolicy.ToString(), Severity = item.Severity.ToString(), Status = item.Status.ToString(), PolicyContext = item.PolicyContext is null ? null : SimulationJson.Serialize(item.PolicyContext), CreatedReceipt = receipt }; db.Add(row);
                await db.SaveChangesAsync(ct);
            }
            else if (SimulationJson.Serialize(before.Incidents.Single(i => i.Id == item.Id)) != SimulationJson.Serialize(item)) { row.PolicyContext = item.PolicyContext is null ? null : SimulationJson.Serialize(item.PolicyContext); row.Status = item.Status.ToString(); row.Severity = item.Severity.ToString(); row.Revision++; await db.SaveChangesAsync(ct); }
            foreach (var occurrence in item.Occurrences)
            {
                var o = await db.Set<OccurrenceRow>().FindAsync([s.TeamId, s.SessionId, occurrence.Id], ct); var json = SimulationJson.Serialize(occurrence);
                if (o is null) db.Add(new OccurrenceRow { TeamId = s.TeamId, SessionId = s.SessionId, Id = occurrence.Id, IncidentId = item.Id, RunId = occurrence.RunId, CreatedReceipt = receipt, UpdatedReceipt = receipt, Payload = json });
                else if (o.Payload != json) { o.Payload = json; o.UpdatedReceipt = receipt; }
            }
            foreach (var recovery in item.Recoveries)
                if (!await db.Set<RecoveryRow>().AnyAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.IncidentId == item.Id && x.RunId == recovery.RunId, ct)) db.Add(new RecoveryRow { TeamId = s.TeamId, SessionId = s.SessionId, IncidentId = item.Id, RunId = recovery.RunId, ReceiptId = receipt, Payload = SimulationJson.Serialize(recovery) });
            foreach (var observation in item.PolicyContext?.Observations ?? [])
                if (!await db.Set<OccurrenceRow>().AnyAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.Id == observation.Id, ct))
                {
                    var origin = await db.Set<ReceiptRow>().SingleAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.Line == observation.EvidenceLine, ct);
                    db.Add(new OccurrenceRow { TeamId = s.TeamId, SessionId = s.SessionId, Id = observation.Id, IncidentId = item.Id, RunId = null, CreatedReceipt = origin.Id, UpdatedReceipt = origin.Id, Payload = SimulationJson.Serialize(observation) });
                }
            for (var n = 0; n < item.History.Length; n++)
                if (!await db.Set<HistoryRow>().AnyAsync(x => x.TeamId == s.TeamId && x.SessionId == s.SessionId && x.IncidentId == item.Id && x.Ordinal == n, ct)) db.Add(new HistoryRow { TeamId = s.TeamId, SessionId = s.SessionId, IncidentId = item.Id, Ordinal = n, Origin = receipt, Payload = SimulationJson.Serialize(item.History[n]) });
            await db.SaveChangesAsync(ct);
            foreach (var occurrence in item.Occurrences)
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO sonda.occurrence_evidence(team_id,session_id,occurrence_id,receipt_id) SELECT team_id,session_id,{occurrence.Id},id FROM sonda.processing_receipts WHERE team_id={s.TeamId} AND session_id={s.SessionId} AND line=ANY({occurrence.EvidenceLines.ToArray()}) ON CONFLICT DO NOTHING", ct);
            foreach (var recovery in item.Recoveries)
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO sonda.recovery_evidence(team_id,session_id,incident_id,run_id,receipt_id) SELECT team_id,session_id,{item.Id},{recovery.RunId},id FROM sonda.processing_receipts WHERE team_id={s.TeamId} AND session_id={s.SessionId} AND line=ANY({recovery.EvidenceLines.ToArray()}) ON CONFLICT DO NOTHING", ct);
        }
    }

    internal async Task ObserveCommit(ReceiptRow receipt, CancellationToken ct)
    {
        try
        {
            await using var db = SondaDbContext.Open(connection);
            var tracking = await db.Database.SqlQueryRaw<string>("SELECT current_setting('track_commit_timestamp') AS \"Value\"").SingleAsync(ct);
            DateTimeOffset? actual = null;
            if (tracking == "on")
            {
                try { actual = await db.Database.SqlQuery<DateTimeOffset?>($"SELECT pg_xact_commit_timestamp({receipt.TransactionId}::xid) AS \"Value\"").SingleAsync(ct); }
                catch (Npgsql.PostgresException) { /* Metadata may be unavailable; retain honest acknowledgment only. */ }
            }
            db.Add(new ObservationRow { TeamId = receipt.TeamId, SessionId = receipt.SessionId, ReceiptId = receipt.Id, DatabaseCommitAt = actual, AcknowledgedAt = DateTimeOffset.UtcNow, Method = actual is null ? "AcknowledgmentOnly" : "PostgresCommitTracking" });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or OperationCanceledException) { /* Optional audit enrichment cannot undo a committed input. */ }
    }

    private static EntryTrace ReadTrace(string json)
    {
        // Populate getter-only diagnostic collection explicitly; never rerun input on replay.
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var trace = SimulationJson.Deserialize<EntryTrace>(json);
        if (trace.Diagnostics.Count == 0) trace.Diagnostics.AddRange(SimulationJson.Deserialize<List<Diagnostic>>(document.RootElement.GetProperty("diagnostics").GetRawText()));
        return trace;
    }
    internal static async Task<DurableState> ReadState(SondaDbContext db, ProcessingScope s, string profileId, CancellationToken ct)
    {
        var lane = await db.Set<LaneRow>().FindAsync([s.TeamId, s.SessionId, profileId], ct) ?? throw new PersistenceConflict("Unknown lane.");
        if (lane.ApplicationId != s.ApplicationId) throw new PersistenceConflict("Lane ownership mismatch.");
        var runs = (await db.Set<RunRow>().Where(r => r.TeamId == s.TeamId && r.SessionId == s.SessionId && r.ProfileId == profileId).ToListAsync(ct)).Select(r => SimulationJson.Deserialize<RunState>(r.Payload)).OrderBy(r => AllocationOrdinal(r.Id)).ToArray();
        List<IncidentState> incidents = [];
        foreach (var i in (await db.Set<IncidentRow>().Where(i => i.TeamId == s.TeamId && i.SessionId == s.SessionId && i.ProfileId == profileId).ToListAsync(ct)).OrderBy(i => AllocationOrdinal(i.Id)))
        {
            var occurrences = (await db.Set<OccurrenceRow>().Where(o => o.TeamId == s.TeamId && o.SessionId == s.SessionId && o.IncidentId == i.Id && o.RunId != null).ToListAsync(ct)).Select(o => SimulationJson.Deserialize<Occurrence>(o.Payload)).OrderBy(o => AllocationOrdinal(o.Id)).ToArray();
            var recoveries = (await db.Set<RecoveryRow>().Where(o => o.TeamId == s.TeamId && o.SessionId == s.SessionId && o.IncidentId == i.Id).OrderBy(o => o.RunId).ToListAsync(ct)).Select(o => SimulationJson.Deserialize<RecoveryEvent>(o.Payload)).ToArray();
            var history = (await db.Set<HistoryRow>().Where(o => o.TeamId == s.TeamId && o.SessionId == s.SessionId && o.IncidentId == i.Id).OrderBy(o => o.Ordinal).ToListAsync(ct)).Select(o => SimulationJson.Deserialize<StatusChange>(o.Payload)).ToArray();
            var context=i.PolicyContext is null ? null : SimulationJson.Deserialize<IncidentPolicyContext>(i.PolicyContext);
            if(context is not null)
            {
                var observations=(await db.Set<OccurrenceRow>().Where(o=>o.TeamId==s.TeamId&&o.SessionId==s.SessionId&&o.IncidentId==i.Id&&o.RunId==null).ToListAsync(ct))
                    .Select(o=>SimulationJson.Deserialize<ObservationOccurrence>(o.Payload)).OrderBy(o=>o.EvidenceLine).ToArray();
                context=context with {Observations=observations};
            }
            incidents.Add(new(i.Id, SimulationJson.Deserialize<ProblemIdentity>(i.Problem), Enum.Parse<RecoveryPolicy>(i.RecoveryPolicy), Enum.Parse<Classification>(i.Severity), Enum.Parse<IncidentStatus>(i.Status), occurrences, recoveries, history) { PolicyContext = context });
        }
        return new(new(1, profileId, lane.CycleSequence, runs, incidents.ToArray()), lane.IdCounter, lane.Blocked);
    }
    public async Task<DurableState> ReadStateAsync(ProcessingScope scope, string profileId, CancellationToken cancellationToken = default)
    {
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        return await ReadState(db, scope, profileId, cancellationToken);
    }

    // These are adapter-issued technical IDs (seed:profile:kind:counter), not source order identifiers.
    // Restore allocation order numerically; lexical ordering changes after the D4 counter reaches 10000.
    private static long AllocationOrdinal(string id) => long.Parse(id.AsSpan(id.LastIndexOf(':') + 1), System.Globalization.CultureInfo.InvariantCulture);
    public async Task<IReadOnlyList<RunMetricFact>> ReadMetricFactsAsync(ProcessingScope scope, CancellationToken cancellationToken = default)
    {
        await using var db = SondaDbContext.Open(connection);
        var facts = await (from f in db.Set<FactRow>() join r in db.Set<RunRow>() on new { f.TeamId, f.SessionId, Id = f.RunId } equals new { r.TeamId, r.SessionId, r.Id } join p in db.Set<ProfileRow>() on new { r.TeamId, r.ProfileId } equals new { p.TeamId, p.ProfileId } where f.TeamId == scope.TeamId && f.SessionId == scope.SessionId && p.ApplicationId == scope.ApplicationId select f).ToListAsync(cancellationToken);
        return facts.Select(f => new RunMetricFact(f.RunId, Enum.Parse<TargetScope>(f.Scope), Enum.Parse<Sonda.Domain.Runs.DetectionResult>(f.Result), new DateTimeOffset(f.EventTicks, TimeSpan.Zero), new DateTimeOffset(f.ProcessedTicks, TimeSpan.Zero))).ToArray();
    }
    public async Task<ApplicationHealth?> ReadHealthAsync(ProcessingScope scope, CancellationToken cancellationToken = default)
    {
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var profiles = db.Set<ProfileRow>().Where(p => p.TeamId == scope.TeamId && p.ApplicationId == scope.ApplicationId).Select(p => p.ProfileId);
        var active = await db.Set<IncidentRow>().Where(i => i.TeamId == scope.TeamId && i.SessionId == scope.SessionId && profiles.Contains(i.ProfileId) && i.Status != "Resolved").Select(i => i.Severity).ToListAsync(cancellationToken);
        if (active.Contains("Error")) return ApplicationHealth.Error; if (active.Count > 0) return ApplicationHealth.Warning;
        return await db.Set<RunRow>().AnyAsync(r => r.TeamId == scope.TeamId && r.SessionId == scope.SessionId && profiles.Contains(r.ProfileId) && r.Result != null, cancellationToken) ? ApplicationHealth.Stable : null;
    }
}
