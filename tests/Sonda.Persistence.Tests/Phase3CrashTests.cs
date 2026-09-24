using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    private async Task<(SimulationRequest Sample,ProcessingScope Scope,PolicyCommand Command)> PolicyBoundarySetup(PolicyCommandKind kind)
    {
        var(sample,scope)=await PolicySetup(timeout:kind==PolicyCommandKind.AdvanceTime);var p=sample.Profiles[0];var store=new PostgresPolicyStore(pg.Connection);
        await store.ExecuteAsync(scope,p.Id,PolicyInput(1,"CAM process started"));
        var command=new PolicyCommand{Id=Guid.NewGuid(),Sequence=2,Kind=kind,ProcessedAt=sample.AsOf};
        if(kind==PolicyCommandKind.AdvanceTime)command=command with {EffectiveAt=sample.AsOf,Frontier=new(1,sample.AsOf,0)};
        if(kind==PolicyCommandKind.ChangeStatus)
        {
            await store.ExecuteAsync(scope,p.Id,PolicyInput(2,"CAM process failed"));var incident=Assert.Single((await store.ReadAsync(scope,p.Id)).Interpreter.Incidents);
            command=command with {Sequence=3,IncidentId=incident.Id,ExpectedRevision=incident.PolicyContext!.Revision,TargetStatus=IncidentStatus.Resolved,Actor="test-operator",Reason="Reviewed"};
        }
        if(kind==PolicyCommandKind.ActivateVersion)
        {
            var next=p with {Version=2};var config=new PostgresConfigurationStore(pg.Connection);var revision=await config.EditDraftAsync(next,0,Guid.NewGuid());
            await config.PublishAsync(sample with {Profiles=[next]},revision,Guid.NewGuid());command=command with {Profile=next};
        }
        return(sample,scope,command);
    }
    [Theory][InlineData(PolicyCommandKind.AdvanceTime)][InlineData(PolicyCommandKind.ChangeStatus)][InlineData(PolicyCommandKind.ActivateVersion)]
    public async Task Policy_commit_then_process_exit_retries_without_new_effects(PolicyCommandKind kind)
    {
        var(sample,scope,command)=await PolicyBoundarySetup(kind);var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var path=Path.Combine(root,"artifacts","test-results",$"policy-crash-{Guid.NewGuid():N}.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,SimulationJson.Serialize(new{Scope=scope,ProfileId=sample.Profiles[0].Id,Command=command}));
        var start=new ProcessStartInfo(Path.Combine(root,".tools","dotnet","dotnet.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{Path.Combine(root,"tools","Sonda.PersistenceHarness","bin","Release","net10.0","Sonda.PersistenceHarness.dll"),"policy-process",path,"crash-after-commit"})start.ArgumentList.Add(arg);
        start.Environment["SONDA_DATABASE"]=pg.Connection;
        using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();
        Assert.True(child.ExitCode==73,$"Child exit {child.ExitCode}: {await stderr}; {await stdout}");
        var store=new PostgresPolicyStore(pg.Connection);var before=await store.ReadAsync(scope,sample.Profiles[0].Id);
        Assert.Equal(command.Id,(await store.ExecuteAsync(scope,sample.Profiles[0].Id,command)).Id);
        Assert.Equal(SimulationJson.Serialize(before),SimulationJson.Serialize(await store.ReadAsync(scope,sample.Profiles[0].Id)));
        var output=Path.Combine(root,"artifacts","phase3","crash");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,kind+".json"),SimulationJson.Serialize(new{kind,childExit=child.ExitCode,committedReceiptFound=true,retryStateUnchanged=true,runs=before.Interpreter.Runs.Length,incidents=before.Interpreter.Incidents.Length,receipts=before.Receipts.Length}));
    }
    public static IEnumerable<object[]> PolicyRollbackCases()=>new[]{PolicyCommandKind.AdvanceTime,PolicyCommandKind.ChangeStatus,PolicyCommandKind.ActivateVersion}.SelectMany(k=>new[]{PersistenceBoundary.AfterEvidence,PersistenceBoundary.AfterRuns,PersistenceBoundary.AfterFacts,PersistenceBoundary.BeforeCommit}.Select(b=>new object[]{k,b}));
    [Theory][MemberData(nameof(PolicyRollbackCases))]
    public async Task Policy_command_family_rolls_back_all_effects(PolicyCommandKind kind,PersistenceBoundary boundary)
    {
        var(sample,scope,command)=await PolicyBoundarySetup(kind);var profile=sample.Profiles[0].Id;var store=new PostgresPolicyStore(pg.Connection);var before=await store.ReadAsync(scope,profile);
        await Assert.ThrowsAsync<IOException>(()=>new PostgresPolicyStore(pg.Connection,b=>{if(b==boundary)throw new IOException("injected");}).ExecuteAsync(scope,profile,command));
        Assert.Equal(SimulationJson.Serialize(before),SimulationJson.Serialize(await store.ReadAsync(scope,profile)));
        Assert.Equal("Applied",(await store.ExecuteAsync(scope,profile,command)).Disposition);
    }
    [Fact]
    public async Task Policy_duplicate_and_competing_workflow_commands_are_serialized()
    {
        var(sample,scope,command)=await PolicyBoundarySetup(PolicyCommandKind.ChangeStatus);var profile=sample.Profiles[0].Id;
        var winners=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>new PostgresPolicyStore(pg.Connection).ExecuteAsync(scope,profile,command)));
        Assert.All(winners,r=>Assert.Equal(command.Id,r.Id));var store=new PostgresPolicyStore(pg.Connection);var state=await store.ReadAsync(scope,profile);
        Assert.Equal(2,Assert.Single(state.Interpreter.Incidents).History.Length);
        var stale=command with {Id=Guid.NewGuid(),Sequence=command.Sequence+1,TargetStatus=IncidentStatus.Active};
        Assert.Contains((await store.ExecuteAsync(scope,profile,stale)).Diagnostics,d=>d.Code=="RevisionConflict");
        await using var db=SondaDbContext.Open(pg.Connection);Assert.Equal(2,await db.Set<HistoryRow>().CountAsync(h=>h.SessionId==scope.SessionId));
    }
}
