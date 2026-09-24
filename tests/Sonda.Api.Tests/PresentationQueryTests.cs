using Microsoft.AspNetCore.DataProtection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Sonda.Server.Queries;
using Xunit;
using System.Text.Json;
using Sonda.Api.Contracts;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class PresentationQueryTests(AccessDatabase database)
{
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-09-23T16:00:00Z"); }
    private async Task<(Actor Actor, Guid Session)> Fixture()
    {
        var actor = await database.ActorAsync();
        var request = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        var profile = request.Profiles.Single() with { TeamId = actor.TeamId };
        request = request with { Profiles = [profile], ServerTimeZoneId = TimeZoneInfo.Local.Id };
        var session = await new PostgresConfigurationStore(database.Connection).CreateSessionAsync(request);
        var scope = new ProcessingScope(actor.TeamId, session, profile.ApplicationId);
        var store = new PostgresProcessingStore(database.Connection);
        for (var i = 0; i < request.Entries.Count; i++)
        {
            var e = request.Entries[i];
            await store.ProcessAsync(new(scope, profile.Id, Guid.NewGuid(), "sample", "synthetic", i, i + 1, i + 1, e.Raw, e.ProcessedAt, request.SampleDate));
        }
        await new PostgresIngestionStore(database.Connection).RegisterAsync(scope,
            new SourceConfiguration { ProfileId = profile.Id, SourceKey = "sample", Root = @"C:\synthetic-logs", Enabled = false, SampleDate = request.SampleDate }, "local-synthetic");
        return (actor, session);
    }

    [Fact]
    public async Task Application_overview_is_team_scoped_and_counts_all_unresolved_facts()
    {
        var (actor, session) = await Fixture();
        var dashboard = await new DashboardQueries(database.Connection, new Clock()).ReadAsync(actor);
        var app = Assert.Single(dashboard.Applications).ApplicationId;
        var result = JsonSerializer.SerializeToElement(await ApplicationOverview.Read(database.Connection, actor, app, default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(session, result.GetProperty("sessionId").GetGuid());
        Assert.Equal(0, result.GetProperty("unresolvedErrorCount").GetInt32());
        Assert.Equal(1, result.GetProperty("unresolvedWarningCount").GetInt32());
        Assert.Single(result.GetProperty("recentRuns").EnumerateArray());
        Assert.Single(result.GetProperty("recentFailedOrders").EnumerateArray());
        var foreign = await database.ActorAsync();
        Assert.Equal(404, (await Assert.ThrowsAsync<AccessFault>(() => ApplicationOverview.Read(database.Connection, foreign, app, default))).Status);
    }

    [Fact]
    public async Task Home_display_is_creation_evidence_and_does_not_change_metrics_or_incident_identity()
    {
        var (actor, session) = await Fixture();
        var result = await new DashboardQueries(database.Connection, new Clock()).ReadAsync(actor);
        var display = Assert.Single(result.IncidentDisplays);
        var incident = Assert.Single(result.RecentIncidents);
        Assert.Equal(incident.Id, display.IncidentId); Assert.Equal(session, display.SessionId);
        Assert.Equal("IncidentCreationEvidence", display.SummaryBasis);
        Assert.NotEmpty(display.ApplicationName); Assert.NotEmpty(display.Summary);
        Assert.NotNull(display.EvidenceId); Assert.Equal(1, display.ProfileVersion);
        Assert.True(display.LastUpdatedProcessedAt >= display.FirstDetectedProcessedAt);
        Assert.Equal(75m, result.SystemHealth.Percentage); Assert.Equal(3, result.CompletedOrderRunsProcessedToday);
        Assert.Equal(1, result.UnresolvedIncidentCount);
        Assert.Equal("Warning", result.ActiveIncidentsBadge);
        Assert.Equal("Warning", result.CurrentSystemBadge.BusinessHealth);
        Assert.True(result.CurrentSystemBadge.IsQualified);
        var foreign = await database.ActorAsync();
        await using var db = SondaDbContext.Open(database.Connection);
        Assert.Equal(404, (await Assert.ThrowsAsync<AccessFault>(() => IncidentPresentation.Read(db, foreign, session, incident.Id, default))).Status);
    }

    [Fact]
    public async Task Parent_filter_is_bounded_and_bound_to_team_session_and_cursor()
    {
        var (actor, session) = await Fixture();
        var clock = new Clock(); var queries = new InvestigationQueries(database.Connection, new CursorCodec(new EphemeralDataProtectionProvider(), clock), clock);
        var parent = Assert.Single((await queries.Runs(actor, "Application", session, 50, null, default)).Items);
        var page = await queries.Runs(actor, "Order", session, 1, null, default, parent.Id);
        Assert.True(page.HasMore); Assert.Equal(parent.Id, Assert.Single(page.Items).ParentId);
        var next = await queries.Runs(actor, "Order", session, 1, page.NextCursor, default, parent.Id);
        Assert.NotEqual(page.Items[0].Id, Assert.Single(next.Items).Id);
        Assert.Equal(400, (await Assert.ThrowsAsync<AccessFault>(() => queries.Runs(actor, "Order", session, 1, page.NextCursor, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AccessFault>(() => queries.Runs(actor, "Order", session, 1, null, default, "missing"))).Status);
        var foreign = await database.ActorAsync();
        Assert.Equal(404, (await Assert.ThrowsAsync<AccessFault>(() => queries.Runs(foreign, "Order", session, 1, null, default, parent.Id))).Status);
    }

    [Fact]
    public async Task Incident_evidence_chronology_is_explicit_and_cursor_cannot_change_order()
    {
        var (actor, session) = await Fixture(); var clock = new Clock();
        var dashboard = await new DashboardQueries(database.Connection, clock).ReadAsync(actor);
        var incident = Assert.Single(dashboard.RecentIncidents);
        await using var access = AccessDbContext.Open(database.Connection);
        var query = new DetailQueries(database.Connection, access, new CursorCodec(new EphemeralDataProtectionProvider(), clock), clock);
        var first = JsonSerializer.SerializeToElement(await query.IncidentEvidence(actor, session, incident.Id, 1, null, default, "processed"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var time = first.GetProperty("items")[0].GetProperty("processedAt").GetDateTimeOffset();
        if (first.GetProperty("hasMore").GetBoolean())
        {
            var cursor = first.GetProperty("nextCursor").GetString();
            var next = JsonSerializer.SerializeToElement(await query.IncidentEvidence(actor, session, incident.Id, 1, cursor, default, "processed"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.True(next.GetProperty("items")[0].GetProperty("processedAt").GetDateTimeOffset() >= time);
            Assert.Equal(400, (await Assert.ThrowsAsync<AccessFault>(() => query.IncidentEvidence(actor, session, incident.Id, 1, cursor, default))).Status);
        }
        Assert.Equal(422, (await Assert.ThrowsAsync<AccessFault>(() => query.IncidentEvidence(actor, session, incident.Id, 1, null, default, "arbitrary"))).Status);
    }
}
