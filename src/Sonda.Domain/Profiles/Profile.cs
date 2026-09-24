namespace Sonda.Domain.Profiles;

public enum PatternKind { Contains, Exact, Regex }
public enum RuleRole { CycleBegin, CycleSuccess, CycleFailure, CycleEnd, OrderBegin, OrderSuccess, OrderFailure, Detection }
public enum TargetScope { Application, Order }
public enum Classification { Error, Warning, Ignore }
public enum CompletionMode { TerminalMarker, ExplicitEnd }
public enum RecoveryPolicy { ManualOnly, NextSuccessfulRun }
public enum IdentifierKind { KeyValue, RegexCapture }

public sealed record PatternAlternative
{
    public PatternKind Kind { get; init; } = PatternKind.Contains;
    public string Expression { get; init; } = "";
    public bool CaseSensitive { get; init; }
}

public sealed record Rule
{
    public string Key { get; init; } = "";
    public RuleRole Role { get; init; }
    public TargetScope Target { get; init; }
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; }
    public Classification? Classification { get; init; }
    public string ConditionKey { get; init; } = "";
    public RecoveryPolicy Recovery { get; init; } = RecoveryPolicy.ManualOnly;
    public List<PatternAlternative> Alternatives { get; init; } = [];
}

public sealed record ParsingOptions
{
    // Empty pattern means the entire physical sample line is the message.
    public string EntryPattern { get; init; } = "";
    public string TimestampFormat { get; init; } = "";
    public bool TimestampHasDate { get; init; } = true;
    public bool TimestampHasOffset { get; init; }
    public string SourceTimeZoneId { get; init; } = "UTC";
}

public sealed record IdentifierOptions
{
    public IdentifierKind Kind { get; init; } = IdentifierKind.KeyValue;
    public string Name { get; init; } = "Transaction ID";
    public string Namespace { get; init; } = "";
    public string Expression { get; init; } = "";
    public string CaptureName { get; init; } = "id";
    public bool CaseSensitive { get; init; }
}

public sealed record TimingOptions
{
    public TimeSpan? ExpectedDuration { get; init; }
    public TimeSpan? GracePeriod { get; init; }
    public TimeSpan? FallbackTimeout { get; init; }
    public bool Requested => ExpectedDuration is not null || GracePeriod is not null || FallbackTimeout is not null;
}

public sealed record Profile
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public InterpretationPolicy? Policy { get; init; }
    public string TeamId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string Id { get; init; } = "";
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "";
    public string StreamKey { get; init; } = "default";
    public CompletionMode CompletionMode { get; init; }
    public ParsingOptions Parsing { get; init; } = new();
    public IdentifierOptions? Identifier { get; init; }
    public Classification CycleFailureSeverity { get; init; } = Classification.Error;
    public Classification OrderFailureSeverity { get; init; } = Classification.Warning;
    public Classification UndefinedOrderSeverity { get; init; } = Classification.Warning;
    public TimingOptions CycleTiming { get; init; } = new();
    public TimingOptions OrderTiming { get; init; } = new();
    public List<Rule> Rules { get; init; } = [];
}
