using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Files;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact] public async Task Real_manifest_scheduler_defers_partial_then_preserves_boundary_success()
    {
        using var directory=new AcquisitionDirectory();var(sample,scope)=await PolicySetup(timeout:true);var source=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true,Completeness=CompletenessMode.ProducerManifest,ProducerContract="test",ProducerManifestPath=directory.PathFor("proof.json")};
        var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,source,"test");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));var at=sample.AsOf;
        await directory.Write("a.log","CAM process started\nCAM process completed");await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(ReaderState.WaitingForPartial,(await pump.VisitAsync(fence,source,at)).State);
        var generation=Assert.Single(await store.GenerationsAsync(scope,"cam","file"));var cover=new SourceCoverage("file",generation.Id,new FileInfo(directory.PathFor("a.log")).Length+1,1,"",at.AddMinutes(1));var manifest=new ProducerCoverageManifest("test","file",cover.CompleteThrough,[cover],true,true,true);await File.WriteAllTextAsync(source.ProducerManifestPath,SimulationJson.Serialize(manifest));var scheduler=new DeadlineScheduler(store,new WindowsFileSource());
        Assert.False((await scheduler.TickAsync(fence,"cam",[source],at.AddMinutes(1))).Dispatched);
        await File.AppendAllTextAsync(directory.PathFor("a.log"),"\n");Assert.Equal(1,(await pump.VisitAsync(fence,source,at.AddMinutes(1))).CommittedRecords);
        Assert.True((await scheduler.TickAsync(fence,"cam",[source],at.AddMinutes(1))).Dispatched);Assert.Equal("FrontierAlreadyConsumed",(await scheduler.TickAsync(fence,"cam",[source],at.AddMinutes(2))).Reason);
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,"cam");Assert.Equal(Sonda.Domain.Runs.DetectionResult.Success,Assert.Single(state.Interpreter.Runs).Result);Assert.Empty(state.Interpreter.Incidents);
        await File.WriteAllTextAsync(Phase4Artifact("producer-boundary.json"),SimulationJson.Serialize(new{partialDeferred=true,successFirst=true,duplicateClockSuppressed=true,state}));
    }
    [Fact] public async Task Evidence_contradicting_consumed_frontier_retains_late_policy_and_blocks_new_proof()
    {
        var(fence,generation,source)=await IngestionSetup(proof:true,timeout:true);var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.UtcNow;var begin=Line("CAM process started");await store.CommitAsync(fence,generation,begin,at);
        var cert=await ReadyCertificate(store,fence,generation,source,begin.End,at.AddMinutes(2));await store.DispatchAsync(fence,cert,cert.CompleteThrough);
        var result=await store.CommitAsync(fence,generation,Line("CAM process completed",begin.End),cert.CompleteThrough);Assert.Equal("LateEvidence",result.Disposition);Assert.True(await store.IsSourceBlockedAsync(fence.Scope,"cam",source.SourceKey));
        Assert.Equal(Sonda.Domain.Runs.DetectionResult.Undefined,Assert.Single((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter.Runs).Result);
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.CreateCertificateAsync(fence,"cam",source.ProducerContract,cert.Sources,cert.CompleteThrough));
    }
    [Fact] public async Task Actual_hardlink_alias_is_one_physical_owner_and_overlapping_patterns_do_not_duplicate()
    {
        using var directory=new AcquisitionDirectory();var source=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true,Include=["*.log","a.*"]};await directory.Write("a.log","trace\n");var(store,fence,at)=await StartSource(source);await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(1,(await pump.VisitAsync(fence,source,at)).CommittedRecords);
        Assert.True(CreateHardLink(directory.PathFor("alias.log"),directory.PathFor("a.log"),IntPtr.Zero),"Windows hard link creation failed.");Assert.Equal(0,(await pump.VisitAsync(fence,source,at.AddSeconds(1))).CommittedRecords);
        Assert.Single(await store.GenerationsAsync(fence.Scope,"cam","file"));await using var db=SondaDbContext.Open(pg.Connection);Assert.Equal(1,await db.Set<FileRecordRow>().CountAsync(r=>r.SessionId==fence.Scope.SessionId));
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string newName,string existing,IntPtr attributes);
    [Fact] public async Task Two_sources_share_durable_admission_without_reusing_physical_keys()
    {
        using var first=new AcquisitionDirectory();using var second=new AcquisitionDirectory();var(sample,scope)=await PolicySetup();var a=new SourceConfiguration{ProfileId="cam",SourceKey="a",Root=first.Root,Enabled=true};var b=a with{SourceKey="b",Root=second.Root};var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,a,"test");await store.RegisterAsync(scope,b,"test");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
        await first.Write("a.log","CAM process started\nFinding order OrderID=x\n");await second.Write("b.log","Order sent OrderID=x\nCAM process completed\n");await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(2,(await pump.VisitAsync(fence,a,sample.AsOf)).CommittedRecords);Assert.Equal(2,(await pump.VisitAsync(fence,b,sample.AsOf.AddSeconds(1))).CommittedRecords);
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,"cam");Assert.Equal(2,state.Interpreter.Runs.Length);Assert.All(state.Interpreter.Runs,r=>Assert.Equal(Sonda.Domain.Runs.DetectionResult.Success,r.Result));Assert.Equal(Enumerable.Range(1,state.Receipts.Length),state.Receipts.Select(r=>r.Command.Sequence));
        var memory=new Sonda.Application.Processing.PolicySession(sample.Profiles[0],sample.Seed);foreach(var r in state.Receipts)memory.Execute(r.Command);Assert.Equal(SimulationJson.Serialize(state),SimulationJson.Serialize(memory.Export()));
    }
}
