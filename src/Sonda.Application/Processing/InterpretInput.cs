using System.Text.RegularExpressions;
using Sonda.Application.Simulation;
using Sonda.Domain.Detection;
using Sonda.Domain.Evidence;
using Sonda.Domain.Processing;
using Sonda.Domain.Profiles;

namespace Sonda.Application.Processing;

public static class InterpretInput
{
    public static EntryTrace Apply(Profile profile, ProfileInterpreter engine, SampleEntry sample, int line, DateOnly? date)
    {
        var trace = new EntryTrace { Line = line, ProfileId = sample.ProfileId, Raw = sample.Raw };
        try
        {
            trace.Parsed = SampleParser.Parse(profile, sample, line, date);
            trace.Matches = PatternMatcher.Match(profile, trace.Parsed.Message);
            var result = engine.Apply(trace.Parsed, trace.Matches);
            trace.Actions = result.Actions.ToList(); trace.ProblemDecisions = result.ProblemDecisions.ToList();
            trace.Diagnostics.AddRange(result.Diagnostics);
            trace.Disposition = result.Actions.Count > 0 ? "Applied" : "EvidenceOnly";
        }
        catch (Exception error) when (error is InterpretationException or RegexMatchTimeoutException or ArgumentException)
        {
            var code = error is InterpretationException interpretation ? interpretation.Code
                : error is RegexMatchTimeoutException ? "RegexTimeout" : "InvalidInput";
            trace.Diagnostics.Add(new(code, DiagnosticLevel.Error, error.Message, sample.ProfileId, line));
            trace.Disposition = "Quarantined";
        }
        return trace;
    }
}
