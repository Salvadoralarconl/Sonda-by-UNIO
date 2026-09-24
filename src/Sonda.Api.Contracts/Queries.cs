namespace Sonda.Api.Contracts;

public sealed record Page<T>(IReadOnlyList<T> Items, bool HasMore, string? NextCursor, DateTimeOffset AsOf);
public sealed record SystemHealthDto(int SuccessfulEvaluatedRuns, int TotalEvaluatedRuns, decimal? Percentage);
public sealed record CurrentSystemBadgeDto(string? BusinessHealth, bool IsQualified, string Qualification,
    int MonitoredApplicationCount, IReadOnlyList<ApplicationStateDto> AvailabilityIssues);
public sealed record VolumeDto(DateOnly Date, int CompletedOrderRunsProcessedCount);
public sealed record ApplicationDto(string Id, string Name, long Revision);
public sealed record ApplicationStateDto(string ApplicationId, string? BusinessHealth, string Availability, bool HealthyNow,
    DateTimeOffset? LastSuccessfulRead, DateTimeOffset? LastRunCompleted, string Reason);
public sealed record IncidentDto(Guid SessionId, string Id, string ProfileId, string ProblemIdentity, string Severity, string Status, long Revision, bool WorkflowSupported)
{
    public string[] AllowedActions => !WorkflowSupported || Status == "Resolved" ? [] : Status == "Active" ? ["Investigating", "Resolved"] : ["Active", "Resolved"];
}
public sealed record DashboardDto(DateTimeOffset AsOf, DateOnly ReportingDate, string ReportingTimeZoneId, SystemHealthDto SystemHealth,
    int CompletedOrderRunsProcessedToday, IReadOnlyList<VolumeDto> FiveDayCompletedOrderHistory, int UnresolvedIncidentCount,
    IReadOnlyList<IncidentDto> RecentIncidents, IReadOnlyList<ApplicationStateDto> Applications)
{
    public IReadOnlyList<IncidentPresentationDto> IncidentDisplays { get; init; } = [];
    public IReadOnlyList<ApplicationDto> ApplicationLabels { get; init; } = [];
    public CurrentSystemBadgeDto CurrentSystemBadge { get; init; } = new(null, true, "No monitored applications.", 0, []);
    public string ActiveIncidentsBadge { get; init; } = "Stable";
}
public sealed record RunDto(Guid SessionId, string Id, string ProfileId, int ProfileVersion, string Scope, string? ParentId,
    string? Identifier, int Attempt, string? Result, DateTimeOffset StartedEventAt, DateTimeOffset StartedProcessedAt,
    DateTimeOffset? CompletedEventAt, DateTimeOffset? CompletedProcessedAt, string? PreviousAttemptId, string? TerminalReason);
public sealed record EvidenceDto(Guid SessionId, Guid Id, string ApplicationId, string ProfileId, int ProfileVersion, string Raw,
    string? SourceTimestamp, DateTimeOffset? NormalizedEventAt, string? TimestampQuality, DateTimeOffset ProcessedAt,
    DateTimeOffset? DatabaseCommitAt, DateTimeOffset? AcknowledgedAt, string[] RunIds, bool RawTruncated = false, bool LinksTruncated = false);
public sealed record SearchFilter(DateTimeOffset From, DateTimeOffset To, string TimeBasis = "processed", string? ApplicationId = null,
    string? ProfileId = null, string? Text = null, bool CaseSensitive = false, string? Identifier = null,
    string? IdentifierNamespace = null, string? Result = null, string? Classification = null, string? IncidentStatus = null);
