using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Access.Tests;

[Collection("AccessPostgres")]
public sealed class RuntimeRoleTests(AccessDatabase database)
{
    [Fact]
    public async Task Least_privilege_role_can_provision_but_cannot_change_schema_and_audit_failure_rolls_back_core()
    {
        var actor = await database.ActorAsync(); var role = "sonda_api_" + Guid.NewGuid().ToString("N"); var password = Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(database.Connection); await admin.OpenAsync();
        async Task Sql(string sql) => await new NpgsqlCommand(sql, admin).ExecuteNonQueryAsync();
        await Sql($"CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE");
        try
        {
            await Sql($"GRANT USAGE ON SCHEMA sonda,sonda_access TO {role}; GRANT SELECT,INSERT,UPDATE ON ALL TABLES IN SCHEMA sonda,sonda_access TO {role}; REVOKE INSERT ON sonda_access.access_audit FROM {role}");
            var restricted = new NpgsqlConnectionStringBuilder(database.Connection) { Username = role, Password = password, Pooling = false }.ConnectionString;
            var store = new ProvisioningStore(restricted, new([@"C:\synthetic-logs"])); var command = new CreateApplication(Guid.NewGuid(), "Atomic audit fixture");
            await Assert.ThrowsAnyAsync<Exception>(() => store.CreateApplicationAsync(actor, command));
            await using (var core = SondaDbContext.Open(database.Connection)) Assert.Empty(await core.Set<ApplicationRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
            await using (var access = AccessDbContext.Open(database.Connection)) Assert.Empty(await access.Set<ApiOperation>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
            await Sql($"GRANT INSERT ON sonda_access.access_audit TO {role}");
            var result = await store.CreateApplicationAsync(actor, command);
            Assert.Equal(result, await store.CreateApplicationAsync(actor, command));
            await using var runtime = new NpgsqlConnection(restricted); await runtime.OpenAsync();
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("CREATE TABLE sonda_access.forbidden(value int)", runtime).ExecuteNonQueryAsync())).SqlState);
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("DELETE FROM sonda_access.access_audit", runtime).ExecuteNonQueryAsync())).SqlState);
        }
        finally { await Sql($"DROP OWNED BY {role}; DROP ROLE {role}"); }
    }
}
