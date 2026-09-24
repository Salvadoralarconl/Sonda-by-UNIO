using Sonda.Application.Simulation;
using Sonda.Phase3.Fixtures;
using Xunit;

namespace Sonda.Phase3.Tests;
public sealed class AcceptanceMatrixTests
{
    public static IEnumerable<object[]> Cases()=>PolicyFixtureCatalog.All().Select(f=>new object[]{f.Name});
    [Theory][MemberData(nameof(Cases))]
    public void Approved_fixture_matches_independent_expectations(string name)
    {
        var fixture=PolicyFixtureCatalog.All().Single(f=>f.Name==name);var report=PolicySimulationRunner.Run(fixture.Request);
        Assert.Equal(fixture.Complete,report.Complete);Assert.Equal(fixture.Evaluated,report.Metrics.TotalEvaluatedRuns);Assert.Equal(fixture.Successful,report.Metrics.SuccessfulEvaluatedRuns);
        Assert.Equal(fixture.Orders,report.Metrics.CompletedOrderRunsProcessedToday);Assert.Equal(fixture.Incidents,report.State.Interpreter.Incidents.Length);
        Assert.Equal(SimulationJson.Serialize(report),SimulationJson.Serialize(PolicySimulationRunner.Run(fixture.Request)));
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var output=Path.Combine(root,"artifacts","phase3","fixtures",name);Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"request.json"),SimulationJson.Serialize(fixture.Request));File.WriteAllText(Path.Combine(output,"memory-report.json"),SimulationJson.Serialize(report));
    }
}
