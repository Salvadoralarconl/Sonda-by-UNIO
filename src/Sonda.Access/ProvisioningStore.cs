using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Access;

public sealed class ProvisioningStore(string connection, MonitoringPathPolicy paths, Action? afterCommit = null)
{
    public Task<ProvisionedResource> CreateApplicationAsync(Actor actor, CreateApplication request, CancellationToken ct = default)
    {
        var name = Name(request.Name);
        if (request.Description is null || request.Description.Length > 2000) throw new AccessFault(422, "invalid_description");
        return Execute(actor, request.OperationId, "CreateApplication", "", new { name, request.Description }, async (core, access) =>
        {
            var id = Guid.NewGuid().ToString("N");
            core.Add(new ApplicationRow { TeamId = actor.TeamId, ApplicationId = id, Name = name });
            await core.SaveChangesAsync(ct);
            access.Add(new ApplicationMetadata { TeamId = actor.TeamId, ApplicationId = id, Description = request.Description });
            return new(id, 0, "Unconfigured");
        }, ct);
    }

    public Task<ProvisionedResource> CreateProfileAsync(Actor actor, string applicationId, CreateProfile request, CancellationToken ct = default)
    {
        var name = Name(request.Name);
        return Execute(actor, request.OperationId, "CreateProfile", applicationId, new { applicationId, name }, async (core, _) =>
        {
            if (!await core.Set<ApplicationRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ApplicationId == applicationId, ct))
                throw new AccessFault(404, "resource_not_found");
            var id = Guid.NewGuid().ToString("N");
            var draft = new Profile { TeamId = actor.TeamId, ApplicationId = applicationId, Id = id, Name = name, Policy = new InterpretationPolicy() };
            core.Add(new ProfileRow { TeamId = actor.TeamId, ApplicationId = applicationId, ProfileId = id, Draft = SimulationJson.Serialize(draft) });
            await core.SaveChangesAsync(ct);
            return new(id, 0, "Draft");
        }, ct);
    }

    public Task<ProvisionedResource> ConfigureSourceAsync(Actor actor, string profileId, Guid operationId,
        string? sourceId, long? expectedRevision, SourceConfiguration supplied, CancellationToken ct = default)
    {
        // Profile/source/revision in the supplied configuration are never authoritative.
        if (supplied is null) throw new AccessFault(422, "source_configuration_required");
        if ((!string.IsNullOrEmpty(supplied.ProfileId) && supplied.ProfileId != profileId) || (!string.IsNullOrEmpty(supplied.SourceKey) && supplied.SourceKey != sourceId)) throw new AccessFault(422, "source_identity_mismatch");
        var safe = paths.Validate(supplied with { ProfileId = profileId, SourceKey = sourceId ?? "new", Revision = 1 });
        return Execute(actor, operationId, "ConfigureSource", profileId, new { profileId, sourceId, expectedRevision, safe }, async (core, _) =>
        {
            if (!await core.Set<ProfileRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profileId, ct))
                throw new AccessFault(404, "resource_not_found");
            // Active source changes require the fenced admission adapter, never this inactive-draft path.
            if (await core.Set<LaneRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profileId, ct))
                throw new AccessFault(409, "source_requires_fenced_configuration");
            SourceRow row;
            if (sourceId is null)
            {
                if (expectedRevision is not null) throw new AccessFault(422, "unexpected_revision");
                row = new SourceRow { TeamId = actor.TeamId, ProfileId = profileId, SourceKey = Guid.NewGuid().ToString("N"), Revision = 1 };
                core.Add(row);
            }
            else
            {
                if (expectedRevision is null) throw new AccessFault(428, "expected_revision_required");
                row = await core.Set<SourceRow>().FindAsync([actor.TeamId, profileId, sourceId], ct) ?? throw new AccessFault(404, "resource_not_found");
                if (row.Revision != expectedRevision) throw new AccessFault(409, "source_revision_conflict");
                row.Revision++;
            }
            row.Configuration = SimulationJson.Serialize(safe with { SourceKey = row.SourceKey, Revision = row.Revision });
            await core.SaveChangesAsync(ct);
            return new(row.SourceKey, row.Revision, "InactiveConfiguration");
        }, ct);
    }

    private async Task<ProvisionedResource> Execute(Actor actor, Guid operationId, string action, string target,
        object payload, Func<SondaDbContext, AccessDbContext, Task<ProvisionedResource>> apply, CancellationToken ct)
    {
        if (operationId == Guid.Empty) throw new AccessFault(422, "operation_id_required");
        await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var core = new SondaDbContext(new DbContextOptionsBuilder<SondaDbContext>().UseNpgsql(conn).Options);
        await using var access = new AccessDbContext(new DbContextOptionsBuilder<AccessDbContext>().UseNpgsql(conn).Options);
        await core.Database.UseTransactionAsync(tx, ct); await access.Database.UseTransactionAsync(tx, ct);
        // Same team lock as existing draft/publication adapter. No schema/model modification.
        await core.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.TeamId},0))", ct);
        await AccessAuthorization.RequireAsync(access, actor, admin: true, ct);
        var fingerprint = SimulationJson.Hash(new { action, target, actor.AccountId, payload });
        var prior = await access.Set<ApiOperation>().FindAsync([actor.TeamId, operationId], ct);
        if (prior is not null)
        {
            if (prior.AccountId != actor.AccountId || prior.Fingerprint != fingerprint) throw new AccessFault(409, "operation_identity_conflict");
            return SimulationJson.Deserialize<ProvisionedResource>(prior.Result);
        }
        var result = await apply(core, access);
        access.Add(new ApiOperation { TeamId = actor.TeamId, Id = operationId, AccountId = actor.AccountId,
            Action = action, Target = target, Fingerprint = fingerprint, State = "Committed", Result = SimulationJson.Serialize(result), SubmittedAt = DateTimeOffset.UtcNow });
        access.Add(new AccessAudit { Id = Guid.NewGuid(), TeamId = actor.TeamId, AccountId = actor.AccountId, OperationId = operationId,
            Action = action, Target = result.Id, Disposition = "Committed", AccessRevision = actor.Revision, RecordedAt = DateTimeOffset.UtcNow });
        await access.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        afterCommit?.Invoke();
        return result;
    }

    private static string Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new AccessFault(422, "invalid_name");
        var name = value.Trim();
        if (name.Length is < 1 or > 160 || name.Any(char.IsControl)) throw new AccessFault(422, "invalid_name");
        return name;
    }
}

public static class AccessAuthorization
{
    public static async Task<Membership> RequireAsync(AccessDbContext db, Actor actor, bool admin, CancellationToken ct = default)
    {
        var member = await db.Set<Membership>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.AccountId == actor.AccountId, ct);
        // Cookie validation may already have tracked this row before a concurrent revocation.
        if (member is not null) await db.Entry(member).ReloadAsync(ct);
        if (member is null || !member.Enabled || member.Revision != actor.Revision) throw new AccessFault(401, "session_invalid");
        if (admin && member.Role != "Admin") throw new AccessFault(403, "admin_required");
        return member;
    }
}
