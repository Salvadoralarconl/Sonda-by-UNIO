using System.Text;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    private async Task<(OwnerFence Fence,Guid Generation,SourceConfiguration Source)> IngestionSetup(bool proof=false,bool timeout=false)
    {
        var(sample,scope)=await PolicySetup(timeout:timeout);
        var config=new SourceConfiguration{ProfileId=sample.Profiles[0].Id,SourceKey="fixture-file",Root=Path.GetFullPath("synthetic-test-input"),Enabled=true,Completeness=proof?CompletenessMode.ProducerManifest:CompletenessMode.None,ProducerContract=proof?"fixture-contract":""};
        var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,config,"test-host");
        var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
        var file=new FileObservation(Path.Combine(config.Root,"test.log"),new("test-host","volume","file","creation"),0,sample.AsOf,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])));
        var checkpoint=await store.ObserveFileAsync(fence,config,file,0,null);
        return(fence,checkpoint.GenerationId,config);
    }
    private static PhysicalRecord Line(string text,long offset=0)=>Assert.Single(new ByteLineFramer(FileEncoding.Utf8,offset).Append(Encoding.UTF8.GetBytes(text+"\n")).Records);
    [Fact] public async Task Physical_checkpoint_commits_with_engine_and_duplicate_bytes_are_idempotent()
    {
        var(fence,generation,_)=await IngestionSetup();var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.Parse("2026-09-23T14:00:00Z");long offset=0;
        List<PhysicalRecord> records=[];
        foreach(var text in new[]{"CAM process started","Finding order OrderID=x","Unable to send order OrderID=x","Finding order OrderID=x","Order sent OrderID=x","CAM process completed"})
        {
            var record=Line(text,offset);records.Add(record);var result=await store.CommitAsync(fence,generation,record,at=at.AddSeconds(1));offset=record.End;Assert.Equal(offset,result.CommittedOffset);
        }
        var before=await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam");
        foreach(var record in records)Assert.True((await store.CommitAsync(fence,generation,record,at.AddHours(1))).Replayed);
        Assert.Equal(SimulationJson.Serialize(before),SimulationJson.Serialize(await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")));
        await using var db=SondaDbContext.Open(pg.Connection);var rows=await db.Set<FileRecordRow>().Where(r=>r.GenerationId==generation).OrderBy(r=>r.StartOffset).ToArrayAsync();
        Assert.Equal(6,rows.Length);Assert.Equal(offset,rows.Sum(r=>r.Bytes.Length));Assert.Equal(3,await db.Set<FactRow>().CountAsync(r=>r.SessionId==fence.Scope.SessionId));
        Assert.Single(before.Interpreter.Incidents);Assert.Single(before.Interpreter.Incidents[0].Recoveries);
    }
    [Theory][InlineData(AcquisitionBoundary.AfterReservation)][InlineData(AcquisitionBoundary.BeforeInterpretation)][InlineData(AcquisitionBoundary.AfterCheckpoint)]
    public async Task Pending_bytes_survive_fault_and_checkpoint_does_not_outrun_commit(AcquisitionBoundary boundary)
    {
        var(fence,generation,_)=await IngestionSetup();var record=Line("CAM process started");
        await Assert.ThrowsAsync<IOException>(()=>new PostgresIngestionStore(pg.Connection,b=>{if(b==boundary)throw new IOException("fault");}).CommitAsync(fence,generation,record,DateTimeOffset.UtcNow));
        var store=new PostgresIngestionStore(pg.Connection);Assert.Equal(0,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
        await using(var db=SondaDbContext.Open(pg.Connection)){Assert.Equal(0,await db.Set<EvidenceRow>().CountAsync(e=>e.SessionId==fence.Scope.SessionId));Assert.NotEqual("",(await db.Set<PendingIngestionRow>().FindAsync(fence.Scope.TeamId,fence.Scope.ApplicationId))!.Command);}
        Assert.Equal(record.End,(await store.RecoverPendingAsync(fence))!.CommittedOffset);Assert.Null(await store.RecoverPendingAsync(fence));
    }
    [Fact] public async Task Commit_acknowledgment_loss_returns_durable_physical_receipt()
    {
        var(fence,generation,_)=await IngestionSetup();var record=Line("CAM process started");
        await Assert.ThrowsAsync<IOException>(()=>new PostgresIngestionStore(pg.Connection,b=>{if(b==AcquisitionBoundary.AfterCommit)throw new IOException("response lost");}).CommitAsync(fence,generation,record,DateTimeOffset.UtcNow));
        var store=new PostgresIngestionStore(pg.Connection);Assert.Null(await store.RecoverPendingAsync(fence));Assert.True((await store.CommitAsync(fence,generation,record,DateTimeOffset.UtcNow)).Replayed);
        Assert.Single((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter.Runs);
    }
    [Fact] public async Task Identical_text_at_distinct_offsets_is_distinct_physical_evidence()
    {
        var(fence,generation,_)=await IngestionSetup();var store=new PostgresIngestionStore(pg.Connection);var first=Line("trace");var second=Line("trace",first.End);
        await store.CommitAsync(fence,generation,first,DateTimeOffset.UtcNow);await store.CommitAsync(fence,generation,second,DateTimeOffset.UtcNow);
        await using var db=SondaDbContext.Open(pg.Connection);Assert.Equal(2,await db.Set<EvidenceRow>().CountAsync(e=>e.SessionId==fence.Scope.SessionId));
    }
    [Fact] public async Task Contending_and_stale_owners_cannot_commit_or_advance()
    {
        var(fence,generation,_)=await IngestionSetup();var store=new PostgresIngestionStore(pg.Connection);
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.ClaimAsync(fence.Scope,Guid.NewGuid(),TimeSpan.FromMinutes(1)));
        await store.ReleaseAsync(fence);var next=await store.ClaimAsync(fence.Scope,Guid.NewGuid(),TimeSpan.FromMinutes(1));Assert.True(next.Epoch>fence.Epoch);
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.CommitAsync(fence,generation,Line("trace"),DateTimeOffset.UtcNow));Assert.Equal(0,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
        await store.CommitAsync(next,generation,Line("trace"),DateTimeOffset.UtcNow);
    }
}
