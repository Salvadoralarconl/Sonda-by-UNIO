using Sonda.Application.Processing;
using Sonda.Domain.Evidence;
using Sonda.Domain.Metrics;
using Sonda.Domain.Processing;
using Sonda.Domain.Profiles;

namespace Sonda.Application.Simulation;

public sealed class SimulationRunner
{
    public const string EngineVersion = "phase1.1";

    public static Provenance ProvenanceFor(SimulationRequest request) => new("1", request.Profiles.Any(p => p.Policy is not null) ? "phase3.1" : EngineVersion,
        SimulationJson.Hash(request.Profiles), SimulationJson.Hash(request.Entries), SimulationJson.Hash(request),
        request.Seed, request.ServerTimeZoneId, request.AsOf);

    public static bool IsApplicable(SimulationReport report, SimulationRequest request) =>
        report.Provenance == ProvenanceFor(request);

    public SimulationReport Run(SimulationRequest request)
    {
        var report = new SimulationReport { Provenance = ProvenanceFor(request) };
        var inputErrors = ValidateRequest(request);
        report.Diagnostics.AddRange(inputErrors);
        if (inputErrors.Count > 0) return report;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.ServerTimeZoneId);
        Dictionary<string, ProfileInterpreter> engines = new(StringComparer.Ordinal);
        HashSet<string> blocked = new(StringComparer.Ordinal);
        foreach (var profile in request.Profiles)
        {
            var diagnostics = ProfileValidator.Validate(profile);
            report.Diagnostics.AddRange(diagnostics);
            if (diagnostics.Any(d => d.Level == DiagnosticLevel.Error)) blocked.Add(profile.Id);
            var counter = 0;
            engines.Add(profile.Id, new(profile, kind => $"{request.Seed}:{profile.Id}:{kind}:{++counter:D4}"));
        }
        var profileMap = request.Profiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
        for (var index = 0; index < request.Entries.Count; index++)
        {
            var sample = request.Entries[index];
            var trace = new EntryTrace { Line = index + 1, ProfileId = sample.ProfileId, Raw = sample.Raw };
            report.Entries.Add(trace);
            if (blocked.Contains(sample.ProfileId)) { trace.Disposition = "BlockedByPriorError"; continue; }
            trace = InterpretInput.Apply(profileMap[sample.ProfileId], engines[sample.ProfileId], sample, trace.Line, request.SampleDate);
            report.Entries[^1] = trace;
            if (trace.Disposition == "Quarantined") blocked.Add(sample.ProfileId);
            report.Diagnostics.AddRange(trace.Diagnostics);
        }
        foreach (var profile in request.Profiles)
        {
            var engine = engines[profile.Id];
            report.ApplicationRuns.AddRange(engine.Runs.Where(r => r.Scope == TargetScope.Application));
            report.OrderRuns.AddRange(engine.Runs.Where(r => r.Scope == TargetScope.Order));
            report.Incidents.AddRange(engine.Incidents);
            report.CurrentStates.Add(new(profile.Id, engine.CurrentHealth, !blocked.Contains(profile.Id)));
            foreach (var rule in profile.Rules.Where(r => r.Enabled))
            {
                var observed = report.Entries.Where(e => e.ProfileId == profile.Id).SelectMany(e => e.Matches).Where(m => m.RuleKey == rule.Key).ToArray();
                report.RuleCoverage.Add(new(profile.Id, rule.Key, observed.Length, observed.Count(m => m.Selection == "Winner")));
            }
            foreach (var run in engine.Runs.Where(r => r.Result is not null))
                report.MetricFacts.Add(new(run.Id, run.Scope, run.Result!.Value, run.CompletedEventAt!.Value, run.CompletedProcessedAt!.Value));
        }
        report.Metrics = MetricCalculator.Calculate(report.MetricFacts, request.AsOf, zone);
        report.Complete = !report.Diagnostics.Any(d => d.Level == DiagnosticLevel.Error || d.Code == "TimingNotImplemented");
        return report;
    }

    private static List<Diagnostic> ValidateRequest(SimulationRequest request)
    {
        List<Diagnostic> errors = [];
        void Error(string text) => errors.Add(new("InvalidRequest", DiagnosticLevel.Error, text));
        if (request.AsOf == default || string.IsNullOrWhiteSpace(request.Seed)) Error("Explicit asOf and seed are required.");
        if (request.Profiles.Count is < 1 or > 100 || request.Entries.Count > 10000) Error("Supply 1–100 Profiles and at most 10000 sample entries.");
        if (request.Profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != request.Profiles.Count) Error("Profile IDs must be unique in a session.");
        if (request.Profiles.Select(p => p.TeamId).Distinct(StringComparer.Ordinal).Count() != 1) Error("One simulation session belongs to one team.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.ServerTimeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { Error("Server timezone is invalid."); }
        DateTimeOffset? last = null;
        foreach (var entry in request.Entries)
        {
            if (!request.Profiles.Any(p => p.Id == entry.ProfileId)) Error($"Unknown Profile {entry.ProfileId}.");
            if (entry.ProcessedAt == default || entry.ProcessedAt > request.AsOf || entry.ProcessedAt < last)
                Error("Processing times must be explicit, nondecreasing, and not later than asOf.");
            last = entry.ProcessedAt;
        }
        return errors;
    }
}
