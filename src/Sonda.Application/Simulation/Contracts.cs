using Sonda.Domain.Detection;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Metrics;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Application.Simulation;

public sealed record SimulationRequest
{
    public string Seed { get; init; } = "sample";
    public string ServerTimeZoneId { get; init; } = "UTC";
    public DateTimeOffset AsOf { get; init; }
    public DateOnly? SampleDate { get; init; }
    public List<Profile> Profiles { get; init; } = [];
    public List<SampleEntry> Entries { get; init; } = [];
}

public sealed record Provenance(string ReportSchemaVersion, string EngineVersion, string ProfileHash,
    string SampleHash, string RequestHash, string Seed, string ServerTimeZoneId, DateTimeOffset AsOf);

public sealed class EntryTrace
{
    public required int Line { get; init; }
    public required string ProfileId { get; init; }
    public required string Raw { get; init; }
    public ParsedEntry? Parsed { get; set; }
    public string Disposition { get; set; } = "Pending";
    public List<RuleMatch> Matches { get; set; } = [];
    public List<string> Actions { get; set; } = [];
    public List<ProblemDecision> ProblemDecisions { get; set; } = [];
    public List<Diagnostic> Diagnostics { get; } = [];
}

public sealed record ProfileState(string ProfileId, ApplicationHealth? LastKnownBusinessHealth, bool InterpretationComplete);
public sealed record RuleCoverage(string ProfileId, string RuleKey, int MatchingEntries, int WinningEntries);

public sealed class SimulationReport
{
    public required Provenance Provenance { get; init; }
    public bool Complete { get; set; }
    public List<Diagnostic> Diagnostics { get; } = [];
    public List<EntryTrace> Entries { get; } = [];
    public List<RunRecord> ApplicationRuns { get; } = [];
    public List<RunRecord> OrderRuns { get; } = [];
    public List<Incident> Incidents { get; } = [];
    public List<ProfileState> CurrentStates { get; } = [];
    public List<RunMetricFact> MetricFacts { get; } = [];
    public List<RuleCoverage> RuleCoverage { get; } = [];
    public Metrics? Metrics { get; set; }
}
