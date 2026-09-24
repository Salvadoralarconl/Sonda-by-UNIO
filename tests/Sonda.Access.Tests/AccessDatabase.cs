using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Access.Tests;

public sealed class AccessDatabase : IAsyncLifetime
{
    public string Connection { get; private set; } = "";
    private string admin = "", name = "";
    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("SONDA_TEST_ADMIN") ?? throw new InvalidOperationException("Real PostgreSQL is required.");
        name = "sonda_access_test_" + Guid.NewGuid().ToString("N");
        await using var conn = new NpgsqlConnection(admin); await conn.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", conn).ExecuteNonQueryAsync();
        Connection = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
        await using var core = SondaDbContext.Open(Connection); await core.Database.MigrateAsync();
        await using var access = AccessDbContext.Open(Connection); await access.Database.MigrateAsync();
    }
    public async Task<Actor> ActorAsync(string role = "Admin", string? team = null)
    {
        team ??= Guid.NewGuid().ToString("N");
        await using var core = SondaDbContext.Open(Connection);
        if (!await core.Set<TeamRow>().AnyAsync(x => x.TeamId == team)) { core.Add(new TeamRow { TeamId = team, Name = "Synthetic" }); await core.SaveChangesAsync(); }
        await using var access = AccessDbContext.Open(Connection);
        var id = Guid.NewGuid();
        access.Add(new Account { Id = id, UserName = id.ToString(), NormalizedUserName = id.ToString() });
        access.Add(new Membership { TeamId = team, AccountId = id, Role = role, Enabled = true });
        await access.SaveChangesAsync(); return new(id, team, role, 0);
    }
    public async Task DisposeAsync()
    {
        if (name.Length == 0) return;
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(admin); await conn.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", conn).ExecuteNonQueryAsync();
    }
}
[CollectionDefinition("AccessPostgres", DisableParallelization = true)]
public sealed class AccessCollection : ICollectionFixture<AccessDatabase>;
