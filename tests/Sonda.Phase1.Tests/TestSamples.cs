using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Profiles;

namespace Sonda.Phase1.Tests;

internal static class TestSamples
{
    public static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-09-23T14:00:00Z");
    public static SimulationRequest Mixed() => SimulationJson.Deserialize<SimulationRequest>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
    public static Profile Profile() => Mixed().Profiles[0] with { Parsing = new(), Name = "Synthetic" };
    public static SimulationRequest Request(Profile profile, params string[] lines) => new()
    {
        Profiles = [profile],
        AsOf = Time.AddHours(1),
        Seed = "test",
        ServerTimeZoneId = "UTC",
        Entries = lines.Select((raw, index) => new SampleEntry { ProfileId = profile.Id, Raw = raw, ProcessedAt = Time.AddSeconds(index) }).ToList()
    };
    public static Rule Detection(string key, string expression, int priority = 10,
        Classification classification = Classification.Warning, PatternKind kind = PatternKind.Contains,
        TargetScope target = TargetScope.Application) => new()
        {
            Key = key,
            Role = RuleRole.Detection,
            Target = target,
            Classification = classification,
            Priority = priority,
            ConditionKey = key,
            Alternatives = [new() { Kind = kind, Expression = expression }]
        };
}
