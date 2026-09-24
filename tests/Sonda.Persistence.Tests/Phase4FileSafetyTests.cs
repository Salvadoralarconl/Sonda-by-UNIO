using System.Text;
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
    private sealed class AcquisitionDirectory:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"sonda-synthetic-"+Guid.NewGuid().ToString("N"));
        public AcquisitionDirectory()=>Directory.CreateDirectory(Root);
        public string PathFor(string name)=>Path.Combine(Root,name);
        public Task Write(string name,string value)=>File.WriteAllTextAsync(PathFor(name),value,new UTF8Encoding(false,true));
        public void Dispose()=>Directory.Delete(Root,true);
    }
    private async Task<(PostgresIngestionStore Store,OwnerFence Fence,DateTimeOffset At)> StartSource(SourceConfiguration config)
    {
        var(sample,scope)=await PolicySetup();var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,config,"synthetic-host");
        return(store,await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2)),sample.AsOf);
    }
    [Fact] public async Task Ordered_daily_files_and_bounded_backlog_reconcile_without_watcher_events()
    {
        using var directory=new AcquisitionDirectory();
        var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true,Rotation=RotationMode.OrderedFiles,FileOrderExpression=@"^(?<sequence>\d{8})\.log$",ReadBytes=32,VisitBytes=128,MaximumRecordBytes=128,VisitRecords=2};
        await directory.Write("20260924.log","CAM process completed\n");await directory.Write("20260923.log","CAM process started\n"+string.Concat(Enumerable.Repeat("trace\n",40)));
        var(store,fence,at)=await StartSource(config);await using var pump=new FileIngestionPump(store,new WindowsFileSource());
        long lastBacklog=long.MaxValue;int total=0;
        for(var n=0;n<30;n++)
        {
            var visit=await pump.VisitAsync(fence,config,at.AddSeconds(n));Assert.InRange(visit.ReadBytes,0,128);Assert.InRange(visit.CommittedRecords,0,2);Assert.True(visit.BytesBehind<=lastBacklog);lastBacklog=visit.BytesBehind!.Value;total+=visit.CommittedRecords;
            if(visit.State==ReaderState.Following)break;
        }
        Assert.Equal(42,total);Assert.Equal(0,lastBacklog);var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam");Assert.Equal(Sonda.Domain.Runs.DetectionResult.Success,Assert.Single(state.Interpreter.Runs).Result);
        Assert.Equal(0,(await pump.VisitAsync(fence,config,at.AddMinutes(1))).ReadBytes);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Lost_unread_tail_or_rapid_regrowth_blocks_without_skipping(bool regrow)
    {
        using var directory=new AcquisitionDirectory();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true,VisitRecords=1};
        await directory.Write("a.log","one\ntwo\nthree\n");var(store,fence,at)=await StartSource(config);
        await using(var pump=new FileIngestionPump(store,new WindowsFileSource()))Assert.Equal(1,(await pump.VisitAsync(fence,config,at)).CommittedRecords);
        var old=Assert.Single(await store.GenerationsAsync(fence.Scope,"cam","file"));
        if(regrow)await directory.Write("a.log","changed content longer than original\n");else{File.Delete(directory.PathFor("a.log"));await directory.Write("a.log","new\n");}
        await using var restarted=new FileIngestionPump(store,new WindowsFileSource());var visit=await restarted.VisitAsync(fence,config,at.AddSeconds(1));Assert.Contains(visit.State,new[]{ReaderState.Gap,ReaderState.IdentityUncertain});Assert.Equal(0,visit.CommittedRecords);
        Assert.Equal(old.Offset,(await store.CheckpointAsync(fence.Scope,old.Id)).Offset);Assert.NotNull((await store.GenerationsAsync(fence.Scope,"cam","file")).Single(g=>g.Id==old.Id).Gap);
        Assert.Empty((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter.Incidents);
    }
    [Theory][InlineData("invalid")][InlineData("bom")][InlineData("oversize")][InlineData("unterminated")]
    public async Task Invalid_or_incomplete_bytes_never_advance_their_range(string kind)
    {
        using var directory=new AcquisitionDirectory();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true,ReadBytes=8,VisitBytes=64,MaximumRecordBytes=32};
        var bytes=kind switch{"invalid"=>new byte[]{0xff,10},"bom"=>new byte[]{0xff,0xfe,65,0,10,0},"oversize"=>Encoding.UTF8.GetBytes(new string('a',40)+"\n"),_=>Encoding.UTF8.GetBytes("trace")};
        await File.WriteAllBytesAsync(directory.PathFor("a.log"),bytes);var(store,fence,at)=await StartSource(config);await using var pump=new FileIngestionPump(store,new WindowsFileSource());var visit=await pump.VisitAsync(fence,config,at);
        Assert.Equal(kind=="unterminated"?ReaderState.WaitingForPartial:ReaderState.Blocked,visit.State);Assert.Equal(0,visit.CommittedRecords);Assert.Equal(0,Assert.Single(await store.GenerationsAsync(fence.Scope,"cam","file")).Offset);
        var second=await pump.VisitAsync(fence,config,at.AddSeconds(1));if(kind!="unterminated")Assert.Equal(0,second.ReadBytes);
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam");Assert.Empty(state.Interpreter.Runs);Assert.Empty(state.Interpreter.Incidents);
    }
    [Fact] public async Task Disable_resume_and_source_revision_preserve_generation_configuration()
    {
        using var directory=new AcquisitionDirectory();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true};await directory.Write("a.log","trace\n");
        var(store,fence,at)=await StartSource(config);await using var pump=new FileIngestionPump(store,new WindowsFileSource());await pump.VisitAsync(fence,config,at);var old=Assert.Single(await store.GenerationsAsync(fence.Scope,"cam","file"));
        await store.SetEnabledAsync(fence,"cam","file",false);var disabled=Assert.Single(await store.SourcesAsync(fence.Scope));Assert.Equal(ReaderState.Disabled,(await pump.VisitAsync(fence,disabled,at)).State);
        await File.AppendAllTextAsync(directory.PathFor("a.log"),"next\n");await store.SetEnabledAsync(fence,"cam","file",true);
        var next=config with{Revision=2,PollInterval=TimeSpan.FromSeconds(2)};await store.ChangeSourceAsync(fence,next,1);
        Assert.Equal(1,(await pump.VisitAsync(fence,next,at.AddSeconds(1))).CommittedRecords);
        Assert.Equal(1,Assert.Single(await store.GenerationsAsync(fence.Scope,"cam","file")).Configuration.Revision);
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.ChangeSourceAsync(fence,next with{Revision=3,Encoding=FileEncoding.Utf16BigEndian},2));
        Assert.Equal(old.Id,Assert.Single(await store.GenerationsAsync(fence.Scope,"cam","file")).Id);
    }
    [Fact] public async Task Backward_clock_does_not_poison_the_pending_slot()
    {
        var(fence,generation,source)=await IngestionSetup();var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.UtcNow;var first=Line("trace");await store.CommitAsync(fence,generation,first,at);
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.CommitAsync(fence,generation,Line("next",first.End),at.AddSeconds(-1)));
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.ObserveSourceAsync(fence,"cam",new(source.SourceKey,ReaderState.Following,"read",at.AddSeconds(-1),at.AddSeconds(-1),ReadSuccess:true)));
        Assert.Null(await store.RecoverPendingAsync(fence));await store.CommitAsync(fence,generation,Line("next",first.End),at.AddSeconds(1));
    }
    [Fact] public async Task Local_sharing_denial_is_operational_only_then_read_recovers()
    {
        using var directory=new AcquisitionDirectory();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true};await directory.Write("a.log","trace\n");var(store,fence,at)=await StartSource(config);
        await using var pump=new FileIngestionPump(store,new WindowsFileSource());using(var exclusive=new FileStream(directory.PathFor("a.log"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Assert.Equal(ReaderState.Unavailable,(await pump.VisitAsync(fence,config,at)).State);
        Assert.Equal(ReaderState.Following,(await pump.VisitAsync(fence,config,at.AddSeconds(1))).State);var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam");Assert.Empty(state.Interpreter.Runs);Assert.Empty(state.Interpreter.Incidents);
    }
    [Fact] public async Task Initially_disabled_source_can_be_enabled_without_mutating_its_framing_revision()
    {
        using var directory=new AcquisitionDirectory();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=false};await directory.Write("a.log","trace\n");var(store,fence,at)=await StartSource(config);
        await store.SetEnabledAsync(fence,"cam","file",true);var enabled=Assert.Single(await store.SourcesAsync(fence.Scope));await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(1,(await pump.VisitAsync(fence,enabled,at)).CommittedRecords);
        Assert.False(Assert.Single(await store.GenerationsAsync(fence.Scope,"cam","file")).Configuration.Enabled);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Earlier_bytes_cannot_arrive_after_a_committed_successor(bool append)
    {
        using var directory=new AcquisitionDirectory();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true,Rotation=RotationMode.OrderedFiles,FileOrderExpression=@"^(?<sequence>\d+)\.log$"};await directory.Write("02.log","second\n");if(append)await directory.Write("01.log","first\n");
        var(store,fence,at)=await StartSource(config);await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(append?2:1,(await pump.VisitAsync(fence,config,at)).CommittedRecords);
        if(append)await File.AppendAllTextAsync(directory.PathFor("01.log"),"late\n");else await directory.Write("01.log","late\n");
        var visit=await pump.VisitAsync(fence,config,at.AddSeconds(1));Assert.Equal(ReaderState.Gap,visit.State);Assert.Equal(0,visit.CommittedRecords);Assert.Equal("EarlierBytesAfterCommittedSuccessor",visit.Code);
    }
}
