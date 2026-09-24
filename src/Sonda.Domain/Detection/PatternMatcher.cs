using System.Diagnostics;
using System.Text.RegularExpressions;
using Sonda.Domain.Evidence;
using Sonda.Domain.Profiles;

namespace Sonda.Domain.Detection;

public sealed record RuleMatch(string RuleKey, RuleRole Role, TargetScope Target, int Priority,
    Classification? Classification, IReadOnlyList<int> Alternatives, string Selection);

public static class PatternMatcher
{
    public const int MaxInputLength = 65536;
    public const int MaxExpressionLength = 2048;
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    public static Regex Regex(string expression, bool caseSensitive = true) => new(expression,
        RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase), RegexTimeout);

    public static bool IsMatch(PatternAlternative pattern, string input) => pattern.Kind switch
    {
        PatternKind.Contains => input.Contains(pattern.Expression,
            pattern.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
        PatternKind.Exact => string.Equals(input, pattern.Expression,
            pattern.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
        PatternKind.Regex => Regex(pattern.Expression, pattern.CaseSensitive).IsMatch(input),
        _ => throw new InterpretationException("InvalidPattern", "Unsupported pattern type.")
    };

    public static List<RuleMatch> Match(Profile profile, string message)
    {
        if (message.Length > MaxInputLength) throw new InterpretationException("InputLimit", "Sample line exceeds the size limit.");
        var watch = Stopwatch.StartNew();
        List<RuleMatch> matches = [];
        foreach (var rule in profile.Rules.Where(r => r.Enabled))
        {
            List<int> alternatives = [];
            for (var index = 0; index < rule.Alternatives.Count; index++)
            {
                if (IsMatch(rule.Alternatives[index], message)) alternatives.Add(index);
                if (watch.ElapsedMilliseconds > 250)
                    throw new InterpretationException("MatchBudgetExceeded", "Total per-entry matching budget exceeded.");
            }
            if (alternatives.Count > 0)
                matches.Add(new(rule.Key, rule.Role, rule.Target, rule.Priority, rule.Classification, alternatives, "Matched"));
        }
        return matches;
    }

    /// <summary>True = a witness exists; false = disjoint; null = not proven. Never assumes regex disjointness.</summary>
    public static bool? Overlap(PatternAlternative a, PatternAlternative b)
    {
        if (a == b) return true;
        if (a.Kind == PatternKind.Contains && b.Kind == PatternKind.Contains) return true;
        if (a.Kind == PatternKind.Exact && b.Kind == PatternKind.Exact)
            return string.Equals(a.Expression, b.Expression,
                a.CaseSensitive && b.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        if (a.Kind == PatternKind.Exact)
        {
            if (IsMatch(b, a.Expression)) return true;
            return a.CaseSensitive ? false : null;
        }
        if (b.Kind == PatternKind.Exact) return Overlap(b, a);
        return null;
    }
}
