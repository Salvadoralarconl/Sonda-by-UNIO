using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Api.Contracts;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Sonda.Server.Queries;
using Xunit;
using Npgsql;
using NpgsqlTypes;
using System.Reflection;
using Sonda.Domain.Profiles;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class QueryTests(AccessDatabase database)
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-09-23T16:00:00Z");
    private CursorCodec Cursors() => new(new EphemeralDataProtectionProvider(), new FixedClock(AsOf));
    private async Task<(Actor Actor, Guid Session, string Profile)> Fixture(bool register = true, string fixture = "mixed-orders", bool late = false, bool quarantine = false)
    {
        var actor = await database.ActorAsync();
        var request = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture + ".json")));
        var profile = request.Profiles.Single() with { TeamId = actor.TeamId };
        request = request with { Profiles = [profile], ServerTimeZoneId = TimeZoneInfo.Local.Id };
        var session = await new PostgresConfigurationStore(database.Connection).CreateSessionAsync(request);
        if (quarantine) request.Entries.Add(request.Entries[^1] with { Raw = "invalid timestamp synthetic evidence", ProcessedAt = request.Entries[^1].ProcessedAt.AddSeconds(1) });
        var scope = new ProcessingScope(actor.TeamId, session, profile.ApplicationId);
        var store = new PostgresProcessingStore(database.Connection);
        for (var i = 0; i < request.Entries.Count; i++)
        {
            var e = request.Entries[i];
            await store.ProcessAsync(new(scope, profile.Id, Guid.NewGuid(), "sample", "synthetic", i, i + 1, i + 1, e.Raw, late ? e.ProcessedAt.AddDays(1) : e.ProcessedAt, request.SampleDate));
        }
        if (register) await new PostgresIngestionStore(database.Connection).RegisterAsync(scope,
            new SourceConfiguration { ProfileId = profile.Id, SourceKey = "sample", Root = @"C:\synthetic-logs", Enabled = false, SampleDate = request.SampleDate }, "local-synthetic");
        return (actor, session, profile.Id);
    }
    [Fact]
    public async Task Dashboard_matches_accepted_mixed_order_fixture()
    {
        var (actor, _, _) = await Fixture();
        var result = await new DashboardQueries(database.Connection, new FixedClock(AsOf)).ReadAsync(actor);
        Assert.Equal(75m, result.SystemHealth.Percentage); Assert.Equal(4, result.SystemHealth.TotalEvaluatedRuns);
        Assert.Equal(3, result.CompletedOrderRunsProcessedToday); Assert.Equal(5, result.FiveDayCompletedOrderHistory.Count);
        Assert.Equal(1, result.UnresolvedIncidentCount); Assert.Equal("Warning", Assert.Single(result.Applications).BusinessHealth);
        Assert.False(result.Applications.Single().HealthyNow);
    }
    [Fact]
    public async Task Unregistered_simulator_data_does_not_appear_in_dashboard_or_search()
    {
        var (actor, _, _) = await Fixture(false);
        var result = await new DashboardQueries(database.Connection, new FixedClock(AsOf)).ReadAsync(actor);
        Assert.Null(result.SystemHealth.Percentage); Assert.Equal(0, result.CompletedOrderRunsProcessedToday);
        var search = await new SearchQueries(database.Connection, Cursors(), new FixedClock(AsOf)).Search(actor, new(AsOf.AddDays(-1), AsOf), null, null, default);
        Assert.Empty(search.Items);
    }
    [Fact]
    public async Task Search_pages_once_per_evidence_and_rejects_filter_or_team_cursor_reuse()
    {
        var (actor, _, _) = await Fixture(); var cursors = Cursors();
        var query = new SearchQueries(database.Connection, cursors, new FixedClock(AsOf));
        var filter = new SearchFilter(AsOf.AddDays(-1), AsOf);
        var first = await query.Search(actor, filter, 2, null, default); Assert.True(first.HasMore); Assert.Equal(2, first.Items.Count);
        var second = await query.Search(actor, filter, 2, first.NextCursor, default);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.Equal(400, (await Assert.ThrowsAsync<AccessFault>(() => query.Search(actor, filter with { Text = "CAM" }, 2, first.NextCursor, default))).Status);
        var foreign = await database.ActorAsync();
        Assert.Equal(400, (await Assert.ThrowsAsync<AccessFault>(() => query.Search(foreign, filter, 2, first.NextCursor, default))).Status);
    }
    [Fact]
    public async Task Search_literal_text_identifier_result_and_incident_filters_use_persisted_links()
    {
        var (actor, _, profile) = await Fixture(); var query = new SearchQueries(database.Connection, Cursors(), new FixedClock(AsOf));
        var filter = new SearchFilter(AsOf.AddDays(-1), AsOf);
        Assert.Empty((await query.Search(actor, filter with { Text = "%_' OR true --" }, null, null, default)).Items);
        var failed = await query.Search(actor, filter with { Result = "Failure", IncidentStatus = "Active" }, null, null, default);
        Assert.NotEmpty(failed.Items);
        var scoped = await query.Search(actor, filter with { ProfileId = profile, IdentifierNamespace = "cam-order-v1", Identifier = "001" }, null, null, default);
        Assert.NotEmpty(scoped.Items); Assert.All(scoped.Items, x => Assert.NotEmpty(x.RunIds));
    }
    [Fact]
    public async Task Foreign_session_cannot_be_used_to_investigate_runs()
    {
        var (_, session, _) = await Fixture(); var stranger = await database.ActorAsync();
        var query = new InvestigationQueries(database.Connection, Cursors(), new FixedClock(AsOf));
        Assert.Equal(404, (await Assert.ThrowsAsync<AccessFault>(() => query.Runs(stranger, "Order", session, null, null, default))).Status);
    }

    [Fact]
    public async Task Ignore_evidence_remains_searchable_and_invalid_windows_and_tampered_cursors_fail()
    {
        var (actor, _, _) = await Fixture(fixture: "priority-ignore");
        var query = new SearchQueries(database.Connection, Cursors(), new FixedClock(AsOf)); var filter = new SearchFilter(AsOf.AddDays(-1), AsOf);
        Assert.NotEmpty((await query.Search(actor, filter with { Classification = "Ignore" }, null, null, default)).Items);
        Assert.Equal(422, (await Assert.ThrowsAsync<AccessFault>(() => query.Search(actor, filter with { From = AsOf.AddDays(-32) }, null, null, default))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<AccessFault>(() => query.Search(actor, filter, 201, null, default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<AccessFault>(() => query.Search(actor, filter, null, "tampered", default))).Status);
    }

    [Fact]
    public async Task Late_processing_uses_processing_day_for_volume_and_event_day_for_health()
    {
        var (actor, _, _) = await Fixture(late: true);
        var result = await new DashboardQueries(database.Connection, new FixedClock(AsOf.AddDays(1))).ReadAsync(actor);
        Assert.Equal(3, result.CompletedOrderRunsProcessedToday);
        // This fixture carries explicit date/time text; its accepted normalizer retains yesterday's event date.
        Assert.Null(result.SystemHealth.Percentage);
        var yesterday = await new DashboardQueries(database.Connection, new FixedClock(AsOf)).ReadAsync(actor);
        Assert.Equal(75m, yesterday.SystemHealth.Percentage); Assert.Equal(0, yesterday.CompletedOrderRunsProcessedToday);
        Assert.Equal(Enumerable.Range(-4, 5).Select(i => result.ReportingDate.AddDays(i)), result.FiveDayCompletedOrderHistory.Select(x => x.Date));
    }

    [Fact]
    public async Task Search_actual_parameterized_plan_is_captured_and_blocked_query_times_out()
    {
        var (actor, _, _) = await Fixture();
        var sql = (string)typeof(SearchQueries).GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        await using var db = new NpgsqlConnection(database.Connection); await db.OpenAsync();
        await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql, db);
        foreach (var key in new[] { "team", "basis", "app", "profile", "text", "identifier", "namespace", "result", "classification", "status" })
            command.Parameters.Add(new NpgsqlParameter(key, NpgsqlDbType.Text) { Value = key == "team" ? actor.TeamId : key == "basis" ? "processed" : DBNull.Value });
        command.Parameters.AddWithValue("case", false); command.Parameters.AddWithValue("from", AsOf.AddDays(-1)); command.Parameters.AddWithValue("to", AsOf); command.Parameters.AddWithValue("take", 51);
        command.Parameters.Add(new NpgsqlParameter("after_time", NpgsqlDbType.TimestampTz) { Value = DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("after_session", NpgsqlDbType.Uuid) { Value = DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("after_id", NpgsqlDbType.Uuid) { Value = DBNull.Value });
        var plan = (string)(await command.ExecuteScalarAsync())!;
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, "artifacts", "phase5", "search-plan.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, plan);
        using var parsed = System.Text.Json.JsonDocument.Parse(plan); Assert.Equal(9, parsed.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble());
        await using var tx = await db.BeginTransactionAsync(); await new NpgsqlCommand("LOCK TABLE sonda.raw_evidence IN ACCESS EXCLUSIVE MODE", db, tx).ExecuteNonQueryAsync();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => new SearchQueries(database.Connection, Cursors(), new FixedClock(AsOf)).Search(actor, new(AsOf.AddDays(-1), AsOf), null, null, default));
        Assert.InRange(watch.Elapsed.TotalSeconds, 4, 12); await tx.RollbackAsync();
    }

    [Fact]
    public async Task Availability_projection_retains_business_health_without_claiming_green_for_stale_failed_or_disabled_sources()
    {
        var actor = await database.ActorAsync();
        var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        var profile = sample.Profiles.Single() with { TeamId = actor.TeamId, Parsing = new(), Policy = new() { RoutingContract = "serial", RecoveryCompatibility = "orders", ReadFreshness = TimeSpan.FromMinutes(1) } };
        sample = sample with { Profiles = [profile], Entries = [], ServerTimeZoneId = TimeZoneInfo.Local.Id };
        var scope = new ProcessingScope(actor.TeamId, await new PostgresConfigurationStore(database.Connection).CreateSessionAsync(sample), profile.ApplicationId);
        var source = new SourceConfiguration { ProfileId = profile.Id, SourceKey = "file", Root = @"C:\synthetic-logs", Enabled = true };
        var store = new PostgresIngestionStore(database.Connection); await store.RegisterAsync(scope, source, "synthetic"); var fence = await store.ClaimAsync(scope, Guid.NewGuid(), TimeSpan.FromMinutes(2));
        async Task<ApplicationStateDto> State(DateTimeOffset at) => Assert.Single((await new DashboardQueries(database.Connection, new FixedClock(at)).ReadAsync(actor)).Applications);
        try
        {
            Assert.Equal("NotObserved", (await State(AsOf)).Availability);
            await store.ObserveSourceAsync(fence, profile.Id, new("file", ReaderState.Following, "read", AsOf, AsOf, ReadSuccess: true));
            Assert.Equal("Fresh", (await State(AsOf)).Availability);
            Assert.Equal("Stale", (await State(AsOf.AddMinutes(2))).Availability);
            await store.ObserveSourceAsync(fence, profile.Id, new("file", ReaderState.Unavailable, "failure", AsOf.AddSeconds(1), AsOf.AddSeconds(1), ReadSuccess: false));
            Assert.Equal("Disconnected", (await State(AsOf.AddSeconds(1))).Availability);
            await store.SetEnabledAsync(fence, profile.Id, "file", false); var disabled = await State(AsOf.AddSeconds(2));
            Assert.Equal("Unknown", disabled.Availability); Assert.False(disabled.HealthyNow); Assert.Equal("Stable", disabled.BusinessHealth);
            Assert.Empty(await new PostgresProcessingStore(database.Connection).ReadMetricFactsAsync(scope));
        }
        finally { await store.ReleaseAsync(fence); }
    }

    [Fact]
    public async Task Dashboard_snapshot_does_not_mix_new_commits_into_old_counts()
    {
        var (actor, session, profile) = await Fixture();
        var query = new DashboardQueries(database.Connection, new FixedClock(AsOf), async () =>
        {
            var store = new PostgresIngestionStore(database.Connection); var scope = new ProcessingScope(actor.TeamId, session, "app-cam");
            var fence = await store.ClaimAsync(scope, Guid.NewGuid(), TimeSpan.FromMinutes(2));
            try
            {
                await store.SetEnabledAsync(fence, profile, "sample", true); var source = Assert.Single(await store.SourcesAsync(scope));
                var generation = (await store.ObserveFileAsync(fence, source, new(@"C:\synthetic-logs\snapshot.log", new("synthetic", "volume", "snapshot", "birth"), 0, AsOf, "empty"), 0, null)).GenerationId;
                PhysicalRecord Line(string text, long offset) { var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\n"); return new(offset, offset + bytes.Length, bytes, text, "LF", 0); }
                var begin = Line("10:01:00 CAM process started", 0); await store.CommitAsync(fence, generation, begin, AsOf.AddSeconds(1));
                await store.CommitAsync(fence, generation, Line("10:01:01 CAM process completed", begin.End), AsOf.AddSeconds(2));
            }
            finally { await store.ReleaseAsync(fence); }
        });
        var snapshot = await query.ReadAsync(actor);
        Assert.Equal(4, snapshot.SystemHealth.TotalEvaluatedRuns); Assert.Equal(75m, snapshot.SystemHealth.Percentage);
        var next = await new DashboardQueries(database.Connection, new FixedClock(AsOf.AddSeconds(3))).ReadAsync(actor);
        Assert.Equal(5, next.SystemHealth.TotalEvaluatedRuns); Assert.Equal(80m, next.SystemHealth.Percentage);
    }

    [Fact]
    public async Task Quarantined_evidence_is_searchable_by_processing_time_without_inventing_event_time_or_metrics()
    {
        var (actor, _, _) = await Fixture(quarantine: true); var query = new SearchQueries(database.Connection, Cursors(), new FixedClock(AsOf));
        var filter = new SearchFilter(AsOf.AddDays(-1), AsOf, Text: "invalid timestamp");
        var row = Assert.Single((await query.Search(actor, filter, null, null, default)).Items);
        Assert.Null(row.NormalizedEventAt);
        Assert.Empty((await query.Search(actor, filter with { TimeBasis = "event" }, null, null, default)).Items);
        var dashboard = await new DashboardQueries(database.Connection, new FixedClock(AsOf)).ReadAsync(actor);
        Assert.Equal(4, dashboard.SystemHealth.TotalEvaluatedRuns); Assert.Equal(3, dashboard.CompletedOrderRunsProcessedToday);
    }

    [Fact]
    public async Task Legacy_incidents_remain_readable_but_live_workflow_does_not_upgrade_their_semantics()
    {
        var (actor, session, _) = await Fixture();
        var incident = Assert.Single((await new InvestigationQueries(database.Connection, Cursors(), new FixedClock(AsOf)).Incidents(actor, session, null, null, default)).Items);
        Assert.False(incident.WorkflowSupported); Assert.Empty(incident.AllowedActions);
        var paths = new MonitoringPathPolicy([@"C:\synthetic-logs"]);
        await using var admission = new Sonda.Server.Hosting.ApplicationAdmission(database.Connection, paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<Sonda.Server.Hosting.ApplicationAdmission>.Instance);
        await using var access = AccessDbContext.Open(database.Connection);
        var service = new Sonda.Server.Hosting.WorkflowService(database.Connection, new OperationLedger(access), admission, new InitialActivationStore(database.Connection, paths, "synthetic"));
        var error = await Assert.ThrowsAsync<AccessFault>(() => service.Status(actor, incident.Id, new(Guid.NewGuid(), session, 0, Sonda.Domain.Incidents.IncidentStatus.Investigating, "review"), default));
        Assert.Equal("legacy_live_mutation_unsupported", error.Code);
        var read = await new InvestigationQueries(database.Connection, Cursors(), new FixedClock(AsOf)).Incident(actor, session, incident.Id, default);
        Assert.Equal(incident, read);
    }
}
