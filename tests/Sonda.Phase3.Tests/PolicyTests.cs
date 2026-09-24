using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Processing;
using Sonda.Domain.Profiles;
using Sonda.Domain.Runs;
using Xunit;

namespace Sonda.Phase3.Tests;

public class PolicyTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-09-23T14:00:00Z");
    internal static Profile Legacy() => SimulationJson.Deserialize<SimulationRequest>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json"))).Profiles[0] with { Parsing = new() };
    internal static Profile Current() => Legacy() with { Policy = new() { RoutingContract = "serial", RecoveryCompatibility = "orders", DeadlineClock = DeadlineClock.Processing } };
    private static SimulationReport Run(Profile p, params string[] lines) => new SimulationRunner().Run(new()
    {
        Profiles = [p], Seed = "revision2", ServerTimeZoneId = "UTC", AsOf = Time.AddHours(1),
        Entries = lines.Select((raw, n) => new SampleEntry { ProfileId = p.Id, Raw = raw, ProcessedAt = Time.AddSeconds(n) }).ToList()
    });
    private static Rule Diagnostic(string classification) => new()
    {
        Key = "independent", Role = RuleRole.Detection, Target = TargetScope.Application,
        Classification = Enum.Parse<Classification>(classification), ConditionKey = "independent", Recovery = RecoveryPolicy.NextSuccessfulRun,
        Priority = 50, Alternatives = [new() { Expression = "diagnostic" }]
    };
    [Fact] public void Legacy_profile_serialization_is_byte_compatible()
    {
        var request = SimulationJson.Deserialize<SimulationRequest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","mixed-orders.json")));
        var accepted = SimulationJson.Deserialize<SimulationReport>(File.ReadAllText(Path.Combine(Root(),"artifacts","phase1","mixed-orders","report.json")));
        Assert.Equal(accepted.Provenance.ProfileHash, SimulationRunner.ProvenanceFor(request).ProfileHash);
        Assert.DoesNotContain("policy", SimulationJson.Serialize(request.Profiles));
    }
    private static string Root() { var p = new DirectoryInfo(AppContext.BaseDirectory); while (!File.Exists(Path.Combine(p.FullName,"Sonda.sln"))) p=p.Parent!; return p.FullName; }
    [Fact] public void Unknown_policy_revision_is_rejected() => Assert.Contains(ProfileValidator.Validate(Current() with { Policy = Current().Policy! with { Revision = 3 } }), d => d.Code == "UnsupportedPolicyRevision");
    [Fact] public void Same_cycle_retry_preserves_failure_and_recovers()
    {
        var report=Run(Current(),"CAM process started","Finding order OrderID=A","Unable to send order OrderID=A","Finding order OrderID=A","Order sent OrderID=A","CAM process completed");
        Assert.True(report.Complete); Assert.Equal(new[]{1,2},report.OrderRuns.Select(r=>r.AttemptNumber));
        Assert.Equal(DetectionResult.Failure,report.OrderRuns[0].Result); Assert.Equal(report.OrderRuns[0].Id,report.OrderRuns[1].PolicyContext!.PreviousAttemptId);
        Assert.Equal(IncidentStatus.Resolved,Assert.Single(report.Incidents).Status); Assert.Single(report.Incidents[0].RecoveryEvents);
        Assert.Equal(2,report.Metrics!.CompletedOrderRunsProcessedToday); Assert.Equal(66.67m,report.Metrics.SystemHealthPercentage);
    }
    [Theory] [InlineData("Warning")] [InlineData("Error")]
    public void Successful_run_keeps_independent_diagnostic(string severity)
    {
        var p=Current(); p.Rules.Add(Diagnostic(severity));
        var report=Run(p,"CAM process started","diagnostic","CAM process completed");
        Assert.True(report.Complete); Assert.Equal(DetectionResult.Success,Assert.Single(report.ApplicationRuns).Result);
        Assert.Equal(IncidentStatus.Active,Assert.Single(report.Incidents).Status); Assert.Empty(report.Incidents[0].RecoveryEvents);
        Assert.Equal(100m,report.Metrics!.SystemHealthPercentage);
    }
    [Fact] public void Ignore_does_not_erase_structural_failure()
    {
        var p=Current(); p.Rules.Add(Diagnostic("Ignore") with { Target=TargetScope.Order,Alternatives=[new(){Expression="Unable to send order"}] });
        var report=Run(p,"CAM process started","Finding order OrderID=A","Unable to send order OrderID=A","CAM process completed");
        Assert.True(report.Complete); Assert.Equal(DetectionResult.Failure,Assert.Single(report.OrderRuns).Result);
        Assert.Equal(Classification.Warning,Assert.Single(report.Incidents).Severity); Assert.Equal(50m,report.Metrics!.SystemHealthPercentage);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Explicit_end_failure_dominates_separate_success(bool reverse)
    {
        var p=Current() with {CompletionMode=CompletionMode.ExplicitEnd};
        p.Rules.Add(new(){Key="end",Role=RuleRole.CycleEnd,Alternatives=[new(){Expression="END"}]});
        p.Rules.Add(new(){Key="fail",Role=RuleRole.CycleFailure,Alternatives=[new(){Expression="FAIL"}]});
        var report=Run(p,"CAM process started",reverse?"FAIL":"CAM process completed",reverse?"CAM process completed":"FAIL","END");
        Assert.True(report.Complete); Assert.Equal(DetectionResult.Failure,Assert.Single(report.ApplicationRuns).Result); Assert.Single(Assert.Single(report.Incidents).Occurrences);
    }
    [Fact] public void Incomplete_end_is_undefined()
    {
        var p=Current(); p.Rules.Add(new(){Key="end",Role=RuleRole.CycleEnd,Alternatives=[new(){Expression="END"}]});
        var report=Run(p,"CAM process started","Finding order OrderID=A","END");
        Assert.True(report.Complete); Assert.Equal(DetectionResult.Undefined,report.ApplicationRuns[0].Result); Assert.Equal(DetectionResult.Undefined,report.OrderRuns[0].Result);
        Assert.Equal(0m,report.Metrics!.SystemHealthPercentage); Assert.Equal(1,report.Metrics.CompletedOrderRunsProcessedToday);
    }
    [Fact] public void Manual_resolution_is_confirmed_without_rewriting_history()
    {
        var p=Current(); var counter=0;var engine=new ProfileInterpreter(p,k=>$"{k}:{++counter}");
        string[] lines=["CAM process started","Finding order OrderID=A","Unable to send order OrderID=A","CAM process completed"];
        for(var n=0;n<lines.Length;n++) Apply(engine,p,lines[n],n+1);
        var i=Assert.Single(engine.Incidents); Assert.True(engine.ChangeIncidentStatus(i.Id,0,IncidentStatus.Resolved,"operator","reviewed",Time.AddSeconds(5)));
        Apply(engine,p,"CAM process started",6);Apply(engine,p,"Finding order OrderID=A",7);Apply(engine,p,"Order sent OrderID=A",8);
        Assert.Equal("Manual",i.PolicyContext!.ResolutionKind); Assert.Equal(2,i.History.Count);Assert.Equal("ConfirmationAfterManualResolution",Assert.Single(i.RecoveryEvents).Method);
        Assert.Throws<InterpretationException>(()=>engine.ChangeIncidentStatus(i.Id,i.PolicyContext.Revision,IncidentStatus.Active,"operator","",Time.AddSeconds(9)));
    }
    [Fact] public void Deadline_closes_without_source_evidence_and_restore_retains_context()
    {
        var p=Current() with {CycleTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(5)}};
        var counter=0;var engine=new ProfileInterpreter(p,k=>$"{k}:{++counter}");Apply(engine,p,"CAM process started",1);
        var state=SimulationJson.Deserialize<InterpreterState>(SimulationJson.Serialize(engine.ExportState()));engine=ProfileInterpreter.Restore(p,k=>$"{k}:{++counter}",state);
        var run=Assert.Single(engine.Runs);engine.ApplyDeadline(run.Id,run.PolicyContext!.Deadline!.Value,Time.AddMinutes(6),2);
        Assert.Equal(DetectionResult.Undefined,run.Result);Assert.Equal("Deadline",run.PolicyContext.CompletionTimeKind);Assert.Equal(new[]{1},run.EvidenceLines);
        Assert.Empty(engine.ApplyDeadline(run.Id,run.PolicyContext.Deadline.Value,Time.AddMinutes(7),3));
    }
    private static void Apply(ProfileInterpreter engine,Profile p,string raw,int n)
    {
        var trace=InterpretInput.Apply(p,engine,new(){ProfileId=p.Id,Raw=raw,ProcessedAt=Time.AddSeconds(n)},n,null);
        Assert.NotEqual("Quarantined",trace.Disposition);
    }
}
