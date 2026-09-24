using System.Diagnostics;
using System.Text;
using Sonda.Access;
using Sonda.Application.Simulation;

namespace Sonda.Server.Profiles;

public sealed class SimulationProcess(IConfiguration configuration)
{
    private readonly SemaphoreSlim slot = new(1, 1);
    public async Task Publish(Func<Task> action, CancellationToken ct)
    {
        if (!await slot.WaitAsync(0, ct)) throw new AccessFault(429, "simulation_busy");
        try { await action(); }
        finally { slot.Release(); }
    }
    public async Task<string> Run(SimulationRequest request, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(request, new System.Text.Json.JsonSerializerOptions(SimulationJson.Options)
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        if (request.Entries.Count > 1000 || Encoding.UTF8.GetByteCount(json) > 1048576) throw new AccessFault(413, "simulation_limit");
        if (!await slot.WaitAsync(0, ct)) throw new AccessFault(429, "simulation_busy");
        var root = Path.Combine(Path.GetTempPath(), "sonda-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executable = configuration["Simulation:DotnetExecutable"] ?? throw new AccessFault(503, "simulation_host_unconfigured");
            var assembly = configuration["Simulation:Assembly"] ?? throw new AccessFault(503, "simulation_host_unconfigured");
            Directory.CreateDirectory(root);
            var input = Path.Combine(root, "sample.json"); var output = Path.Combine(root, "report");
            await File.WriteAllTextAsync(input, json, new UTF8Encoding(false), ct);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            start.ArgumentList.Add(assembly); start.ArgumentList.Add("--input"); start.ArgumentList.Add(input); start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
            // Do not inherit database credentials, host configuration or account secrets.
            start.Environment.Clear();
            foreach (var key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT" })
                if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
            using var process = Process.Start(start) ?? throw new AccessFault(503, "simulation_start_failed");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); throw new AccessFault(422, "simulation_time_limit"); }
            if (process.ExitCode is not (0 or 2)) throw new AccessFault(422, "simulation_failed");
            var path = Path.Combine(output, "report.json");
            if (new FileInfo(path).Length > 16777216) throw new AccessFault(422, "simulation_report_limit");
            return await File.ReadAllTextAsync(path, ct);
        }
        finally
        {
            // Only this generated child of the OS temp directory is removed.
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var cleanupTarget = Path.GetFullPath(root);
            if (!cleanupTarget.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(cleanupTarget).StartsWith("sonda-preview-", StringComparison.Ordinal)) throw new InvalidOperationException("Preview cleanup containment failed.");
            try { if (Directory.Exists(cleanupTarget)) Directory.Delete(cleanupTarget, recursive: true); }
            finally { slot.Release(); }
        }
    }
}
