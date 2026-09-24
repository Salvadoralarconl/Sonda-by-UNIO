using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;
using Xunit;

namespace Sonda.Phase1.Tests;

public class EngineAcceptanceTests
{
    private static SimulationReport Run(params string[] lines) => new SimulationRunner().Run(TestSamples.Request(TestSamples.Profile(), lines));

    [Fact]
    public void Mixed_orders_preserve_independent_parent_result_and_count_each_run()
    {
        var report = new SimulationRunner().Run(TestSamples.Mixed());
        Assert.True(report.Complete);
        Assert.Equal(DetectionResult.Success, Assert.Single(report.ApplicationRuns).Result);
        Assert.Equal(3, report.OrderRuns.Count);
        Assert.Equal(2, report.OrderRuns.Count(r => r.Result == DetectionResult.Success));
        var failure = Assert.Single(report.OrderRuns, r => r.Result == DetectionResult.Failure);
        Assert.Equal("002", failure.Identifier);
        var incident = Assert.Single(report.Incidents);
        Assert.Equal(Classification.Warning, incident.Severity);
        Assert.Equal(IncidentStatus.Active, incident.Status);
        Assert.Equal(failure.Id, Assert.Single(incident.Occurrences).RunId);
        Assert.Equal(ApplicationHealth.Warning, Assert.Single(report.CurrentStates).LastKnownBusinessHealth);
        Assert.Equal(3, report.Metrics!.CompletedOrderRunsProcessedToday);
        Assert.Equal(4, report.Metrics.TotalEvaluatedRuns);
        Assert.Equal(3, report.Metrics.SuccessfulEvaluatedRuns);
        Assert.Equal(75m, report.Metrics.SystemHealthPercentage);
        Assert.Contains(report.Entries.SelectMany(e => e.ProblemDecisions), d => d.IncidentKey == incident.IncidentKey && d.Action == "CreatedIncident");
        Assert.Equal(new[] { 2, 4, 5 }, report.OrderRuns.Single(r => r.Identifier == "001").EvidenceLines);
    }

    [Fact]
    public void Two_failed_occurrences_then_retry_resolve_one_exact_problem()
    {
        var report = Run("CAM process started", "Finding order OrderID=93822", "Unable to send order OrderID=93822", "CAM process completed",
            "CAM process started", "Finding order OrderID=93822", "Unable to send order OrderID=93822", "CAM process completed",
            "CAM process started", "Finding order OrderID=93822", "Order sent OrderID=93822", "CAM process completed");
        Assert.True(report.Complete);
        var incident = Assert.Single(report.Incidents);
        Assert.Equal(2, incident.Occurrences.Count);
        Assert.All(incident.Occurrences, o => Assert.Equal(DetectionResult.Failure, o.Result));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(report.OrderRuns[2].Id, Assert.Single(incident.RecoveryEvents).RunId);
        Assert.Equal(3, report.OrderRuns.Count);
        Assert.Equal(66.67m, report.Metrics!.SystemHealthPercentage);
        Assert.Equal(3, report.Metrics.CompletedOrderRunsProcessedToday);
        Assert.Equal(ApplicationHealth.Stable, report.CurrentStates[0].LastKnownBusinessHealth);
        Assert.Contains(report.Entries.SelectMany(e => e.ProblemDecisions), d => d.Action == "AppendedOccurrence" && d.IncidentKey == incident.IncidentKey);
        Assert.Contains(report.Entries.SelectMany(e => e.ProblemDecisions), d => d.Action == "ResolvedBySuccessfulRun" && d.IncidentKey == incident.IncidentKey);
    }

    [Fact]
    public void Different_identifiers_create_separate_incidents()
    {
        var report = Run("CAM process started", "Finding order OrderID=A", "Unable to send order OrderID=A",
            "Finding order OrderID=B", "Unable to send order OrderID=B", "CAM process completed");
        Assert.True(report.Complete);
        Assert.Equal(2, report.Incidents.Count);
        Assert.Equal(2, report.Incidents.Select(i => i.IncidentKey).Distinct().Count());
    }

