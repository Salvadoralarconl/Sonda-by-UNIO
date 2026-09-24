using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;

namespace Sonda.Domain.Availability;

public enum AvailabilityState { NotObserved, Fresh, Stale, Disconnected, Unknown }
public sealed record SourceObservation(string Source, long Sequence, DateTimeOffset EffectiveAt, DateTimeOffset ProcessedAt, bool Success);
public sealed record AvailabilityResult(ApplicationHealth? BusinessHealth, AvailabilityState Availability,
    DateTimeOffset? LastSuccessfulRead, DateTimeOffset? LastRunCompleted, DateTimeOffset AsOf, string Reason)
{
    public bool HealthyNow => BusinessHealth == ApplicationHealth.Stable && Availability == AvailabilityState.Fresh;
}
public static class AvailabilityProjection
{
    public static AvailabilityResult Calculate(ApplicationHealth? health, IReadOnlyCollection<string> requiredSources,
        IEnumerable<SourceObservation> observations, InterpretationPolicy policy, DateTimeOffset? lastRunCompleted, DateTimeOffset asOf)
    {
        var rows = observations.Where(o => o.ProcessedAt <= asOf && o.EffectiveAt <= asOf).ToArray();
        if (rows.GroupBy(o => (o.Source,o.Sequence)).Any(g => g.Distinct().Count() > 1)) throw new ArgumentException("Conflicting observation identity.");
        var latest = rows.GroupBy(o => o.Source,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.MaxBy(o=>o.Sequence)!);
        var lastRead = rows.Where(o=>o.Success).Select(o=>(DateTimeOffset?)o.EffectiveAt).Max();
        AvailabilityResult Result(AvailabilityState state,string reason) => new(health,state,lastRead,lastRunCompleted,asOf,reason);
        if (rows.Length==0 && lastRunCompleted is null) return Result(AvailabilityState.NotObserved,"No observations.");
        if (requiredSources.Any(s=>latest.TryGetValue(s,out var o)&&!o.Success)) return Result(AvailabilityState.Disconnected,"Required source reports failure.");
        if (requiredSources.Count==0 || requiredSources.Any(s=>!latest.ContainsKey(s))) return Result(AvailabilityState.Unknown,"Required source coverage is incomplete.");
        if (policy.ReadFreshness is null) return Result(AvailabilityState.Unknown,"No configured read freshness threshold.");
        if (requiredSources.Any(s=>asOf-latest[s].EffectiveAt > policy.ReadFreshness)) return Result(AvailabilityState.Stale,"Required source read is stale.");
        if (policy.RunCadence is not null && (lastRunCompleted is null || asOf-lastRunCompleted > policy.RunCadence)) return Result(AvailabilityState.Stale,"Configured run cadence not met.");
        return Result(AvailabilityState.Fresh,"All required checks pass.");
    }
}
