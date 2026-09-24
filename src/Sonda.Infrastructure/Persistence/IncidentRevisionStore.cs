using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;

namespace Sonda.Infrastructure.Persistence;

// Persistence primitive exercised by tests, not a public application workflow or authorization policy.
internal sealed class IncidentRevisionStore(string connection)
{
    internal async Task<long> CompareExchangeAsync(ProcessingScope scope, string incidentId, long expected,
        IncidentStatus status, Guid commandId, DateTimeOffset at)
    {
        var fingerprint = SimulationJson.Hash(new { scope, incidentId, expected, status, at });
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync();
        await PostgresProcessingStore.Lock(db, scope, default);
        var prior = await db.Set<CommandRow>().FindAsync(scope.TeamId, commandId);
        if (prior is not null) { PostgresConfigurationStore.Check(prior, fingerprint); return long.Parse(prior.Result, System.Globalization.CultureInfo.InvariantCulture); }
        var incident = await db.Set<IncidentRow>().FindAsync(scope.TeamId, scope.SessionId, incidentId) ?? throw new PersistenceConflict("Incident missing.");
        var profile = await db.Set<ProfileRow>().FindAsync(scope.TeamId, incident.ProfileId);
        if (profile!.ApplicationId != scope.ApplicationId || incident.Revision != expected) throw new PersistenceConflict("Incident revision conflict.");
        var from = Enum.Parse<IncidentStatus>(incident.Status);
        if (from != status)
        {
            incident.Status = status.ToString(); incident.Revision++;
            var ordinal = await db.Set<HistoryRow>().CountAsync(x => x.TeamId == scope.TeamId && x.SessionId == scope.SessionId && x.IncidentId == incidentId);
            db.Add(new HistoryRow { TeamId = scope.TeamId, SessionId = scope.SessionId, IncidentId = incidentId, Ordinal = ordinal, Origin = commandId, Payload = SimulationJson.Serialize(new StatusChange(from, status, at, "Harness revision test")) });
        }
        db.Add(new CommandRow { TeamId = scope.TeamId, Id = commandId, Fingerprint = fingerprint, Result = incident.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return incident.Revision;
    }
}
