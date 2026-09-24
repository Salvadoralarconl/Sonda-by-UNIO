using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Metrics;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;
using Xunit;

namespace Sonda.Phase1.Tests;

public class ParsingMetricsAndReportTests
{
    [Fact]
    public void Numeric_specification_example_is_985_of_1006()
    {
        List<RunMetricFact> facts = [];
        void Add(int count, TargetScope scope, DetectionResult result)
        {
            for (var i = 0; i < count; i++) facts.Add(new($"run-{facts.Count}", scope, result, TestSamples.Time, TestSamples.Time));
        }
        Add(980, TargetScope.Order, DetectionResult.Success);
        Add(20, TargetScope.Order, DetectionResult.Failure);
        Add(5, TargetScope.Application, DetectionResult.Success);
        Add(1, TargetScope.Application, DetectionResult.Failure);
        var metrics = MetricCalculator.Calculate(facts, TestSamples.Time, TimeZoneInfo.Utc);
        Assert.Equal(985, metrics.SuccessfulEvaluatedRuns);
        Assert.Equal(1006, metrics.TotalEvaluatedRuns);
        Assert.Equal(97.91m, metrics.SystemHealthPercentage);
        Assert.Equal(1000, metrics.CompletedOrderRunsProcessedToday);
    }

    [Fact]
    public void Volume_uses_processing_day_and_reprojection_does_not_double_count()
    {
        var fact = new RunMetricFact("order", TargetScope.Order, DetectionResult.Success, TestSamples.Time.AddDays(-1), TestSamples.Time);
        var metrics = MetricCalculator.Calculate([fact, fact], TestSamples.Time, TimeZoneInfo.Utc);
        Assert.Equal(1, metrics.CompletedOrderRunsProcessedToday);
        Assert.Equal(0, metrics.TotalEvaluatedRuns);
        Assert.Null(metrics.SystemHealthPercentage);
        Assert.Equal(5, metrics.FiveDayHistory.Count);
        Assert.Throws<ArgumentException>(() => MetricCalculator.Calculate([fact, fact with { Result = DetectionResult.Failure }], TestSamples.Time, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Server_timezone_selects_date_not_utc_midnight()
    {
        var instant = DateTimeOffset.Parse("2026-09-24T01:00:00Z");
        var fact = new RunMetricFact("one", TargetScope.Order, DetectionResult.Undefined, instant, instant);
        var metrics = MetricCalculator.Calculate([fact], instant, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.Equal(new DateOnly(2026, 9, 23), metrics.FiveDayHistory[^1].Date);
        Assert.Equal(1, metrics.CompletedOrderRunsProcessedToday);
        Assert.Equal(0m, metrics.SystemHealthPercentage);
    }

    [Fact]
    public void Identifier_regex_preserves_case_and_leading_zeros()
    {
        var profile = TestSamples.Profile() with { Identifier = new() { Kind = IdentifierKind.RegexCapture, Namespace = "ref", Expression = @"Ref:(?<id>[A-Za-z0-9]+)" } };
        var parsed = SampleParser.Parse(profile, new() { ProfileId = profile.Id, Raw = "Message Ref:Ab001", ProcessedAt = TestSamples.Time }, 1, null);
        Assert.Equal("Ab001", parsed.Identifier);
    }

    [Fact]
    public void Ambiguous_identifier_is_not_guessed()
    {
        var request = TestSamples.Request(TestSamples.Profile(), "CAM process started", "Finding order OrderID=A OrderID=B", "CAM process completed");
        var report = new SimulationRunner().Run(request);
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Code == "AmbiguousIdentifier");
        Assert.Equal("BlockedByPriorError", report.Entries[2].Disposition);
        Assert.Empty(report.OrderRuns);
        Assert.Null(report.ApplicationRuns[0].Result);
    }

    [Fact]
    public void Missing_identifier_does_not_invent_order()
    {
        var report = new SimulationRunner().Run(TestSamples.Request(TestSamples.Profile(), "CAM process started", "Finding order"));
        Assert.False(report.Complete);
        Assert.Empty(report.OrderRuns);
        Assert.Contains(report.Diagnostics, d => d.Code == "MissingIdentifier");
    }

    [Fact]
    public void Time_only_format_requires_explicit_date()
    {
        var request = TestSamples.Mixed() with { SampleDate = null };
        var report = new SimulationRunner().Run(request);
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Code == "DateContextRequired");
    }

    [Fact]
    public void Parsed_error_level_does_not_imply_error_classification()
    {
        var profile = TestSamples.Profile() with { Parsing = new() { EntryPattern = @"^(?<level>\w+) (?<message>.*)$" } };
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "INFO CAM process started", "ERROR harmless text", "INFO CAM process completed"));
        Assert.True(report.Complete);
        Assert.Empty(report.Incidents);
        Assert.Equal("ERROR", report.Entries[1].Parsed!.Fields["level"]);
        Assert.Equal(100m, report.Metrics!.SystemHealthPercentage);
    }

    [Fact]
    public void Reports_are_deterministic_and_editing_inputs_invalidates_provenance()
    {
        var request = TestSamples.Mixed();
        var runner = new SimulationRunner();
        var a = runner.Run(request);
        var b = runner.Run(request);
        Assert.Equal(SimulationJson.Serialize(a), SimulationJson.Serialize(b));
        Assert.Equal(MarkdownReport.Render(a), MarkdownReport.Render(b));
        Assert.True(SimulationRunner.IsApplicable(a, request));
        Assert.False(SimulationRunner.IsApplicable(a, request with { Seed = "different" }));
        var changed = request with { Profiles = [request.Profiles[0] with { Name = "edited" }] };
        Assert.False(SimulationRunner.IsApplicable(a, changed));
        var changedSamples = request with { Entries = [.. request.Entries, request.Entries[^1] with { Raw = "changed" }] };
        Assert.False(SimulationRunner.IsApplicable(a, changedSamples));
        Assert.Contains("problem:v1:", MarkdownReport.Render(a));
        Assert.Contains("CreatedIncident", MarkdownReport.Render(a));
    }

    [Fact]
    public void Deferred_timing_is_reported_without_fake_execution()
    {
        var profile = TestSamples.Profile() with { CycleTiming = new() { ExpectedDuration = TimeSpan.FromSeconds(5), GracePeriod = TimeSpan.FromSeconds(1) } };
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "Finding order OrderID=A"));
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Code == "TimingNotImplemented");
        Assert.Null(report.OrderRuns[0].Result);
        Assert.Empty(report.Incidents);
    }

    [Theory]
    [InlineData("CAM process started", "CAM process started")]
    [InlineData("CAM process started", "Finding order OrderID=A", "Order sent OrderID=A", "Finding order OrderID=A")]
    public void Unapproved_policies_stop_lane_without_guessing(params string[] lines)
    {
        var report = new SimulationRunner().Run(TestSamples.Request(TestSamples.Profile(), lines));
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Code == "PolicyDecisionRequired");
    }

    [Fact]
    public void Markup_in_samples_is_escaped_in_human_report()
    {
        var report = new SimulationRunner().Run(TestSamples.Request(TestSamples.Profile(), "CAM process started", "<script>alert(1)</script>"));
        Assert.DoesNotContain("<script>", MarkdownReport.Render(report));
        Assert.Contains("&lt;script&gt;", MarkdownReport.Render(report));
    }
}
