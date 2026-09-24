namespace Sonda.Domain.Profiles;

public enum CycleMode { Serial, Correlated }
public enum UnexpectedBeginPolicy { RejectAmbiguousBegin, CloseIncompleteAndStartNew }
public enum DeadlineClock { Event, Processing }

/// <summary>Absent on legacy snapshots. Every new behavior requires explicit revision 2.</summary>
public sealed record InterpretationPolicy
{
    public int Revision { get; init; } = 2;
    public Classification UndefinedApplicationSeverity { get; init; } = Classification.Error;
    public CycleMode CycleMode { get; init; }
    public UnexpectedBeginPolicy UnexpectedBegin { get; init; }
    public DeadlineClock DeadlineClock { get; init; }
    public bool OrderFallbackEnabled { get; init; }
    public string CorrelationExpression { get; init; } = "";
    public string CorrelationCapture { get; init; } = "cycle";
    public string CorrelationEpoch { get; init; } = "";
    public string RoutingContract { get; init; } = "";
    public string RecoveryCompatibility { get; init; } = "";
    public string[] ApplicationWideRules { get; init; } = [];
    public string[] LateObservationRules { get; init; } = [];
    public TimeSpan? ReadFreshness { get; init; }
    public TimeSpan? RunCadence { get; init; }
}
