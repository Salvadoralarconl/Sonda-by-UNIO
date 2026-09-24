using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;
using Sonda.Domain.Metrics;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed partial class PersistenceTests
{
    [Fact]
    public async Task Versions_activate_only_at_temporary_safe_boundary_and_keep_history()
    {
        var (sample, scope, inputs) = await Setup(); var p = sample.Profiles[0]; var config = new PostgresConfigurationStore(pg.Connection);
        var newer = p with { Version = 2, Name = "Version two" }; var request = sample with { Profiles = [newer] };
        var edit = Guid.NewGuid(); Assert.Equal(1, await config.EditDraftAsync(newer, 0, edit)); Assert.Equal(1, await config.EditDraftAsync(newer, 0, edit));
        await Assert.ThrowsAsync<PersistenceConflict>(() => config.EditDraftAsync(newer with { Name = "stale" }, 0, Guid.NewGuid()));
        await Assert.ThrowsAsync<PersistenceConflict>(() => config.PublishAsync(request, 0, Guid.NewGuid()));
        await config.PublishAsync(request, 1, Guid.NewGuid()); await Apply(inputs.Take(1));
        await Assert.ThrowsAsync<PersistenceConflict>(() => config.ActivateAsync(scope, p.Id, 2, 1, 1, Guid.NewGuid()));
        await Apply(inputs.Skip(1)); await using var db = SondaDbContext.Open(pg.Connection);
        var revision = (await db.Set<LaneRow>().FindAsync(scope.TeamId, scope.SessionId, p.Id))!.Revision;
        var command = Guid.NewGuid(); await config.ActivateAsync(scope, p.Id, 2, revision, 1, command); await config.ActivateAsync(scope, p.Id, 2, revision, 1, command);
        await Assert.ThrowsAsync<PersistenceConflict>(() => config.ActivateAsync(scope, p.Id, 1, revision, 1, Guid.NewGuid()));
        var next = inputs[0] with { RequestId = Guid.NewGuid(), Generation = "next", Line = inputs.Length + 1, Sequence = inputs.Length + 1, ProcessedAt = inputs[^1].ProcessedAt.AddMinutes(1) };
        Assert.Equal(2, (await new PostgresProcessingStore(pg.Connection).ProcessAsync(next)).Version);
        Assert.Equal(1, (await new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[0])).Version);
        var old = await db.Set<RunRow>().Where(r => r.SessionId == scope.SessionId && r.Result != null).ToListAsync(); Assert.All(old, r => Assert.Equal(1, r.Version));
    }
    [Fact]
    public async Task Concurrent_incident_status_writers_conflict_without_changing_metrics()
    {
        var (sample, scope, inputs) = await Setup(); await Apply(inputs); var store = new PostgresProcessingStore(pg.Connection); var facts = await store.ReadMetricFactsAsync(scope);
        await using var db = SondaDbContext.Open(pg.Connection); var incident = await db.Set<IncidentRow>().SingleAsync(x => x.SessionId == scope.SessionId);
        var command = Guid.NewGuid(); var revisions = new IncidentRevisionStore(pg.Connection);
        Assert.Equal(incident.Revision + 1, await revisions.CompareExchangeAsync(scope, incident.Id, incident.Revision, IncidentStatus.Investigating, command, sample.AsOf));
        Assert.Equal(incident.Revision + 1, await revisions.CompareExchangeAsync(scope, incident.Id, incident.Revision, IncidentStatus.Investigating, command, sample.AsOf));
        await Assert.ThrowsAsync<PersistenceConflict>(() => revisions.CompareExchangeAsync(scope, incident.Id, incident.Revision, IncidentStatus.Resolved, Guid.NewGuid(), sample.AsOf));
        Assert.Equal(SimulationJson.Serialize(facts), SimulationJson.Serialize(await store.ReadMetricFactsAsync(scope))); Assert.Equal(ApplicationHealth.Warning, await store.ReadHealthAsync(scope));
    }
    [Theory]
    [InlineData("version")]
    [InlineData("rule")]
    [InlineData("pattern")]
    [InlineData("run")]
    [InlineData("fact")]
    [InlineData("recovery")]
    public async Task Committed_interpretation_rows_are_database_immutable(string kind)
    {
        var (_, scope, inputs) = await Setup("repeated-recovery"); await Apply(inputs); await using var db = SondaDbContext.Open(pg.Connection);
        var table = kind switch { "version" => "profile_versions", "rule" => "profile_rules", "pattern" => "profile_patterns", "run" => "runs", "fact" => "run_metric_facts", _ => "recoveries" };
        // Table comes only from the closed test list; values remain bound parameters.
        var sql = "UPDATE sonda." + table + " SET team_id=team_id WHERE team_id={0}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, scope.TeamId)); Assert.Equal("23514", error.SqlState);
    }
    [Theory]
    [InlineData("cycle")]
    [InlineData("order")]
    [InlineData("occurrence")]
    [InlineData("recovery")]
    [InlineData("fact")]
    [InlineData("incident")]
    public async Task Duplicate_business_identities_are_rejected_by_postgres(string kind)
    {
        var (_, scope, inputs) = await Setup(kind == "incident" ? "mixed-orders" : "repeated-recovery"); await Apply(inputs); await using var db = SondaDbContext.Open(pg.Connection);
        var sql = kind switch
        {
            "cycle" => "INSERT INTO sonda.runs SELECT team_id,session_id,id||'-dup',profile_id,version,scope,cycle_sequence,parent_id,identifier,attempt,result,created_receipt,completed_receipt,jsonb_set(payload::jsonb,'{{id}}',to_jsonb(id||'-dup'))::text FROM sonda.runs WHERE session_id={0} AND scope='Application' LIMIT 1",
            "order" => "INSERT INTO sonda.runs SELECT team_id,session_id,id||'-dup',profile_id,version,scope,cycle_sequence,parent_id,identifier,attempt,result,created_receipt,completed_receipt,jsonb_set(payload::jsonb,'{{id}}',to_jsonb(id||'-dup'))::text FROM sonda.runs WHERE session_id={0} AND scope='Order' LIMIT 1",
            "incident" => "INSERT INTO sonda.incidents SELECT team_id,session_id,id||'-dup',profile_id,problem_id,problem,recovery_policy,severity,status,revision,created_receipt FROM sonda.incidents WHERE session_id={0} LIMIT 1",
            "occurrence" => "INSERT INTO sonda.incident_occurrences SELECT team_id,session_id,id||'-dup',incident_id,run_id,created_receipt,updated_receipt,jsonb_set(payload::jsonb,'{{id}}',to_jsonb(id||'-dup'))::text FROM sonda.incident_occurrences WHERE session_id={0} LIMIT 1",
            "recovery" => "INSERT INTO sonda.recoveries SELECT * FROM sonda.recoveries WHERE session_id={0} LIMIT 1",
            _ => "INSERT INTO sonda.run_metric_facts SELECT * FROM sonda.run_metric_facts WHERE session_id={0} LIMIT 1"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, scope.SessionId)); Assert.Equal("23505", error.SqlState);
    }
    [Fact]
    public async Task Reprojection_is_repeatable_and_does_not_insert_facts()
    {
        var (sample, scope, inputs) = await Setup(); await Apply(inputs); var store = new PostgresProcessingStore(pg.Connection);
        var first = await store.ReadMetricFactsAsync(scope); var zone = TimeZoneInfo.FindSystemTimeZoneById(sample.ServerTimeZoneId);
        for (var n = 0; n < 3; n++)
        {
            var projection = MetricCalculator.Calculate(await new PostgresProcessingStore(pg.Connection).ReadMetricFactsAsync(scope), sample.AsOf, zone);
            Assert.Equal(75m, projection.SystemHealthPercentage); Assert.Equal(3, projection.CompletedOrderRunsProcessedToday); Assert.Equal(5, projection.FiveDayHistory.Count);
        }
        Assert.Equal(first.Count, (await store.ReadMetricFactsAsync(scope)).Count);
    }
    [Fact]
    public async Task Problem_digest_collision_never_merges_exact_keys()
    {
        var (_, scope, _) = await Setup(); await using var db = SondaDbContext.Open(pg.Connection);
        var one = new ProblemRow { TeamId = scope.TeamId, SessionId = scope.SessionId, Id = Guid.NewGuid(), Key = "problem-one", Digest = "forced-collision" };
        var two = new ProblemRow { TeamId = scope.TeamId, SessionId = scope.SessionId, Id = Guid.NewGuid(), Key = "problem-two", Digest = "forced-collision" }; db.AddRange(one, two); await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<ProblemRow>().CountAsync(p => p.SessionId == scope.SessionId));
        db.Add(new ProblemRow { TeamId = scope.TeamId, SessionId = scope.SessionId, Id = Guid.NewGuid(), Key = one.Key, Digest = "different-digest" }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Event_processing_completion_and_commit_clocks_remain_separate()
    {
        var (sample, scope, inputs) = await Setup(); await Apply(inputs.Select(r => r with { ProcessedAt = r.ProcessedAt.AddDays(1) })); await using var db = SondaDbContext.Open(pg.Connection);
        var fact = await db.Set<FactRow>().FirstAsync(x => x.SessionId == scope.SessionId); Assert.NotEqual(fact.EventDate, fact.ProcessedDate); Assert.NotEqual(fact.EventTicks, fact.ProcessedTicks);
        var receipt = await db.Set<ReceiptRow>().FindAsync(scope.TeamId, scope.SessionId, fact.ReceiptId); Assert.Equal(receipt!.ProcessedTicks, fact.ProcessedTicks);
        var normalized = await db.Set<NormalizedRow>().FindAsync(scope.TeamId, scope.SessionId, fact.ReceiptId); Assert.Equal("Parsed", normalized!.Quality); Assert.NotNull(normalized.SourceTimestamp);
        var observation = await db.Set<ObservationRow>().FindAsync(scope.TeamId, scope.SessionId, fact.ReceiptId); Assert.NotNull(observation); Assert.NotEqual(receipt.ProcessedAt, observation!.AcknowledgedAt);
        var tracking = await db.Database.SqlQueryRaw<string>("SELECT current_setting('track_commit_timestamp') AS \"Value\"").SingleAsync();
        if (tracking == "on") Assert.NotNull(observation.DatabaseCommitAt); else Assert.Null(observation.DatabaseCommitAt);
    }
    [Fact]
    public async Task Blocked_lane_survives_restart_without_fabricated_runs()
    {
        var (_, scope, inputs) = await Setup(); var store = new PostgresProcessingStore(pg.Connection);
        var rejected = await store.ProcessAsync(inputs[0] with { Raw = "bad timestamp" }); Assert.Equal("Quarantined", rejected.Trace.Disposition);
        Assert.Equal("BlockedByPriorError", (await new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[1])).Trace.Disposition);
        Assert.Empty((await store.ReadStateAsync(scope, "cam")).Interpreter.Runs); Assert.Null(await store.ReadHealthAsync(scope));
    }
}
