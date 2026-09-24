using System.Globalization;
using System.Text;

namespace Sonda.Application.Simulation;

public static class MarkdownReport
{
    private static string Safe(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("|", "&#124;")
        .Replace("`", "&#96;").Replace("[", "&#91;").Replace("]", "&#93;")
        .Replace("\r", "").Replace("\n", "<br>") ?? "—";

    public static string Render(SimulationReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# SONDA Profile simulation");
        text.AppendLine();
        text.AppendLine($"Interpretation complete: **{report.Complete}**. Incomplete reports contain partial facts only; do not treat them as validated results.");
        text.AppendLine($"Engine: {Safe(report.Provenance.EngineVersion)}. Server timezone: {Safe(report.Provenance.ServerTimeZoneId)}. As of: {report.Provenance.AsOf:O}.");
        text.AppendLine($"Profile hash: `{report.Provenance.ProfileHash}`. Sample hash: `{report.Provenance.SampleHash}`.");
        text.AppendLine($"Request hash: `{report.Provenance.RequestHash}`. Report schema: {report.Provenance.ReportSchemaVersion}.");
        text.AppendLine();
        text.AppendLine("## Metrics (sample only)");
        text.AppendLine();
        if (report.Metrics is { } metrics)
        {
            text.AppendLine($"System Health: **{Safe(metrics.SystemHealthPercentage)}%** ({metrics.SuccessfulEvaluatedRuns}/{metrics.TotalEvaluatedRuns} evaluated runs). Null means no evaluated runs.");
            text.AppendLine($"Logs Today / completedOrderRunsProcessedToday: **{metrics.CompletedOrderRunsProcessedToday}**.");
            text.AppendLine();
            text.AppendLine("| Server date | Completed Order Runs processed |\n| --- | ---: |");
            foreach (var bucket in metrics.FiveDayHistory) text.AppendLine($"| {bucket.Date:yyyy-MM-dd} | {bucket.CompletedOrderRunsProcessedCount} |");
            text.AppendLine("Sample buckets describe supplied evidence only; zeros do not prove production coverage.");
        }
        text.AppendLine();
        text.AppendLine("## Application and Order Runs");
        text.AppendLine();
        text.AppendLine("| Run | Scope | Parent | Identifier | Result | Evidence lines |\n| --- | --- | --- | --- | --- | --- |");
        foreach (var run in report.ApplicationRuns.Concat(report.OrderRuns))
            text.AppendLine($"| {Safe(run.Id)} | {run.Scope} | {Safe(run.ApplicationRunId)} | {Safe(run.Identifier)} | {Safe(run.Result?.ToString() ?? "Running")} | {string.Join(", ", run.EvidenceLines)} |");
        text.AppendLine();
        text.AppendLine("## Incidents and exact problem keys");
        foreach (var incident in report.Incidents)
        {
            text.AppendLine();
            text.AppendLine($"### {Safe(incident.Id)} — {incident.Severity} / {incident.Status}");
            text.AppendLine();
            text.AppendLine($"Problem key: {Safe(incident.IncidentKey)}");
            foreach (var occurrence in incident.Occurrences)
                text.AppendLine($"- Occurrence {Safe(occurrence.Id)}: run {Safe(occurrence.RunId)}, cycle {occurrence.CycleSequence}, {Safe(occurrence.Result)} / {occurrence.Severity}; evidence {string.Join(", ", occurrence.EvidenceLines)}.");
            foreach (var recovery in incident.RecoveryEvents)
                text.AppendLine($"- Recovery: {recovery.Method}, successful run {Safe(recovery.RunId)}, cycle {recovery.CycleSequence}; evidence {string.Join(", ", recovery.EvidenceLines)}.");
        }
        text.AppendLine();
        text.AppendLine("## Per-input explanation");
        foreach (var entry in report.Entries)
        {
            text.AppendLine();
            text.AppendLine($"### Line {entry.Line} — {Safe(entry.ProfileId)} — {entry.Disposition}");
            text.AppendLine();
            text.AppendLine($"Raw: {Safe(entry.Raw)}");
            if (entry.Parsed is { } parsed)
            {
                text.AppendLine($"Message: {Safe(parsed.Message)}; identifier: {Safe(parsed.Identifier)}; event: {parsed.EventAt:O}; processed: {parsed.ProcessedAt:O}; timestamp quality: {parsed.TimestampQuality}.");
                text.AppendLine($"Fields: {Safe(string.Join(", ", parsed.Fields.Select(f => f.Key + "=" + f.Value)))}");
            }
            foreach (var match in entry.Matches)
                text.AppendLine($"- Rule {Safe(match.RuleKey)}: {match.Role}/{match.Target}, priority {match.Priority}, {Safe(match.Classification)}, **{match.Selection}**, alternatives {string.Join(", ", match.Alternatives)}.");
            foreach (var action in entry.Actions) text.AppendLine($"- {Safe(action)}");
            foreach (var decision in entry.ProblemDecisions)
                text.AppendLine($"- **{decision.Action}** → {Safe(decision.IncidentId)}; problem key: {Safe(decision.IncidentKey)}; run {Safe(decision.RunId)}.");
        }
        text.AppendLine();
        text.AppendLine("## Diagnostics");
        text.AppendLine();
        foreach (var diagnostic in report.Diagnostics)
            text.AppendLine($"- {diagnostic.Level} **{diagnostic.Code}**, Profile {Safe(diagnostic.ProfileId)}, line {Safe(diagnostic.Line)}: {Safe(diagnostic.Message)}");
        text.AppendLine();
        text.AppendLine("## Rule coverage");
        text.AppendLine();
        foreach (var coverage in report.RuleCoverage)
            text.AppendLine($"- {Safe(coverage.ProfileId)}/{Safe(coverage.RuleKey)}: {coverage.MatchingEntries} matched input(s), {coverage.WinningEntries} classification selection(s).");
        text.AppendLine();
        text.AppendLine("## Current state");
        text.AppendLine();
        foreach (var state in report.CurrentStates)
            text.AppendLine($"- {Safe(state.ProfileId)}: {Safe(state.LastKnownBusinessHealth)}, interpretation complete: {state.InterpretationComplete}.");
        return text.ToString().Replace("\r\n", "\n");
    }
}
