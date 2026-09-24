using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Domain.Metrics;

public sealed record RunMetricFact(string RunId, TargetScope Scope, DetectionResult Result,
    DateTimeOffset CompletedEventAt, DateTimeOffset CompletedProcessedAt);
public sealed record VolumeBucket(DateOnly Date, int CompletedOrderRunsProcessedCount);
public sealed record Metrics(int SuccessfulEvaluatedRuns, int TotalEvaluatedRuns, decimal? SystemHealthPercentage,
    int CompletedOrderRunsProcessedToday, IReadOnlyList<VolumeBucket> FiveDayHistory);

public static class MetricCalculator
{
    public static Metrics Calculate(IEnumerable<RunMetricFact> input, DateTimeOffset asOf, TimeZoneInfo zone)
    {
        // Projection deduplication only. No claim of persistent transaction semantics in Phase 1.
        var facts = input.GroupBy(f => f.RunId, StringComparer.Ordinal).Select(group =>
        {
            if (group.Distinct().Count() != 1) throw new ArgumentException($"Conflicting metric facts for {group.Key}.");
            return group.First();
        }).ToArray();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(asOf, zone).DateTime);
        DateOnly Day(DateTimeOffset timestamp) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, zone).DateTime);
        var evaluated = facts.Where(f => Day(f.CompletedEventAt) == today).ToArray();
        var successful = evaluated.Count(f => f.Result == DetectionResult.Success);
        var history = Enumerable.Range(-4, 5).Select(offset =>
        {
            var date = today.AddDays(offset);
            return new VolumeBucket(date, facts.Count(f => f.Scope == TargetScope.Order && Day(f.CompletedProcessedAt) == date));
        }).ToArray();
        return new(successful, evaluated.Length,
            evaluated.Length == 0 ? null : decimal.Round(successful * 100m / evaluated.Length, 2),
            history[^1].CompletedOrderRunsProcessedCount, history);
    }
}
