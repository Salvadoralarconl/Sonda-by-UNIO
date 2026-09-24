namespace Sonda.Api.Contracts;

public sealed record IncidentPresentationDto(Guid SessionId, string IncidentId, string ApplicationId,
    string ApplicationName, string Summary, bool SummaryTruncated, string SummaryBasis,
    DateTimeOffset FirstDetectedProcessedAt, DateTimeOffset LastUpdatedProcessedAt,
    Guid? EvidenceId, string ProfileId, int ProfileVersion);
