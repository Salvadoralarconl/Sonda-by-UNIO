using System.Text.RegularExpressions;
using Sonda.Domain.Detection;
using Sonda.Domain.Evidence;

namespace Sonda.Domain.Profiles;

public static class ProfileValidator
{
    public static IReadOnlyList<Diagnostic> Validate(Profile profile)
    {
        List<Diagnostic> diagnostics = [];
        void Error(string code, string text) => diagnostics.Add(new(code, DiagnosticLevel.Error, text, profile.Id));
        void Warning(string code, string text) => diagnostics.Add(new(code, DiagnosticLevel.Warning, text, profile.Id));
        if (profile.Policy is { } policy)
        {
            if (policy.Revision != 2) Error("UnsupportedPolicyRevision", "Only explicit policy revision 2 is supported.");
            if (!Enum.IsDefined(policy.CycleMode) || !Enum.IsDefined(policy.UnexpectedBegin) || !Enum.IsDefined(policy.DeadlineClock))
                Error("InvalidPolicy", "Unknown policy setting.");
            if (policy.UndefinedApplicationSeverity is not (Classification.Error or Classification.Warning))
                Error("InvalidSeverity", "Undefined application severity must be Error or Warning.");
            if (string.IsNullOrWhiteSpace(policy.RoutingContract) || string.IsNullOrWhiteSpace(policy.RecoveryCompatibility))
                Error("PolicyIdentityRequired", "Routing and recovery compatibility identities must be explicit.");
            if (policy.CycleMode == CycleMode.Correlated)
            {
                if (string.IsNullOrWhiteSpace(policy.CorrelationEpoch) || string.IsNullOrWhiteSpace(policy.CorrelationExpression))
                    Error("CorrelationRequired", "Correlated cycles require an exact extractor and epoch.");
                else if(policy.CorrelationExpression.Length>PatternMatcher.MaxExpressionLength) Error("CorrelationLimit","Cycle correlation expression exceeds the pattern limit.");
                else CheckRegex(policy.CorrelationExpression, policy.CorrelationCapture, Error);
            }
            foreach (var key in policy.ApplicationWideRules.Concat(policy.LateObservationRules))
                if (!profile.Rules.Any(r => r.Key == key && r.Role == RuleRole.Detection && r.Target == TargetScope.Application))
                    Error("InvalidObservationRule", "Observation rules must identify configured application diagnostic rules.");
            if (profile.OrderTiming.FallbackTimeout is not null && !policy.OrderFallbackEnabled)
                Error("OrderFallbackNotEnabled", "Order fallback must be explicitly enabled.");
            if (policy.ReadFreshness <= TimeSpan.Zero || policy.RunCadence <= TimeSpan.Zero)
                Error("InvalidAvailability", "Availability thresholds must be positive.");
        }
        if (new[] { profile.TeamId, profile.ApplicationId, profile.Id, profile.Name, profile.StreamKey }.Any(string.IsNullOrWhiteSpace))
            Error("RequiredIdentity", "Team, application, Profile ID/name, and stream key must be explicit.");
        if (profile.Version < 1 || !Enum.IsDefined(profile.CompletionMode)) Error("InvalidProfile", "Invalid version or completion mode.");
        foreach (var severity in new[] { profile.CycleFailureSeverity, profile.OrderFailureSeverity, profile.UndefinedOrderSeverity })
            if (severity is not (Classification.Error or Classification.Warning)) Error("InvalidSeverity", "Outcome severity must be Error or Warning.");
        if (profile.Rules.Count > 128) Error("RuleLimit", "At most 128 rules are supported per Profile.");
        if (profile.Rules.GroupBy(r => r.Key, StringComparer.Ordinal).Any(g => g.Count() > 1)) Error("DuplicateRuleKey", "Rule keys must be unique.");
        foreach (var rule in profile.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Key) || !Enum.IsDefined(rule.Role) || !Enum.IsDefined(rule.Target) || !Enum.IsDefined(rule.Recovery))
                Error("InvalidRule", "Every rule requires a key and supported role, target, and recovery policy.");
            if (rule.Alternatives.Count is < 1 or > 16) Error("AlternativeLimit", $"Rule {rule.Key} requires 1–16 alternatives.");
            if (rule.Role == RuleRole.Detection && (rule.Classification is null || !Enum.IsDefined(rule.Classification.Value) || string.IsNullOrWhiteSpace(rule.ConditionKey)))
                Error("InvalidDetection", $"Detection rule {rule.Key} requires classification and an exact condition key.");
            if (rule.Role != RuleRole.Detection && rule.Classification is not null)
                Error("StructuralClassification", $"Structural rule {rule.Key} uses the configured outcome severity, not a diagnostic classification.");
            var expected = rule.Role is RuleRole.OrderBegin or RuleRole.OrderSuccess or RuleRole.OrderFailure ? TargetScope.Order : TargetScope.Application;
            if (rule.Role != RuleRole.Detection && rule.Target != expected) Error("InvalidTarget", $"Rule {rule.Key} has the wrong target scope.");
            foreach (var pattern in rule.Alternatives)
            {
                if (!Enum.IsDefined(pattern.Kind) || string.IsNullOrEmpty(pattern.Expression) || pattern.Expression.Length > PatternMatcher.MaxExpressionLength)
                    Error("InvalidPattern", $"Rule {rule.Key} needs a supported, nonempty expression of at most {PatternMatcher.MaxExpressionLength} characters.");
                else if (pattern.Kind == PatternKind.Regex)
                    CheckRegex(pattern.Expression, null, Error);
            }
        }
        bool Has(RuleRole role) => profile.Rules.Any(r => r.Enabled && r.Role == role);
        if (!Has(RuleRole.CycleBegin)) Error("MissingCycleBegin", "A cycle Begin rule is required.");
        if (profile.CompletionMode == CompletionMode.ExplicitEnd && !Has(RuleRole.CycleEnd)) Error("MissingCycleEnd", "ExplicitEnd mode requires an End rule.");
        if (!Has(RuleRole.CycleSuccess) && !Has(RuleRole.CycleFailure)) Error("MissingCycleOutcome", "Configure at least one cycle outcome rule.");
        if (profile.Rules.Any(r => r.Enabled && r.Target == TargetScope.Order))
        {
            if (profile.Identifier is null || !Has(RuleRole.OrderBegin)) Error("MissingOrderConfiguration", "Order rules require an identifier and Begin rule.");
            if (!Has(RuleRole.OrderSuccess) && !Has(RuleRole.OrderFailure)) Error("MissingOrderOutcome", "Configure at least one order outcome.");
        }
        if (profile.Identifier is { } id)
        {
            if (!Enum.IsDefined(id.Kind) || string.IsNullOrWhiteSpace(id.Namespace) || string.IsNullOrWhiteSpace(id.Expression) || id.Expression.Length > PatternMatcher.MaxExpressionLength)
                Error("InvalidIdentifier", "Identifier kind, namespace, and expression must be explicit and bounded.");
            if (id.Kind == IdentifierKind.RegexCapture) CheckRegex(id.Expression, id.CaptureName, Error);
        }
        if (profile.Parsing.EntryPattern.Length > 0)
        {
            CheckRegex(profile.Parsing.EntryPattern, "message", Error);
            try
            {
                if (PatternMatcher.Regex(profile.Parsing.EntryPattern).GetGroupNames().Contains("timestamp", StringComparer.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(profile.Parsing.TimestampFormat)) Error("TimestampFormatRequired", "Timestamp capture requires an explicit format.");
                    if (profile.Parsing.TimestampHasDate && !profile.Parsing.TimestampFormat.Contains('y'))
                        Error("TimestampDateRequired", "Dated timestamps require an explicit year in the format; time-only input needs sample date context.");
                }
            }
            catch (ArgumentException) { /* CheckRegex already reports the invalid expression. */ }
        }
        if (profile.Parsing.EntryPattern.Length > PatternMatcher.MaxExpressionLength) Error("ParsingLimit", "Entry parser exceeds the expression limit.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(profile.Parsing.SourceTimeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { Error("InvalidTimeZone", "Source timezone is not recognized."); }
        foreach (var timing in new[] { profile.CycleTiming, profile.OrderTiming })
        {
            if (timing.ExpectedDuration <= TimeSpan.Zero || timing.FallbackTimeout <= TimeSpan.Zero || timing.GracePeriod < TimeSpan.Zero)
                Error("InvalidTiming", "Expected duration and fallback timeout must be positive; grace must be nonnegative.");
            if (profile.Policy is null && timing.Requested) Warning("TimingNotImplemented", "Profile timing contract retained; Phase 1 does not execute fallback deadlines.");
            if (profile.Policy is not null && ((timing.GracePeriod is not null && timing.ExpectedDuration is null && timing.FallbackTimeout is null) ||
                (timing.ExpectedDuration is not null && timing.FallbackTimeout is not null && timing.FallbackTimeout < timing.ExpectedDuration)))
                Error("InvalidTiming", "Grace needs a duration; fallback cannot precede expected duration.");
        }
        if (diagnostics.Any(d => d.Level == DiagnosticLevel.Error)) return diagnostics;
        var active = profile.Rules.Where(r => r.Enabled && r.Role == RuleRole.Detection).ToArray();
        for (var i = 0; i < active.Length; i++)
            for (var j = i + 1; j < active.Length; j++)
            {
                var a = active[i]; var b = active[j];
                if (a.Target != b.Target) continue;
                bool proven = false, unknown = false;
                foreach (var p in a.Alternatives)
                    foreach (var q in b.Alternatives)
                    {
                        try { var overlap = PatternMatcher.Overlap(p, q); proven |= overlap == true; unknown |= overlap is null; }
                        catch (RegexMatchTimeoutException) { unknown = true; }
                    }
                if (proven && a.Priority == b.Priority) Error("EqualPriorityOverlap", $"Rules {a.Key} and {b.Key} compete at priority {a.Priority} on target {a.Target}.");
                else if (proven) Warning("RuleOverlap", $"Rules {a.Key} and {b.Key} can overlap; the higher configured priority wins.");
                else if (unknown) Warning("OverlapUnproven", $"Overlap between {a.Key} and {b.Key} is not statically proven; sample coverage is required.");
            }
        return diagnostics;
    }

    private static void CheckRegex(string expression, string? group, Action<string, string> error)
    {
        try
        {
            var regex = PatternMatcher.Regex(expression);
            if (group is not null && (string.IsNullOrWhiteSpace(group) || !regex.GetGroupNames().Contains(group, StringComparer.Ordinal)))
                error("MissingCapture", $"Regex requires named capture '{group}'.");
        }
        catch (ArgumentException e) { error("InvalidRegex", e.Message); }
    }
}
