using System.Text.Json;
using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Profiles;
using Xunit;

namespace Sonda.Phase1.Tests;

public class BoundarySafetyTests
{
    [Theory]
    [InlineData("{\"profiles\":null}")]
    [InlineData("{\"profiles\":[null]}")]
    [InlineData("{\"surprise\":1}")]
    public void Malformed_contracts_are_json_errors(string json) =>
        Assert.Throws<JsonException>(() => SimulationJson.Deserialize<SimulationRequest>(json));

    [Fact]
    public void Both_outcomes_on_one_input_leave_state_unmodified()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(new() { Key = "conflict", Role = RuleRole.CycleFailure, Alternatives = [new() { Expression = "CAM process completed" }] });
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "CAM process completed"));
        Assert.False(report.Complete);
        Assert.Null(Assert.Single(report.ApplicationRuns).Result);
        Assert.Empty(report.Incidents);
        Assert.Equal(new[] { 1 }, report.ApplicationRuns[0].EvidenceLines);
    }

    [Fact]
    public void Winning_ignore_plus_independent_failure_is_explicitly_deferred()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("override", "Unable to send order", 50, Classification.Ignore, target: TargetScope.Order));
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "Finding order OrderID=A", "Unable to send order OrderID=A"));
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Code == "PolicyDecisionRequired");
        Assert.Empty(report.Incidents);
        Assert.Null(report.OrderRuns[0].Result);
    }

    [Fact]
    public void Oversized_evidence_is_quarantined_without_interpretation()
    {
        var report = new SimulationRunner().Run(TestSamples.Request(TestSamples.Profile(), new string('x', 65537)));
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Code == "InputLimit");
        Assert.Empty(report.ApplicationRuns);
    }

    [Theory]
    [InlineData("2026-11-01 01:30:00", "AmbiguousTimestamp")]
    [InlineData("2026-03-08 02:30:00", "AmbiguousTimestamp")]
    public void Ambiguous_or_nonexistent_source_time_is_not_guessed(string timestamp, string code)
    {
        var profile = TestSamples.Profile() with
        {
            Parsing = new()
            {
                EntryPattern = @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (?<message>.*)$",
                TimestampFormat = "yyyy-MM-dd HH:mm:ss",
                SourceTimeZoneId = "America/New_York"
            }
        };
        var exception = Assert.Throws<InterpretationException>(() => SampleParser.Parse(profile,
            new() { Raw = timestamp + " CAM process started", ProcessedAt = TestSamples.Time }, 1, null));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Regex_identifier_does_not_merge_case_variants()
    {
        var profile = TestSamples.Profile() with { Identifier = new() { Kind = IdentifierKind.RegexCapture, Namespace = "ref", Expression = @"OrderID=(?<id>[A-Za-z]+)" } };
        var report = new SimulationRunner().Run(TestSamples.Request(profile,
            "CAM process started", "Finding order OrderID=a", "Unable to send order OrderID=a", "CAM process completed",
            "CAM process started", "Finding order OrderID=A", "Order sent OrderID=A", "CAM process completed"));
        Assert.True(report.Complete);
        Assert.Empty(Assert.Single(report.Incidents).RecoveryEvents);
    }

    [Fact]
    public void Source_entry_date_format_cannot_implicitly_take_machine_year()
    {
        var profile = TestSamples.Profile() with { Parsing = new() { EntryPattern = @"^(?<timestamp>\S+) (?<message>.*)$", TimestampFormat = "HH:mm:ss" } };
        Assert.Contains(ProfileValidator.Validate(profile), d => d.Code == "TimestampDateRequired");
    }
}
