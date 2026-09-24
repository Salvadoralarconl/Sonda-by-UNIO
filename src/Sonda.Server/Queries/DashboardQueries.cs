using System.Data;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Api.Contracts;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Application.Processing;
using Sonda.Domain.Availability;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Server.Queries;

public sealed class DashboardQueries(string connection, TimeProvider clock, Func<Task>? afterSnapshot = null)
{
    public async Task<DashboardDto> ReadAsync(Actor actor, CancellationToken ct = default)
    {
        await using var db = SondaDbContext.Open(connection);
        db.Database.SetCommandTimeout(5);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var now = clock.GetUtcNow(); var zone = TimeZoneInfo.Local;
        var monitors = await db.Set<MonitoringSessionRow>().AsNoTracking().Where(x => x.TeamId == actor.TeamId).ToArrayAsync(ct);
        if (afterSnapshot is not null) await afterSnapshot();
        var sessions = monitors.Select(x => x.SessionId).ToArray();
        foreach (var session in await db.Set<SessionRow>().Where(x => x.TeamId == actor.TeamId && sessions.Contains(x.SessionId)).ToArrayAsync(ct))
            if (session.ZoneRules != zone.ToSerializedString()) throw new AccessFault(409, "reporting_timezone_configuration_conflict");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var facts = db.Set<FactRow>().Where(x => x.TeamId == actor.TeamId && sessions.Contains(x.SessionId));
        var evaluated = await facts.CountAsync(x => x.EventDate == today, ct);
        var successful = await facts.CountAsync(x => x.EventDate == today && x.Result == "Success", ct);
        var first = today.AddDays(-4);
        var volume = await facts.Where(x => x.Scope == "Order" && x.ProcessedDate >= first && x.ProcessedDate <= today)
            .GroupBy(x => x.ProcessedDate).Select(g => new { Date = g.Key, Count = g.Count() }).ToArrayAsync(ct);
        var history = Enumerable.Range(-4, 5).Select(i => new VolumeDto(today.AddDays(i), volume.SingleOrDefault(v => v.Date == today.AddDays(i))?.Count ?? 0)).ToArray();
        var incidents = db.Set<IncidentRow>().Where(x => x.TeamId == actor.TeamId && sessions.Contains(x.SessionId));
        var unresolved = await incidents.CountAsync(x => x.Status != "Resolved", ct);
        var incidentBadge = await incidents.AnyAsync(x => x.Status != "Resolved" && x.Severity == "Error", ct) ? "Error" :
            await incidents.AnyAsync(x => x.Status != "Resolved" && x.Severity == "Warning", ct) ? "Warning" : "Stable";
        var recent = await (from i in incidents join r in db.Set<ReceiptRow>() on new { i.TeamId, i.SessionId, Id = i.CreatedReceipt } equals new { r.TeamId, r.SessionId, r.Id }
                            orderby r.ProcessedAt descending, i.SessionId, i.Id
                            select new IncidentDto(i.SessionId, i.Id, i.ProfileId, i.Problem, i.Severity, i.Status, i.Revision, i.PolicyContext != null)).Take(50).ToArrayAsync(ct);
        var apps = await db.Set<ApplicationRow>().Where(x => x.TeamId == actor.TeamId).OrderBy(x => x.ApplicationId).Take(201).ToArrayAsync(ct);
        if (apps.Length > 200) throw new AccessFault(422, "dashboard_application_limit");
        List<ApplicationStateDto> states = [];
        foreach (var application in apps)
        {
            var monitor = monitors.SingleOrDefault(x => x.ApplicationId == application.ApplicationId);
            if (monitor is null) { states.Add(new(application.ApplicationId, null, "NotObserved", false, null, null, "No activated monitoring session.")); continue; }
            var open = incidents.Where(x => x.SessionId == monitor.SessionId && x.Status != "Resolved");
            var business = await open.AnyAsync(x => x.Severity == "Error", ct) ? ApplicationHealth.Error :
                await open.AnyAsync(x => x.Severity == "Warning", ct) ? ApplicationHealth.Warning : ApplicationHealth.Stable;
            var lanes = await db.Set<LaneRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == monitor.SessionId).ToArrayAsync(ct);
            List<AvailabilityResult> laneStates = [];
            foreach (var lane in lanes)
            {
                var version = await db.Set<VersionRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.ProfileId == lane.ProfileId && x.Version == lane.Version, ct);
                var profile = SimulationJson.Deserialize<Profile>(version.Snapshot);
                var lastRun = await (from f in facts join r in db.Set<RunRow>() on new { f.TeamId, f.SessionId, Id = f.RunId } equals new { r.TeamId, r.SessionId, r.Id }
                                     where f.SessionId == monitor.SessionId && r.ProfileId == lane.ProfileId
                                     select (DateTimeOffset?)f.ProcessedAt).MaxAsync(ct);
                if (profile.Policy is null) { laneStates.Add(new(business, AvailabilityState.Unknown, null, lastRun, now, "Legacy Profile availability is not configured.")); continue; }
                var sourceRows = await db.Set<AcquisitionSourceRow>().Where(x => x.TeamId == actor.TeamId && x.ProfileId == lane.ProfileId).ToArrayAsync(ct);
                var configs = await (from s in db.Set<AcquisitionSourceRow>() join r in db.Set<AcquisitionRevisionRow>() on new { s.TeamId, s.ProfileId, s.SourceKey, s.Revision } equals new { r.TeamId, r.ProfileId, r.SourceKey, r.Revision }
                                     where s.TeamId == actor.TeamId && s.ProfileId == lane.ProfileId select r.Configuration).ToArrayAsync(ct);
                var required = configs.Select(SimulationJson.Deserialize<SourceConfiguration>).Where(x => x.Required).Select(x => x.SourceKey).ToArray();
                var raw = await db.Set<ReceiptRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == monitor.SessionId && x.ProfileId == lane.ProfileId && x.Kind == "PolicySourceObservation" && x.ProcessedAt <= now).OrderByDescending(x => x.Sequence).Take(10000).Select(x => x.Trace).ToArrayAsync(ct);
                var observations = raw.Select(SimulationJson.Deserialize<PolicyReceipt>).Where(x => x.Disposition == "Applied")
                    .Select(x => new SourceObservation(x.Command.Source, x.Command.Sequence, x.Command.EffectiveAt, x.Command.ProcessedAt, x.Command.ReadSuccess)).ToArray();
                var availability = AvailabilityProjection.Calculate(business, required, observations, profile.Policy, lastRun, now);
                if (raw.Length == 10000) availability = availability with { Availability = AvailabilityState.Unknown, Reason = "Observation projection limit reached." };
                if (sourceRows.Any(x => required.Contains(x.SourceKey) && (!x.Enabled || x.State is "Gap" or "IdentityUncertain" or "Blocked")))
                    availability = availability with { Availability = AvailabilityState.Unknown, Reason = "Required source disabled, blocked or uncertain." };
                laneStates.Add(availability);
            }
            var state = laneStates.OrderBy(x => Rank(x.Availability)).FirstOrDefault() ?? new(business, AvailabilityState.NotObserved, null, null, now, "No observations.");
            states.Add(new(application.ApplicationId, business.ToString(), state.Availability.ToString(), state.HealthyNow, state.LastSuccessfulRead, state.LastRunCompleted, state.Reason));
        }
        List<IncidentPresentationDto> displays = [];
        foreach (var incident in recent) displays.Add(await IncidentPresentation.Read(db, actor, incident.SessionId, incident.Id, ct));
        await tx.CommitAsync(ct);
        return new(now, today, zone.Id, new(successful, evaluated, evaluated == 0 ? null : decimal.Round(successful * 100m / evaluated, 2)), history[^1].CompletedOrderRunsProcessedCount, history, unresolved, recent, states)
        { IncidentDisplays = displays, ApplicationLabels = apps.Select(x => new ApplicationDto(x.ApplicationId, x.Name, x.Revision)).ToArray(),
            ActiveIncidentsBadge = incidentBadge,
            CurrentSystemBadge = HomeBadgeProjection.System(states.Where(x => monitors.Any(m => m.ApplicationId == x.ApplicationId)).ToArray()) };
    }
    private static int Rank(AvailabilityState state) => state switch { AvailabilityState.Disconnected => 0, AvailabilityState.Unknown => 1, AvailabilityState.Stale => 2, AvailabilityState.NotObserved => 3, _ => 4 };
}
