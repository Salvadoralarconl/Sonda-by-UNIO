using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;

namespace Sonda.Infrastructure.Persistence;

public sealed class PostgresConfigurationStore(string connection) : IConfigurationStore
{
    public async Task<Guid> CreateSessionAsync(SimulationRequest request, string kind = "DurabilityHarness", CancellationToken ct = default)
    {
        var report = new SimulationRunner().Run(request);
        if (!report.Complete) throw new PersistenceConflict("Complete applicable simulation required before publication.");
        var team = request.Profiles[0].TeamId;
        await using var db = SondaDbContext.Open(connection);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({team},0))", ct);
        if (!await db.Set<TeamRow>().AnyAsync(x => x.TeamId == team, ct)) { db.Add(new TeamRow { TeamId = team, Name = team }); await db.SaveChangesAsync(ct); }
        foreach (var app in request.Profiles.Select(p => p.ApplicationId).Distinct())
            if (!await db.Set<ApplicationRow>().AnyAsync(x => x.TeamId == team && x.ApplicationId == app, ct)) db.Add(new ApplicationRow { TeamId = team, ApplicationId = app, Name = app });
        await db.SaveChangesAsync(ct);
        foreach (var p in request.Profiles)
        {
            var existing = await db.Set<ProfileRow>().FindAsync([team, p.Id], ct);
            if (existing is null) { db.Add(new ProfileRow { TeamId = team, ProfileId = p.Id, ApplicationId = p.ApplicationId, Draft = SimulationJson.Serialize(p) }); await db.SaveChangesAsync(ct); }
            else if (existing.ApplicationId != p.ApplicationId) throw new PersistenceConflict("Profile owner mismatch.");
            if (!await db.Set<SourceRow>().AnyAsync(x => x.TeamId == team && x.ProfileId == p.Id && x.SourceKey == "sample", ct))
                db.Add(new SourceRow { TeamId = team, ProfileId = p.Id, SourceKey = "sample" });
            await db.SaveChangesAsync(ct);
            await PublishRows(db, p, request, report, ct);
        }
        await db.SaveChangesAsync(ct);
        var id = Guid.NewGuid(); var zone = TimeZoneInfo.FindSystemTimeZoneById(request.ServerTimeZoneId);
        db.Add(new SessionRow { TeamId = team, SessionId = id, Seed = request.Seed, Kind = kind, Zone = zone.Id, ZoneRules = zone.ToSerializedString() });
        await db.SaveChangesAsync(ct);
        foreach (var app in request.Profiles.Select(p => p.ApplicationId).Distinct()) db.Add(new RuntimeRow { TeamId = team, SessionId = id, ApplicationId = app });
        await db.SaveChangesAsync(ct);
        foreach (var p in request.Profiles) db.Add(new LaneRow { TeamId = team, SessionId = id, ProfileId = p.Id, ApplicationId = p.ApplicationId, Version = p.Version });
        await db.SaveChangesAsync(ct);
        foreach (var p in request.Profiles) db.Add(new ActivationRow { TeamId = team, SessionId = id, ProfileId = p.Id, Version = p.Version, Revision = 0, EffectiveSequence = 1, CommandId = Guid.NewGuid() });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return id;
    }

    internal static async Task PublishRows(SondaDbContext db, Profile p, SimulationRequest request, SimulationReport report, CancellationToken ct)
    {
        var text = SimulationJson.Serialize(p); var existing = await db.Set<VersionRow>().FindAsync([p.TeamId, p.Id, p.Version], ct);
        if (existing is not null) { if (existing.Snapshot != text) throw new PersistenceConflict("Version number already has different immutable content."); return; }
        var row = new VersionRow { TeamId = p.TeamId, ProfileId = p.Id, Version = p.Version, Snapshot = text, Hash = SimulationJson.Hash(p), Report = SimulationJson.Serialize(report), Request = SimulationJson.Serialize(request) };
        db.Add(row); await db.SaveChangesAsync(ct);
        db.Add(new ParsingRow { TeamId = p.TeamId, ProfileId = p.Id, Version = p.Version, Payload = SimulationJson.Serialize(p.Parsing) });
        if (p.Identifier is not null) db.Add(new IdentifierRow { TeamId = p.TeamId, ProfileId = p.Id, Version = p.Version, Payload = SimulationJson.Serialize(p.Identifier) });
        for (var i = 0; i < p.Rules.Count; i++) db.Add(new RuleRow { TeamId = p.TeamId, ProfileId = p.Id, Version = p.Version, Key = p.Rules[i].Key, Ordinal = i, Payload = SimulationJson.Serialize(p.Rules[i]) });
        await db.SaveChangesAsync(ct);
        foreach (var rule in p.Rules) for (var i = 0; i < rule.Alternatives.Count; i++) db.Add(new PatternRow { TeamId = p.TeamId, ProfileId = p.Id, Version = p.Version, RuleKey = rule.Key, Ordinal = i, Payload = SimulationJson.Serialize(rule.Alternatives[i]) });
        await db.SaveChangesAsync(ct);
        var reportId = Guid.NewGuid(); var requestHash = SimulationJson.Hash(request); var reportText = SimulationJson.Serialize(report); var requestText = SimulationJson.Serialize(request);
        var revision = (await db.Set<ProfileRow>().FindAsync([p.TeamId, p.Id], ct))!.Revision;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO sonda.simulation_reports(team_id,id,profile_id,version,request_hash,profile_hash,engine_version,request,report) VALUES ({p.TeamId},{reportId},{p.Id},{p.Version},{requestHash},{row.Hash},{report.Provenance.EngineVersion},{requestText},{reportText})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO sonda.profile_validations(team_id,profile_id,version,draft_revision,profile_hash,report_id) VALUES ({p.TeamId},{p.Id},{p.Version},{revision},{row.Hash},{reportId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO sonda.profile_version_sources(team_id,profile_id,version,source_key,configuration) SELECT team_id,profile_id,{p.Version},source_key,configuration FROM sonda.log_sources WHERE team_id={p.TeamId} AND profile_id={p.Id}", ct);
        row.Sealed = true; await db.SaveChangesAsync(ct);
    }

    public async Task<long> EditDraftAsync(Profile profile, long expectedRevision, Guid commandId, CancellationToken ct = default)
    {
        var fingerprint = SimulationJson.Hash(new { profile, expectedRevision, kind = "Draft" });
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({profile.TeamId},0))", ct);
        var prior = await db.Set<CommandRow>().FindAsync([profile.TeamId, commandId], ct);
        if (prior is not null) { Check(prior, fingerprint); return long.Parse(prior.Result, System.Globalization.CultureInfo.InvariantCulture); }
        var row = await db.Set<ProfileRow>().SingleAsync(x => x.TeamId == profile.TeamId && x.ProfileId == profile.Id, ct);
        if (row.Revision != expectedRevision || row.ApplicationId != profile.ApplicationId) throw new PersistenceConflict("Draft revision/ownership conflict.");
        row.Draft = SimulationJson.Serialize(profile); row.Revision++;
        db.Add(new CommandRow { TeamId = profile.TeamId, Id = commandId, Fingerprint = fingerprint, Result = row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return row.Revision;
    }

    public async Task PublishAsync(SimulationRequest request, long expectedRevision, Guid commandId, CancellationToken ct = default)
    {
        if (request.Profiles.Count != 1) throw new ArgumentException("Publish one Profile at a time.");
        var p = request.Profiles[0]; var report = new SimulationRunner().Run(request);
        if (!report.Complete) throw new PersistenceConflict("Simulation incomplete.");
        var fingerprint = SimulationJson.Hash(new { request, expectedRevision, kind = "Publish" });
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({p.TeamId},0))", ct);
        var prior = await db.Set<CommandRow>().FindAsync([p.TeamId, commandId], ct); if (prior is not null) { Check(prior, fingerprint); return; }
        var draft = await db.Set<ProfileRow>().FindAsync([p.TeamId, p.Id], ct) ?? throw new PersistenceConflict("Draft missing.");
        if (draft.Revision != expectedRevision || draft.Draft != SimulationJson.Serialize(p)) throw new PersistenceConflict("Validation is stale for current draft.");
        await PublishRows(db, p, request, report, ct);
        db.Add(new CommandRow { TeamId = p.TeamId, Id = commandId, Fingerprint = fingerprint, Result = p.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task ActivateAsync(ProcessingScope scope, string profileId, int version, long expectedRevision, int expectedVersion, Guid commandId, CancellationToken ct = default)
    {
        var fingerprint = SimulationJson.Hash(new { scope, profileId, version, expectedRevision, expectedVersion, kind = "Activate" });
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var runtime = await PostgresProcessingStore.Lock(db, scope, ct);
        var prior = await db.Set<CommandRow>().FindAsync([scope.TeamId, commandId], ct); if (prior is not null) { Check(prior, fingerprint); return; }
        var lane = await db.Set<LaneRow>().FindAsync([scope.TeamId, scope.SessionId, profileId], ct) ?? throw new PersistenceConflict("Unknown lane.");
        if (lane.ApplicationId != scope.ApplicationId || lane.Revision != expectedRevision || lane.Version != expectedVersion) throw new PersistenceConflict("Activation revision conflict.");
        // Temporary Phase 2 safeguard; not a permanent domain rule.
        if (await db.Set<RunRow>().AnyAsync(r => r.TeamId == scope.TeamId && r.SessionId == scope.SessionId && r.ProfileId == profileId && r.Result == null, ct)) throw new PersistenceConflict("Phase 2 safe boundary: open runs prevent activation.");
        var old = SimulationJson.Deserialize<Profile>((await db.Set<VersionRow>().FindAsync([scope.TeamId, profileId, lane.Version], ct))!.Snapshot);
        var nextRow = await db.Set<VersionRow>().FindAsync([scope.TeamId, profileId, version], ct) ?? throw new PersistenceConflict("Version not published.");
        var next = SimulationJson.Deserialize<Profile>(nextRow.Snapshot);
        if (!nextRow.Sealed || old.StreamKey != next.StreamKey || old.Identifier?.Namespace != next.Identifier?.Namespace) throw new PersistenceConflict("Incompatible stream/namespace.");
        if (await db.Set<IncidentRow>().AnyAsync(i => i.TeamId == scope.TeamId && i.SessionId == scope.SessionId && i.ProfileId == profileId && i.Status != "Resolved", ct)
            && (SimulationJson.Serialize(old.Identifier) != SimulationJson.Serialize(next.Identifier) || SimulationJson.Serialize(old.Rules) != SimulationJson.Serialize(next.Rules))) throw new PersistenceConflict("Unresolved problems require unchanged extraction/rules in Phase 2.");
        lane.Version = version; lane.Revision++;
        db.Add(new ActivationRow { TeamId = scope.TeamId, SessionId = scope.SessionId, ProfileId = profileId, Revision = lane.Revision, Version = version, EffectiveSequence = runtime.NextSequence, CommandId = commandId });
        db.Add(new CommandRow { TeamId = scope.TeamId, Id = commandId, Fingerprint = fingerprint, Result = "Activated" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    internal static void Check(CommandRow receipt, string fingerprint) { if (receipt.Fingerprint != fingerprint) throw new PersistenceConflict("Command ID reused with different content."); }

    public async Task<long> EditSourceAsync(string teamId, string profileId, string sourceKey, string configuration, long expectedRevision, Guid commandId, CancellationToken ct = default)
    {
        using var document = System.Text.Json.JsonDocument.Parse(configuration);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new ArgumentException("Source metadata must be an object.");
        var fingerprint = SimulationJson.Hash(new { teamId, profileId, sourceKey, configuration, expectedRevision, kind = "Source" });
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({teamId},0))", ct);
        var prior = await db.Set<CommandRow>().FindAsync([teamId, commandId], ct);
        if (prior is not null) { Check(prior, fingerprint); return long.Parse(prior.Result, System.Globalization.CultureInfo.InvariantCulture); }
        var source = await db.Set<SourceRow>().FindAsync([teamId, profileId, sourceKey], ct) ?? throw new PersistenceConflict("Unknown source.");
        if (source.Revision != expectedRevision) throw new PersistenceConflict("Source revision conflict.");
        source.Configuration = configuration; source.Revision++;
        db.Add(new CommandRow { TeamId = teamId, Id = commandId, Fingerprint = fingerprint, Result = source.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return source.Revision;
    }
}
