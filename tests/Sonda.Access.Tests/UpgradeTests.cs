using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Access.Tests;

[Collection("AccessPostgres")]
public sealed class UpgradeTests
{
    [Fact]
    public async Task Access_migrations_preserve_populated_phase4_schema_hashes_receipts_runs_and_incidents()
    {
        var adminConnection = Environment.GetEnvironmentVariable("SONDA_TEST_ADMIN")!; var name = "sonda_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", admin).ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            await using (var core = SondaDbContext.Open(connection)) await core.Database.MigrateAsync();
            var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
            var session = await new PostgresConfigurationStore(connection).CreateSessionAsync(sample); var profile = sample.Profiles.Single();
            var scope = new ProcessingScope(profile.TeamId, session, profile.ApplicationId); var engine = new PostgresProcessingStore(connection);
            for (var i = 0; i < sample.Entries.Count; i++) await engine.ProcessAsync(new(scope, profile.Id, Guid.NewGuid(), "sample", "upgrade", i, i + 1, i + 1, sample.Entries[i].Raw, sample.Entries[i].ProcessedAt, sample.SampleDate));
            var before = await Fingerprint(connection);
            await using (var access = AccessDbContext.Open(connection)) { await access.Database.MigrateAsync(); await access.Database.MigrateAsync(); Assert.Equal(2, (await access.Database.GetAppliedMigrationsAsync()).Count()); }
            Assert.Equal(before, await Fingerprint(connection));
            var state = await engine.ReadStateAsync(scope, profile.Id); Assert.Equal(4, state.Interpreter.Runs.Length); Assert.Single(state.Interpreter.Incidents);
            Assert.Equal(4, (await engine.ReadMetricFactsAsync(scope)).Count);
        }
        finally { NpgsqlConnection.ClearAllPools(); await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin).ExecuteNonQueryAsync(); }
    }
    private static async Task<string> Fingerprint(string connection)
    {
        await using var db = new NpgsqlConnection(connection); await db.OpenAsync(); var tables = new List<string>();
        await using (var reader = await new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='sonda' ORDER BY tablename", db).ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var text = new StringBuilder();
        foreach (var table in tables)
        {
            text.Append(table);
            await using var reader = await new NpgsqlCommand($"SELECT row_to_json(t)::text FROM sonda.\"{table}\" t ORDER BY row_to_json(t)::text", db).ExecuteReaderAsync();
            while (await reader.ReadAsync()) text.Append(reader.GetString(0));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
