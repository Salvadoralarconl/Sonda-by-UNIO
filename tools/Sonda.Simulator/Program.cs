using System.Text;
using System.Text.Json;
using Sonda.Application.Simulation;

if (args.Length != 4 || args[0] != "--input" || args[2] != "--output")
{
    Console.Error.WriteLine("Usage: Sonda.Simulator --input <sample.json> --output <report-directory>");
    return 64;
}

try
{
    var input = Path.GetFullPath(args[1]);
    var output = Path.GetFullPath(args[3]);
    if (new FileInfo(input).Length > 16 * 1024 * 1024) throw new ArgumentException("Sample JSON exceeds the 16 MiB limit.");
    var json = File.ReadAllText(input, new UTF8Encoding(false, true));
    using var document = JsonDocument.Parse(json);
    if(document.RootElement.TryGetProperty("commands",out _))
    {
        var policyRequest=SimulationJson.Deserialize<PolicySimulationRequest>(json);
        var policyReport=PolicySimulationRunner.Run(policyRequest);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"report.json"),SimulationJson.Serialize(policyReport),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output,"report.md"),$"# Revision-2 simulation\n\nComplete: {policyReport.Complete}\n\nEngine: {policyReport.EngineVersion}\n\nRuns: {policyReport.State.Interpreter.Runs.Length}; incidents: {policyReport.State.Interpreter.Incidents.Length}\n\nSystem Health: {policyReport.Metrics.SystemHealthPercentage}; completed orders today: {policyReport.Metrics.CompletedOrderRunsProcessedToday}\n\nAvailability: {policyReport.Availability.Availability}\n\nSee report.json for exact keys, commands, policy versions, evidence and workflow history.\n");
        Console.WriteLine($"Policy interpretation complete: {policyReport.Complete}; reports: {output}");return policyReport.Complete?0:2;
    }
    var request = SimulationJson.Deserialize<SimulationRequest>(json);
    var report = new SimulationRunner().Run(request);
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output, "report.json"), SimulationJson.Serialize(report), new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(output, "report.md"), MarkdownReport.Render(report), new UTF8Encoding(false));
    Console.WriteLine($"Interpretation complete: {report.Complete}; cycles: {report.ApplicationRuns.Count}; orders: {report.OrderRuns.Count}; incidents: {report.Incidents.Count}");
    Console.WriteLine($"Reports: {output}");
    return report.Complete ? 0 : 2;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
{
    Console.Error.WriteLine($"Invalid sample or output: {error.Message}");
    return 1;
}
