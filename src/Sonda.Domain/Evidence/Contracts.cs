namespace Sonda.Domain.Evidence;

public enum DiagnosticLevel { Warning, Error }

public sealed record Diagnostic(string Code, DiagnosticLevel Level, string Message,
    string? ProfileId = null, int? Line = null);

public sealed record SampleEntry
{
    public string ProfileId { get; init; } = "";
    public string Raw { get; init; } = "";
    public DateTimeOffset ProcessedAt { get; init; }
}

public sealed record ParsedEntry(int Line, string ProfileId, string Raw, string Message,
    DateTimeOffset EventAt, DateTimeOffset ProcessedAt, string TimestampQuality,
    IReadOnlyDictionary<string, string> Fields, string? Identifier);

public sealed class InterpretationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
