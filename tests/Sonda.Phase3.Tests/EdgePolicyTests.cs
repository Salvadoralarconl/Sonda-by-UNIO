using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;
using Sonda.Phase3.Fixtures;
using Xunit;

namespace Sonda.Phase3.Tests;
public sealed class EdgePolicyTests
{
    [Fact] public void Revision2_cannot_downgrade_or_mutate_published_profile()
    {
        var p=PolicyFixtureCatalog.Profile();var s=new PolicySession(p,"immutable");
        var expected=SimulationJson.Serialize(s.Engine.ActiveProfile);p.Rules.Clear();
        Assert.Equal(expected,SimulationJson.Serialize(s.Engine.ActiveProfile));
        var result=s.Execute(new(){Id=Guid.NewGuid(),Sequence=1,Kind=PolicyCommandKind.ActivateVersion,ProcessedAt=T,Profile=s.Engine.ActiveProfile with {Version=2,Policy=null}});
        Assert.Contains(result.Diagnostics,d=>d.Code=="PolicyDowngradeUnsupported");
        Assert.Equal(expected,SimulationJson.Serialize(PolicySession.Restore(s.Export()).Engine.ActiveProfile));
    }
    private static readonly DateTimeOffset T=PolicyFixtureCatalog.Start;
    private static PolicyCommand E(int n,string raw,DateTimeOffset? at=null)=>new(){Id=Guid.NewGuid(),Sequence=n,Kind=PolicyCommandKind.Evidence,EvidenceKey=$"entry:{n}",Raw=raw,ProcessedAt=at??T.AddSeconds(n)};
    private static void Good(PolicySession s,PolicyCommand c){var r=s.Execute(c);Assert.True(r.Disposition!="Rejected",SimulationJson.Serialize(r));}
    [Theory][InlineData("grace")][InlineData("negative")][InlineData("fallback")][InlineData("overlap")][InlineData("order")][InlineData("severity")]
    public void Invalid_policy_configuration_cannot_activate(string kind)
    {
        var p=PolicyFixtureCatalog.Profile();
        p=kind switch
        {
            "grace"=>p with {CycleTiming=new(){GracePeriod=TimeSpan.FromSeconds(1)}},
            "negative"=>p with {CycleTiming=new(){ExpectedDuration=TimeSpan.FromSeconds(-1)}},
            "fallback"=>p with {CycleTiming=new(){ExpectedDuration=TimeSpan.FromMinutes(5),FallbackTimeout=TimeSpan.FromMinutes(1)}},
            "overlap"=>p with {Policy=p.Policy! with {CycleMode=CycleMode.Correlated}},
            "order"=>p with {OrderTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}},
            _=>p with {Policy=p.Policy! with {UndefinedApplicationSeverity=Classification.Ignore}}
        };
        Assert.Contains(ProfileValidator.Validate(p),d=>d.Level==DiagnosticLevel.Error);Assert.Throws<ArgumentException>(()=>new PolicySession(p,"invalid"));
    }
    [Fact] public void Event_deadline_never_implicitly_uses_processing_time()
    {
        var p=PolicyFixtureCatalog.Profile();p=p with {Policy=p.Policy! with {DeadlineClock=DeadlineClock.Event},CycleTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}};
        var s=new PolicySession(p,"anchor");var r=s.Execute(E(1,"APP BEGIN"));Assert.Contains(r.Diagnostics,d=>d.Code=="MissingDeadlineAnchor");Assert.Empty(s.Engine.Runs);
    }
    [Fact] public void Evidence_at_exact_deadline_wins_when_frontier_includes_it()
    {
        var p=PolicyFixtureCatalog.Profile();p=p with {Policy=p.Policy! with {OrderFallbackEnabled=true},OrderTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}};
        var s=new PolicySession(p,"tie");Good(s,E(1,"APP BEGIN"));Good(s,E(2,"ORDER BEGIN OrderID=x"));var due=T.AddSeconds(62);
        Good(s,E(3,"ORDER SUCCESS OrderID=x",due));Good(s,new(){Id=Guid.NewGuid(),Sequence=4,Kind=PolicyCommandKind.AdvanceTime,ProcessedAt=due,EffectiveAt=due,Frontier=new(3,due,0)});
        Good(s,E(5,"APP SUCCESS",due.AddSeconds(1)));Assert.All(s.Engine.Runs,r=>Assert.Equal(DetectionResult.Success,r.Result));Assert.Empty(s.Engine.Incidents);
    }
    [Fact] public void Failure_incident_is_visible_before_explicit_end()
    {
        var s=new PolicySession(PolicyFixtureCatalog.Profile() with {CompletionMode=CompletionMode.ExplicitEnd},"early");Good(s,E(1,"APP BEGIN"));Good(s,E(2,"APP FAILURE"));
        Assert.Equal(RunLifecycle.Running,Assert.Single(s.Engine.Runs).Lifecycle);Assert.Single(s.Engine.Incidents);Good(s,E(3,"APP END"));Assert.Single(s.Engine.Incidents[0].Occurrences);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Manual_resolution_noop_and_reopening_are_explicit(bool investigating)
    {
        var s=new PolicySession(PolicyFixtureCatalog.Profile(),"manual");Good(s,E(1,"APP BEGIN"));Good(s,E(2,"APP FAILURE"));var i=Assert.Single(s.Engine.Incidents);var n=3;
        if(investigating)Good(s,new(){Id=Guid.NewGuid(),Sequence=n++,Kind=PolicyCommandKind.ChangeStatus,ProcessedAt=T.AddSeconds(3),IncidentId=i.Id,TargetStatus=IncidentStatus.Investigating,Actor="operator",ExpectedRevision=0});
        var command=new PolicyCommand{Id=Guid.NewGuid(),Sequence=n++,Kind=PolicyCommandKind.ChangeStatus,ProcessedAt=T.AddSeconds(5),IncidentId=i.Id,TargetStatus=IncidentStatus.Resolved,Actor="operator",Reason="Reviewed",ExpectedRevision=i.PolicyContext!.Revision};
        Good(s,command);var count=i.History.Count;
        var noop=command with {Id=Guid.NewGuid(),Sequence=n++,ExpectedRevision=i.PolicyContext.Revision};Assert.Equal("NoOp",s.Execute(noop).Disposition);Assert.Equal(count,i.History.Count);
        var reopen=noop with {Id=Guid.NewGuid(),Sequence=n,TargetStatus=IncidentStatus.Active};Assert.Contains(s.Execute(reopen).Diagnostics,d=>d.Code=="ReopeningNotSupported");
        Assert.Equal("operator",s.Engine.Incidents[0].History[^1].Actor);
    }
    [Fact] public void Automatic_resolution_then_failure_creates_new_episode()
    {
        var s=new PolicySession(PolicyFixtureCatalog.Profile(),"episodes");string[] lines=["APP BEGIN","APP FAILURE","APP BEGIN","APP SUCCESS","APP BEGIN","APP FAILURE"];
        for(var n=0;n<lines.Length;n++)Good(s,E(n+1,lines[n]));Assert.Equal(2,s.Engine.Incidents.Count);Assert.Equal(s.Engine.Incidents[0].Problem,s.Engine.Incidents[1].Problem);
        Assert.Equal(new[]{1,2},s.Engine.Incidents.Select(i=>i.PolicyContext!.Episode));Assert.Equal(IncidentStatus.Resolved,s.Engine.Incidents[0].Status);Assert.Equal(IncidentStatus.Active,s.Engine.Incidents[1].Status);
    }
    [Fact] public void Manual_only_diagnostic_survives_later_success()
    {
        var p=PolicyFixtureCatalog.Profile();p=p with {Rules=p.Rules.Select(r=>r.Key=="diagnostic"?r with {Recovery=RecoveryPolicy.ManualOnly}:r).ToList()};
        var s=new PolicySession(p,"manual-only");string[] lines=["APP BEGIN","DIAGNOSTIC","APP SUCCESS","APP BEGIN","APP SUCCESS"];
        for(var n=0;n<lines.Length;n++)Good(s,E(n+1,lines[n]));Assert.Equal(IncidentStatus.Active,Assert.Single(s.Engine.Incidents).Status);Assert.Empty(s.Engine.Incidents[0].RecoveryEvents);
    }
    [Fact] public void Partition_error_survives_restart_without_blocking_other_cycle()
    {
        var p=PolicyFixtureCatalog.Profile();p=p with {Policy=p.Policy! with {CycleMode=CycleMode.Correlated,CorrelationExpression=@"cycle=(?<cycle>\w+)",CorrelationEpoch="test"}};
        var s=new PolicySession(p,"partition");Good(s,E(1,"APP BEGIN cycle=A"));Good(s,E(2,"APP BEGIN cycle=B"));Assert.Equal("Rejected",s.Execute(E(3,"APP SUCCESS APP FAILURE cycle=A")).Disposition);
        s=PolicySession.Restore(SimulationJson.Deserialize<PolicySessionState>(SimulationJson.Serialize(s.Export())));Good(s,E(4,"APP SUCCESS cycle=B"));Assert.Equal("BlockedByPriorError",s.Execute(E(5,"APP SUCCESS cycle=A")).Disposition);
        Assert.Null(s.Engine.Runs[0].Result);Assert.Equal(DetectionResult.Success,s.Engine.Runs[1].Result);
    }
    [Fact] public void Old_evidence_after_frontier_uses_only_opted_in_diagnostic()
    {
        var p=PolicyFixtureCatalog.Profile();p=p with {Policy=p.Policy! with {LateObservationRules=["diagnostic"]},Parsing=new(){EntryPattern=@"^(?<timestamp>\S+) (?<message>.*)$",TimestampFormat="yyyy-MM-dd'T'HH:mm:sszzz",TimestampHasDate=true,TimestampHasOffset=true}};
        var s=new PolicySession(p,"late");Good(s,new(){Id=Guid.NewGuid(),Sequence=1,Kind=PolicyCommandKind.AdvanceTime,EffectiveAt=T,ProcessedAt=T,Frontier=new(0,T,0)});
        var r=s.Execute(E(2,"2026-09-23T09:00:00-04:00 DIAGNOSTIC"));Assert.Equal("LateEvidence",r.Disposition);Assert.Empty(s.Engine.Runs);Assert.Single(s.Engine.Incidents);Assert.Single(s.Engine.Incidents[0].PolicyContext!.Observations);
    }
    [Fact] public void Expected_duration_only_is_overdue_without_finalization()
    {
        var p=PolicyFixtureCatalog.Profile() with {CycleTiming=new(){ExpectedDuration=TimeSpan.FromMinutes(1)}};
        var request=new PolicySimulationRequest(p,"overdue","UTC",T.AddMinutes(2),[E(1,"APP BEGIN")],[]);var report=PolicySimulationRunner.Run(request);
        Assert.Single(report.OverdueRunIds);Assert.Null(report.State.Interpreter.Runs[0].Result);Assert.Empty(report.State.Interpreter.Incidents);Assert.Null(report.Metrics.SystemHealthPercentage);
    }
}
