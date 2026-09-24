using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact] public async Task Pending_input_precedes_activation_and_open_run_remains_pinned()
    {
        var(sample,scope)=await PolicySetup();var source=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=Path.GetFullPath("synthetic-test-input"),Enabled=true};var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,source,"test");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
        var generation=(await store.ObserveFileAsync(fence,source,new(Path.Combine(source.Root,"a.log"),new("test","v","f","birth"),0,sample.AsOf,"empty"),0,null)).GenerationId;
        var begin=Line("CAM process started");await Assert.ThrowsAsync<IOException>(()=>new PostgresIngestionStore(pg.Connection,b=>{if(b==AcquisitionBoundary.AfterReservation)throw new IOException("pause");}).CommitAsync(fence,generation,begin,sample.AsOf));
        var next=sample.Profiles[0] with{Version=2};var config=new PostgresConfigurationStore(pg.Connection);var revision=await config.EditDraftAsync(next,0,Guid.NewGuid());await config.PublishAsync(sample with{Profiles=[next]},revision,Guid.NewGuid());
        var command=new PolicyCommand{Id=Guid.NewGuid(),Sequence=2,Kind=PolicyCommandKind.ActivateVersion,Profile=next,ProcessedAt=sample.AsOf.AddSeconds(1)};
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.SubmitControlAsync(fence,"cam",command));await store.RecoverPendingAsync(fence);
        Assert.Equal("Applied",(await store.SubmitControlAsync(fence,"cam",command)).Disposition);await store.SubmitControlAsync(fence,"cam",command);
        var end=Line("CAM process completed",begin.End);await store.CommitAsync(fence,generation,end,sample.AsOf.AddSeconds(2));await store.CommitAsync(fence,generation,Line("CAM process started",end.End),sample.AsOf.AddSeconds(3));
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,"cam");Assert.Equal(new[]{1,2},state.Interpreter.Runs.Select(r=>r.PolicyContext!.ProfileVersion));
        var memory=new PolicySession(sample.Profiles[0],sample.Seed);foreach(var receipt in state.Receipts)memory.Execute(receipt.Command);Assert.Equal(SimulationJson.Serialize(memory.Export()),SimulationJson.Serialize(state));
    }
    private async Task<CompletenessCertificate> ReadyCertificate(PostgresIngestionStore store,OwnerFence fence,Guid generation,SourceConfiguration source,long offset,DateTimeOffset at)
    {
        var coverage=new SourceCoverage(source.SourceKey,generation,offset,source.Revision,"",at);var manifest=new ProducerCoverageManifest(source.ProducerContract,source.SourceKey,at,[coverage],true,true,true);var hash=SimulationJson.Hash(manifest);
        await store.RecordManifestAsync(fence,source.ProfileId,manifest,hash);return await store.CreateCertificateAsync(fence,source.ProfileId,source.ProducerContract,[coverage with{ManifestHash=hash}],at);
    }
    [Theory][InlineData("generation")][InlineData("membership")][InlineData("pending")][InlineData("partial")]
    public async Task Frontier_is_revalidated_after_inventory_membership_or_pending_change(string change)
    {
        var(fence,generation,source)=await IngestionSetup(proof:true,timeout:true);var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.UtcNow;var begin=Line("CAM process started");await store.CommitAsync(fence,generation,begin,at);
        var certificate=await ReadyCertificate(store,fence,generation,source,begin.End,at.AddMinutes(2));
        if(change=="generation")await store.ObserveFileAsync(fence,source,new(Path.Combine(source.Root,"b.log"),new("host","v","new","birth"),0,at,"empty"),0,null);
        if(change=="membership"){await store.SetEnabledAsync(fence,"cam",source.SourceKey,false);await store.SetEnabledAsync(fence,"cam",source.SourceKey,true);}
        if(change=="pending")await Assert.ThrowsAsync<IOException>(()=>new PostgresIngestionStore(pg.Connection,b=>{if(b==AcquisitionBoundary.AfterReservation)throw new IOException();}).CommitAsync(fence,generation,Line("CAM process completed",begin.End),at.AddSeconds(1)));
        if(change=="partial")await store.ObserveSourceAsync(fence,"cam",new(source.SourceKey,ReaderState.WaitingForPartial,"partial",at,at));
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.DispatchAsync(fence,certificate,at.AddMinutes(2)));
        Assert.Null(Assert.Single((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter.Runs).Result);
        Assert.Equal(begin.End,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
    }
    [Theory][InlineData("jump")][InlineData("backward")][InlineData("wrong-receipt")][InlineData("stale-fence")]
    public async Task PostgreSQL_rejects_direct_checkpoint_corruption(string change)
    {
        var(fence,generation,_)=await IngestionSetup();var store=new PostgresIngestionStore(pg.Connection);var record=Line("trace");await store.CommitAsync(fence,generation,record,DateTimeOffset.UtcNow);
        await using var connection=new NpgsqlConnection(pg.Connection);await connection.OpenAsync();await using var tx=await connection.BeginTransactionAsync();
        await using(var settings=new NpgsqlCommand("SELECT set_config('sonda.fence_epoch',@epoch,true),set_config('sonda.fence_instance',@instance,true)",connection,tx)){settings.Parameters.AddWithValue("epoch",(change=="stale-fence"?fence.Epoch+1:fence.Epoch).ToString());settings.Parameters.AddWithValue("instance",fence.InstanceId.ToString());await settings.ExecuteNonQueryAsync();}
        await using var corrupt=new NpgsqlCommand("UPDATE sonda.file_checkpoints SET \"offset\"=@offset,revision=revision+1,receipt_id=@receipt WHERE team_id=@team AND generation_id=@generation",connection,tx);
        corrupt.Parameters.AddWithValue("offset",change=="backward"?0L:record.End+5);corrupt.Parameters.AddWithValue("receipt",Guid.NewGuid());corrupt.Parameters.AddWithValue("team",fence.Scope.TeamId);corrupt.Parameters.AddWithValue("generation",generation);
        await Assert.ThrowsAsync<PostgresException>(()=>corrupt.ExecuteNonQueryAsync());await tx.RollbackAsync();Assert.Equal(record.End,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
    }
    [Fact] public async Task Byte_acquisition_uses_unchanged_legacy_engine()
    {
        using var directory=new AcquisitionDirectory();var(sample,scope,inputs)=await Setup("mixed-orders");
        var source=new SourceConfiguration{ProfileId=sample.Profiles[0].Id,SourceKey="file",Root=directory.Root,Enabled=true,SampleDate=sample.SampleDate};var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,source,"host");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
        await directory.Write("a.log","");await using var pump=new Sonda.Infrastructure.Files.FileIngestionPump(store,new Sonda.Infrastructure.Files.WindowsFileSource());
        foreach(var input in inputs)
        {
            var half=input.Raw.Length/2;await File.AppendAllTextAsync(directory.PathFor("a.log"),input.Raw[..half]);Assert.Equal(0,(await pump.VisitAsync(fence,source,input.ProcessedAt)).CommittedRecords);
            await File.AppendAllTextAsync(directory.PathFor("a.log"),input.Raw[half..]+"\n");Assert.Equal(1,(await pump.VisitAsync(fence,source,input.ProcessedAt)).CommittedRecords);
        }
        var generation=Assert.Single(await store.GenerationsAsync(scope,source.ProfileId,source.SourceKey)).Id;
        var state=await new PostgresProcessingStore(pg.Connection).ReadStateAsync(scope,source.ProfileId);Assert.NotEmpty(state.Interpreter.Runs);
        await using var db=SondaDbContext.Open(pg.Connection);Assert.Equal(inputs.Length,await db.Set<FileRecordRow>().CountAsync(r=>r.GenerationId==generation));
        var report=new SimulationRunner().Run(sample);Assert.Equal(SimulationJson.Serialize(report.ApplicationRuns.Concat(report.OrderRuns).Select(Sonda.Domain.Processing.RunState.From).OrderBy(r=>r.Id)),SimulationJson.Serialize(state.Interpreter.Runs.OrderBy(r=>r.Id)));
        var metrics=Sonda.Domain.Metrics.MetricCalculator.Calculate(await new PostgresProcessingStore(pg.Connection).ReadMetricFactsAsync(scope),sample.AsOf,TimeZoneInfo.FindSystemTimeZoneById(sample.ServerTimeZoneId));Assert.Equal(SimulationJson.Serialize(report.Metrics),SimulationJson.Serialize(metrics));Assert.Single(state.Interpreter.Incidents);
        await File.WriteAllTextAsync(Phase4Artifact("legacy-mixed-byte-parity.json"),SimulationJson.Serialize(new{source,metrics,state,exactRuns=true,exactMetrics=true,splitWrites=true,explicitSampleDate=sample.SampleDate}));
    }
}

