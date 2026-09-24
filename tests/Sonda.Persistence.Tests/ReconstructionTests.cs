using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed partial class PersistenceTests
{
    [Fact] public async Task Restart_preserves_allocation_order_across_four_digit_boundary()
    {
        var (sample,scope,inputs)=await Setup();
        await using(var db=SondaDbContext.Open(pg.Connection)) {var lane=await db.Set<LaneRow>().FindAsync(scope.TeamId,scope.SessionId,"cam");lane!.IdCounter=9996;lane.Revision++;await db.SaveChangesAsync();}
        long counter=9996;var profile=sample.Profiles[0];var reference=new Sonda.Domain.Processing.ProfileInterpreter(profile,kind=>$"{sample.Seed}:{profile.Id}:{kind}:{++counter:D4}");
        foreach(var r in inputs)
        {
            Sonda.Application.Processing.InterpretInput.Apply(profile,reference,new SampleEntry{ProfileId=r.ProfileId,Raw=r.Raw,ProcessedAt=r.ProcessedAt},r.Line,r.SampleDate);
            await new PostgresProcessingStore(pg.Connection).ProcessAsync(r);
            var restored=await new PostgresProcessingStore(pg.Connection).ReadStateAsync(scope,profile.Id);
            Assert.Equal(SimulationJson.Serialize(reference.ExportState()),SimulationJson.Serialize(restored.Interpreter));
        }
    }
    [Fact] public async Task Identical_identifiers_in_two_profiles_remain_independent()
    {
        var original=Sample();var team="test-"+Guid.NewGuid().ToString("N");var p=original.Profiles[0] with{TeamId=team};var second=p with{Id="second"};
        var sample=original with{Profiles=[p,second],Entries=original.Entries.SelectMany(e=>new[]{e,e with{ProfileId=second.Id}}).ToList()};
        var session=await new PostgresConfigurationStore(pg.Connection).CreateSessionAsync(sample);var scope=new ProcessingScope(team,session,p.ApplicationId);var store=new PostgresProcessingStore(pg.Connection);
        for(var n=0;n<sample.Entries.Count;n++){var e=sample.Entries[n];await store.ProcessAsync(new(scope,e.ProfileId,Guid.NewGuid(),"sample","fixture",n,n+1,n+1,e.Raw,e.ProcessedAt,sample.SampleDate));}
        var one=await store.ReadStateAsync(scope,p.Id);var two=await store.ReadStateAsync(scope,second.Id);
        Assert.Single(one.Interpreter.Incidents);Assert.Single(two.Interpreter.Incidents);Assert.NotEqual(one.Interpreter.Incidents[0].Problem.IncidentKey,two.Interpreter.Incidents[0].Problem.IncidentKey);
        Assert.Equal(8,(await store.ReadMetricFactsAsync(scope)).Count);
    }
    [Fact] public async Task Application_failure_reconstructs_error_without_order_volume()
    {
        var (_,scope,inputs)=await Setup();await Apply(inputs.Take(1));
        var failed=inputs[1] with{Raw="10:05:00 CAM process failed"};var store=new PostgresProcessingStore(pg.Connection);await store.ProcessAsync(failed);
        Assert.Equal(ApplicationHealth.Error,await new PostgresProcessingStore(pg.Connection).ReadHealthAsync(scope));
        var facts=await store.ReadMetricFactsAsync(scope);Assert.Single(facts);Assert.Equal(TargetScope.Application,facts[0].Scope);Assert.Equal(Sonda.Domain.Runs.DetectionResult.Failure,facts[0].Result);
    }
    [Fact] public async Task Repeated_same_run_diagnostics_enrich_one_occurrence_and_retain_evidence()
    {
        var original=Sample();var profile=original.Profiles[0] with{TeamId="test-"+Guid.NewGuid().ToString("N")};
        profile.Rules.Add(new Rule{Key="soft-warning",Role=RuleRole.Detection,Target=TargetScope.Order,Classification=Classification.Warning,ConditionKey="connection",Recovery=RecoveryPolicy.NextSuccessfulRun,Priority=20,Alternatives=[new PatternAlternative{Kind=PatternKind.Contains,Expression="soft-warning"}]});
        var sample=original with{Profiles=[profile]};var session=await new PostgresConfigurationStore(pg.Connection).CreateSessionAsync(sample);var scope=new ProcessingScope(profile.TeamId,session,profile.ApplicationId);
        var store=new PostgresProcessingStore(pg.Connection);
        for(var n=0;n<2;n++)await store.ProcessAsync(new(scope,profile.Id,Guid.NewGuid(),"sample","fixture",n,n+1,n+1,sample.Entries[n].Raw,sample.Entries[n].ProcessedAt,sample.SampleDate));
        var identifier=SampleParser.Parse(profile,sample.Entries[1],2,sample.SampleDate).Identifier;
        for(var n=2;n<4;n++)await new PostgresProcessingStore(pg.Connection).ProcessAsync(new(scope,profile.Id,Guid.NewGuid(),"sample","fixture",n,n+1,n+1,$"10:03:00 soft-warning OrderID={identifier}",sample.Entries[n].ProcessedAt,sample.SampleDate));
        var state=await store.ReadStateAsync(scope,profile.Id);var incident=Assert.Single(state.Interpreter.Incidents);var occurrence=Assert.Single(incident.Occurrences);
        Assert.Contains(3,occurrence.EvidenceLines);Assert.Contains(4,occurrence.EvidenceLines);Assert.Empty(await store.ReadMetricFactsAsync(scope));Assert.Equal(ApplicationHealth.Warning,await store.ReadHealthAsync(scope));
    }
}
