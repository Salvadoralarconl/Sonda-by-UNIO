using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    private static string Phase4Artifact(string name)
    {
        var dir=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/phase4"));Directory.CreateDirectory(dir);return Path.Combine(dir,name);
    }
    [Theory][InlineData("crash-after-read",76)][InlineData("crash-after-reservation",75)][InlineData("crash-before-commit",74)][InlineData("crash-after-commit",73)]
    public async Task Acquisition_process_exit_recovers_exact_contiguous_chain(string mode,int expectedExit)
    {
        var(fence,generation,_)=await IngestionSetup();var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.UtcNow;long offset=0;
        // The final input recovers an existing failed order, so duplicate counts exercise all business facts.
        foreach(var text in new[]{"CAM process started","Finding order OrderID=x","Unable to send order OrderID=x","Finding order OrderID=x"})
        {var row=Line(text,offset);await store.CommitAsync(fence,generation,row,at);offset=row.End;}
        var record=Line("Order sent OrderID=x",offset);var request=Phase4Artifact(mode+"-request.json");
        var byteFile=Phase4Artifact(mode+"-synthetic.log");List<byte> original=[];await foreach(var prior in store.ReadPhysicalRecordsAsync(fence.Scope,generation))original.AddRange(prior.Bytes);original.AddRange(record.Bytes);await File.WriteAllBytesAsync(byteFile,original.ToArray());
        await File.WriteAllTextAsync(request,SimulationJson.Serialize(new{Fence=fence,Generation=generation,Record=record,ProcessedAt=at,ByteFile=byteFile}));
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var start=new ProcessStartInfo(Path.Combine(root,".tools/dotnet/dotnet.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{Path.Combine(root,"tools/Sonda.PersistenceHarness/bin/Release/net10.0/Sonda.PersistenceHarness.dll"),"ingest-record",request,mode})start.ArgumentList.Add(arg);
        start.Environment["SONDA_DATABASE"]=pg.Connection;
        using(var child=Process.Start(start)!){var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();Assert.True(child.ExitCode==expectedExit,$"Exit {child.ExitCode}: {await error}; {await output}");}
        Assert.Equal(expectedExit==73?record.End:offset,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
        await store.ReleaseAsync(fence);fence=await store.ClaimAsync(fence.Scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
        await store.RecoverPendingAsync(fence);await store.CommitAsync(fence,generation,record,at);
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam");
        Assert.Equal(3,state.Interpreter.Runs.Length);var incident=Assert.Single(state.Interpreter.Incidents);Assert.Single(incident.Occurrences);Assert.Single(incident.Recoveries);
        for(var n=0;n<2;n++)Assert.True((await store.CommitAsync(fence,generation,record,at)).Replayed);
        Assert.Equal(SimulationJson.Serialize(state),SimulationJson.Serialize(await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")));
        await using var db=SondaDbContext.Open(pg.Connection);var records=await db.Set<FileRecordRow>().Where(r=>r.GenerationId==generation).OrderBy(r=>r.StartOffset).ToArrayAsync();long contiguous=0;
        foreach(var r in records){Assert.Equal(contiguous,r.StartOffset);contiguous=r.EndOffset;}
        Assert.Equal(contiguous,(await store.CheckpointAsync(fence.Scope,generation)).Offset);Assert.Equal(5,records.Length);
        Assert.Equal(2,await db.Set<FactRow>().CountAsync(f=>f.SessionId==fence.Scope.SessionId));
        await File.WriteAllTextAsync(Phase4Artifact(mode+"-trace.json"),SimulationJson.Serialize(new{exit=expectedExit,contiguous,records=records.Select(r=>new{r.StartOffset,r.EndOffset,r.ReceiptId}),state}));
    }
    [Fact] public async Task Actual_deferred_commit_failure_rolls_back_checkpoint_then_retries_pending()
    {
        var(fence,generation,_)=await IngestionSetup();var store=new PostgresIngestionStore(pg.Connection);await store.ReleaseAsync(fence);
        fence=await store.ClaimAsync(fence.Scope,Guid.NewGuid(),TimeSpan.FromSeconds(2));var record=Line("CAM process started");
        // Delay after every SQL write, so only the deferred COMMIT constraint rejects the expired fence.
        var failure=await Assert.ThrowsAsync<PostgresException>(()=>new PostgresIngestionStore(pg.Connection,b=>{if(b==AcquisitionBoundary.AfterCheckpoint)Thread.Sleep(2200);}).CommitAsync(fence,generation,record,DateTimeOffset.UtcNow));
        Assert.Equal("40001",failure.SqlState);Assert.Equal(0,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
        fence=await store.ClaimAsync(fence.Scope,Guid.NewGuid(),TimeSpan.FromMinutes(1));Assert.Equal(record.End,(await store.RecoverPendingAsync(fence))!.CommittedOffset);
        Assert.Single((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter.Runs);
    }
    [Theory][InlineData(PersistenceBoundary.AfterEvidence)][InlineData(PersistenceBoundary.AfterRuns)][InlineData(PersistenceBoundary.AfterFacts)][InlineData(PersistenceBoundary.BeforeCommit)]
    public async Task Acquisition_domain_boundary_rollback_keeps_checkpoint_and_pending(PersistenceBoundary boundary)
    {
        var(fence,generation,_)=await IngestionSetup();var record=Line("CAM process started");var store=new PostgresIngestionStore(pg.Connection);
        await Assert.ThrowsAsync<IOException>(()=>new PostgresIngestionStore(pg.Connection,interpretationFault:b=>{if(b==boundary)throw new IOException("fault");}).CommitAsync(fence,generation,record,DateTimeOffset.UtcNow));
        Assert.Equal(0,(await store.CheckpointAsync(fence.Scope,generation)).Offset);Assert.Empty((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter.Runs);
        Assert.Equal(record.End,(await store.RecoverPendingAsync(fence))!.CommittedOffset);
    }
    [Fact] public async Task Concurrent_duplicate_reads_produce_one_evidence_and_changed_bytes_conflict()
    {
        var(fence,generation,_)=await IngestionSetup();var record=Line("trace");var at=DateTimeOffset.UtcNow;
        // Reservation is a separate transaction. One caller may observe the already-completed pending slot; retry is safe.
        var results=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>{try{return await new PostgresIngestionStore(pg.Connection).CommitAsync(fence,generation,record,at);}catch(PersistenceConflict){return await new PostgresIngestionStore(pg.Connection).CommitAsync(fence,generation,record,at);}}));
        Assert.Single(results.Select(r=>r.ReceiptId).Distinct());
        await Assert.ThrowsAsync<PersistenceConflict>(()=>new PostgresIngestionStore(pg.Connection).CommitAsync(fence,generation,Line("other"),at));
        await using var db=SondaDbContext.Open(pg.Connection);Assert.Equal(1,await db.Set<FileRecordRow>().CountAsync(r=>r.GenerationId==generation));
    }
}
