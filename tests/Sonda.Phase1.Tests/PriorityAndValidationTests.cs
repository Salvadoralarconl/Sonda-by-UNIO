using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Profiles;
using Xunit;

namespace Sonda.Phase1.Tests;

public class PriorityAndValidationTests
{
    [Theory]
    [InlineData(20, Classification.Ignore, 0)]
    [InlineData(1, Classification.Error, 1)]
    public void Highest_priority_wins_without_severity_precedence(int ignorePriority, Classification winner, int incidentCount)
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("generic", "connection failed", 10, Classification.Error));
        profile.Rules.Add(TestSamples.Detection("specific", "optional service connection failed", ignorePriority, Classification.Ignore));
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "optional service connection failed"));
        Assert.True(report.Complete);
        Assert.Equal(winner, Assert.Single(report.Entries[1].Matches, m => m.Selection == "Winner").Classification);
        Assert.Single(report.Entries[1].Matches, m => m.Selection == "Suppressed");
        Assert.Equal(incidentCount, report.Incidents.Count);
        Assert.Contains(report.Diagnostics, d => d.Code == "RuleOverlap");
    }

    [Fact]
    public void Unrelated_exact_rules_can_share_priority_in_the_same_scope()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("alpha", "Alpha", 5, kind: PatternKind.Exact));
        profile.Rules.Add(TestSamples.Detection("beta", "Beta", 5, kind: PatternKind.Exact));
        Assert.DoesNotContain(ProfileValidator.Validate(profile), d => d.Level == DiagnosticLevel.Error);
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "Alpha", "Beta"));
        Assert.True(report.Complete);
        Assert.Equal(2, report.Incidents.Count);
        Assert.NotEqual(report.Incidents[0].IncidentKey, report.Incidents[1].IncidentKey);
    }

    [Fact]
    public void Different_targets_can_share_priority_and_predicate()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("application", "notice", 5, Classification.Ignore));
        profile.Rules.Add(TestSamples.Detection("order", "notice", 5, Classification.Ignore, target: TargetScope.Order));
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "Finding order OrderID=A", "notice OrderID=A"));
        Assert.True(report.Complete);
        Assert.Equal(2, report.Entries[2].Matches.Count(m => m.Selection == "Winner"));
    }

    [Fact]
    public void Proven_equal_priority_overlap_is_invalid()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("broad", "failed", 5));
        profile.Rules.Add(TestSamples.Detection("specific", "optional failed", 5, Classification.Ignore));
        Assert.Contains(ProfileValidator.Validate(profile), d => d.Code == "EqualPriorityOverlap" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Unknown_regex_intersection_is_reported_and_actual_tie_is_rejected()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("regex-a", "^optional.*$", 5, kind: PatternKind.Regex));
        profile.Rules.Add(TestSamples.Detection("regex-b", "failed$", 5, kind: PatternKind.Regex));
        var validation = ProfileValidator.Validate(profile);
        Assert.DoesNotContain(validation, d => d.Level == DiagnosticLevel.Error);
        Assert.Contains(validation, d => d.Code == "OverlapUnproven");
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "optional failed"));
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Code == "EqualPriorityOverlap");
        Assert.Empty(report.Incidents);
        Assert.All(report.Entries[1].Matches, m => Assert.Equal("Ambiguous", m.Selection));
    }

    [Fact]
    public void Disabled_rules_do_not_compete()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("active", "notice", 5));
        profile.Rules.Add(TestSamples.Detection("disabled", "notice", 5) with { Enabled = false });
        Assert.DoesNotContain(ProfileValidator.Validate(profile), d => d.Code == "EqualPriorityOverlap");
    }

    [Fact]
    public void Multiple_alternatives_produce_one_rule_decision()
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("ignore", "notice", 5, Classification.Ignore) with
        {
            Alternatives = [new() { Expression = "notice" }, new() { Expression = "a notice" }]
        });
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started", "a notice"));
        var match = Assert.Single(report.Entries[1].Matches);
        Assert.Equal(2, match.Alternatives.Count);
        Assert.Equal("Winner", match.Selection);
        Assert.Empty(report.Incidents);
    }

    [Theory]
    [InlineData("(")]
    [InlineData("")]
    public void Invalid_pattern_is_reported(string expression)
    {
        var profile = TestSamples.Profile();
        profile.Rules.Add(TestSamples.Detection("bad", expression, kind: PatternKind.Regex));
        var report = new SimulationRunner().Run(TestSamples.Request(profile, "CAM process started"));
        Assert.False(report.Complete);
        Assert.Contains(report.Diagnostics, d => d.Level == DiagnosticLevel.Error);
    }
}
