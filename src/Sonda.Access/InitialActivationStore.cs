using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Access;

/// <summary>Initial configuration admission only; never interprets evidence or replaces an existing lane.</summary>
public sealed class InitialActivationStore(string connection, MonitoringPathPolicy paths, string authority)
{
    public async Task<object> Activate(Actor actor, string profileId, int version, Guid operation, OwnerFence? fence, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var core = new SondaDbContext(new DbContextOptionsBuilder<SondaDbContext>().UseNpgsql(conn).Options);
        await using var access = new AccessDbContext(new DbContextOptionsBuilder<AccessDbContext>().UseNpgsql(conn).Options);
        await core.Database.UseTransactionAsync(tx, ct); await access.Database.UseTransactionAsync(tx, ct);
        await core.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.TeamId},0))", ct);
        await AccessAuthorization.RequireAsync(access, actor, true, ct);
        var op = await access.Set<ApiOperation>().FindAsync([actor.TeamId, operation], ct) ?? throw new AccessFault(409, "durable_intent_required");
        if (op.AccountId != actor.AccountId || op.Action != "ActivateProfile" || op.Target != profileId) throw new AccessFault(409, "operation_identity_conflict");
        if (op.State == "Committed") return System.Text.Json.JsonDocument.Parse(op.Result).RootElement.Clone();
        var profile = await core.Set<ProfileRow>().FindAsync([actor.TeamId, profileId], ct) ?? throw new AccessFault(404, "resource_not_found");
        var published = await core.Set<VersionRow>().FindAsync([actor.TeamId, profileId, version], ct) ?? throw new AccessFault(409, "published_version_required");
        if (!published.Sealed || SimulationJson.Deserialize<Profile>(published.Snapshot).Policy?.Revision != 2) throw new AccessFault(409, "revision2_published_version_required");
        var snapshots = await core.Set<VersionSourceRow>().Where(x => x.TeamId == actor.TeamId && x.ProfileId == profileId && x.Version == version).ToArrayAsync(ct);
        if (snapshots.Length == 0) throw new AccessFault(409, "published_source_configuration_required");
        var current = await core.Set<SourceRow>().Where(x => x.TeamId == actor.TeamId && x.ProfileId == profileId).ToArrayAsync(ct);
        if (snapshots.Length != current.Length || snapshots.Any(s => current.SingleOrDefault(c => c.SourceKey == s.SourceKey)?.Configuration != s.Configuration)) throw new AccessFault(409, "source_configuration_changed_since_publication");
        var sources = snapshots.Select(x => paths.Validate(SimulationJson.Deserialize<SourceConfiguration>(x.Configuration))).ToArray();
        var monitor = await core.Set<MonitoringSessionRow>().FindAsync([actor.TeamId, profile.ApplicationId], ct);
        Guid session; RuntimeRow runtime;
        if (monitor is null)
        {
            if (fence is not null) throw new AccessFault(409, "unexpected_monitoring_fence");
            session = Guid.NewGuid(); var zone = TimeZoneInfo.Local;
            // Core schema retains its accepted Kind values. Monitoring registration is the operational discriminator.
            core.Add(new SessionRow { TeamId = actor.TeamId, SessionId = session, Seed = "monitor-" + session.ToString("N"), Kind = "DurabilityHarness", Zone = zone.Id, ZoneRules = zone.ToSerializedString() });
            await core.SaveChangesAsync(ct);
            runtime = new RuntimeRow { TeamId = actor.TeamId, SessionId = session, ApplicationId = profile.ApplicationId };
            core.Add(runtime); await core.SaveChangesAsync(ct);
        }
        else
        {
            session = monitor.SessionId;
            if (fence?.Scope != new ProcessingScope(actor.TeamId, session, profile.ApplicationId)) throw new AccessFault(409, "monitoring_fence_required");
            await core.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('sonda.fence_epoch',{fence.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)},true),set_config('sonda.fence_instance',{fence.InstanceId.ToString()},true)", ct);
            await core.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM sonda.ingestion_owners WHERE team_id={actor.TeamId} AND application_id={profile.ApplicationId} FOR UPDATE", ct);
            await core.Database.ExecuteSqlInterpolatedAsync($"SELECT sonda.assert_ingestion_fence({actor.TeamId},{profile.ApplicationId})", ct);
            runtime = await core.Set<RuntimeRow>().FromSqlInterpolated($"SELECT * FROM sonda.application_runtime WHERE team_id={actor.TeamId} AND session_id={session} AND application_id={profile.ApplicationId} FOR UPDATE").SingleAsync(ct);
            if (await core.Set<PendingIngestionRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ApplicationId == profile.ApplicationId && x.Command != "", ct)) throw new AccessFault(409, "pending_input_requires_recovery");
        }
        if (await core.Set<LaneRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.ProfileId == profileId, ct)) throw new AccessFault(409, "profile_already_activated");
        core.Add(new LaneRow { TeamId = actor.TeamId, SessionId = session, ProfileId = profileId, ApplicationId = profile.ApplicationId, Version = version });
        await core.SaveChangesAsync(ct);
        core.Add(new ActivationRow { TeamId = actor.TeamId, SessionId = session, ProfileId = profileId, Version = version, Revision = 0, EffectiveSequence = runtime.NextSequence, CommandId = operation });
        if (monitor is null)
        {
            core.Add(new MonitoringSessionRow { TeamId = actor.TeamId, SessionId = session, ApplicationId = profile.ApplicationId, HostAuthority = authority });
            await core.SaveChangesAsync(ct);
            core.Add(new IngestionOwnerRow { TeamId = actor.TeamId, ApplicationId = profile.ApplicationId, ExpiresAt = DateTimeOffset.UnixEpoch });
        }
        foreach (var source in sources)
        {
            core.Add(new AcquisitionRevisionRow { TeamId = actor.TeamId, ProfileId = profileId, SourceKey = source.SourceKey, Revision = source.Revision, Configuration = SimulationJson.Serialize(source), Hash = SimulationJson.Hash(source) });
        }
        await core.SaveChangesAsync(ct);
        foreach (var source in sources) core.Add(new AcquisitionSourceRow { TeamId = actor.TeamId, ProfileId = profileId, SourceKey = source.SourceKey, Revision = source.Revision, Enabled = source.Enabled, State = source.Enabled ? "Discovering" : "Disabled" });
        core.Add(new SourceMembershipRow { TeamId = actor.TeamId, ProfileId = profileId, Revision = 1, EffectiveSequence = runtime.NextSequence,
            Sources = SimulationJson.Serialize(sources.OrderBy(x => x.SourceKey).Select(x => new { x.SourceKey, x.Revision, x.Enabled }).ToArray()) });
        await core.SaveChangesAsync(ct);
        var result = new { sessionId = session, profileId, version, revision = 0, state = "Activated", filesRead = 0 };
        op.State = "Committed"; op.Result = SimulationJson.Serialize(result);
        access.Add(new AccessAudit { Id = Guid.NewGuid(), TeamId = actor.TeamId, AccountId = actor.AccountId, OperationId = operation, Action = "ActivateProfile", Target = profileId, Disposition = "Committed", AccessRevision = actor.Revision, RecordedAt = DateTimeOffset.UtcNow });
        await access.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }
}
