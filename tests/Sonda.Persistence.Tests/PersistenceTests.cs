using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Processing;
using Sonda.Domain.Metrics;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

[Collection("Postgres")]
public sealed partial class PersistenceTests(PostgresFixture pg)
{
    private static SimulationRequest Sample(string name = "mixed-orders") => SimulationJson.Deserialize<SimulationRequest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".json")));
    private async Task<(SimulationRequest Sample, ProcessingScope Scope, ProcessingRequest[] Inputs)> Setup(string name = "mixed-orders")
    {
        var original = Sample(name); var sample = original with { Profiles = original.Profiles.Select(p => p with { TeamId = "test-" + Guid.NewGuid().ToString("N") }).ToList() }; var p = sample.Profiles[0]; var id = await new PostgresConfigurationStore(pg.Connection).CreateSessionAsync(sample);
        var scope = new ProcessingScope(p.TeamId, id, p.ApplicationId);
        return (sample, scope, sample.Entries.Select((e, n) => new ProcessingRequest(scope, e.ProfileId, Guid.NewGuid(), "sample", "fixture", n, n + 1, n + 1, e.Raw, e.ProcessedAt, sample.SampleDate)).ToArray());
    }
    private async Task Apply(IEnumerable<ProcessingRequest> inputs) { foreach (var r in inputs) await new PostgresProcessingStore(pg.Connection).ProcessAsync(r); }
    [Theory]
    [InlineData("mixed-orders")]
    [InlineData("repeated-recovery")]
    [InlineData("priority-ignore")]
    public async Task Durable_restart_and_replay_match_phase1(string name)
    {
        var (sample, scope, inputs) = await Setup(name); var baseline = new SimulationRunner().Run(sample);
        foreach (var (input, index) in inputs.Select((value, index) => (value, index)))
        {
            var actual = await new PostgresProcessingStore(pg.Connection).ProcessAsync(input);
            Assert.Equal(SimulationJson.Serialize(baseline.Entries[index]), SimulationJson.Serialize(actual.Trace));
        }
        var store = new PostgresProcessingStore(pg.Connection); var state = await store.ReadStateAsync(scope, sample.Profiles[0].Id); var report = new SimulationRunner().Run(sample);
        var expected = report.ApplicationRuns.Concat(report.OrderRuns).Select(RunState.From).OrderBy(r => r.Id).ToArray();
        Assert.Equal(SimulationJson.Serialize(expected), SimulationJson.Serialize(state.Interpreter.Runs.OrderBy(r => r.Id).ToArray()));
        Assert.Equal(SimulationJson.Serialize(report.Incidents.Select(IncidentState.From).OrderBy(i => i.Id)), SimulationJson.Serialize(state.Interpreter.Incidents.OrderBy(i => i.Id)));
        var facts = await store.ReadMetricFactsAsync(scope); Assert.Equal(SimulationJson.Serialize(report.Metrics), SimulationJson.Serialize(MetricCalculator.Calculate(facts, sample.AsOf, TimeZoneInfo.FindSystemTimeZoneById(sample.ServerTimeZoneId))));
        Assert.Equal(report.CurrentStates[0].LastKnownBusinessHealth, await store.ReadHealthAsync(scope));
        foreach (var input in inputs) Assert.True((await store.ProcessAsync(input)).Replayed);
        Assert.Equal(SimulationJson.Serialize(state), SimulationJson.Serialize(await store.ReadStateAsync(scope, sample.Profiles[0].Id)));
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/phase2", name)); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "request.json"), SimulationJson.Serialize(sample));
        await File.WriteAllTextAsync(Path.Combine(directory, "phase1-report.json"), SimulationJson.Serialize(report));
        await File.WriteAllTextAsync(Path.Combine(directory, "durable-state.json"), SimulationJson.Serialize(state));
        await File.WriteAllTextAsync(Path.Combine(directory, "parity.json"), SimulationJson.Serialize(new { fixture = name, originalFixtureHash = SimulationJson.Hash(Sample(name)), isolatedTeam = scope.TeamId, entryTracesEqual = true, runsEqual = true, incidentsEqual = true, metricsEqual = true, replayUnchanged = true, metrics = MetricCalculator.Calculate(facts, sample.AsOf, TimeZoneInfo.FindSystemTimeZoneById(sample.ServerTimeZoneId)) }));
    }
    [Theory]
    [InlineData(PersistenceBoundary.AfterEvidence)]
    [InlineData(PersistenceBoundary.AfterRuns)]
    [InlineData(PersistenceBoundary.AfterFacts)]
    [InlineData(PersistenceBoundary.BeforeCommit)]
    public async Task Failure_rolls_back_entire_input(PersistenceBoundary boundary)
    {
        var (_, scope, inputs) = await Setup(); await Apply(inputs.Take(3));
        var before = await new PostgresProcessingStore(pg.Connection).ReadStateAsync(scope, "cam");
        var broken = new PostgresProcessingStore(pg.Connection, b => { if (b == boundary) throw new IOException("Injected fault"); });
        await Assert.ThrowsAsync<IOException>(() => broken.ProcessAsync(inputs[3]));
        Assert.Equal(SimulationJson.Serialize(before), SimulationJson.Serialize(await broken.ReadStateAsync(scope, "cam")));
        await using var db = SondaDbContext.Open(pg.Connection); Assert.Equal(3, await db.Set<ReceiptRow>().CountAsync(x => x.SessionId == scope.SessionId));
        Assert.False((await new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[3])).Replayed);
    }
    [Fact]
    public async Task Concurrent_duplicate_requests_commit_once()
    {
        var (_, scope, inputs) = await Setup(); var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[0])));
        Assert.Single(results, r => !r.Replayed); await using var db = SondaDbContext.Open(pg.Connection); Assert.Equal(1, await db.Set<RunRow>().CountAsync(r => r.SessionId == scope.SessionId));
        await Assert.ThrowsAsync<PersistenceConflict>(() => new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[0] with { Raw = "different" }));
        var alias = Guid.NewGuid(); Assert.True((await new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[0] with { RequestId = alias })).Replayed);
        await Assert.ThrowsAsync<PersistenceConflict>(() => new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[1] with { RequestId = alias }));
    }
    [Fact]
    public async Task Commit_then_process_exit_before_response_retries_without_effects()
    {
        var (_, scope, inputs) = await Setup("repeated-recovery"); await Apply(inputs.Take(inputs.Length - 2));
        var input = inputs[^2]; var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var requestPath = Path.Combine(root, "artifacts", "test-results", $"crash-{Guid.NewGuid():N}.json"); Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!); await File.WriteAllTextAsync(requestPath, SimulationJson.Serialize(input));
        var start = new ProcessStartInfo(Path.Combine(root, ".tools", "dotnet", "dotnet.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root, "tools", "Sonda.PersistenceHarness", "bin", "Release", "net10.0", "Sonda.PersistenceHarness.dll")); start.ArgumentList.Add("process"); start.ArgumentList.Add(requestPath); start.ArgumentList.Add("crash-after-commit"); start.Environment["SONDA_DATABASE"] = pg.Connection;
        using var child = Process.Start(start)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync(); await child.WaitForExitAsync();
        Assert.True(child.ExitCode == 73, $"Expected crash boundary 73, got {child.ExitCode}: {await stderr}; {await stdout}");
        var store = new PostgresProcessingStore(pg.Connection); var before = await store.ReadStateAsync(scope, "cam"); var facts = await store.ReadMetricFactsAsync(scope);
        Assert.True((await store.ProcessAsync(input)).Replayed); Assert.Equal(SimulationJson.Serialize(before), SimulationJson.Serialize(await store.ReadStateAsync(scope, "cam"))); Assert.Equal(facts.Count, (await store.ReadMetricFactsAsync(scope)).Count);
        Assert.Single(before.Interpreter.Incidents); Assert.Equal(2, before.Interpreter.Incidents[0].Occurrences.Length); Assert.Single(before.Interpreter.Incidents[0].Recoveries);
        Directory.CreateDirectory(Path.Combine(root, "artifacts", "phase2"));
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "phase2", "crash-retry.json"), SimulationJson.Serialize(new { childExitCode = child.ExitCode, boundary = "COMMIT succeeded; process exited before return", committedReceiptDetected = true, duplicateRuns = 0, duplicateOccurrences = 0, duplicateRecoveries = 0, duplicateMetricFacts = 0, runs = before.Interpreter.Runs.Length, occurrences = before.Interpreter.Incidents.Sum(i => i.Occurrences.Length), recoveries = before.Interpreter.Incidents.Sum(i => i.Recoveries.Length), metricFacts = facts.Count }));
    }
}
