using Sonda.Application.Simulation;
using Sonda.Domain.Availability;
using Sonda.Domain.Detection;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Processing;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;

namespace Sonda.Application.Processing;

public enum PolicyCommandKind { Evidence, AdvanceTime, ChangeStatus, ActivateVersion, SourceObservation }
public sealed record EvidenceFrontier(long ThroughSequence, DateTimeOffset CompleteThrough, int PendingEarlierInputs);
public sealed record PolicyCommand
{
    public Guid Id { get; init; }
    public int Sequence { get; init; }
    public PolicyCommandKind Kind { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
    public DateTimeOffset EffectiveAt { get; init; }
    public string Raw { get; init; } = "";
    public string EvidenceKey { get; init; } = "";
    public DateOnly? SampleDate { get; init; }
    public EvidenceFrontier? Frontier { get; init; }
    public string IncidentId { get; init; } = "";
    public long ExpectedRevision { get; init; }
    public IncidentStatus TargetStatus { get; init; }
    public string Actor { get; init; } = "";
    public string Reason { get; init; } = "";
    public Profile? Profile { get; init; }
    public string Source { get; init; } = "";
    public bool ReadSuccess { get; init; }
}
public sealed record PolicyReceipt(Guid Id, string Fingerprint, PolicyCommand Command, int ProfileVersion, string Disposition,
    EntryInterpretation? Interpretation, Diagnostic[] Diagnostics)
{
    public string? Partition { get; init; }
    public ParsedEntry? Normalized { get; init; }
}
public sealed record PolicySessionState(string Seed, long Counter, int NextSequence, DateTimeOffset LastProcessedAt,
    DateTimeOffset? Frontier, InterpreterState Interpreter, PolicyReceipt[] Receipts, SourceObservation[] Observations)
{
    public string[] BlockedPartitions { get; init; } = [];
}

/// <summary>Deterministic command adapter around ProfileInterpreter. Persistence owns its transaction and external receipt index.</summary>
public sealed class PolicySession
{
    private readonly string seed;
    private long counter;
    private int nextSequence = 1;
    private DateTimeOffset lastProcessed;
    private DateTimeOffset? frontier;
    private ProfileInterpreter engine;
    private readonly List<PolicyReceipt> receipts = [];
    private readonly List<SourceObservation> observations = [];
    private readonly HashSet<string> blocked = new(StringComparer.Ordinal);
    public ProfileInterpreter Engine => engine;
    public IReadOnlyList<SourceObservation> Observations => observations;
    public PolicySession(Profile profile, string seed)
    {
        if (profile.Policy?.Revision != 2 || ProfileValidator.Validate(profile).Any(d=>d.Level==DiagnosticLevel.Error))
            throw new ArgumentException("A validated explicit revision-2 Profile is required.");
        this.seed=seed; engine=new(profile,NextId);
    }
    private string NextId(string kind) => $"{seed}:{engine.ActiveProfile.Id}:{kind}:{++counter:D4}";
    public PolicySessionState Export() => new(seed,counter,nextSequence,lastProcessed,frontier,engine.ExportState(),receipts.ToArray(),observations.ToArray()) { BlockedPartitions = blocked.Order(StringComparer.Ordinal).ToArray() };
    public static PolicySession Restore(PolicySessionState state)
    {
        var versions=state.Interpreter.Versions ?? throw new ArgumentException("Missing pinned versions.");
        var active=versions.Published.Single(p=>p.Version==versions.ActiveVersion);
        var session=new PolicySession(active,state.Seed) {counter=state.Counter,nextSequence=state.NextSequence,lastProcessed=state.LastProcessedAt,frontier=state.Frontier};
        session.engine=ProfileInterpreter.Restore(active,session.NextId,state.Interpreter);
        session.receipts.AddRange(state.Receipts);session.observations.AddRange(state.Observations);session.blocked.UnionWith(state.BlockedPartitions);return session;
    }
    public PolicyReceipt Execute(PolicyCommand command)
    {
        command=SimulationJson.Deserialize<PolicyCommand>(SimulationJson.Serialize(command));
        var fingerprint=SimulationJson.Hash(command with {Id=Guid.Empty});
        var existing=receipts.SingleOrDefault(r=>r.Id==command.Id);
        if(existing is null && command.Kind==PolicyCommandKind.Evidence && command.EvidenceKey.Length>0)
            existing=receipts.FirstOrDefault(r=>r.Command.Kind==PolicyCommandKind.Evidence && r.Command.EvidenceKey==command.EvidenceKey);
        if(existing is not null)
        {
            if(existing.Fingerprint!=fingerprint) throw new InterpretationException("IdempotencyConflict","Command/evidence identity reused with different content.");
            if(!receipts.Any(r=>r.Id==command.Id)) receipts.Add(existing with {Id=command.Id});
            return existing;
        }
        if(command.Id==Guid.Empty || !Enum.IsDefined(command.Kind) || command.Sequence!=nextSequence || command.ProcessedAt==default || command.ProcessedAt<lastProcessed)
            throw new InterpretationException("CommandOrderConflict","Explicit ordered commands and nondecreasing processing time are required.");
        var before=Export(); EntryInterpretation? interpretation=null; List<Diagnostic> diagnostics=[]; var disposition="Applied";var version=engine.ActiveProfile.Version;
        string? partition=null;ParsedEntry? normalized=null;
        try
        {
            switch(command.Kind)
            {
                case PolicyCommandKind.Evidence:
                    if(string.IsNullOrWhiteSpace(command.EvidenceKey)) throw new InterpretationException("EvidenceIdentityRequired","Evidence needs a stable identity.");
                    var sample=new SampleEntry {ProfileId=engine.ActiveProfile.Id,Raw=command.Raw,ProcessedAt=command.ProcessedAt};
                    var routed=SampleParser.Parse(engine.ActiveProfile,sample,command.Sequence,command.SampleDate);
                    partition=engine.PartitionKey(routed);
                    if(blocked.Contains("*")||blocked.Contains(partition)){disposition="BlockedByPriorError";break;}
                    var selected=engine.SelectInputProfile(routed);version=selected.Version;
                    var parsed=SampleParser.Parse(selected,sample,command.Sequence,command.SampleDate);
                    normalized=parsed;
                    if(frontier is not null && parsed.EventAt<=frontier)
                    {
                        disposition="LateEvidence";interpretation=engine.ApplyLateObservation(parsed,PatternMatcher.Match(selected,parsed.Message));
                    }
                    else interpretation=engine.Apply(parsed,PatternMatcher.Match(selected,parsed.Message));
                    break;
                case PolicyCommandKind.AdvanceTime:
                    var f=command.Frontier;
                    if(f is null || f.ThroughSequence!=command.Sequence-1 || f.PendingEarlierInputs!=0 || f.CompleteThrough<command.EffectiveAt ||
                        command.EffectiveAt==default || command.EffectiveAt<frontier || command.EffectiveAt>command.ProcessedAt)
                        throw new InterpretationException("IncompleteFrontier","Consume the declared evidence frontier before advancing time.");
                    foreach(var run in engine.Runs.Where(r=>r.Lifecycle==RunLifecycle.Running && r.PolicyContext?.Deadline<=command.EffectiveAt)
                        .OrderBy(r=>r.PolicyContext!.Deadline).ThenBy(r=>r.Scope==TargetScope.Order?0:1).ThenBy(r=>r.Id,StringComparer.Ordinal).ToArray())
                        if(run.Lifecycle==RunLifecycle.Running && !blocked.Contains("*") && !blocked.Contains(run.PolicyContext?.CorrelationKey??"serial")) engine.ApplyDeadline(run.Id,run.PolicyContext!.Deadline!.Value,command.ProcessedAt,command.Sequence);
                    frontier=command.EffectiveAt;
                    break;
                case PolicyCommandKind.ChangeStatus:
                    if(!engine.ChangeIncidentStatus(command.IncidentId,command.ExpectedRevision,command.TargetStatus,command.Actor,command.Reason,command.ProcessedAt)) disposition="NoOp";
                    break;
                case PolicyCommandKind.ActivateVersion:
                    if(command.ExpectedRevision!=receipts.Count(r=>r.Command.Kind==PolicyCommandKind.ActivateVersion&&r.Disposition=="Applied"))
                        throw new InterpretationException("RevisionConflict","Activation revision changed.");
                    if(command.Profile is null) throw new InterpretationException("ProfileRequired","Activation requires a published simulated Profile.");
                    engine.Activate(command.Profile);version=command.Profile.Version;break;
                case PolicyCommandKind.SourceObservation:
                    if(string.IsNullOrWhiteSpace(command.Source) || command.EffectiveAt==default || command.EffectiveAt>command.ProcessedAt)
                        throw new InterpretationException("InvalidSourceObservation","Source observation must have explicit valid times and identity.");
                    observations.Add(new(command.Source,command.Sequence,command.EffectiveAt,command.ProcessedAt,command.ReadSuccess));break;
            }
        }
        catch (Exception e) when(e is InterpretationException or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            counter=before.Counter;
            var active=before.Interpreter.Versions!.Published.Single(p=>p.Version==before.Interpreter.Versions.ActiveVersion);
            engine=ProfileInterpreter.Restore(active,NextId,before.Interpreter);frontier=before.Frontier;
            observations.Clear();observations.AddRange(before.Observations);
            diagnostics.Add(new(e is InterpretationException ie?ie.Code:"InvalidInput",DiagnosticLevel.Error,e.Message));disposition="Rejected";
            if(command.Kind==PolicyCommandKind.Evidence)blocked.Add(partition??"*");
        }
        var result=new PolicyReceipt(command.Id,fingerprint,command,version,disposition,interpretation,diagnostics.ToArray()) { Partition=partition,Normalized=normalized };
        receipts.Add(result);nextSequence++;lastProcessed=command.ProcessedAt;return result;
    }
}
