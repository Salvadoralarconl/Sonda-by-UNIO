using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Server.Hosting;

public sealed record SourceCommandEnvelope(string ProfileId, string SourceId, long ExpectedRevision, SourceConfiguration Configuration);
public sealed class SourceService(string connection, MonitoringPathPolicy paths, ProvisioningStore provisioning, OperationLedger ledger, ApplicationAdmission admission)
{
    public async Task<object> Configure(Actor actor, string profile, string source, Guid operation, long? expectedRevision, SourceConfiguration configuration, CancellationToken ct)
    {
        var expected = expectedRevision ?? throw new AccessFault(428, "expected_revision_required");
        if (expected < 0 || expected == long.MaxValue) throw new AccessFault(422, "invalid_revision");
        await using (var access = AccessDbContext.Open(connection))
            if (await access.Set<ApiOperation>().AnyAsync(x => x.TeamId == actor.TeamId && x.Id == operation && x.Action == "ConfigureSource", ct))
                return await provisioning.ConfigureSourceAsync(actor, profile, operation, source, expected, configuration, ct);
        await using var db = SondaDbContext.Open(connection);
        var owner = await db.Set<ProfileRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile, ct) ?? throw new AccessFault(404, "resource_not_found");
        var registered = await db.Set<AcquisitionSourceRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile && x.SourceKey == source, ct);
        if (!registered) return await provisioning.ConfigureSourceAsync(actor, profile, operation, source, expected, configuration, ct);
        if (configuration is null) throw new AccessFault(422, "source_configuration_required");
        if ((!string.IsNullOrEmpty(configuration.ProfileId) && configuration.ProfileId != profile) || (!string.IsNullOrEmpty(configuration.SourceKey) && configuration.SourceKey != source)) throw new AccessFault(422, "source_identity_mismatch");
        var next = paths.Validate(configuration with { ProfileId = profile, SourceKey = source, Revision = checked(expected + 1) });
        var command = new SourceCommandEnvelope(profile, source, expected, next);
        var op = await ledger.Begin(actor, operation, "ConfigureAcquisitionSource", profile + "/" + source, command, true, ct);
        if (op.State == "Committed") return JsonDocument.Parse(op.Result).RootElement.Clone();
        var result = await admission.ChangeSource(actor, owner.ApplicationId, next, expected, operation, ct);
        await ledger.Finish(actor, operation, result, false, CancellationToken.None); return result;
    }
}
