namespace Sonda.Api.Contracts;

// Presentation-only projection. Authentication and authorization still use Actor.
public sealed record SessionDto(Guid AccountId, string TeamId, string Role, long Revision, string DisplayName)
{
    public DateTimeOffset AsOf { get; init; }
    public string ReportingTimeZoneId { get; init; } = "";
    public string? ReportingTimeZoneIanaId { get; init; }
}
