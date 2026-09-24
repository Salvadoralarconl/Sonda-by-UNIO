using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Phase3.Fixtures;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    public static IEnumerable<object[]> Phase3Fixtures()=>PolicyFixtureCatalog.All().Select(f=>new object[]{f.Name});
    [Theory][MemberData(nameof(Phase3Fixtures))]
    public async Task Revision2_full_fixture_has_postgres_parity_and_restart_replay(string name)
    {
        var fixture=PolicyFixtureCatalog.All().Single(f=>f.Name==name);
        var team="matrix-"+Guid.NewGuid().ToString("N");var profile=fixture.Request.Profile with {TeamId=team};
        var request=fixture.Request with {Profile=profile,Commands=fixture.Request.Commands.Select(c=>c.Profile is null?c:c with {Profile=c.Profile with {TeamId=team}}).ToArray()};
        var initialization=new SimulationRequest{Profiles=[profile],Entries=[],Seed=request.Seed,ServerTimeZoneId=request.ServerTimeZoneId,AsOf=request.AsOf};
        var configuration=new PostgresConfigurationStore(pg.Connection);var id=await configuration.CreateSessionAsync(initialization);
        var scope=new ProcessingScope(team,id,profile.ApplicationId);var memory=new PolicySession(profile,request.Seed);
        foreach(var command in request.Commands)
        {
            if(command.Kind==PolicyCommandKind.ActivateVersion)
            {
                var version=command.Profile!;var revision=await configuration.EditDraftAsync(version,0,Guid.NewGuid());
                await configuration.PublishAsync(initialization with {Profiles=[version]},revision,Guid.NewGuid());
            }
            var expected=memory.Execute(command);var store=new PostgresPolicyStore(pg.Connection);var actual=await store.ExecuteAsync(scope,profile.Id,command);
            Assert.Equal(SimulationJson.Serialize(expected),SimulationJson.Serialize(actual));
            Assert.Equal(SimulationJson.Serialize(memory.Export()),SimulationJson.Serialize(await store.ReadAsync(scope,profile.Id)));
        }
        var final=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,profile.Id);
        foreach(var command in request.Commands)await new PostgresPolicyStore(pg.Connection).ExecuteAsync(scope,profile.Id,command);
        Assert.Equal(SimulationJson.Serialize(final),SimulationJson.Serialize(await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,profile.Id)));
        var report=PolicySimulationRunner.Report(request,final,final.Receipts);
        Assert.Equal(fixture.Evaluated,report.Metrics.TotalEvaluatedRuns);Assert.Equal(fixture.Successful,report.Metrics.SuccessfulEvaluatedRuns);Assert.Equal(fixture.Orders,report.Metrics.CompletedOrderRunsProcessedToday);
        Assert.Equal(SimulationJson.Serialize(PolicySimulationRunner.Run(request)),SimulationJson.Serialize(report));
        await using(var db=SondaDbContext.Open(pg.Connection))
        {
            var storedFacts=await db.Set<FactRow>().Where(f=>f.SessionId==id).ToArrayAsync();
            var facts=storedFacts.Select(f=>new Sonda.Domain.Metrics.RunMetricFact(f.RunId,Enum.Parse<TargetScope>(f.Scope),Enum.Parse<Sonda.Domain.Runs.DetectionResult>(f.Result),new DateTimeOffset(f.EventTicks,TimeSpan.Zero),new DateTimeOffset(f.ProcessedTicks,TimeSpan.Zero))).ToArray();
            var zone=TimeZoneInfo.FindSystemTimeZoneById(request.ServerTimeZoneId);
            for(var i=0;i<2;i++)Assert.Equal(SimulationJson.Serialize(report.Metrics),SimulationJson.Serialize(Sonda.Domain.Metrics.MetricCalculator.Calculate(facts,request.AsOf,zone)));
            Assert.Equal(final.Interpreter.Runs.Count(r=>r.Result is not null),storedFacts.Length);
        }
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/phase3/fixtures",name));Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root,"postgres-report.json"),SimulationJson.Serialize(report));
        await File.WriteAllTextAsync(Path.Combine(root,"parity.json"),SimulationJson.Serialize(new {fixture=name,isolatedTeam=team,tracesEqual=true,restartedAfterEveryCommand=true,replayUnchanged=true,expectedMetrics=report.Metrics}));
    }
}
