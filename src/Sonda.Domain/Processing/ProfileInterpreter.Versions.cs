using System.Text.Json;
using Sonda.Domain.Evidence;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Domain.Processing;

public sealed partial class ProfileInterpreter
{
    private Profile activeProfile;
    private readonly Dictionary<int, Profile> published = new();
    private static Profile CopyProfile(Profile value)=>JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(value))!;
    public Profile ActiveProfile => activeProfile.Policy is null ? activeProfile : CopyProfile(activeProfile);

    public Profile SelectInputProfile(ParsedEntry routingEntry)
    {
        profile = activeProfile;
        RunRecord? existing;
        if (activeProfile.Policy?.CycleMode == CycleMode.Correlated)
        {
            var key = Correlation(routingEntry.Message);
            existing = key is null ? null : Runs.SingleOrDefault(r => r.Scope == TargetScope.Application && r.PolicyContext?.CorrelationKey == key);
        }
        else
        {
            var begin = Sonda.Domain.Detection.PatternMatcher.Match(activeProfile, routingEntry.Message).Any(m => m.Role == RuleRole.CycleBegin);
            existing = begin ? null : Runs.SingleOrDefault(r => r.Scope == TargetScope.Application && r.Lifecycle == RunLifecycle.Running);
        }
        if (existing?.PolicyContext is { } context) profile = published[context.ProfileVersion];
        return CopyProfile(profile);
    }

    public void Activate(Profile next)
    {
        next=CopyProfile(next);
        if (activeProfile.Policy?.Revision == 2 && next.Policy?.Revision != 2)
            throw new InterpretationException("PolicyDowngradeUnsupported", "Revision-2 sessions cannot activate LegacyV1 semantics.");
        if (next.Id != activeProfile.Id || next.TeamId != activeProfile.TeamId || next.ApplicationId != activeProfile.ApplicationId)
            throw new InterpretationException("ProfileIdentityConflict", "Activation cannot change ownership.");
        if (ProfileValidator.Validate(next).Any(d => d.Level == DiagnosticLevel.Error))
            throw new InterpretationException("InvalidProfile", "Activation requires a valid Profile.");
        var open = Runs.Any(r => r.Lifecycle == RunLifecycle.Running);
        if (open && (activeProfile.Policy is null || next.Policy is null || !RoutingCompatible(activeProfile,next)))
            throw new InterpretationException("DrainRequired", "Open runs need a compatible routing contract.");
        if (published.TryGetValue(next.Version,out var old) && JsonSerializer.Serialize(old) != JsonSerializer.Serialize(next))
            throw new InterpretationException("ImmutableVersion", "Published version content cannot change.");
        var incompatibleKey=Incidents.Any(i=>i.Status!=Sonda.Domain.Incidents.IncidentStatus.Resolved &&
            i.Problem.StreamKey==next.StreamKey && (i.Problem.Scope==TargetScope.Application || i.Problem.IdentifierNamespace==next.Identifier?.Namespace) &&
            HasRecoveryCondition(next,i.Problem) &&
            i.PolicyContext is { } contract && contract.RecoveryCompatibility!=next.Policy?.RecoveryCompatibility);
        if (incompatibleKey)
            throw new InterpretationException("RecoveryContractConflict", "Unresolved keys retain their original recovery meaning; use an explicit new namespace.");
        published[next.Version] = next; activeProfile = next; profile = next;
    }

    private static bool HasRecoveryCondition(Profile p,Sonda.Domain.Incidents.ProblemIdentity problem) =>
        problem.ConditionKey==(problem.Scope==TargetScope.Application?"cycle-outcome":"order-outcome") ||
        p.Rules.Any(r=>r.Enabled&&r.Role==RuleRole.Detection&&r.Target==problem.Scope&&r.ConditionKey==problem.ConditionKey&&r.Recovery==RecoveryPolicy.NextSuccessfulRun);

    private static bool RoutingCompatible(Profile a, Profile b) =>
        a.StreamKey == b.StreamKey && a.Policy!.RoutingContract == b.Policy!.RoutingContract &&
        a.Policy.CycleMode == b.Policy.CycleMode && a.Policy.CorrelationExpression == b.Policy.CorrelationExpression &&
        a.Policy.CorrelationCapture == b.Policy.CorrelationCapture && a.Policy.CorrelationEpoch == b.Policy.CorrelationEpoch &&
        JsonSerializer.Serialize(a.Parsing) == JsonSerializer.Serialize(b.Parsing) &&
        JsonSerializer.Serialize(a.Rules.Where(r=>r.Role == RuleRole.CycleBegin)) == JsonSerializer.Serialize(b.Rules.Where(r=>r.Role == RuleRole.CycleBegin));
}
