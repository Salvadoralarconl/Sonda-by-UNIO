using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    public string Connection { get; private set; } = "";
    public string OwnerConnection { get; private set; } = "";
    private string admin = "", database = "", role = "";
    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("SONDA_TEST_ADMIN") ?? throw new InvalidOperationException("Real PostgreSQL required: set SONDA_TEST_ADMIN. Integration tests do not skip or use mocks.");
        database = "sonda_test_" + Guid.NewGuid().ToString("N"); role = database + "_app";
        await using var conn = new NpgsqlConnection(admin); await conn.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {database}", conn).ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = database }; OwnerConnection = builder.ConnectionString;
        await using (var db = SondaDbContext.Open(OwnerConnection)) await db.Database.MigrateAsync();
        var password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        await new NpgsqlCommand($"CREATE ROLE {role} LOGIN PASSWORD '{password}'", conn).ExecuteNonQueryAsync();
        await using (var owner = new NpgsqlConnection(OwnerConnection))
        {
            await owner.OpenAsync();
            await new NpgsqlCommand($"GRANT USAGE ON SCHEMA sonda TO {role}; GRANT SELECT,INSERT,UPDATE ON ALL TABLES IN SCHEMA sonda TO {role}; GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA sonda TO {role};", owner).ExecuteNonQueryAsync();
        }
        builder.Username = role; builder.Password = password; Connection = builder.ConnectionString;
    }
    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (database.Length == 0) return;
        await using var conn = new NpgsqlConnection(admin); await conn.OpenAsync();
        // Names are locally generated fixed-prefix hex identifiers, never user paths/databases.
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", conn).ExecuteNonQueryAsync();
        await new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", conn).ExecuteNonQueryAsync();
    }
}
[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
