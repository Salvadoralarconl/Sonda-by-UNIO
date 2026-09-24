using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed partial class PersistenceTests
{
    [Fact]
    public async Task Database_commit_rejection_rolls_back_and_retry_succeeds()
    {
        var (_, scope, inputs) = await Setup(); await Apply(inputs.Take(1)); var input = inputs[1];
        await using var owner = SondaDbContext.Open(pg.OwnerConnection);
        await owner.Database.ExecuteSqlRawAsync("CREATE FUNCTION sonda.test_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test deferred commit rejection'; END $$");
        // Generated UUID is the only interpolated identifier/value in this test-only DDL.
        var ddl = "CREATE CONSTRAINT TRIGGER test_commit_failure AFTER INSERT ON sonda.processing_receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.request_id='" + input.RequestId + "'::uuid) EXECUTE FUNCTION sonda.test_commit_failure()";
        await owner.Database.ExecuteSqlRawAsync(ddl);
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => new PostgresProcessingStore(pg.Connection).ProcessAsync(input)); Assert.Equal("P0001", error.SqlState);
            Assert.Equal(1, await owner.Set<ReceiptRow>().CountAsync(x => x.SessionId == scope.SessionId));
        }
        finally { await owner.Database.ExecuteSqlRawAsync("DROP TRIGGER test_commit_failure ON sonda.processing_receipts; DROP FUNCTION sonda.test_commit_failure()"); }
        Assert.False((await new PostgresProcessingStore(pg.Connection).ProcessAsync(input)).Replayed);
        Assert.True((await new PostgresProcessingStore(pg.Connection).ProcessAsync(input)).Replayed);
    }
    [Fact]
    public async Task Connection_termination_before_commit_is_recoverable()
    {
        var (_, scope, inputs) = await Setup(); var applicationName = "sonda_terminate_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(pg.Connection) { ApplicationName = applicationName }.ConnectionString;
        var store = new PostgresProcessingStore(connection, b =>
        {
            if (b != PersistenceBoundary.BeforeCommit) return;
            using var admin = new NpgsqlConnection(pg.OwnerConnection); admin.Open();
            using var cmd = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name=@name", admin); cmd.Parameters.AddWithValue("name", applicationName); cmd.ExecuteNonQuery();
        });
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => store.ProcessAsync(inputs[0]));
        Assert.False((await new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[0])).Replayed);
        await using var db = SondaDbContext.Open(pg.Connection); Assert.Equal(1, await db.Set<ReceiptRow>().CountAsync(r => r.SessionId == scope.SessionId));
    }
    [Fact]
    public async Task Reapplying_migrations_is_noop_and_database_is_postgres18()
    {
        await using var db = SondaDbContext.Open(pg.OwnerConnection); await db.Database.MigrateAsync();
        Assert.Equal(new[] { "20260924023552_DurablePersistence", "20260924032911_InterpretationPolicies", "20260924044645_FileAcquisition" }, await db.Database.GetAppliedMigrationsAsync());
        var version = await db.Database.SqlQueryRaw<int>("SELECT current_setting('server_version_num')::int AS \"Value\"").SingleAsync(); Assert.InRange(version, 180000, 189999);
    }
    [Fact]
    public async Task Cross_team_parent_cannot_be_inserted()
    {
        var (_, a, inputs) = await Setup(); await Apply(inputs.Take(1)); var (_, b, _) = await Setup();
        await using var db = SondaDbContext.Open(pg.Connection); var original = await db.Set<RunRow>().SingleAsync(r => r.SessionId == a.SessionId);
        var record = Sonda.Application.Simulation.SimulationJson.Deserialize<Sonda.Domain.Processing.RunState>(original.Payload);
        var clone = record with { Id = "invalid-cross-team", Scope = Sonda.Domain.Profiles.TargetScope.Order, ApplicationRunId = original.Id, Identifier = "x" };
        db.Add(new RunRow { TeamId = b.TeamId, SessionId = b.SessionId, Id = clone.Id, ProfileId = "cam", Version = 1, Scope = "Order", CycleSequence = 1, ParentId = original.Id, Identifier = "x", Attempt = 1, CreatedReceipt = original.CreatedReceipt, Payload = SimulationJson.Serialize(clone) });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
