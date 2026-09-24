using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Domain.Processing;

public sealed partial class ProfileInterpreter
{
    private bool Revision2 => profile.Policy?.Revision == 2;

    private RunPolicyContext CreateRunContext(RunRecord run, ParsedEntry entry)
    {
        var policy = profile.Policy!;
        var timing = run.Scope == TargetScope.Application ? profile.CycleTiming : profile.OrderTiming;
        var anchor = policy.DeadlineClock == DeadlineClock.Event ? entry.EventAt : entry.ProcessedAt;
        var grace = timing.GracePeriod ?? TimeSpan.Zero;
        var previous = run.Scope == TargetScope.Order
            ? Runs.LastOrDefault(r => r.ApplicationRunId == run.ApplicationRunId && r.Identifier == run.Identifier)?.Id : null;
        return new(profile.Version, policy.RecoveryCompatibility, selectedCorrelation, previous, entry.Line, entry.ProcessedAt,
            anchor, timing.FallbackTimeout is { } timeout ? anchor + timeout + grace : null,
            timing.ExpectedDuration is { } expected ? anchor + expected + grace : null);
    }

    private void CloseIncompleteCycle(ParsedEntry entry, List<ProblemDecision> decisions, string reason)
    {
        if (cycle is null) return;
        var selectedProfile = profile;
        if (cycle.PolicyContext is { } pinned) profile = published[pinned.ProfileVersion];
        foreach (var order in openOrders.Values.ToArray())
            Finalize(order, DetectionResult.Undefined, reason, entry, null, decisions);
        openOrders.Clear();
        Finalize(cycle, cycle.ObservedFailure ? DetectionResult.Failure : DetectionResult.Undefined, reason, entry, null, decisions);
        cycle = null;
        profile = selectedProfile;
    }

    private void RecoverRevision2(RunRecord successful, ParsedEntry entry, List<ProblemDecision> decisions)
    {
        var context = successful.PolicyContext!;
        // A drained LegacyV1 episode retains its original cross-cycle recovery contract.
        foreach(var legacy in Incidents.Where(i=>i.PolicyContext is null && i.Status!=IncidentStatus.Resolved && i.RecoveryPolicy==RecoveryPolicy.NextSuccessfulRun))
        {
            if(legacy.Problem.Scope!=successful.Scope || legacy.Problem!=ProblemIdentity.For(profile,successful.Scope,successful.Identifier,legacy.Problem.ConditionKey) ||
                legacy.Occurrences.Any(o=>o.CycleSequence>=successful.CycleSequence))continue;
            legacy.RecoveryEvents.Add(new(successful.Id,successful.ApplicationRunId??successful.Id,successful.CycleSequence,entry.ProcessedAt,"Automatic",successful.EvidenceLines.ToArray()));
            legacy.History.Add(new(legacy.Status,IncidentStatus.Resolved,entry.ProcessedAt,"Later configured successful run"));legacy.Status=IncidentStatus.Resolved;
            decisions.Add(new(entry.Line,legacy.IncidentKey,legacy.Id,"ResolvedBySuccessfulRun",successful.Id));
        }
        foreach (var incident in Incidents.Where(i => i.PolicyContext is not null && i.RecoveryPolicy == RecoveryPolicy.NextSuccessfulRun && i.RecoveryEvents.Count == 0))
        {
            var key = incident.Problem;
            if (key.Scope != successful.Scope || key.Identifier != successful.Identifier ||
                key != ProblemIdentity.For(profile, successful.Scope, successful.Identifier, key.ConditionKey) ||
                !HasRecoveryCondition(profile,key) ||
                incident.PolicyContext!.RecoveryCompatibility != context.RecoveryCompatibility ||
                successful.DiagnosticProblemKeys.Contains(key.IncidentKey)) continue;
            // Both processing causality and source chronology must prove the retry is later.
            var eligible = incident.Occurrences.All(o =>
            {
                var failed = Runs.Single(r => r.Id == o.RunId);
                return failed.Id != successful.Id && failed.PolicyContext is { CompletedSequence: { } completed } &&
                    completed < context.StartedSequence && failed.CompletedEventAt <= successful.StartedEventAt;
            });
            eligible &= incident.PolicyContext.Observations.All(o => o.EvidenceLine < context.StartedSequence && o.EventAt <= successful.StartedEventAt);
            if (!eligible) continue;
            incident.RecoveryEvents.Add(new(successful.Id, successful.ApplicationRunId ?? successful.Id,
                successful.CycleSequence, entry.ProcessedAt,
                incident.Status == IncidentStatus.Resolved ? "ConfirmationAfterManualResolution" : "Automatic", successful.EvidenceLines.ToArray()));
            var old = incident.PolicyContext;
            incident.PolicyContext = old with { Revision = old.Revision + 1 };
            if (incident.Status != IncidentStatus.Resolved)
            {
                incident.History.Add(new(incident.Status, IncidentStatus.Resolved, entry.ProcessedAt, "Later causally eligible successful run"));
                incident.Status = IncidentStatus.Resolved;
                incident.PolicyContext = incident.PolicyContext with { ResolutionKind = "Automatic" };
            }
            decisions.Add(new(entry.Line, key.IncidentKey, incident.Id, "ResolvedBySuccessfulRun", successful.Id));
        }
    }

