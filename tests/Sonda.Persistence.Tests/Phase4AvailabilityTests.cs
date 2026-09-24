using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Availability;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact] public async Task Acquisition_diagnostics_reconstruct_fresh_stale_failed_disabled_without_business_error()
    {
        var original=Sample();var p=original.Profiles[0] with{TeamId="availability-"+Guid.NewGuid().ToString("N"),Parsing=new(),Policy=new(){RoutingContract="serial",RecoveryCompatibility="orders",ReadFreshness=TimeSpan.FromMinutes(1)}};
        var sample=original with{Profiles=[p],Entries=[]};var scope=new ProcessingScope(p.TeamId,await new PostgresConfigurationStore(pg.Connection).CreateSessionAsync(sample),p.ApplicationId);
        var source=new SourceConfiguration{ProfileId=p.Id,SourceKey="file",Root=Path.GetFullPath("synthetic-test-input"),Enabled=true};var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,source,"host");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(1));var at=sample.AsOf;
        Assert.Equal(AvailabilityState.NotObserved,(await store.DiagnoseAsync(scope,p.Id,at)).Availability.Availability);
        await store.ObserveSourceAsync(fence,p.Id,new("file",ReaderState.Following,"read",at,at,ReadSuccess:true));var fresh=await store.DiagnoseAsync(scope,p.Id,at);Assert.Equal(AvailabilityState.Fresh,fresh.Availability.Availability);Assert.False(fresh.Availability.HealthyNow);
        Assert.Equal(AvailabilityState.Stale,(await store.DiagnoseAsync(scope,p.Id,at.AddMinutes(2))).Availability.Availability);
        await store.ObserveSourceAsync(fence,p.Id,new("file",ReaderState.Unavailable,"reader failure",at.AddSeconds(1),at.AddSeconds(1),ReadSuccess:false));Assert.Equal(AvailabilityState.Disconnected,(await store.DiagnoseAsync(scope,p.Id,at.AddSeconds(1))).Availability.Availability);
        await store.ObserveSourceAsync(fence,p.Id,new("file",ReaderState.Following,"recovered",at.AddSeconds(2),at.AddSeconds(2),ReadSuccess:true));await store.SetEnabledAsync(fence,p.Id,"file",false);var disabled=await store.DiagnoseAsync(scope,p.Id,at.AddSeconds(2));Assert.Equal(AvailabilityState.Unknown,disabled.Availability.Availability);Assert.False(disabled.Availability.HealthyNow);
        Assert.Empty((await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,p.Id)).Interpreter.Incidents);Assert.Empty(await new PostgresProcessingStore(pg.Connection).ReadMetricFactsAsync(scope));
        await File.WriteAllTextAsync(Phase4Artifact("availability.json"),SimulationJson.Serialize(new{fresh,disabled,sourceFailuresCreateBusinessFacts=false}));
    }
}
