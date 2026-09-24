using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Activation_and_begin_race_obeys_command_order(bool activationFirst)
    {
        var(sample,scope,activation)=await PolicyBoundarySetup(PolicyCommandKind.ActivateVersion);
        var profile=sample.Profiles[0].Id;var store=new PostgresPolicyStore(pg.Connection);
        await store.ExecuteAsync(scope,profile,PolicyInput(2,"CAM process completed"));
        activation=activation with {Sequence=activationFirst?3:4};
        var begin=PolicyInput(activationFirst?4:3,"CAM process started") with {ProcessedAt=sample.AsOf};
        await OrderedRace(scope,profile,activationFirst?activation:begin,activationFirst?begin:activation);
        var state=await store.ReadAsync(scope,profile);
        Assert.Equal(activationFirst?2:1,state.Interpreter.Runs[^1].PolicyContext!.ProfileVersion);
        Assert.Equal(2,state.Interpreter.Versions!.ActiveVersion);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Evidence_and_timer_race_respects_declared_frontier(bool evidenceFirst)
    {
        var(sample,scope,clock)=await PolicyBoundarySetup(PolicyCommandKind.AdvanceTime);
        var profile=sample.Profiles[0].Id;
        clock=clock with {Sequence=evidenceFirst?3:2,Frontier=new(evidenceFirst?2:1,sample.AsOf,0)};
        var success=PolicyInput(evidenceFirst?2:3,"CAM process completed") with {ProcessedAt=sample.AsOf};
        await OrderedRace(scope,profile,evidenceFirst?success:clock,evidenceFirst?clock:success);
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,profile);
        Assert.Equal(evidenceFirst?Sonda.Domain.Runs.DetectionResult.Success:Sonda.Domain.Runs.DetectionResult.Undefined,Assert.Single(state.Interpreter.Runs).Result);
        await using var db=SondaDbContext.Open(pg.Connection);
        Assert.Equal(1,await db.Set<FactRow>().CountAsync(x=>x.SessionId==scope.SessionId));
    }
    private async Task OrderedRace(Sonda.Application.Persistence.ProcessingScope scope,string profile,PolicyCommand first,PolicyCommand second)
    {
        using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var task=Task.Run(()=>new PostgresPolicyStore(pg.Connection,b=>{if(b==Sonda.Application.Persistence.PersistenceBoundary.AfterEvidence){reached.Set();if(!release.Wait(TimeSpan.FromSeconds(20)))throw new TimeoutException();}}).ExecuteAsync(scope,profile,first));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(20)));
        var contender=new PostgresPolicyStore(pg.Connection).ExecuteAsync(scope,profile,second);release.Set();
        Assert.NotEqual("Rejected",(await task).Disposition);Assert.NotEqual("Rejected",(await contender).Disposition);
    }
    [Theory][InlineData(PolicyCommandKind.AdvanceTime)][InlineData(PolicyCommandKind.ChangeStatus)][InlineData(PolicyCommandKind.ActivateVersion)]
    public async Task Policy_actual_commit_rejection_is_atomic_and_retryable(PolicyCommandKind kind)
    {
        var(sample,scope,command)=await PolicyBoundarySetup(kind);var p=sample.Profiles[0].Id;var store=new PostgresPolicyStore(pg.Connection);
        var before=SimulationJson.Serialize(await store.ReadAsync(scope,p));
        await using var owner=SondaDbContext.Open(pg.OwnerConnection);
        await owner.Database.ExecuteSqlRawAsync("CREATE FUNCTION sonda.policy_test_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test deferred rejection'; END $$");
        // The interpolated value is a generated Guid, never external SQL text.
        var ddl="CREATE CONSTRAINT TRIGGER policy_test_commit_failure AFTER INSERT ON sonda.processing_receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.request_id='"+command.Id+"'::uuid) EXECUTE FUNCTION sonda.policy_test_commit_failure()";
        await owner.Database.ExecuteSqlRawAsync(ddl);
        try
        {
            await Assert.ThrowsAsync<PostgresException>(()=>store.ExecuteAsync(scope,p,command));
            Assert.Equal(before,SimulationJson.Serialize(await store.ReadAsync(scope,p)));
        }
        finally {await owner.Database.ExecuteSqlRawAsync("DROP TRIGGER policy_test_commit_failure ON sonda.processing_receipts; DROP FUNCTION sonda.policy_test_commit_failure()");}
        Assert.Equal("Applied",(await store.ExecuteAsync(scope,p,command)).Disposition);
        var after=SimulationJson.Serialize(await store.ReadAsync(scope,p));await store.ExecuteAsync(scope,p,command);
        Assert.Equal(after,SimulationJson.Serialize(await store.ReadAsync(scope,p)));
    }
    [Theory][InlineData("pinned")][InlineData("episode")][InlineData("observation")]
    public async Task Revision2_database_rejects_invalid_policy_context(string kind)
    {
        var(sample,scope)=await PolicySetup(outside:true);var p=sample.Profiles[0].Id;var store=new PostgresPolicyStore(pg.Connection);
        await store.ExecuteAsync(scope,p,PolicyInput(1,"diagnostic"));await store.ExecuteAsync(scope,p,PolicyInput(2,"CAM process started"));
        await using var db=SondaDbContext.Open(pg.Connection);
        var sql=kind switch {
            "pinned"=>"UPDATE sonda.runs SET payload=jsonb_set(payload::jsonb,'{{policyContext,profileVersion}}','99')::text WHERE session_id={0}",
            "episode"=>"UPDATE sonda.incidents SET policy_context=jsonb_set(policy_context::jsonb,'{{episode}}','2')::text WHERE session_id={0}",
            _=>"UPDATE sonda.incident_occurrences SET run_id=(SELECT id FROM sonda.runs WHERE session_id={0} LIMIT 1) WHERE session_id={0}"};
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql,scope.SessionId));
    }
}