    /// <summary>Pure workflow decision. The Application command boundary owns receipts and actor authentication.</summary>
    public bool ChangeIncidentStatus(string incidentId, long expectedRevision, IncidentStatus target,
        string actor, string reason, DateTimeOffset processedAt)
    {
        if (!Revision2) throw new InterpretationException("PolicyDecisionRequired", "Manual workflow requires revision 2.");
        if (!Enum.IsDefined(target) || string.IsNullOrWhiteSpace(actor) || processedAt == default ||
            (target == IncidentStatus.Resolved && string.IsNullOrWhiteSpace(reason)))
            throw new InterpretationException("InvalidWorkflowCommand", "Explicit actor/time and a resolution reason are required.");
        var incident = Incidents.SingleOrDefault(i => i.Id == incidentId)
            ?? throw new InterpretationException("UnknownIncident", "Incident does not exist.");
        var context = incident.PolicyContext ?? throw new InterpretationException("PolicyDecisionRequired", "Legacy incident workflow is unchanged.");
        if (context.Revision != expectedRevision) throw new InterpretationException("RevisionConflict", "Incident revision changed.");
        if (incident.History.Any(h => h.At > processedAt)) throw new InterpretationException("ClockConflict", "Workflow cannot precede prior history.");
        if (incident.Status == target) return false;
        if (incident.Status == IncidentStatus.Resolved) throw new InterpretationException("ReopeningNotSupported", "Resolved incidents cannot be reopened.");
        incident.History.Add(new(incident.Status, target, processedAt, reason) { Actor = actor });
        incident.Status = target;
        incident.PolicyContext = context with { Revision = context.Revision + 1, Actor = actor, Reason = reason,
            ResolutionKind = target == IncidentStatus.Resolved ? "Manual" : null };
        return true;
    }

    /// <summary>The caller must first consume the declared evidence frontier; this method never reads a wall clock.</summary>
    public IReadOnlyList<ProblemDecision> ApplyDeadline(string runId, DateTimeOffset effectiveAt,
        DateTimeOffset processedAt, int commandSequence)
    {
        if (!Revision2) throw new InterpretationException("PolicyDecisionRequired", "Deadlines require revision 2.");
        var run = Runs.Single(r => r.Id == runId);
        if (run.Lifecycle == RunLifecycle.Finalized) return [];
        var context = run.PolicyContext!;
        profile = published[context.ProfileVersion];
        if (context.Deadline is null || context.Deadline != effectiveAt || processedAt < effectiveAt || commandSequence <= context.StartedSequence)
            throw new InterpretationException("InvalidDeadline", "Use the pinned due instant and a later processing command.");
        if (run.Scope==TargetScope.Application && Runs.Any(r=>r.ApplicationRunId==run.Id&&r.Lifecycle==RunLifecycle.Running&&r.StartedEventAt>effectiveAt))
            throw new InterpretationException("DeadlineOrderingConflict","The declared frontier crossed a parent deadline before processing later child starts; correct the simulation order.");
        var entry = new ParsedEntry(commandSequence, profile.Id, "", "", effectiveAt, processedAt,
            "SyntheticDeadline", new Dictionary<string, string>(), run.Identifier);
        List<ProblemDecision> decisions = [];
        if (profile.Policy!.CycleMode == CycleMode.Correlated)
        {
            cycle = run.Scope == TargetScope.Application ? run : Runs.Single(r => r.Id == run.ApplicationRunId);
            openOrders.Clear();
            foreach (var order in Runs.Where(r => r.ApplicationRunId == cycle.Id && r.Lifecycle == RunLifecycle.Running)) openOrders.Add(order.Identifier!, order);
        }
        if (run.Scope == TargetScope.Order)
        {
            Finalize(run, DetectionResult.Undefined, "OrderFallbackDeadline", entry, null, decisions);
            openOrders.Remove(run.Identifier!);
        }
        else CloseIncompleteCycle(entry, decisions, "ApplicationFallbackDeadline");
        foreach (var completed in Runs.Where(r => r.PolicyContext?.CompletedSequence == commandSequence))
        {
            completed.PolicyContext = completed.PolicyContext! with { CompletionTimeKind = "Deadline" };
            completed.EvidenceLines.Remove(commandSequence);
        }
        return decisions;
    }
}
