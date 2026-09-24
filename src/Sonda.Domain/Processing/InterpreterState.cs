using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Domain.Processing;

// Data-only persistence boundary. Does not interpret or replay evidence.
public sealed record RunState(string Id, string ProfileId, TargetScope Scope, int CycleSequence,
    string? ApplicationRunId, string? Identifier, int AttemptNumber, DateTimeOffset StartedEventAt,
    RunLifecycle Lifecycle, DetectionResult? Result, DateTimeOffset? CompletedEventAt,
    DateTimeOffset? CompletedProcessedAt, string? TerminalReason, int[] EvidenceLines,
    string[] DiagnosticProblemKeys, bool ObservedSuccess, bool ObservedFailure)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RunPolicyContext? PolicyContext { get; init; }
    public static RunState From(RunRecord r) => new(r.Id, r.ProfileId, r.Scope, r.CycleSequence,
        r.ApplicationRunId, r.Identifier, r.AttemptNumber, r.StartedEventAt, r.Lifecycle, r.Result,
        r.CompletedEventAt, r.CompletedProcessedAt, r.TerminalReason, r.EvidenceLines.ToArray(),
        r.DiagnosticProblemKeys.ToArray(), r.ObservedSuccess, r.ObservedFailure) { PolicyContext = r.PolicyContext };
    public RunRecord Restore()
    {
        var r = new RunRecord
        {
            Id = Id,
            PolicyContext = PolicyContext,
            ProfileId = ProfileId,
            Scope = Scope,
            CycleSequence = CycleSequence,
            ApplicationRunId = ApplicationRunId,
            Identifier = Identifier,
            AttemptNumber = AttemptNumber,
            StartedEventAt = StartedEventAt,
            Lifecycle = Lifecycle,
            Result = Result,
            CompletedEventAt = CompletedEventAt,
            CompletedProcessedAt = CompletedProcessedAt,
            TerminalReason = TerminalReason,
            ObservedSuccess = ObservedSuccess,
            ObservedFailure = ObservedFailure
        };
        r.EvidenceLines.AddRange(EvidenceLines); r.DiagnosticProblemKeys.AddRange(DiagnosticProblemKeys);
        return r;
    }
}
public sealed record IncidentState(string Id, ProblemIdentity Problem, RecoveryPolicy RecoveryPolicy,
    Classification Severity, IncidentStatus Status, Occurrence[] Occurrences, RecoveryEvent[] Recoveries,
    StatusChange[] History)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IncidentPolicyContext? PolicyContext { get; init; }
    public static IncidentState From(Incident i) => new(i.Id, i.Problem, i.RecoveryPolicy, i.Severity, i.Status,
        i.Occurrences.Select(o => o with { EvidenceLines = o.EvidenceLines.ToArray() }).ToArray(),
        i.RecoveryEvents.Select(r => r with { EvidenceLines = r.EvidenceLines.ToArray() }).ToArray(), i.History.ToArray()) { PolicyContext = i.PolicyContext is null ? null : i.PolicyContext with { Observations=i.PolicyContext.Observations.ToArray() } };
    public Incident Restore()
    {
        var i = new Incident { Id = Id, Problem = Problem, RecoveryPolicy = RecoveryPolicy, Severity = Severity, Status = Status, PolicyContext = PolicyContext is null ? null : PolicyContext with { Observations=PolicyContext.Observations.ToArray() } };
        i.Occurrences.AddRange(Occurrences.Select(o => o with { EvidenceLines = o.EvidenceLines.ToArray() }));
        i.RecoveryEvents.AddRange(Recoveries.Select(r => r with { EvidenceLines = r.EvidenceLines.ToArray() }));
        i.History.AddRange(History); return i;
    }
}
public sealed record InterpreterState(int Format, string ProfileId, int CycleSequence, RunState[] Runs, IncidentState[] Incidents)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProfileVersionState? Versions { get; init; }
}
public sealed record ProfileVersionState(int ActiveVersion, Profile[] Published);
