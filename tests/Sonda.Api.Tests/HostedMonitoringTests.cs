using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
using Sonda.Server.Hosting;
using Xunit;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class HostedMonitoringTests(AccessDatabase database)
{
    [Fact]
    public async Task Hosted_worker_reads_only_activated_synthetic_source_without_a_browser_and_does_not_duplicate_lines()
    {
        var workspace = new DirectoryInfo(AppContext.BaseDirectory);
        while (workspace is not null && !File.Exists(Path.Combine(workspace.FullName, "global.json"))) workspace = workspace.Parent;
        var parent = Path.Combine(workspace!.FullName, "artifacts", "phase5");
        var directory = Path.Combine(parent, "synthetic-monitor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var paths = new MonitoringPathPolicy([directory]); var actor = await database.ActorAsync();
        try
        {
            var provisioning = new ProvisioningStore(database.Connection, paths);
            var app = await provisioning.CreateApplicationAsync(actor, new(Guid.NewGuid(), "Hosted synthetic fixture"));
            var profile = await provisioning.CreateProfileAsync(actor, app.Id, new(Guid.NewGuid(), "Profile"));
            var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
            var configuration = sample.Profiles.Single() with { TeamId = actor.TeamId, ApplicationId = app.Id, Id = profile.Id,
                Policy = new InterpretationPolicy { RoutingContract = "fixture-route", RecoveryCompatibility = "fixture-recovery" } };
            sample = sample with { Profiles = [configuration], Entries = sample.Entries.Select(x => x with { ProfileId = profile.Id }).ToList(), ServerTimeZoneId = TimeZoneInfo.Local.Id };
            await File.WriteAllTextAsync(Path.Combine(directory, "synthetic.log"), string.Join('\n', sample.Entries.Select(x => x.Raw)) + "\n");
            await provisioning.ConfigureSourceAsync(actor, profile.Id, Guid.NewGuid(), null, null, new SourceConfiguration { Root = directory, SampleDate = sample.SampleDate, Enabled = true });
            var configurationStore = new PostgresConfigurationStore(database.Connection);
            await configurationStore.EditDraftAsync(configuration, 0, Guid.NewGuid()); await configurationStore.PublishAsync(sample, 1, Guid.NewGuid());
            await using var admission = new ApplicationAdmission(database.Connection, paths, NullLogger<ApplicationAdmission>.Instance);
            await admission.VisitAll(default);
            await using (var db = SondaDbContext.Open(database.Connection)) Assert.Empty(await db.Set<EvidenceRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
            await using (var access = AccessDbContext.Open(database.Connection))
                await new WorkflowService(database.Connection, new OperationLedger(access), admission, new InitialActivationStore(database.Connection, paths, "synthetic-local"))
                    .Activate(actor, profile.Id, new(Guid.NewGuid(), 1, 0, 0), default);
            using var worker = new SharedMonitoringWorker(admission, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:RunEnabled"] = "true" }).Build(), NullLogger<SharedMonitoringWorker>.Instance);
            await worker.StartAsync(default);
            try
            {
                for (var n = 0; n < 60; n++)
                {
                    await using var db = SondaDbContext.Open(database.Connection);
                    if (await db.Set<FactRow>().CountAsync(x => x.TeamId == actor.TeamId) == 4) break;
                    await Task.Delay(100);
                }
            }
            finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await worker.StopAsync(stop.Token); }
            await Task.Delay(1100); await admission.VisitAll(default);
            await using var final = SondaDbContext.Open(database.Connection);
            Assert.Equal(sample.Entries.Count, await final.Set<EvidenceRow>().CountAsync(x => x.TeamId == actor.TeamId));
            Assert.Equal(4, await final.Set<FactRow>().CountAsync(x => x.TeamId == actor.TeamId));
            Assert.Equal(3, await final.Set<FactRow>().CountAsync(x => x.TeamId == actor.TeamId && x.Scope == "Order"));
            Assert.Single(await final.Set<IncidentRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(Path.GetFullPath(directory), recursive: true);
        }
    }
}
