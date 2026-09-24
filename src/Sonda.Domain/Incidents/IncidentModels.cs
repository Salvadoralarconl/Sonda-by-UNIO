using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Domain.Incidents;

public enum IncidentStatus { Active, Investigating, Resolved }
public enum ApplicationHealth { Stable, Warning, Error }

public sealed record Occurrence(string Id, string RunId, string ApplicationRunId,
    int CycleSequence, DetectionResult? Result, Classification Severity,
    DateTimeOffset DetectedAt, IReadOnlyList<int> EvidenceLines);
public sealed record RecoveryEvent(string RunId, string ApplicationRunId, int CycleSequence,
    DateTimeOffset RecoveredAt, string Method, IReadOnlyList<int> EvidenceLines);
public sealed record StatusChange(IncidentStatus? From, IncidentStatus To, DateTimeOffset At, string Reason)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Actor { get; init; }
}
public sealed record ProblemDecision(int Line, string IncidentKey, string IncidentId, string Action, string RunId);

public sealed class Incident
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IncidentPolicyContext? PolicyContext { get; internal set; }
    public required string Id { get; init; }
    public required ProblemIdentity Problem { get; init; }
    public string IncidentKey => Problem.IncidentKey;
    public required RecoveryPolicy RecoveryPolicy { get; init; }
    public Classification Severity { get; internal set; }
    public IncidentStatus Status { get; internal set; } = IncidentStatus.Active;
    public List<Occurrence> Occurrences { get; } = [];
    public List<RecoveryEvent> RecoveryEvents { get; } = [];
    public List<StatusChange> History { get; } = [];
}

public sealed record IncidentPolicyContext(int Episode, int ProfileVersion, string RecoveryCompatibility,
    long Revision = 0, string? ResolutionKind = null, string? Actor = null, string? Reason = null)
{
    public ObservationOccurrence[] Observations { get; init; } = [];
}
public sealed record ObservationOccurrence(string Id, int EvidenceLine, DateTimeOffset EventAt,
    DateTimeOffset ProcessedAt, Classification Severity, string? ClosedRunId);
