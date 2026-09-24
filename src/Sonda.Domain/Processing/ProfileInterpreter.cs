using Sonda.Domain.Detection;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Domain.Processing;

public sealed record EntryInterpretation(IReadOnlyList<RuleMatch> Matches, IReadOnlyList<string> Actions,
    IReadOnlyList<ProblemDecision> ProblemDecisions, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>One serial Profile/stream. No clocks, files, database, or application-specific phrases.</summary>
public sealed partial class ProfileInterpreter
{
    private Profile profile;
    private readonly Func<string, string> nextId;
    public ProfileInterpreter(Profile profile, Func<string, string> nextId)
    {
        if(profile.Policy is not null)profile=CopyProfile(profile);
        this.profile = profile; this.nextId = nextId;
        activeProfile = profile; published.Add(profile.Version, profile);
    }
    private RunRecord? cycle;
    private readonly Dictionary<string, RunRecord> openOrders = new(StringComparer.Ordinal);
    private int cycleSequence;
    public List<RunRecord> Runs { get; } = [];
    public List<Incident> Incidents { get; } = [];
    public InterpreterState ExportState() => new(1, profile.Id, cycleSequence,
        Runs.Select(RunState.From).ToArray(), Incidents.Select(IncidentState.From).ToArray())
        { Versions = Revision2 ? new(activeProfile.Version, published.Values.OrderBy(p => p.Version).Select(CopyProfile).ToArray()) : null };

    public static ProfileInterpreter Restore(Profile profile, Func<string, string> nextId, InterpreterState state)
    {
        if (state.Format != 1 || state.ProfileId != profile.Id || state.CycleSequence < 0)
            throw new ArgumentException("Incompatible interpreter state.");
        if (state.Runs.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != state.Runs.Length ||
            state.Incidents.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != state.Incidents.Length ||
            state.Runs.Any(r => r.ProfileId != profile.Id || r.CycleSequence <= 0 || r.CycleSequence > state.CycleSequence ||
                (r.Lifecycle == RunLifecycle.Finalized) != (r.Result is not null && r.CompletedEventAt is not null && r.CompletedProcessedAt is not null)))
            throw new ArgumentException("Invalid run state.");
        var engine = new ProfileInterpreter(profile, nextId) { cycleSequence = state.CycleSequence };
        if (state.Versions is { } versions)
        {
            engine.published.Clear();
            foreach (var version in versions.Published) engine.published.Add(version.Version, CopyProfile(version));
            engine.activeProfile = engine.published[versions.ActiveVersion];
        }
        engine.Runs.AddRange(state.Runs.Select(r => r.Restore()));
        engine.Incidents.AddRange(state.Incidents.Select(i => i.Restore()));
        engine.cycle = profile.Policy?.CycleMode == CycleMode.Correlated ? null : engine.Runs.SingleOrDefault(r => r.Scope == TargetScope.Application && r.Lifecycle == RunLifecycle.Running);
        foreach (var order in engine.Runs.Where(r => r.Scope == TargetScope.Order && r.Lifecycle == RunLifecycle.Running))
        {
            if (profile.Policy?.CycleMode == CycleMode.Correlated)
            {
                if (!engine.Runs.Any(r => r.Id == order.ApplicationRunId && r.Lifecycle == RunLifecycle.Running)) throw new ArgumentException("Open order has no open parent.");
                continue;
            }
            if (engine.cycle is null || order.ApplicationRunId != engine.cycle.Id || order.Identifier is null)
                throw new ArgumentException("An open order requires its open parent.");
            engine.openOrders.Add(order.Identifier, order);
        }
        if (engine.Incidents.Any(i => i.Problem.TeamId != profile.TeamId || i.Problem.ApplicationId != profile.ApplicationId || i.Problem.ProfileId != profile.Id))
            throw new ArgumentException("Incident ownership differs from Profile.");
        return engine;
    }
    public ApplicationHealth? CurrentHealth =>
        Incidents.Any(i => i.Status != IncidentStatus.Resolved && i.Severity == Classification.Error) ? ApplicationHealth.Error
        : Incidents.Any(i => i.Status != IncidentStatus.Resolved) ? ApplicationHealth.Warning
        : Runs.Any(r => r.Result is not null) ? ApplicationHealth.Stable : null;

    public EntryInterpretation Apply(ParsedEntry entry, List<RuleMatch> matches)
    {
        if (profile.Policy is { Revision: not 2 }) throw new InterpretationException("UnsupportedPolicyRevision", "Unknown policy revision.");
        List<string> actions = [];
        List<ProblemDecision> decisions = [];
        List<Diagnostic> warnings = [];
        var winners = SelectDetections(entry, matches, warnings);
        bool Has(RuleRole role) => matches.Any(m => m.Role == role);
        if (Revision2)
        {
            SelectPartition(entry, matches);
            var detached = HandleDetachedEvidence(entry, matches, winners, warnings);
            if (detached is not null) return detached;
        }
        Preflight(entry, matches, winners);

        if (Revision2 && Has(RuleRole.CycleBegin) && cycle is not null)
            CloseIncompleteCycle(entry, decisions, "SerialBeginBoundary");

        if (Has(RuleRole.CycleBegin))
        {
            cycle = NewRun(entry, TargetScope.Application, null);
            actions.Add($"CycleStarted:{cycle.Id}");
        }
        if (cycle is null)
        {
            warnings.Add(new("UnmatchedEvidence", DiagnosticLevel.Warning, "No active cycle; evidence preserved without an invented run.", profile.Id, entry.Line));
            return new(matches, actions, decisions, warnings);
        }
        AddEvidence(cycle, entry.Line);
        if (Has(RuleRole.OrderBegin))
        {
            var order = NewRun(entry, TargetScope.Order, entry.Identifier!);
            openOrders.Add(entry.Identifier!, order);
            actions.Add($"OrderStarted:{order.Id}");
        }
        RunRecord? currentOrder = null;
        if (entry.Identifier is not null && openOrders.TryGetValue(entry.Identifier, out currentOrder)) AddEvidence(currentOrder, entry.Line);
        foreach (var winner in winners.Values)
        {
            if (winner.Classification == Classification.Ignore)
            {
                actions.Add($"Ignored:{winner.Key}");
                if (Revision2 && (winner.Target == TargetScope.Application ? Has(RuleRole.CycleFailure) : Has(RuleRole.OrderFailure)))
                    warnings.Add(new("IgnoreDoesNotSuppressStructuralFailure", DiagnosticLevel.Warning, "Diagnostic Ignore does not suppress an independent structural failure.", profile.Id, entry.Line));
                continue;
            }
            var target = winner.Target == TargetScope.Application ? cycle : currentOrder!;
            var problem = ProblemIdentity.For(profile, winner.Target, target.Identifier, winner.ConditionKey);
            // A condition shared with the structural outcome is coalesced at finalization.
            var outcomeOnThisLine = winner.Target == TargetScope.Order ? Has(RuleRole.OrderFailure) : Has(RuleRole.CycleFailure);
            var outcomeKey = winner.Target == TargetScope.Order ? "order-outcome" : "cycle-outcome";
            if (!(outcomeOnThisLine && winner.ConditionKey == outcomeKey))
            {
                AddProblem(target, problem, winner.Classification!.Value, winner.Recovery, entry, decisions);
                if (!target.DiagnosticProblemKeys.Contains(problem.IncidentKey)) target.DiagnosticProblemKeys.Add(problem.IncidentKey);
            }
        }
        if (currentOrder is not null && (Has(RuleRole.OrderSuccess) || Has(RuleRole.OrderFailure)))
        {
            Finalize(currentOrder, Has(RuleRole.OrderSuccess) ? DetectionResult.Success : DetectionResult.Failure,
                "ConfiguredOrderOutcome", entry, winners.GetValueOrDefault(TargetScope.Order), decisions);
            openOrders.Remove(currentOrder.Identifier!);
            actions.Add($"OrderFinalized:{currentOrder.Id}:{currentOrder.Result}");
        }
        cycle.ObservedSuccess |= Has(RuleRole.CycleSuccess);
        cycle.ObservedFailure |= Has(RuleRole.CycleFailure);
        if (Revision2 && profile.CompletionMode == CompletionMode.ExplicitEnd && Has(RuleRole.CycleFailure))
            AddProblem(cycle, ProblemIdentity.For(profile, TargetScope.Application, null, "cycle-outcome"),
                profile.CycleFailureSeverity, RecoveryPolicy.NextSuccessfulRun, entry, decisions);
        var closes = profile.CompletionMode == CompletionMode.TerminalMarker
            ? Has(RuleRole.CycleSuccess) || Has(RuleRole.CycleFailure) || Has(RuleRole.CycleEnd)
            : Has(RuleRole.CycleEnd);
        if (closes)
        {
            // Preflight guarantees a single known application outcome, without policy guesses.
            var result = Revision2
                ? cycle.ObservedFailure ? DetectionResult.Failure : cycle.ObservedSuccess ? DetectionResult.Success : DetectionResult.Undefined
                : cycle.ObservedSuccess ? DetectionResult.Success : DetectionResult.Failure;
            foreach (var order in openOrders.Values.ToArray())
            {
                Finalize(order, DetectionResult.Undefined, "ParentCycleClosedWithoutOrderOutcome", entry, null, decisions);
                actions.Add($"OrderFinalized:{order.Id}:Undefined");
            }
            openOrders.Clear();
            Finalize(cycle, result, "ConfiguredCycleCompletion", entry, winners.GetValueOrDefault(TargetScope.Application), decisions);
            actions.Add($"CycleFinalized:{cycle.Id}:{cycle.Result}");
            cycle = null;
        }
        return new(matches, actions, decisions, warnings);
    }

    private Dictionary<TargetScope, Rule> SelectDetections(ParsedEntry entry, List<RuleMatch> matches, List<Diagnostic> warnings)
    {
        Dictionary<TargetScope, Rule> winners = [];
        foreach (var group in matches.Where(m => m.Role == RuleRole.Detection).GroupBy(m => m.Target).ToArray())
        {
            var ordered = group.OrderByDescending(m => m.Priority).ToArray();
            if (ordered.Length > 1) warnings.Add(new("RuleOverlap", DiagnosticLevel.Warning,
                $"Matching rules [{string.Join(", ", ordered.Select(m => m.RuleKey))}] compete on {group.Key}.", profile.Id, entry.Line));
            if (ordered.GroupBy(m => m.Priority).Any(g => g.Count() > 1))
            {
                foreach (var match in ordered) matches[matches.IndexOf(match)] = match with { Selection = "Ambiguous" };
                throw new InterpretationException("EqualPriorityOverlap", $"Matching rules share priority for this input/{group.Key}; no winner selected.");
            }
            foreach (var match in ordered) matches[matches.IndexOf(match)] = match with { Selection = match == ordered[0] ? "Winner" : "Suppressed" };
            winners[group.Key] = profile.Rules.Single(r => r.Key == ordered[0].RuleKey);
        }
        return winners;
    }

    private void Preflight(ParsedEntry entry, List<RuleMatch> matches, Dictionary<TargetScope, Rule> winners)
    {
        bool Has(RuleRole role) => matches.Any(m => m.Role == role);
        void Policy(string reason) => throw new InterpretationException("PolicyDecisionRequired", reason);
        if (Revision2 && (Has(RuleRole.CycleBegin) || Has(RuleRole.OrderBegin)) && profile.Policy!.DeadlineClock == DeadlineClock.Event &&
            entry.TimestampQuality != "Parsed" && (profile.CycleTiming.Requested || profile.OrderTiming.Requested))
            throw new InterpretationException("MissingDeadlineAnchor", "Event-clock timing requires a parsed source instant; select processing clock explicitly if needed.");
        if (Has(RuleRole.CycleBegin) && cycle is not null &&
            (!Revision2 || profile.Policy!.UnexpectedBegin != UnexpectedBeginPolicy.CloseIncompleteAndStartNew)) Policy("Overlapping cycles need an approved correlation policy.");
        if (Has(RuleRole.CycleBegin) && matches.Any(m => m.Role is RuleRole.CycleSuccess or RuleRole.CycleFailure or RuleRole.CycleEnd))
            Policy("A combined Begin/terminal line needs an approved policy.");
        if (Has(RuleRole.CycleSuccess) && Has(RuleRole.CycleFailure)) Policy("Conflicting cycle outcomes.");
        if (Has(RuleRole.OrderSuccess) && Has(RuleRole.OrderFailure)) Policy("Conflicting order outcomes.");
        var anyOrderRule = matches.Any(m => m.Target == TargetScope.Order);
        if (anyOrderRule && string.IsNullOrEmpty(entry.Identifier)) throw new InterpretationException("MissingIdentifier", "Order rule matched without a configured identifier.");
        if (cycle is null && !Has(RuleRole.CycleBegin))
        {
            if (matches.Count > 0) Policy("Structural or diagnostic input outside a known cycle is not interpreted in Phase 1.");
            return;
        }
        var active = entry.Identifier is null ? null : openOrders.GetValueOrDefault(entry.Identifier);
        if (Has(RuleRole.OrderBegin) && cycle is not null && Runs.Any(r => r.ApplicationRunId == cycle.Id && r.Identifier == entry.Identifier && (!Revision2 || r.Lifecycle == RunLifecycle.Running)))
            Policy("Repeated Begin or same-cycle retry requires an approved attempt policy.");
        if (anyOrderRule && active is null && !Has(RuleRole.OrderBegin))
            throw new InterpretationException("UnmatchedOrder", "Order outcome/detection has no open attempt; no attempt was invented.");
        var observedSuccess = (cycle?.ObservedSuccess ?? false) || Has(RuleRole.CycleSuccess);
        var observedFailure = (cycle?.ObservedFailure ?? false) || Has(RuleRole.CycleFailure);
        if (!Revision2 && observedSuccess && observedFailure) Policy("Both cycle success and failure evidence need an explicit outcome policy.");
        var closes = profile.CompletionMode == CompletionMode.TerminalMarker
            ? Has(RuleRole.CycleSuccess) || Has(RuleRole.CycleFailure) || Has(RuleRole.CycleEnd) : Has(RuleRole.CycleEnd);
        if (!Revision2 && closes && !observedSuccess && !observedFailure) Policy("Incomplete application-cycle outcome needs an approved policy.");
        if (!Revision2 && profile.CompletionMode == CompletionMode.ExplicitEnd && Has(RuleRole.CycleFailure) && !Has(RuleRole.CycleEnd))
            Policy("Immediate incidents before explicit End are deferred; evidence is preserved for policy review.");
        foreach (var scope in new[] { TargetScope.Application, TargetScope.Order })
        {
            var win = winners.GetValueOrDefault(scope);
            var target = scope == TargetScope.Application ? cycle : active;
            var success = scope == TargetScope.Application ? closes && observedSuccess : Has(RuleRole.OrderSuccess);
            var failure = scope == TargetScope.Application ? Has(RuleRole.CycleFailure) : Has(RuleRole.OrderFailure);
            if (!Revision2 && failure && win?.Classification == Classification.Ignore) Policy("Winning Ignore with independent structural Failure needs policy review.");
            if (!Revision2 && success && ((target?.DiagnosticProblemKeys.Count ?? 0) > 0 || win?.Classification is Classification.Error or Classification.Warning))
                Policy("Terminal Success with its own diagnostic problem has an unresolved health policy.");
            if (target is not null && entry.EventAt < target.StartedEventAt && (success || failure || closes))
                throw new InterpretationException("NonMonotonicEventTime", "Completion precedes its run's start; evidence retained without a negative duration.");
        }
        if (closes && cycle is not null && openOrders.Values.Any(r => entry.EventAt < r.StartedEventAt))
            throw new InterpretationException("NonMonotonicEventTime", "Parent completion precedes an open order's start.");
    }

    private RunRecord NewRun(ParsedEntry entry, TargetScope scope, string? identifier)
    {
        if (scope == TargetScope.Application) cycleSequence++;
        var run = new RunRecord
        {
            Id = nextId(scope == TargetScope.Application ? "cycle" : "order"),
            ProfileId = profile.Id,
            Scope = scope,
            CycleSequence = scope == TargetScope.Order ? cycle!.CycleSequence : cycleSequence,
            ApplicationRunId = scope == TargetScope.Order ? cycle!.Id : null,
            Identifier = identifier,
            StartedEventAt = entry.EventAt,
            AttemptNumber = Revision2 && scope == TargetScope.Order ? Runs.Count(r => r.ApplicationRunId == cycle!.Id && r.Identifier == identifier) + 1 : 1
        };
        if (Revision2) run.PolicyContext = CreateRunContext(run, entry);
        AddEvidence(run, entry.Line);
        Runs.Add(run);
        return run;
    }

    private void Finalize(RunRecord run, DetectionResult result, string reason, ParsedEntry entry, Rule? winner, List<ProblemDecision> decisions)
    {
        run.Lifecycle = RunLifecycle.Finalized;
        run.Result = result;
        run.CompletedEventAt = entry.EventAt;
        run.CompletedProcessedAt = entry.ProcessedAt;
        run.TerminalReason = reason;
        if (run.PolicyContext is { } context) run.PolicyContext = context with { CompletedSequence = entry.Line };
        if (entry.TimestampQuality != "SyntheticDeadline") AddEvidence(run, entry.Line);
        if (result != DetectionResult.Success)
        {
            var condition = run.Scope == TargetScope.Order ? "order-outcome" : "cycle-outcome";
            var severity = result == DetectionResult.Undefined ? Revision2 && run.Scope == TargetScope.Application ? profile.Policy!.UndefinedApplicationSeverity : profile.UndefinedOrderSeverity
                : run.Scope == TargetScope.Order ? profile.OrderFailureSeverity : profile.CycleFailureSeverity;
            if (winner?.ConditionKey == condition && winner.Classification is Classification.Error or Classification.Warning)
                severity = winner.Classification.Value;
            AddProblem(run, ProblemIdentity.For(profile, run.Scope, run.Identifier, condition), severity, RecoveryPolicy.NextSuccessfulRun, entry, decisions);
        }
        else Recover(run, entry, decisions);
    }

    private void AddProblem(RunRecord run, ProblemIdentity key, Classification severity, RecoveryPolicy recovery,
        ParsedEntry entry, List<ProblemDecision> decisions)
    {
        var incident = Incidents.SingleOrDefault(i => i.Problem == key && i.Status != IncidentStatus.Resolved);
        var created = incident is null;
        if (incident is null)
        {
            incident = new Incident { Id = nextId("incident"), Problem = key, RecoveryPolicy = recovery, Severity = severity };
            if (Revision2) incident.PolicyContext = new(Incidents.Count(i => i.Problem == key) + 1, profile.Version, profile.Policy!.RecoveryCompatibility);
            incident.History.Add(new(null, IncidentStatus.Active, entry.ProcessedAt, "First occurrence"));
            Incidents.Add(incident);
        }
        if (severity == Classification.Error) incident.Severity = severity;
        if (!created && incident.PolicyContext is { } revision) incident.PolicyContext = revision with { Revision = revision.Revision + 1 };
        var existing = incident.Occurrences.FindIndex(o => o.RunId == run.Id);
        var occurrence = new Occurrence(existing >= 0 ? incident.Occurrences[existing].Id : nextId("occurrence"), run.Id,
            run.ApplicationRunId ?? run.Id, run.CycleSequence, run.Result, severity, entry.EventAt, run.EvidenceLines.ToArray());
        if (existing < 0) incident.Occurrences.Add(occurrence);
        else
        {
            var old = incident.Occurrences[existing];
            incident.Occurrences[existing] = occurrence with
            {
                Severity = old.Severity == Classification.Error ? old.Severity : severity,
                DetectedAt = old.DetectedAt,
                EvidenceLines = old.EvidenceLines.Union(run.EvidenceLines).Order().ToArray()
            };
        }
        decisions.Add(new(entry.Line, key.IncidentKey, incident.Id, created ? "CreatedIncident" : existing < 0 ? "AppendedOccurrence" : "AddedEvidence", run.Id));
    }

    private void Recover(RunRecord successful, ParsedEntry entry, List<ProblemDecision> decisions)
    {
        if (Revision2) { RecoverRevision2(successful, entry, decisions); return; }
        foreach (var incident in Incidents.Where(i => i.Status != IncidentStatus.Resolved && i.RecoveryPolicy == RecoveryPolicy.NextSuccessfulRun))
        {
            var key = incident.Problem;
            if (key.Scope != successful.Scope || key.Identifier != successful.Identifier ||
                incident.Occurrences.Any(o => o.CycleSequence >= successful.CycleSequence)) continue;
            incident.RecoveryEvents.Add(new(successful.Id, successful.ApplicationRunId ?? successful.Id,
                successful.CycleSequence, entry.ProcessedAt, "Automatic", successful.EvidenceLines.ToArray()));
            incident.History.Add(new(incident.Status, IncidentStatus.Resolved, entry.ProcessedAt, "Later configured successful run"));
            incident.Status = IncidentStatus.Resolved;
            decisions.Add(new(entry.Line, key.IncidentKey, incident.Id, "ResolvedBySuccessfulRun", successful.Id));
        }
    }

    private static void AddEvidence(RunRecord run, int line)
    {
        if (!run.EvidenceLines.Contains(line)) run.EvidenceLines.Add(line);
    }
}
