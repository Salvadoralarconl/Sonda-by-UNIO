using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact] public async Task Unavailable_database_cannot_advance_or_discard_pending_bytes()
    {
        var(fence,generation,_)=await IngestionSetup();var record=Line("trace");await Assert.ThrowsAsync<IOException>(()=>new PostgresIngestionStore(pg.Connection,b=>{if(b==AcquisitionBoundary.AfterReservation)throw new IOException();}).CommitAsync(fence,generation,record,DateTimeOffset.UtcNow));
        var unavailable=new NpgsqlConnectionStringBuilder(pg.Connection){Port=1,Timeout=1,Pooling=false};await Assert.ThrowsAsync<NpgsqlException>(()=>new PostgresIngestionStore(unavailable.ConnectionString).RecoverPendingAsync(fence));
        var store=new PostgresIngestionStore(pg.Connection);Assert.Equal(0,(await store.CheckpointAsync(fence.Scope,generation)).Offset);Assert.Equal(record.End,(await store.RecoverPendingAsync(fence))!.CommittedOffset);
    }
    [Fact] public async Task PostgreSQL_storage_error_rolls_back_without_purging_reserved_work()
    {
        var(fence,generation,_)=await IngestionSetup();var record=Line("trace");var name="test_disk_"+Guid.NewGuid().ToString("N");await using var owner=new NpgsqlConnection(pg.OwnerConnection);await owner.OpenAsync();
        // SQLSTATE disk_full fault, not a claim that this machine's disk was filled.
        await new NpgsqlCommand($"CREATE FUNCTION sonda.{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic disk full' USING ERRCODE='53100'; END $$; CREATE CONSTRAINT TRIGGER {name} AFTER INSERT ON sonda.file_record_evidence DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.{name}();",owner).ExecuteNonQueryAsync();
        try{var error=await Assert.ThrowsAsync<PostgresException>(()=>new PostgresIngestionStore(pg.Connection).CommitAsync(fence,generation,record,DateTimeOffset.UtcNow));Assert.Equal("53100",error.SqlState);Assert.Equal(0,(await new PostgresIngestionStore(pg.Connection).CheckpointAsync(fence.Scope,generation)).Offset);}
        finally{await new NpgsqlCommand($"DROP TRIGGER {name} ON sonda.file_record_evidence; DROP FUNCTION sonda.{name}();",owner).ExecuteNonQueryAsync();}
        Assert.Equal(record.End,(await new PostgresIngestionStore(pg.Connection).RecoverPendingAsync(fence))!.CommittedOffset);
    }
    [Fact] public async Task Application_sequence_capacity_stops_without_reservation_or_wrap()
    {
        var(fence,generation,_)=await IngestionSetup();await using(var db=SondaDbContext.Open(pg.Connection))await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('sonda.fence_epoch',{fence.Epoch.ToString()},true),set_config('sonda.fence_instance',{fence.InstanceId.ToString()},true)");
            var runtime=(await db.Set<RuntimeRow>().FindAsync(fence.Scope.TeamId,fence.Scope.SessionId,fence.Scope.ApplicationId))!;runtime.NextSequence=int.MaxValue;runtime.Revision++;await db.SaveChangesAsync();await tx.CommitAsync();
        }
        var store=new PostgresIngestionStore(pg.Connection);await Assert.ThrowsAsync<PersistenceConflict>(()=>store.CommitAsync(fence,generation,Line("trace"),DateTimeOffset.UtcNow));Assert.Null(await store.RecoverPendingAsync(fence));Assert.Equal(0,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
    }
}

