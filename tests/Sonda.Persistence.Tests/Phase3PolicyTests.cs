using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed partial class PersistenceTests
{
    private async Task<(SimulationRequest Sample,ProcessingScope Scope)> PolicySetup(bool timeout=false,bool outside=false)
    {
        var sample=Sample();var p=sample.Profiles[0] with {TeamId="policy-"+Guid.NewGuid().ToString("N"),Parsing=new(),
            Policy=new(){RoutingContract="serial",RecoveryCompatibility="orders",DeadlineClock=DeadlineClock.Processing},
            CycleTiming=timeout?new(){FallbackTimeout=TimeSpan.FromMinutes(1)}:new()};
        if(outside)
        {
            p.Rules.Add(new(){Key="outside",Role=RuleRole.Detection,Target=TargetScope.Application,ConditionKey="outage",Classification=Classification.Error,Recovery=RecoveryPolicy.NextSuccessfulRun,Alternatives=[new(){Expression="diagnostic"}]});
            p=p with {Policy=p.Policy! with {ApplicationWideRules=["outside"]}};
        }
        sample=sample with {Profiles=[p],Entries=[]};
        var id=await new PostgresConfigurationStore(pg.Connection).CreateSessionAsync(sample);
        return(sample,new(p.TeamId,id,p.ApplicationId));
    }
    private static PolicyCommand PolicyInput(int sequence,string raw)=>new(){Id=Guid.NewGuid(),Sequence=sequence,Kind=PolicyCommandKind.Evidence,
        EvidenceKey=$"sample:{sequence}",Raw=raw,ProcessedAt=DateTimeOffset.Parse("2026-09-23T14:00:00Z").AddSeconds(sequence)};
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Revision2_persistence_matches_command_engine_and_replays(bool outside)
    {
        var(sample,scope)=await PolicySetup(outside:outside);var p=sample.Profiles[0];var memory=new PolicySession(p,sample.Seed);
        string[] lines=outside?["diagnostic","diagnostic","CAM process started","CAM process completed"]:
            ["CAM process started","Finding order OrderID=A","Unable to send order OrderID=A","Finding order OrderID=A","Order sent OrderID=A","CAM process completed"];
        List<PolicyCommand> commands=[];
        for(var n=0;n<lines.Length;n++)
        {
            var command=PolicyInput(n+1,lines[n]);commands.Add(command);var expected=memory.Execute(command);
            Assert.NotEqual("Rejected",expected.Disposition);
            var actual=await new PostgresPolicyStore(pg.Connection).ExecuteAsync(scope,p.Id,command);
            Assert.Equal(SimulationJson.Serialize(expected),SimulationJson.Serialize(actual));
            var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,p.Id);
            Assert.Equal(SimulationJson.Serialize(memory.Export()),SimulationJson.Serialize(state));
        }
        var before=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,p.Id);
        foreach(var command in commands)await new PostgresPolicyStore(pg.Connection).ExecuteAsync(scope,p.Id,command);
        Assert.Equal(SimulationJson.Serialize(before),SimulationJson.Serialize(await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,p.Id)));
    }
    [Theory] [InlineData(PersistenceBoundary.AfterEvidence)] [InlineData(PersistenceBoundary.AfterRuns)] [InlineData(PersistenceBoundary.AfterFacts)] [InlineData(PersistenceBoundary.BeforeCommit)]
    public async Task Revision2_deadline_transaction_rollback_and_retry(PersistenceBoundary boundary)
    {
        var(sample,scope)=await PolicySetup(timeout:true);var p=sample.Profiles[0];var store=new PostgresPolicyStore(pg.Connection);
        await store.ExecuteAsync(scope,p.Id,PolicyInput(1,"CAM process started"));
        var clock=new PolicyCommand{Id=Guid.NewGuid(),Sequence=2,Kind=PolicyCommandKind.AdvanceTime,ProcessedAt=sample.AsOf,EffectiveAt=sample.AsOf,Frontier=new(1,sample.AsOf,0)};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new PostgresPolicyStore(pg.Connection,b=>{if(b==boundary)throw new InvalidOperationException("injected");}).ExecuteAsync(scope,p.Id,clock));
        var before=await store.ReadAsync(scope,p.Id);Assert.Null(Assert.Single(before.Interpreter.Runs).Result);
        Assert.Equal("Applied",(await store.ExecuteAsync(scope,p.Id,clock)).Disposition);
        await store.ExecuteAsync(scope,p.Id,clock);var after=await store.ReadAsync(scope,p.Id);
        Assert.Equal(Sonda.Domain.Runs.DetectionResult.Undefined,Assert.Single(after.Interpreter.Runs).Result);
        await using var db=SondaDbContext.Open(pg.Connection);
        Assert.Equal(1,await db.Set<EvidenceRow>().CountAsync(e=>e.SessionId==scope.SessionId));
        Assert.Equal(1,await db.Set<FactRow>().CountAsync(e=>e.SessionId==scope.SessionId));
    }
}
