using System.Globalization;
using System.Text.RegularExpressions;
using Sonda.Domain.Detection;
using Sonda.Domain.Profiles;

namespace Sonda.Domain.Evidence;

public static class SampleParser
{
    public static ParsedEntry Parse(Profile profile, SampleEntry sample, int line, DateOnly? sampleDate)
    {
        if (sample.Raw.Length > PatternMatcher.MaxInputLength) throw new InterpretationException("InputLimit", "Sample line exceeds the size limit.");
        var message = sample.Raw;
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        var eventAt = sample.ProcessedAt;
        var quality = "ProcessingTimeFallback";
        if (profile.Parsing.EntryPattern.Length > 0)
        {
            var regex = PatternMatcher.Regex(profile.Parsing.EntryPattern);
            var match = regex.Match(sample.Raw);
            if (!match.Success || match.Index != 0 || match.Length != sample.Raw.Length || !match.Groups["message"].Success)
                throw new InterpretationException("ParseFailed", "The configured parser must match the complete entry and capture message.");
            foreach (var name in regex.GetGroupNames().Where(n => !int.TryParse(n, out _)))
                if (match.Groups[name].Success) fields[name] = match.Groups[name].Value;
            message = fields["message"];
            if (fields.TryGetValue("timestamp", out var timestamp))
            {
                eventAt = Timestamp(profile.Parsing, timestamp, sampleDate);
                quality = "Parsed";
            }
        }
        return new(line, profile.Id, sample.Raw, message, eventAt, sample.ProcessedAt, quality, fields,
            profile.Identifier is null ? null : Extract(profile.Identifier, message));
    }

    private static DateTimeOffset Timestamp(ParsingOptions options, string text, DateOnly? sampleDate)
    {
        if (string.IsNullOrWhiteSpace(options.TimestampFormat)) throw new InterpretationException("TimestampFormatRequired", "Captured timestamp requires an explicit .NET format.");
        if (options.TimestampHasOffset)
        {
            if (!options.TimestampHasDate || !DateTimeOffset.TryParseExact(text, options.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
                throw new InterpretationException("InvalidTimestamp", "Offset timestamps require a complete date and configured format.");
            return offset.ToUniversalTime();
        }
        DateTime local;
        if (!options.TimestampHasDate)
        {
            if (sampleDate is null || !TimeOnly.TryParseExact(text, options.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new InterpretationException("DateContextRequired", "Time-only timestamps require an explicit sample date and valid time format.");
            local = sampleDate.Value.ToDateTime(time);
        }
        else if (!DateTime.TryParseExact(text, options.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
            throw new InterpretationException("InvalidTimestamp", "Timestamp does not match the configured format.");
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.SourceTimeZoneId);
        if (zone.IsAmbiguousTime(local) || zone.IsInvalidTime(local))
            throw new InterpretationException("AmbiguousTimestamp", "Timestamp is ambiguous or nonexistent in the configured source timezone.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    private static string? Extract(IdentifierOptions options, string message)
    {
        var expression = options.Kind == IdentifierKind.KeyValue
            ? @"(?<![\w])" + Regex.Escape(options.Expression) + @"=(?<id>[^\s;,]+)"
            : options.Expression;
        var captureName = options.Kind == IdentifierKind.KeyValue ? "id" : options.CaptureName;
        var matches = PatternMatcher.Regex(expression, options.CaseSensitive).Matches(message);
        var values = matches.SelectMany(m => m.Groups[captureName].Captures.Select(c => c.Value))
            .Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > 1) throw new InterpretationException("AmbiguousIdentifier", "Multiple different identifiers were extracted from one message.");
        return values.SingleOrDefault();
    }
}
