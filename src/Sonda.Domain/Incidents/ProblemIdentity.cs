using System.Text.Json;
using Sonda.Domain.Profiles;

namespace Sonda.Domain.Incidents;

/// <summary>Exact problem identity across attempts; cycle IDs deliberately belong to occurrences.</summary>
public sealed record ProblemIdentity(
    string TeamId, string ApplicationId, string ProfileId, TargetScope Scope,
    string StreamKey, string? IdentifierNamespace, string? Identifier, string ConditionKey)
{
    // JSON tuple encoding preserves separators, case, leading zeros, and null distinctly.
    public string IncidentKey => "problem:v1:" + JsonSerializer.Serialize(new string?[]
    {
        TeamId, ApplicationId, ProfileId, Scope.ToString(), StreamKey,
        IdentifierNamespace, Identifier, ConditionKey
    });

    public static ProblemIdentity For(Profile profile, TargetScope scope, string? identifier, string conditionKey)
    {
        if (string.IsNullOrWhiteSpace(conditionKey)) throw new ArgumentException("A condition key is required.");
        if (scope == TargetScope.Order && (string.IsNullOrEmpty(identifier) || profile.Identifier is null))
            throw new ArgumentException("An order problem requires an explicit identifier and namespace.");
        return new(profile.TeamId, profile.ApplicationId, profile.Id, scope, profile.StreamKey,
            scope == TargetScope.Order ? profile.Identifier!.Namespace : null,
            scope == TargetScope.Order ? identifier : null, conditionKey);
    }
}
