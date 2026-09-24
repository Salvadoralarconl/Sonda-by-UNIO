using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact]
    public async Task Policy_competing_retry_begins_cannot_duplicate_attempt()
    {
        var(sample,scope)=await PolicySetup();var p=sample.Profiles[0].Id;var store=new PostgresPolicyStore(pg.Connection);
        await store.ExecuteAsync(scope,p,PolicyInput(1,"CAM process started"));await store.ExecuteAsync(scope,p,PolicyInput(2,"Finding order OrderID=x"));await store.ExecuteAsync(scope,p,PolicyInput(3,"Unable to send order OrderID=x"));
        var a=PolicyInput(4,"Finding order OrderID=x");var b=a with {Id=Guid.NewGuid(),EvidenceKey="other-entry"};
        var outcomes=await Task.WhenAll(Attempt(a),Attempt(b));Assert.Equal(1,outcomes.Count(x=>x));
        var state=await store.ReadAsync(scope,p);Assert.Equal(new[]{1,2},state.Interpreter.Runs.Where(r=>r.Scope==Sonda.Domain.Profiles.TargetScope.Order).Select(r=>r.AttemptNumber));
        await store.ExecuteAsync(scope,p,PolicyInput(5,"Order sent OrderID=x"));Assert.Equal(IncidentStatus.Resolved,Assert.Single((await store.ReadAsync(scope,p)).Interpreter.Incidents).Status);
        async Task<bool> Attempt(PolicyCommand command){try{return (await new PostgresPolicyStore(pg.Connection).ExecuteAsync(scope,p,command)).Disposition=="Applied";}catch(InterpretationException e)when(e.Code=="CommandOrderConflict"){return false;}}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Policy_manual_and_recovery_lock_orders_preserve_resolution_origin(bool manualFirst)
    {
        var(sample,scope)=await PolicySetup();var p=sample.Profiles[0].Id;var store=new PostgresPolicyStore(pg.Connection);
        string[] lines=["CAM process started","CAM process failed","CAM process started"];
        for(var n=0;n<lines.Length;n++)await store.ExecuteAsync(scope,p,PolicyInput(n+1,lines[n]));
        var incident=Assert.Single((await store.ReadAsync(scope,p)).Interpreter.Incidents);
        var manual=new PolicyCommand{Id=Guid.NewGuid(),Sequence=manualFirst?4:5,Kind=PolicyCommandKind.ChangeStatus,ProcessedAt=sample.AsOf,IncidentId=incident.Id,ExpectedRevision=0,TargetStatus=IncidentStatus.Resolved,Actor="operator",Reason="Reviewed"};
        var success=PolicyInput(manualFirst?5:4,"CAM process completed") with {ProcessedAt=sample.AsOf};
        using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var first=manualFirst?manual:success;var second=manualFirst?success:manual;
        var firstTask=Task.Run(()=>new PostgresPolicyStore(pg.Connection,b=>{if(b==PersistenceBoundary.AfterEvidence){reached.Set();if(!release.Wait(TimeSpan.FromSeconds(20)))throw new TimeoutException("test synchronization");}}).ExecuteAsync(scope,p,first));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(20)));var secondTask=new PostgresPolicyStore(pg.Connection).ExecuteAsync(scope,p,second);release.Set();
        await firstTask;var secondResult=await secondTask;
        var result=Assert.Single((await store.ReadAsync(scope,p)).Interpreter.Incidents);Assert.Single(result.Recoveries);Assert.Equal(2,result.History.Length);
        Assert.Equal(manualFirst?"Manual":"Automatic",result.PolicyContext!.ResolutionKind);
        if(!manualFirst)Assert.Contains(secondResult.Diagnostics,d=>d.Code=="RevisionConflict");
    }
    [Fact]
    public async Task Legacy_lane_drains_then_uses_revision2_without_rewriting_old_receipts()
    {
        var(sample,scope,inputs)=await Setup("mixed-orders");await Apply(inputs);var original=await new PostgresProcessingStore(pg.Connection).ReadStateAsync(scope,"cam");
        var old=sample.Profiles[0];var next=old with {Version=2,Policy=new(){RoutingContract="serial",RecoveryCompatibility="orders",DeadlineClock=Sonda.Domain.Profiles.DeadlineClock.Processing}};
        var config=new PostgresConfigurationStore(pg.Connection);var revision=await config.EditDraftAsync(next,0,Guid.NewGuid());await config.PublishAsync(sample with {Profiles=[next]},revision,Guid.NewGuid());
        await using(var db=SondaDbContext.Open(pg.Connection)){var lane=(await db.Set<LaneRow>().FindAsync(scope.TeamId,scope.SessionId,"cam"))!;await config.ActivateAsync(scope,"cam",2,lane.Revision,1,Guid.NewGuid());}
        Assert.True((await new PostgresProcessingStore(pg.Connection).ProcessAsync(inputs[0])).Replayed);
        var command=new PolicyCommand{Id=Guid.NewGuid(),Sequence=inputs.Length+1,Kind=PolicyCommandKind.Evidence,ProcessedAt=sample.AsOf,EvidenceKey="new-version",SampleDate=sample.SampleDate,Raw="10:30:00 CAM process started"};
        var store=new PostgresPolicyStore(pg.Connection);Assert.Equal("Applied",(await store.ExecuteAsync(scope,"cam",command)).Disposition);
        var state=await store.ReadAsync(scope,"cam");Assert.Equal(SimulationJson.Serialize(original.Interpreter.Runs),SimulationJson.Serialize(state.Interpreter.Runs.Take(original.Interpreter.Runs.Length).ToArray()));Assert.Equal(2,state.Interpreter.Runs[^1].PolicyContext!.ProfileVersion);
    }
}
