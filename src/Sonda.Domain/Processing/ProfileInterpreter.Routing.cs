using Sonda.Domain.Detection;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Domain.Processing;

public sealed partial class ProfileInterpreter
{
    private string? selectedCorrelation;

    public string PartitionKey(ParsedEntry entry)
    {
        var previous=profile;profile=activeProfile;
        try { return activeProfile.Policy?.CycleMode==CycleMode.Correlated ? Correlation(entry.Message) ?? "*" : "serial"; }
        finally { profile=previous; }
    }

    public EntryInterpretation ApplyLateObservation(ParsedEntry entry,List<RuleMatch> matches)
    {
        selectedCorrelation=Correlation(entry.Message);
        List<Diagnostic> warnings=[];
        var winners=SelectDetections(entry,matches,warnings);
        return HandleDetachedEvidence(entry,matches,winners,warnings,true)!;
    }

    private string? Correlation(string message)
    {
        var p = profile.Policy!;
        if (p.CycleMode != CycleMode.Correlated) return null;
        var values = PatternMatcher.Regex(p.CorrelationExpression).Matches(message)
            .SelectMany(m => m.Groups[p.CorrelationCapture].Captures.Select(c => c.Value))
            .Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > 1) throw new InterpretationException("AmbiguousCorrelation", "Multiple cycle keys; no partition chosen.");
        return values.Length == 0 ? null : System.Text.Json.JsonSerializer.Serialize(new[] { p.CorrelationEpoch, values[0] });
    }

    private void SelectPartition(ParsedEntry entry, List<RuleMatch> matches)
    {
        if (profile.Policy!.CycleMode != CycleMode.Correlated) return;
        selectedCorrelation = Correlation(entry.Message);
        var applicationWideOnly = matches.All(m => m.Role == RuleRole.Detection && profile.Policy.ApplicationWideRules.Contains(m.RuleKey));
        if (selectedCorrelation is null && matches.Count > 0 && !applicationWideOnly)
            throw new InterpretationException("MissingCorrelation", "Run-scoped input needs an exact cycle key.");
        var existing = selectedCorrelation is null ? null : Runs.SingleOrDefault(r => r.Scope == TargetScope.Application && r.PolicyContext?.CorrelationKey == selectedCorrelation);
        if (existing is not null && matches.Any(m => m.Role == RuleRole.CycleBegin))
            throw new InterpretationException("DuplicateCorrelation", "A cycle key cannot be reused within its epoch.");
        cycle = existing?.Lifecycle == RunLifecycle.Running ? existing : null;
        openOrders.Clear();
        if (cycle is not null)
            foreach (var order in Runs.Where(r => r.ApplicationRunId == cycle.Id && r.Lifecycle == RunLifecycle.Running))
                openOrders.Add(order.Identifier!, order);
    }

    private EntryInterpretation? HandleDetachedEvidence(ParsedEntry entry, List<RuleMatch> matches,
        Dictionary<TargetScope, Rule> winners, List<Diagnostic> warnings, bool forceLate=false)
    {
        if (!forceLate && (cycle is not null || matches.Any(m => m.Role == RuleRole.CycleBegin))) return null;
        var closed = selectedCorrelation is null ? null : Runs.SingleOrDefault(r => r.Scope == TargetScope.Application && r.PolicyContext?.CorrelationKey == selectedCorrelation && r.Lifecycle==RunLifecycle.Finalized);
        if (!forceLate && closed is null && matches.Any(m => m.Target == TargetScope.Order))
            throw new InterpretationException("UnmatchedOrder", "Order input needs a reliably associated open parent and attempt.");
        List<ProblemDecision> decisions = [];
        List<string> actions = [];
        foreach (var winner in winners.Values)
        {
            if(winner.Target!=TargetScope.Application)continue;
            var allowed = forceLate || closed is not null ? profile.Policy!.LateObservationRules.Contains(winner.Key)
                : profile.Policy!.ApplicationWideRules.Contains(winner.Key);
            if (!allowed || winner.Classification == Classification.Ignore) continue;
            var key = ProblemIdentity.For(profile, TargetScope.Application, null, winner.ConditionKey);
            var incident = Incidents.SingleOrDefault(i => i.Problem == key && i.Status != IncidentStatus.Resolved);
            var created = incident is null;
            if (incident is null)
            {
                incident = new() { Id = nextId("incident"), Problem = key, RecoveryPolicy = winner.Recovery, Severity = winner.Classification!.Value,
                    PolicyContext = new(Incidents.Count(i => i.Problem == key) + 1, profile.Version, profile.Policy!.RecoveryCompatibility) };
                incident.History.Add(new(null, IncidentStatus.Active, entry.ProcessedAt, "Evidence observation"));
                Incidents.Add(incident);
            }
            var context = incident.PolicyContext!;
            if (!context.Observations.Any(o => o.EvidenceLine == entry.Line))
                incident.PolicyContext = context with { Revision = created ? context.Revision : context.Revision + 1, Observations = [..context.Observations,
                    new(nextId("observation"), entry.Line, entry.EventAt, entry.ProcessedAt, winner.Classification!.Value, closed?.Id)] };
            if (winner.Classification == Classification.Error) incident.Severity = Classification.Error;
            decisions.Add(new(entry.Line, key.IncidentKey, incident.Id, created ? "CreatedIncident" : "AppendedObservation", ""));
            actions.Add($"Observation:{incident.Id}");
        }
        warnings.Add(new(!forceLate && closed is null ? "UnmatchedEvidence" : "LateEvidence", DiagnosticLevel.Warning,
            "Evidence retained separately; no finalized run or contribution changed.", profile.Id, entry.Line));
        return new(matches, actions, decisions, warnings);
    }
}
