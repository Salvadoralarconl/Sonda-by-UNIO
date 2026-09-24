using Sonda.Domain.Profiles;

namespace Sonda.Domain.Runs;

public enum DetectionResult { Success, Failure, Undefined }
public enum RunLifecycle { Running, Finalized }

public sealed class RunRecord
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RunPolicyContext? PolicyContext { get; internal set; }
    public required string Id { get; init; }
    public required string ProfileId { get; init; }
    public required TargetScope Scope { get; init; }
    public required int CycleSequence { get; init; }
    public string? ApplicationRunId { get; init; }
    public string? Identifier { get; init; }
    public int AttemptNumber { get; init; } = 1;
    public required DateTimeOffset StartedEventAt { get; init; }
    public RunLifecycle Lifecycle { get; internal set; }
    public DetectionResult? Result { get; internal set; }
    public DateTimeOffset? CompletedEventAt { get; internal set; }
    public DateTimeOffset? CompletedProcessedAt { get; internal set; }
    public string? TerminalReason { get; internal set; }
    public List<int> EvidenceLines { get; } = [];
    public List<string> DiagnosticProblemKeys { get; } = [];
    public bool ObservedSuccess { get; internal set; }
    public bool ObservedFailure { get; internal set; }
}

public sealed record RunPolicyContext(int ProfileVersion, string RecoveryCompatibility, string? CorrelationKey,
    string? PreviousAttemptId, long StartedSequence, DateTimeOffset StartedProcessedAt,
    DateTimeOffset DeadlineAnchor, DateTimeOffset? Deadline, DateTimeOffset? OverdueAt,
    string CompletionTimeKind = "Evidence", long? CompletedSequence = null);