    [Fact]
    public void Closing_cycle_finalizes_unfinished_order_as_undefined()
    {
        var report = Run("CAM process started", "Finding order OrderID=77482", "Connecting OrderID=77482", "CAM process completed");
        Assert.True(report.Complete);
        Assert.Equal(DetectionResult.Undefined, Assert.Single(report.OrderRuns).Result);
        Assert.Equal("ParentCycleClosedWithoutOrderOutcome", report.OrderRuns[0].TerminalReason);
        Assert.Equal(IncidentStatus.Active, Assert.Single(report.Incidents).Status);
        Assert.Equal(1, report.Metrics!.CompletedOrderRunsProcessedToday);
        Assert.Equal(50m, report.Metrics.SystemHealthPercentage);
    }

    [Fact]
    public void Sample_eof_does_not_close_runs()
    {
        var report = Run("CAM process started", "Finding order OrderID=A");
        Assert.True(report.Complete);
        Assert.Null(report.OrderRuns[0].Result);
        Assert.Null(report.ApplicationRuns[0].Result);
        Assert.Empty(report.Incidents);
        Assert.Equal(0, report.Metrics!.CompletedOrderRunsProcessedToday);
        Assert.Null(report.Metrics.SystemHealthPercentage);
        Assert.Null(report.CurrentStates[0].LastKnownBusinessHealth);
    }

    [Fact]
    public void Application_failure_recovers_without_order_volume()
    {
        var report = Run("CAM process started", "CAM process failed", "CAM process started", "CAM process completed");
        Assert.True(report.Complete);
        Assert.Equal(IncidentStatus.Resolved, Assert.Single(report.Incidents).Status);
        Assert.Equal(50m, report.Metrics!.SystemHealthPercentage);
        Assert.Equal(0, report.Metrics.CompletedOrderRunsProcessedToday);
    }

    [Fact]
    public void Same_identifier_in_another_profile_cannot_resolve_failure()
    {
        var cam = TestSamples.Profile();
        var other = cam with { Id = "other", ApplicationId = "app-other" };
        var first = TestSamples.Request(cam, "CAM process started", "Finding order OrderID=X", "Unable to send order OrderID=X", "CAM process completed");
        var second = TestSamples.Request(other, "CAM process started", "Finding order OrderID=X", "Order sent OrderID=X", "CAM process completed");
        var request = first with { Profiles = [cam, other], Entries = [.. first.Entries, .. second.Entries.Select(e => e with { ProcessedAt = e.ProcessedAt.AddMinutes(1) })] };
        var report = new SimulationRunner().Run(request);
        Assert.True(report.Complete);
        Assert.Equal(IncidentStatus.Active, Assert.Single(report.Incidents).Status);
        Assert.Empty(report.Incidents[0].RecoveryEvents);
    }

    [Fact]
    public void Explicit_end_waits_and_then_closes_orders()
    {
        var profile = TestSamples.Profile() with { CompletionMode = CompletionMode.ExplicitEnd };
        profile.Rules.Add(new() { Key = "end", Role = RuleRole.CycleEnd, Alternatives = [new() { Kind = PatternKind.Exact, Expression = "Execution ended" }] });
        var request = TestSamples.Request(profile, "CAM process started", "Finding order OrderID=A", "CAM process completed");
        var open = new SimulationRunner().Run(request);
        Assert.Null(open.ApplicationRuns[0].Result);
        Assert.Null(open.OrderRuns[0].Result);
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "Finding order OrderID=A", "CAM process completed", "Execution ended"));
        Assert.True(report.Complete);
        Assert.Equal(DetectionResult.Success, report.ApplicationRuns[0].Result);
        Assert.Equal(DetectionResult.Undefined, report.OrderRuns[0].Result);
    }

    [Fact]
    public void Problem_keys_are_exact_and_escape_component_boundaries()
    {
        var profile = TestSamples.Profile();
        var a = ProblemIdentity.For(profile, TargetScope.Order, "a|b", "c");
        var b = ProblemIdentity.For(profile, TargetScope.Order, "a", "b|c");
        Assert.NotEqual(a.IncidentKey, b.IncidentKey);
        Assert.NotEqual(a, b);
        Assert.NotEqual(ProblemIdentity.For(profile, TargetScope.Order, "001", "c"), ProblemIdentity.For(profile, TargetScope.Order, "1", "c"));
        Assert.Equal(a, ProblemIdentity.For(profile, TargetScope.Order, "a|b", "c"));
    }
}
