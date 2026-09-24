using System.Data;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Availability;
using Sonda.Domain.Evidence;
using Sonda.Domain.Processing;
using Sonda.Domain.Profiles;

namespace Sonda.Infrastructure.Persistence;

public sealed class PostgresPolicyStore(string connection, Action<PersistenceBoundary>? fault = null) : IPolicyProcessingStore
{
    public async Task<PolicyReceipt> ExecuteAsync(ProcessingScope scope, string profileId, PolicyCommand command, CancellationToken ct = default)
    {
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(ct);
        ReceiptRow? created = null;
        var result = await ExecuteInTransactionAsync(db, scope, profileId, command, null, row => created = row, ct);
        await tx.CommitAsync(ct);
        if (created is not null) { fault?.Invoke(PersistenceBoundary.AfterCommit); await new PostgresProcessingStore(connection).ObserveCommit(created, ct); }
        return result;
    }

    internal async Task<PolicyReceipt> ExecuteInTransactionAsync(SondaDbContext db, ProcessingScope scope, string profileId,
        PolicyCommand command, PhysicalEvidenceOrigin? origin, Action<ReceiptRow>? onCreated, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("An active transaction is required.");
        var runtime = await PostgresProcessingStore.Lock(db, scope, ct);
        var bound = await db.Set<RequestKeyRow>().FindAsync([scope.TeamId, scope.SessionId, command.Id], ct);
        var existing = bound is null ? null : await db.Set<ReceiptRow>().FindAsync([scope.TeamId, scope.SessionId, bound.ReceiptId], ct);
        if (existing is null && command.Kind == PolicyCommandKind.Evidence)
        {
            var evidence = await db.Set<EvidenceRow>().SingleOrDefaultAsync(e => e.TeamId == scope.TeamId && e.SessionId == scope.SessionId && e.ProfileId == profileId && e.Generation == (origin == null ? command.EvidenceKey : origin.Generation) && e.SourceKey == (origin == null ? "sample" : origin.SourceKey) && e.SourceOrdinal == (origin == null ? 0 : origin.Start), ct);
            if (evidence is not null) existing = await db.Set<ReceiptRow>().SingleAsync(r => r.TeamId == scope.TeamId && r.SessionId == scope.SessionId && r.EvidenceId == evidence.Id, ct);
        }
        var fingerprint = SimulationJson.Hash(command with { Id = Guid.Empty });
        if (existing is not null)
        {
            if (existing.ProfileId != profileId || existing.ApplicationId != scope.ApplicationId || existing.Fingerprint != fingerprint || !existing.Kind.StartsWith("Policy", StringComparison.Ordinal))
                throw new PersistenceConflict("Committed command identity conflict.");
            if (bound is null) { db.Add(new RequestKeyRow { TeamId = scope.TeamId, SessionId = scope.SessionId, RequestId = command.Id, ReceiptId = existing.Id }); await db.SaveChangesAsync(ct); }
            return SimulationJson.Deserialize<PolicyReceipt>(existing.Trace);
        }
        var before = await Read(db, scope, profileId, ct);
        var session = PolicySession.Restore(before);
        if (command.Kind == PolicyCommandKind.ActivateVersion && command.Profile is { } candidate)
        {
            var published = await db.Set<VersionRow>().FindAsync([scope.TeamId, profileId, candidate.Version], ct);
            if (published is null || !published.Sealed || published.Snapshot != SimulationJson.Serialize(candidate)) throw new PersistenceConflict("Activation requires the exact published Profile version.");
        }
        var receipt = session.Execute(command); var after = session.Export();
        Guid? evidenceId = null;
        if (command.Kind == PolicyCommandKind.Evidence)
        {
            evidenceId = Guid.NewGuid(); db.Add(new EvidenceRow { TeamId = scope.TeamId, SessionId = scope.SessionId, Id = evidenceId.Value, ProfileId = profileId, SourceKey = origin?.SourceKey ?? "sample", Generation = origin?.Generation ?? command.EvidenceKey, SourceOrdinal = origin?.Start ?? 0, Raw = command.Raw });
            await db.SaveChangesAsync(ct);
        }
        fault?.Invoke(PersistenceBoundary.AfterEvidence);
        var stored = new ReceiptRow
        {
            TeamId = scope.TeamId,
            SessionId = scope.SessionId,
            Id = command.Id,
            RequestId = command.Id,
            EvidenceId = evidenceId,
            Kind = "Policy" + command.Kind,
            ApplicationId = scope.ApplicationId,
            ProfileId = profileId,
            Version = receipt.ProfileVersion,
            Sequence = command.Sequence,
            Line = command.Sequence,
            Fingerprint = fingerprint,
            InputContext = SimulationJson.Serialize(command),
            Trace = SimulationJson.Serialize(receipt),
            ProcessedTicks = command.ProcessedAt.UtcTicks,
            ProcessedAt = command.ProcessedAt.ToUniversalTime()
        };
        db.Add(stored); await db.SaveChangesAsync(ct);
        db.Add(new RequestKeyRow { TeamId = scope.TeamId, SessionId = scope.SessionId, RequestId = command.Id, ReceiptId = stored.Id });
        if (command.Kind == PolicyCommandKind.Evidence && receipt.Normalized is { } parsed)
        {
            db.Add(new NormalizedRow { TeamId = scope.TeamId, SessionId = scope.SessionId, ReceiptId = stored.Id, SourceTimestamp = parsed.Fields.GetValueOrDefault("timestamp"), Quality = parsed.TimestampQuality, EventTicks = parsed.EventAt.UtcTicks, EventAt = parsed.EventAt.ToUniversalTime(), Payload = SimulationJson.Serialize(parsed) });
        }
        await db.SaveChangesAsync(ct);
        await PostgresProcessingStore.PersistRuns(db, scope, receipt.ProfileVersion, before.Interpreter, after.Interpreter, stored.Id, ct); fault?.Invoke(PersistenceBoundary.AfterRuns);
        await PostgresProcessingStore.PersistIncidents(db, scope, before.Interpreter, after.Interpreter, stored.Id, ct);
        var metadata = (await db.Set<SessionRow>().FindAsync([scope.TeamId, scope.SessionId], ct))!; var zone = TimeZoneInfo.FromSerializedString(metadata.ZoneRules);
        foreach (var run in after.Interpreter.Runs.Where(r => r.Result is not null && !before.Interpreter.Runs.Any(old => old.Id == r.Id && old.Result is not null)))
        {
            var eventAt = run.CompletedEventAt!.Value; var processed = run.CompletedProcessedAt!.Value;
            db.Add(new FactRow
            {
                TeamId = scope.TeamId,
                SessionId = scope.SessionId,
                RunId = run.Id,
                ReceiptId = stored.Id,
                Scope = run.Scope.ToString(),
                Result = run.Result!.Value.ToString(),
                EventTicks = eventAt.UtcTicks,
                ProcessedTicks = processed.UtcTicks,
                EventAt = eventAt.ToUniversalTime(),
                ProcessedAt = processed.ToUniversalTime(),
                EventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(eventAt, zone).DateTime),
                ProcessedDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(processed, zone).DateTime)
            });
        }
        var lane = (await db.Set<LaneRow>().FindAsync([scope.TeamId, scope.SessionId, profileId], ct))!;
        lane.IdCounter = after.Counter; lane.CycleSequence = after.Interpreter.CycleSequence; lane.Version = session.Engine.ActiveProfile.Version; lane.Revision++;
        runtime.NextSequence = after.NextSequence; runtime.LastProcessedTicks = after.LastProcessedAt.UtcTicks; runtime.Revision++;
        var timing = await db.Set<PolicyRuntimeRow>().FindAsync([scope.TeamId, scope.SessionId, profileId], ct);
        if (timing is null) { timing = new() { TeamId = scope.TeamId, SessionId = scope.SessionId, ProfileId = profileId }; db.Add(timing); }
        timing.Frontier = after.Frontier?.ToUniversalTime();
        if (command.Kind == PolicyCommandKind.ActivateVersion && receipt.Disposition == "Applied")
            db.Add(new ActivationRow { TeamId = scope.TeamId, SessionId = scope.SessionId, ProfileId = profileId, Version = lane.Version, Revision = lane.Revision, EffectiveSequence = after.NextSequence, CommandId = command.Id });
        await db.SaveChangesAsync(ct); fault?.Invoke(PersistenceBoundary.AfterFacts); fault?.Invoke(PersistenceBoundary.BeforeCommit);
        onCreated?.Invoke(stored); return receipt;
    }

    public async Task<PolicySessionState> ReadAsync(ProcessingScope scope, string profileId, CancellationToken ct = default)
    {
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct); return await Read(db, scope, profileId, ct);
    }
    private static async Task<PolicySessionState> Read(SondaDbContext db, ProcessingScope scope, string profileId, CancellationToken ct)
    {
        var state = await PostgresProcessingStore.ReadState(db, scope, profileId, ct);
        var lane = (await db.Set<LaneRow>().FindAsync([scope.TeamId, scope.SessionId, profileId], ct))!;
        var runtime = (await db.Set<RuntimeRow>().FindAsync([scope.TeamId, scope.SessionId, scope.ApplicationId], ct))!;
        var metadata = (await db.Set<SessionRow>().FindAsync([scope.TeamId, scope.SessionId], ct))!;
        var rows = await db.Set<ReceiptRow>().Where(r => r.TeamId == scope.TeamId && r.SessionId == scope.SessionId && r.ProfileId == profileId).OrderBy(r => r.Sequence).ToListAsync(ct);
        var receipts = rows.Where(r => r.Kind.StartsWith("Policy", StringComparison.Ordinal)).Select(r => SimulationJson.Deserialize<PolicyReceipt>(r.Trace)).ToArray();
        var usedVersions = await db.Set<ActivationRow>().Where(a => a.TeamId == scope.TeamId && a.SessionId == scope.SessionId && a.ProfileId == profileId).Select(a => a.Version).Distinct().ToListAsync(ct);
        var versions = (await db.Set<VersionRow>().Where(v => v.TeamId == scope.TeamId && v.ProfileId == profileId && v.Sealed && usedVersions.Contains(v.Version)).OrderBy(v => v.Version).ToListAsync(ct)).Select(v => SimulationJson.Deserialize<Profile>(v.Snapshot)).ToArray();
        var active = versions.Single(p => p.Version == lane.Version); if (active.Policy?.Revision != 2) throw new PersistenceConflict("Use the legacy adapter for LegacyV1 Profiles.");
        var observations = receipts.Where(r => r.Disposition == "Applied" && r.Command.Kind == PolicyCommandKind.SourceObservation)
            .Select(r => new SourceObservation(r.Command.Source, r.Command.Sequence, r.Command.EffectiveAt, r.Command.ProcessedAt, r.Command.ReadSuccess)).ToArray();
        var frontier = receipts.LastOrDefault(r => r.Disposition == "Applied" && r.Command.Kind == PolicyCommandKind.AdvanceTime)?.Command.EffectiveAt;
        var latest = await db.Set<ReceiptRow>().Where(r => r.TeamId == scope.TeamId && r.SessionId == scope.SessionId && r.ApplicationId == scope.ApplicationId).OrderByDescending(r => r.Sequence).FirstOrDefaultAsync(ct);
        var lastProcessed = latest?.Kind.StartsWith("Policy", StringComparison.Ordinal) == true
            ? SimulationJson.Deserialize<PolicyCommand>(latest.InputContext).ProcessedAt
            : new DateTimeOffset(runtime.LastProcessedTicks, TimeSpan.Zero);
        // Reconstruct business state from normalized rows. Receipts supply command provenance, not replayed interpretation.
        return new(metadata.Seed, lane.IdCounter, checked((int)runtime.NextSequence), lastProcessed, frontier,
            state.Interpreter with { Versions = new(lane.Version, versions) }, receipts, observations)
        { BlockedPartitions = receipts.Where(r => r.Command.Kind == PolicyCommandKind.Evidence && r.Disposition == "Rejected").Select(r => r.Partition ?? "*").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
    }
}
