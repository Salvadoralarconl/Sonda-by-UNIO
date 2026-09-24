using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed partial class PersistenceTests
{
    [Fact]
    public async Task Dump_restore_preserves_open_state_receipts_and_recovery()
    {
        var (_, scope, inputs) = await Setup("repeated-recovery"); await Apply(inputs.Take(inputs.Length - 2));
        var before = await new PostgresProcessingStore(pg.Connection).ReadStateAsync(scope, "cam");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var bin = Environment.GetEnvironmentVariable("SONDA_PG_BIN") ?? Path.Combine(root, ".tools", "postgresql", "pgsql", "bin");
        var path = Path.Combine(root, "artifacts", "test-results", $"restore-{Guid.NewGuid():N}.dump"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var restoreName = "sonda_restore_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnection);
        await Run("pg_dump", builder, ["--format=custom", "--no-owner", "--no-privileges", "--file", path]);
        await using var admin = new NpgsqlConnection(pg.OwnerConnection); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE " + restoreName, admin).ExecuteNonQueryAsync();
        builder.Database = restoreName;
        try
        {
            await Run("pg_restore", builder, ["--no-owner", "--no-privileges", "--dbname", restoreName, path]);
            var restored = new PostgresProcessingStore(builder.ConnectionString);
            Assert.Equal(SimulationJson.Serialize(before), SimulationJson.Serialize(await restored.ReadStateAsync(scope, "cam")));
            Assert.True((await restored.ProcessAsync(inputs[0])).Replayed);
            foreach (var input in inputs.TakeLast(2)) await restored.ProcessAsync(input);
            var after = await restored.ReadStateAsync(scope, "cam"); Assert.Single(after.Interpreter.Incidents[0].Recoveries); Assert.Equal(Sonda.Domain.Incidents.IncidentStatus.Resolved, after.Interpreter.Incidents[0].Status);
        }
        finally { NpgsqlConnection.ClearAllPools(); await new NpgsqlCommand("DROP DATABASE " + restoreName + " WITH (FORCE)", admin).ExecuteNonQueryAsync(); }
        async Task Run(string name, NpgsqlConnectionStringBuilder settings, string[] arguments)
        {
            var executable = Path.Combine(bin, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in arguments) start.ArgumentList.Add(arg);
            start.Environment["PGHOST"] = settings.Host; start.Environment["PGPORT"] = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture); start.Environment["PGUSER"] = settings.Username; start.Environment["PGPASSWORD"] = settings.Password; start.Environment["PGDATABASE"] = settings.Database;
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, $"{name} failed: {await errors}; {await output}");
        }
    }
}
