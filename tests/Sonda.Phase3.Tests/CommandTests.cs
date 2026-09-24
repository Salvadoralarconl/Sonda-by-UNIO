using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Availability;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;
using Xunit;

namespace Sonda.Phase3.Tests;

public class CommandTests
{
    private static readonly DateTimeOffset Time=DateTimeOffset.Parse("2026-09-23T14:00:00Z");
    private static PolicyCommand Evidence(int n,string raw) => new(){Id=Guid.NewGuid(),Sequence=n,Kind=PolicyCommandKind.Evidence,Raw=raw,EvidenceKey=$"source:{n}",ProcessedAt=Time.AddSeconds(n)};
    private static PolicySession Restart(PolicySession s)=>PolicySession.Restore(SimulationJson.Deserialize<PolicySessionState>(SimulationJson.Serialize(s.Export())));
    private static void Applied(PolicySession s,PolicyCommand c) { var r=s.Execute(c);Assert.True(r.Disposition!="Rejected",SimulationJson.Serialize(r)); }
    [Fact] public void Correlated_cycles_and_restore_preserve_independent_orders()
    {
        var p=PolicyTests.Current();p=p with {Policy=p.Policy! with {CycleMode=CycleMode.Correlated,CorrelationExpression=@"cycle=(?<cycle>\w+)",CorrelationEpoch="test"}};
        p=p with {Rules=p.Rules.Select(r=>r with {Alternatives=r.Alternatives.Select(a=>a with {Kind=PatternKind.Contains}).ToList()}).ToList()};
        var s=new PolicySession(p,"overlap");
        string[] lines=["CAM process started cycle=A","CAM process started cycle=B","Finding order OrderID=x cycle=A","Finding order OrderID=x cycle=B","Unable to send order OrderID=x cycle=A","Order sent OrderID=x cycle=B","CAM process completed cycle=A","CAM process completed cycle=B"];
        for(var n=0;n<lines.Length;n++){Applied(s,Evidence(n+1,lines[n]));s=Restart(s);}
        Assert.Equal(4,s.Engine.Runs.Count);Assert.Equal(IncidentStatus.Active,Assert.Single(s.Engine.Incidents).Status);Assert.Empty(s.Engine.Incidents[0].RecoveryEvents);
    }
    [Fact] public void Compatible_activation_pins_parent_and_new_child()
    {
        var p=PolicyTests.Current();var s=new PolicySession(p,"versions");Applied(s,Evidence(1,"CAM process started"));
        Applied(s,new(){Id=Guid.NewGuid(),Sequence=2,Kind=PolicyCommandKind.ActivateVersion,Profile=p with {Version=2},ProcessedAt=Time.AddSeconds(2)});
        s=Restart(s);Applied(s,Evidence(3,"Finding order OrderID=x"));Applied(s,Evidence(4,"Order sent OrderID=x"));Applied(s,Evidence(5,"CAM process completed"));Applied(s,Evidence(6,"CAM process started"));
        Assert.Equal(new[]{1,1,2},s.Engine.Runs.Select(r=>r.PolicyContext!.ProfileVersion));
    }
    [Fact] public void Incompatible_activation_is_rejected_without_mutation()
    {
        var p=PolicyTests.Current();var s=new PolicySession(p,"versions");Applied(s,Evidence(1,"CAM process started"));
        var receipt=s.Execute(new(){Id=Guid.NewGuid(),Sequence=2,Kind=PolicyCommandKind.ActivateVersion,Profile=p with {Version=2,Policy=p.Policy! with {RoutingContract="changed"}},ProcessedAt=Time.AddSeconds(2)});
        Assert.Equal("Rejected",receipt.Disposition);Assert.Equal(1,s.Engine.ActiveProfile.Version);Assert.Equal(RunLifecycle.Running,Assert.Single(s.Engine.Runs).Lifecycle);
    }
    [Fact] public void Explicit_frontier_and_duplicate_clock_do_not_duplicate_completion()
    {
        var p=PolicyTests.Current() with {CycleTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}};
        var s=new PolicySession(p,"deadline");Applied(s,Evidence(1,"CAM process started"));
        var clock=new PolicyCommand{Id=Guid.NewGuid(),Sequence=2,Kind=PolicyCommandKind.AdvanceTime,EffectiveAt=Time.AddMinutes(2),ProcessedAt=Time.AddMinutes(3),Frontier=new(1,Time.AddMinutes(2),1)};
        Assert.Equal("Rejected",s.Execute(clock).Disposition);Assert.Null(s.Engine.Runs[0].Result);
        clock=clock with {Id=Guid.NewGuid(),Sequence=3,Frontier=new(2,Time.AddMinutes(2),0)};Applied(s,clock);s=Restart(s);
        var before=SimulationJson.Serialize(s.Export());Assert.Equal(clock.Id,s.Execute(clock).Id);Assert.Equal(before,SimulationJson.Serialize(s.Export()));
        Assert.Equal(DetectionResult.Undefined,s.Engine.Runs[0].Result);Assert.Single(s.Engine.Incidents);
    }
    [Fact] public void Outside_cycle_incident_has_observation_without_invented_run()
    {
        var p=PolicyTests.Current();p.Rules.Add(new(){Key="outside",Role=RuleRole.Detection,Target=TargetScope.Application,ConditionKey="outage",Classification=Classification.Error,Recovery=RecoveryPolicy.NextSuccessfulRun,Alternatives=[new(){Expression="diagnostic"}]});
        p=p with {Policy=p.Policy! with {ApplicationWideRules=["outside"]}};
        var s=new PolicySession(p,"outside");Applied(s,Evidence(1,"diagnostic"));Applied(s,Evidence(2,"diagnostic"));Assert.Empty(s.Engine.Runs);Assert.Equal(2,Assert.Single(s.Engine.Incidents).PolicyContext!.Observations.Length);
        s=Restart(s);Applied(s,Evidence(3,"CAM process started"));Applied(s,Evidence(4,"CAM process completed"));Assert.Equal(IncidentStatus.Resolved,s.Engine.Incidents[0].Status);
    }
    [Fact] public void Command_collision_is_rejected()
    {
        var s=new PolicySession(PolicyTests.Current(),"idempotent");var c=Evidence(1,"CAM process started");Applied(s,c);
        Assert.Throws<Sonda.Domain.Evidence.InterpretationException>(()=>s.Execute(c with {Raw="different"}));
    }
    [Fact] public void Availability_does_not_invent_green()
    {
        var p=PolicyTests.Current().Policy! with {ReadFreshness=TimeSpan.FromMinutes(1)};
        var empty=AvailabilityProjection.Calculate(null,["a"],[],p,null,Time);Assert.Equal(AvailabilityState.NotObserved,empty.Availability);Assert.False(empty.HealthyNow);
        SourceObservation[] rows=[new("a",1,Time,Time,true)];
        Assert.False(AvailabilityProjection.Calculate(null,["a"],rows,p,null,Time).HealthyNow);
        Assert.True(AvailabilityProjection.Calculate(ApplicationHealth.Stable,["a"],rows,p,Time,Time).HealthyNow);
        Assert.Equal(AvailabilityState.Stale,AvailabilityProjection.Calculate(ApplicationHealth.Stable,["a"],rows,p,Time,Time.AddMinutes(2)).Availability);
        rows=[..rows,new("a",2,Time.AddMinutes(2),Time.AddMinutes(2),false)];
        Assert.Equal(AvailabilityState.Disconnected,AvailabilityProjection.Calculate(ApplicationHealth.Stable,["a"],rows,p,Time,Time.AddMinutes(2)).Availability);
        Assert.Equal(AvailabilityState.Unknown,AvailabilityProjection.Calculate(ApplicationHealth.Stable,["a","b"],rows.Take(1),p,Time,Time).Availability);
    }
}
