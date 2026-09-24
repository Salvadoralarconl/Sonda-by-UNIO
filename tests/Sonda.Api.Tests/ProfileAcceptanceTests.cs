using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
using Sonda.Server.Profiles;
using Sonda.Server.Hosting;
using Xunit;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class ProfileAcceptanceTests(AccessDatabase database)
{
    [Fact]
    public async Task Concurrent_draft_edits_have_one_winner_and_oversize_or_canceled_preview_has_no_business_effect()
    {
        await using var factory = new ServerFactory(database.Connection); var actor = await database.ActorAsync();
        var provision = new ProvisioningStore(database.Connection, new([@"C:\synthetic-logs"]));
        var app = await provision.CreateApplicationAsync(actor, new(Guid.NewGuid(), "Draft race")); var profile = await provision.CreateProfileAsync(actor, app.Id, new(Guid.NewGuid(), "Draft"));
        async Task<bool> Edit(string name)
        {
            using var scope = factory.Services.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<ProfileService>().Edit(actor, profile.Id, new(Guid.NewGuid(), 0, new Profile { TeamId = actor.TeamId, ApplicationId = app.Id, Id = profile.Id, Name = name }), default); return true; }
            catch (PersistenceConflict) { return false; }
        }
        Assert.Single(await Task.WhenAll(Edit("First"), Edit("Second")), x => x);
        using var services = factory.Services.CreateScope(); var simulator = services.ServiceProvider.GetRequiredService<SimulationProcess>();
        var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        Assert.Equal(413, (await Assert.ThrowsAsync<AccessFault>(() => simulator.Run(sample with { Entries = Enumerable.Repeat(sample.Entries[0], 1001).ToList() }, default))).Status);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => simulator.Run(sample, canceled.Token));
        // An executable-looking string is ordinary log data. It is passed only inside sample JSON.
        var harmless = sample with { Entries = [sample.Entries[0] with { Raw = "10:00:00 $(not-a-command) ; no execution" }] };
        var report = await simulator.Run(harmless, default); Assert.Contains("not-a-command", report);
        await using var db = SondaDbContext.Open(database.Connection);
        Assert.Empty(await db.Set<VersionRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync()); Assert.Empty(await db.Set<EvidenceRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
    }
    [Fact]
    public async Task Preview_replay_survives_draft_edit_but_stale_report_cannot_publish_and_publication_ack_loss_reconciles()
    {
        await using var factory = new ServerFactory(database.Connection); var actor = await database.ActorAsync();
        var provision = new ProvisioningStore(database.Connection, new([@"C:\synthetic-logs"]));
        var app = await provision.CreateApplicationAsync(actor, new(Guid.NewGuid(), "Profile acceptance", ""));
        var created = await provision.CreateProfileAsync(actor, app.Id, new(Guid.NewGuid(), "Profile acceptance"));
        var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        var profile = sample.Profiles.Single() with { TeamId = actor.TeamId, ApplicationId = app.Id, Id = created.Id,
            Policy = new InterpretationPolicy { RoutingContract = "route", RecoveryCompatibility = "recovery" } };
        async Task<object> Call(Func<ProfileService, Task<object>> action) { using var scope = factory.Services.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<ProfileService>()); }
        await Call(s => s.Edit(actor, profile.Id, new(Guid.NewGuid(), 0, profile), default));
        var preview = new PreviewRequest(Guid.NewGuid(), 1, sample.AsOf, sample.SampleDate, sample.Entries.Select(x => x with { ProfileId = profile.Id }).ToArray());
        var original = await Call(s => s.Preview(actor, profile.Id, preview, default));
        await Call(s => s.Edit(actor, profile.Id, new(Guid.NewGuid(), 1, profile with { Name = "Edited" }), default));
        var replay = await Call(s => s.Preview(actor, profile.Id, preview, default));
        Assert.Equal(SimulationJson.Serialize(original), SimulationJson.Serialize(replay));
        await Assert.ThrowsAsync<PersistenceConflict>(() => Call(s => s.Publish(actor, profile.Id, new(Guid.NewGuid(), 1, preview.OperationId, true), default)));
        var second = preview with { OperationId = Guid.NewGuid(), ExpectedRevision = 2 };
        await Call(s => s.Preview(actor, profile.Id, second, default));
        // Durable intent and original core publication commit, followed by simulated loss before access acknowledgment.
        var publish = new PublishRequest(Guid.NewGuid(), 2, second.OperationId, true);
        await using (var access = AccessDbContext.Open(database.Connection))
        {
            var saved = await access.Set<ProfilePreview>().SingleAsync(x => x.TeamId == actor.TeamId && x.Id == second.OperationId);
            var request = System.Text.Json.JsonSerializer.Deserialize<SimulationRequest>(saved.Request, SimulationJson.Options)!;
            var hash = SimulationJson.Hash(new { action = "Publish", id = profile.Id, input = publish });
            access.Add(new ApiOperation { TeamId = actor.TeamId, Id = publish.OperationId, AccountId = actor.AccountId, Action = "PublishProfile", Target = profile.Id,
                Fingerprint = hash, Envelope = SimulationJson.Hash(new { request, expectedRevision = 2L, kind = "Publish" }), SubmittedAt = DateTimeOffset.UtcNow });
            access.Add(new AccessAudit { Id = Guid.NewGuid(), TeamId = actor.TeamId, AccountId = actor.AccountId, OperationId = publish.OperationId, Action = "PublishProfile", Target = profile.Id, Disposition = "Submitted", RecordedAt = DateTimeOffset.UtcNow });
            await access.SaveChangesAsync();
            await new PostgresConfigurationStore(database.Connection).PublishAsync(request, 2, publish.OperationId);
        }
        await using (var access = AccessDbContext.Open(database.Connection))
        {
            var op = (await access.Set<ApiOperation>().FindAsync(actor.TeamId, publish.OperationId))!;
            await OperationReconciler.Reconcile(database.Connection, access, op, default);
            Assert.Equal("Committed", op.State);
        }
        await Call(s => s.Publish(actor, profile.Id, publish, default));
        await using var core = SondaDbContext.Open(database.Connection);
        Assert.Single(await core.Set<VersionRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
        Assert.Empty(await core.Set<MonitoringSessionRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
        Assert.Empty(await core.Set<FactRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
    }
}
