using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;

namespace Sonda.Phase3.Fixtures;

public sealed record PolicyFixture(string Name,PolicySimulationRequest Request,int Evaluated,int Successful,int Orders,int Incidents,bool Complete=true);
public static class PolicyFixtureCatalog
{
    public static readonly DateTimeOffset Start=DateTimeOffset.Parse("2026-09-23T14:00:00Z");
    public static Profile Profile()=>new()
    {
        TeamId="phase3-fixtures",ApplicationId="application",Id="profile",Name="Synthetic revision-2",Identifier=new(){Namespace="order",Expression="OrderID"},
        Policy=new(){RoutingContract="routing-v1",RecoveryCompatibility="recovery-v1",DeadlineClock=DeadlineClock.Processing,ReadFreshness=TimeSpan.FromMinutes(2)},
        Rules=[Rule("b",RuleRole.CycleBegin,"APP BEGIN"),Rule("s",RuleRole.CycleSuccess,"APP SUCCESS"),Rule("f",RuleRole.CycleFailure,"APP FAILURE"),Rule("e",RuleRole.CycleEnd,"APP END"),
            Rule("ob",RuleRole.OrderBegin,"ORDER BEGIN",TargetScope.Order),Rule("os",RuleRole.OrderSuccess,"ORDER SUCCESS",TargetScope.Order),Rule("of",RuleRole.OrderFailure,"ORDER FAILURE",TargetScope.Order),
            Rule("diagnostic",RuleRole.Detection,"DIAGNOSTIC") with {Classification=Classification.Error,ConditionKey="diagnostic",Recovery=RecoveryPolicy.NextSuccessfulRun,Priority=10}]
    };
    private static Rule Rule(string key,RuleRole role,string text,TargetScope scope=TargetScope.Application)=>new(){Key=key,Role=role,Target=scope,Alternatives=[new(){Expression=text}]};
    public static IEnumerable<PolicyFixture> All()
    {
        yield return Build("application-duration",p=>p with {CompletionMode=CompletionMode.ExplicitEnd,CycleTiming=new(){ExpectedDuration=TimeSpan.FromMinutes(1),GracePeriod=TimeSpan.FromMinutes(1),FallbackTimeout=TimeSpan.FromMinutes(3)}},b=>{b.E("APP BEGIN");b.Clock(2);b.Clock(5);},1,0,0,1);
        yield return Build("failure-before-missing-end",p=>p with {CompletionMode=CompletionMode.ExplicitEnd,CycleTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}},b=>{b.E("APP BEGIN");b.E("APP FAILURE");b.Clock(2);},1,0,0,1);
        yield return Build("success-without-end",p=>p with {CompletionMode=CompletionMode.ExplicitEnd,CycleTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}},b=>{b.E("APP BEGIN");b.E("APP SUCCESS");b.Clock(2);},1,0,0,1);
        yield return Build("incomplete-explicit-end",p=>p with {CompletionMode=CompletionMode.ExplicitEnd},b=>{b.E("APP BEGIN");b.E("ORDER BEGIN OrderID=x");b.E("APP END");},2,0,1,2);
        yield return Build("timing-disabled",p=>p,b=>{b.E("APP BEGIN");b.Clock(20);},0,0,0,0);
        yield return Build("order-fallback",p=>p with {Policy=p.Policy! with {OrderFallbackEnabled=true},OrderTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}},b=>{b.E("APP BEGIN");b.E("ORDER BEGIN OrderID=x");b.Clock(2);b.E("APP SUCCESS");},2,1,1,1);
        yield return Build("boundary-before-timer",p=>p with {Policy=p.Policy! with {OrderFallbackEnabled=true},OrderTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}},b=>{b.E("APP BEGIN");b.E("ORDER BEGIN OrderID=x");b.E("ORDER SUCCESS OrderID=x");b.E("APP SUCCESS");b.Clock(2);},2,2,1,0);
        yield return Build("backlog-frontier",p=>p with {CycleTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(1)}},b=>{b.E("APP BEGIN");b.Clock(2,1);b.E("APP SUCCESS");b.Clock(3);},1,1,0,0,false);
        yield return Build("same-cycle-retry",p=>p,b=>{b.E("APP BEGIN");b.E("ORDER BEGIN OrderID=x");b.E("ORDER FAILURE OrderID=x");b.E("ORDER BEGIN OrderID=x");b.E("ORDER SUCCESS OrderID=x");b.E("APP SUCCESS");},3,2,2,1);
        yield return Build("open-duplicate-begin",p=>p,b=>{b.E("APP BEGIN");b.E("ORDER BEGIN OrderID=x");b.E("ORDER BEGIN OrderID=x");b.E("APP SUCCESS");},0,0,0,0,false);
        yield return Build("correlated-overlap",Correlated,b=>{b.E("APP BEGIN cycle=A");b.E("APP BEGIN cycle=B");b.E("ORDER BEGIN OrderID=x cycle=A");b.E("ORDER BEGIN OrderID=x cycle=B");b.E("ORDER FAILURE OrderID=x cycle=A");b.E("ORDER SUCCESS OrderID=x cycle=B");b.E("APP SUCCESS cycle=A");b.E("APP SUCCESS cycle=B");},4,3,2,1);
        yield return Build("missing-correlation",Correlated,b=>{b.E("APP BEGIN cycle=A");b.E("ORDER BEGIN OrderID=x");b.E("APP SUCCESS cycle=A");},0,0,0,0,false);
        yield return Build("serial-new-begin",p=>p with {Policy=p.Policy! with {UnexpectedBegin=UnexpectedBeginPolicy.CloseIncompleteAndStartNew}},b=>{b.E("APP BEGIN");b.E("ORDER BEGIN OrderID=x");b.E("APP BEGIN");},2,0,1,2);
        yield return Build("explicit-end-conflicts",p=>p with {CompletionMode=CompletionMode.ExplicitEnd},b=>{b.E("APP BEGIN");b.E("APP SUCCESS");b.E("APP FAILURE");b.E("APP END");},1,0,0,1);
        yield return Build("same-input-contradiction",p=>p,b=>{b.E("APP BEGIN");b.E("APP SUCCESS APP FAILURE");b.E("APP END");},0,0,0,0,false);
        yield return Build("failure-and-ignore",p=>p with {Rules=[..p.Rules,Rule("ignore",RuleRole.Detection,"ORDER FAILURE",TargetScope.Order) with {Classification=Classification.Ignore,ConditionKey="ignore",Priority=50}]},b=>{b.E("APP BEGIN");b.E("ORDER BEGIN OrderID=x");b.E("ORDER FAILURE OrderID=x");b.E("APP SUCCESS");},2,1,1,1);
        yield return Build("success-with-diagnostic",p=>p,b=>{b.E("APP BEGIN");b.E("DIAGNOSTIC");b.E("APP SUCCESS");},1,1,0,1);
        yield return Build("later-clean-recovery",p=>p,b=>{b.E("APP BEGIN");b.E("DIAGNOSTIC");b.E("APP SUCCESS");b.E("APP BEGIN");b.E("APP SUCCESS");},2,2,0,1);
        yield return Build("manual-workflow",p=>p,b=>{b.E("APP BEGIN");b.E("APP FAILURE");b.Status(IncidentStatus.Investigating,0);b.Status(IncidentStatus.Active,1);b.Status(IncidentStatus.Resolved,2);},1,0,0,1);
        yield return Build("manual-then-success",p=>p,b=>{b.E("APP BEGIN");b.E("APP FAILURE");b.Status(IncidentStatus.Resolved,0);b.E("APP BEGIN");b.E("APP SUCCESS");},2,1,0,1);
        yield return Build("recurrence-episodes",p=>p,b=>{b.E("APP BEGIN");b.E("APP FAILURE");b.Status(IncidentStatus.Resolved,0);b.E("APP BEGIN");b.E("APP FAILURE");},2,0,0,2);
        yield return Build("recovery-causality",Correlated,b=>{b.E("APP BEGIN cycle=A");b.E("APP BEGIN cycle=B");b.E("APP FAILURE cycle=A");b.E("APP SUCCESS cycle=B");},2,1,0,1);
        yield return Build("late-terminal",Correlated,b=>{b.E("APP BEGIN cycle=A");b.E("APP SUCCESS cycle=A");b.E("APP FAILURE cycle=A");},1,1,0,0);
        yield return Build("late-diagnostic",p=>Correlated(p) with {Policy=Correlated(p).Policy! with {LateObservationRules=["diagnostic"]}},b=>{b.E("APP BEGIN cycle=A");b.E("APP SUCCESS cycle=A");b.E("DIAGNOSTIC cycle=A");},1,1,0,1);
        yield return Build("open-out-of-order",Dated,b=>{b.E("2026-09-23T09:00:00-04:00 APP BEGIN");b.E("2026-09-23T08:59:00-04:00 APP SUCCESS");},0,0,0,0,false);
        yield return Build("clock-and-date",Dated,b=>{b.E("2026-09-22T23:50:00-04:00 APP BEGIN");b.E("2026-09-22T23:51:00-04:00 ORDER BEGIN OrderID=x");b.E("2026-09-22T23:52:00-04:00 ORDER SUCCESS OrderID=x");b.E("2026-09-22T23:53:00-04:00 APP SUCCESS");},0,0,1,0);
        yield return Build("pinned-version-transition",p=>p,b=>{b.E("APP BEGIN");b.Activate(b.Profile with {Version=2});b.E("ORDER BEGIN OrderID=x");b.E("ORDER SUCCESS OrderID=x");b.E("APP SUCCESS");b.E("APP BEGIN");b.E("APP SUCCESS");},3,3,1,0);
        yield return Build("incompatible-transition",p=>p,b=>{b.E("APP BEGIN");b.Activate(b.Profile with {Version=2,Policy=b.Profile.Policy! with {RoutingContract="different"}});b.E("APP SUCCESS");},1,1,0,0,false);
        yield return Build("versioned-recovery",p=>p,b=>{b.E("APP BEGIN");b.E("APP FAILURE");b.Activate(b.Profile with {Version=2});b.E("APP BEGIN");b.E("APP SUCCESS");},2,1,0,1);
        yield return Build("outside-cycle",p=>p with {Policy=p.Policy! with {ApplicationWideRules=["diagnostic"]}},b=>{b.E("DIAGNOSTIC");b.E("DIAGNOSTIC");b.E("APP BEGIN");b.E("APP SUCCESS");},1,1,0,1);
        yield return Build("availability",p=>p,b=>{b.E("APP BEGIN");b.E("APP SUCCESS");b.Read("required",true);b.Read("required",false);},1,1,0,0);
        yield return Build("required-sources",p=>p,b=>{b.E("APP BEGIN");b.E("APP SUCCESS");b.Read("optional",true);},1,1,0,0);
        yield return Build("warning-with-success",p=>p with {Rules=p.Rules.Select(r=>r.Key=="diagnostic"?r with {Classification=Classification.Warning}:r).ToList()},b=>{b.E("APP BEGIN");b.E("DIAGNOSTIC");b.E("APP SUCCESS");},1,1,0,1);
        yield return Build("manual-only-recovery",p=>p with {Rules=p.Rules.Select(r=>r.Key=="diagnostic"?r with {Recovery=RecoveryPolicy.ManualOnly}:r).ToList()},b=>{b.E("APP BEGIN");b.E("DIAGNOSTIC");b.E("APP SUCCESS");b.E("APP BEGIN");b.E("APP SUCCESS");},2,2,0,1);
        yield return Build("changed-diagnostic-key",p=>p,b=>{b.E("APP BEGIN");b.E("DIAGNOSTIC");b.E("APP SUCCESS");b.Activate(b.Profile with {Version=2,Rules=b.Profile.Rules.Select(r=>r.Key=="diagnostic"?r with {ConditionKey="different"}:r).ToList()});b.E("APP BEGIN");b.E("APP SUCCESS");},2,2,0,1);
        yield return Build("incompatible-recovery-contract",p=>p,b=>{b.E("APP BEGIN");b.E("APP FAILURE");b.Activate(b.Profile with {Version=2,Policy=b.Profile.Policy! with {RecoveryCompatibility="different"}});},1,0,0,1,false);
        yield return Build("closed-correlation-reuse",Correlated,b=>{b.E("APP BEGIN cycle=A");b.E("APP SUCCESS cycle=A");b.E("APP BEGIN cycle=A");},1,1,0,0,false);
        yield return Build("serial-begin-rejected",p=>p,b=>{b.E("APP BEGIN");b.E("APP BEGIN");},0,0,0,0,false);
        foreach(var date in new[]{"2026-03-08T01:59:00-05:00","2026-11-01T01:59:00-04:00","2026-09-23T23:59:00-04:00"})
        {
            var start=DateTimeOffset.Parse(date);var due=start.AddMinutes(2);var processed=due.AddDays(1);
            var name="deadline-date-"+start.ToString("MMdd");var p=Dated(Profile());p=p with {Policy=p.Policy! with {DeadlineClock=DeadlineClock.Event,OrderFallbackEnabled=true},OrderTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(2)}};
            PolicyCommand C(int n)=>new(){Id=new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name+n)).AsSpan(0,16)),Sequence=n,ProcessedAt=processed};
            var commands=new[]{C(1) with {Kind=PolicyCommandKind.Evidence,EvidenceKey="1",Raw=date+" APP BEGIN"},C(2) with {Kind=PolicyCommandKind.Evidence,EvidenceKey="2",Raw=date+" ORDER BEGIN OrderID=x"},C(3) with {Kind=PolicyCommandKind.AdvanceTime,EffectiveAt=due,Frontier=new(2,due,0)}};
            yield return new(name,new(p,name,"America/New_York",processed,commands,[]),0,0,1,1);
        }
    }
    private static Profile Correlated(Profile p)=>p with {Policy=p.Policy! with {CycleMode=CycleMode.Correlated,CorrelationExpression=@"cycle=(?<cycle>\w+)",CorrelationEpoch="test"}};
    private static Profile Dated(Profile p)=>p with {Parsing=new(){EntryPattern=@"^(?<timestamp>\S+) (?<message>.*)$",TimestampFormat="yyyy-MM-dd'T'HH:mm:sszzz",TimestampHasDate=true,TimestampHasOffset=true}};
    private static PolicyFixture Build(string name,Func<Profile,Profile> profile,Action<Builder> build,int evaluated,int success,int orders,int incidents,bool complete=true)
    {
        var b=new Builder(profile(Profile()),name);build(b);
        return new(name,new(b.Profile,name,"America/New_York",Start.AddHours(2),b.Commands.ToArray(),["required"]),evaluated,success,orders,incidents,complete);
    }
    public sealed class Builder(Profile profile,string seed)
    {
        public Profile Profile=>profile;public List<PolicyCommand> Commands {get;}=[];
        private DateTimeOffset last=Start;
        private PolicyCommand Next(PolicyCommandKind kind)
        {
            var n=Commands.Count+1;last=last.AddSeconds(1);
            var bytes=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed+":"+n));
            return new(){Id=new Guid(bytes.AsSpan(0,16)),Sequence=n,Kind=kind,ProcessedAt=last};
        }
        public void E(string raw){var c=Next(PolicyCommandKind.Evidence);Commands.Add(c with {Raw=raw,EvidenceKey=$"fixture:{c.Sequence}"});}
        public void Clock(int minutes,int pending=0){var effective=Start.AddMinutes(minutes);if(last<effective)last=effective;var c=Next(PolicyCommandKind.AdvanceTime);Commands.Add(c with {EffectiveAt=effective,Frontier=new(c.Sequence-1,effective,pending)});}
        public void Status(IncidentStatus status,long revision){var c=Next(PolicyCommandKind.ChangeStatus);Commands.Add(c with {IncidentId=$"{seed}:{profile.Id}:incident:0002",TargetStatus=status,ExpectedRevision=revision,Actor="fixture-operator",Reason="Reviewed"});}
        public void Activate(Profile p){var c=Next(PolicyCommandKind.ActivateVersion);Commands.Add(c with {Profile=p});}
        public void Read(string source,bool success){var c=Next(PolicyCommandKind.SourceObservation);Commands.Add(c with {Source=source,ReadSuccess=success,EffectiveAt=c.ProcessedAt});}
    }
}
