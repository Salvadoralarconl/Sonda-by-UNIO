using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Access.Tests;

[Collection("AccessPostgres")]
public sealed class ProvisioningTests(AccessDatabase database)
{
    private ProvisioningStore Store(Action? afterCommit = null) => new(database.Connection,
        new MonitoringPathPolicy([@"C:\synthetic-logs", @"\\fixture-server\logs"]), afterCommit);

    [Fact]
    public async Task Admin_creates_inactive_configuration_without_publication_or_runtime()
    {
        var actor = await database.ActorAsync(); var store = Store();
        var app = await store.CreateApplicationAsync(actor, new(Guid.NewGuid(), "CAM", "Synthetic fixture"));
        var profile = await store.CreateProfileAsync(actor, app.Id, new(Guid.NewGuid(), "CAM profile"));
        var source = await store.ConfigureSourceAsync(actor, profile.Id, Guid.NewGuid(), null, null,
            new SourceConfiguration { Root = @"C:\synthetic-logs", Enabled = true });
        await using var db = SondaDbContext.Open(database.Connection);
        var row = await db.Set<ProfileRow>().SingleAsync(x => x.TeamId == actor.TeamId);
        var draft = SimulationJson.Deserialize<Profile>(row.Draft);
        Assert.Empty(draft.Rules); Assert.Equal(2, draft.Policy!.Revision);
        Assert.Empty(await db.Set<VersionRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
        Assert.Empty(await db.Set<LaneRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
        Assert.Empty(await db.Set<MonitoringSessionRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
        Assert.Equal("InactiveConfiguration", source.State);
    }

    [Fact]
    public async Task Member_cannot_provision_even_with_spoofed_role()
    {
        var actor = await database.ActorAsync("Member");
        var error = await Assert.ThrowsAsync<AccessFault>(() => Store().CreateApplicationAsync(actor with { Role = "Admin" }, new(Guid.NewGuid(), "Denied")));
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Foreign_parent_is_indistinguishable_from_missing_parent()
    {
        var owner = await database.ActorAsync(); var stranger = await database.ActorAsync(); var store = Store();
        var app = await store.CreateApplicationAsync(owner, new(Guid.NewGuid(), "Private"));
        foreach (var id in new[] { app.Id, "missing" })
        {
            var fault = await Assert.ThrowsAsync<AccessFault>(() => store.CreateProfileAsync(stranger, id, new(Guid.NewGuid(), "Denied")));
            Assert.Equal(404, fault.Status); Assert.Equal("resource_not_found", fault.Code);
        }
    }

    [Fact]
    public async Task Concurrent_identical_commands_create_one_application_and_audit()
    {
        var actor = await database.ActorAsync(); var request = new CreateApplication(Guid.NewGuid(), "Once");
        var results = await Task.WhenAll(Store().CreateApplicationAsync(actor, request), Store().CreateApplicationAsync(actor, request));
        Assert.Equal(results[0], results[1]);
        await using var core = SondaDbContext.Open(database.Connection);
        Assert.Single(await core.Set<ApplicationRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
        await using var access = AccessDbContext.Open(database.Connection);
        Assert.Single(await access.Set<AccessAudit>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
    }

    [Fact]
    public async Task Conflicting_reuse_and_cross_actor_reuse_are_rejected()
    {
        var actor = await database.ActorAsync(); var peer = await database.ActorAsync(team: actor.TeamId);
        var request = new CreateApplication(Guid.NewGuid(), "First"); var store = Store();
        await store.CreateApplicationAsync(actor, request);
        Assert.Equal(409, (await Assert.ThrowsAsync<AccessFault>(() => store.CreateApplicationAsync(actor, request with { Name = "Different" }))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<AccessFault>(() => store.CreateApplicationAsync(peer, request))).Status);
    }

    [Fact]
    public async Task Commit_then_lost_acknowledgment_replays_after_new_store_instance()
    {
        var actor = await database.ActorAsync(); var request = new CreateApplication(Guid.NewGuid(), "Crash fixture");
        await Assert.ThrowsAsync<IOException>(() => Store(() => throw new IOException("Synthetic lost acknowledgment")).CreateApplicationAsync(actor, request));
        var recovered = await Store().CreateApplicationAsync(actor, request);
        Assert.Equal("Unconfigured", recovered.State);
        await using var core = SondaDbContext.Open(database.Connection);
        Assert.Single(await core.Set<ApplicationRow>().Where(x => x.TeamId == actor.TeamId).ToArrayAsync());
    }

    [Fact]
    public async Task Allowed_unc_configuration_does_not_claim_share_capability_and_edits_require_revision()
    {
        var actor = await database.ActorAsync(); var store = Store();
        var app = await store.CreateApplicationAsync(actor, new(Guid.NewGuid(), "UNC fixture"));
        var profile = await store.CreateProfileAsync(actor, app.Id, new(Guid.NewGuid(), "Draft"));
        var config = new SourceConfiguration { Root = @"\\fixture-server\logs\CAM" };
        var source = await store.ConfigureSourceAsync(actor, profile.Id, Guid.NewGuid(), null, null, config);
        var updated = await store.ConfigureSourceAsync(actor, profile.Id, Guid.NewGuid(), source.Id, source.Revision, config with { Recursive = true });
        Assert.Equal(2, updated.Revision);
        Assert.Equal(409, (await Assert.ThrowsAsync<AccessFault>(() => store.ConfigureSourceAsync(actor, profile.Id, Guid.NewGuid(), source.Id, source.Revision, config))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<AccessFault>(() => store.ConfigureSourceAsync(actor, profile.Id, Guid.NewGuid(), null, null, config with { Root = @"C:\private" }))).Status);
    }

    [Fact]
    public async Task Access_migration_does_not_change_core_chain()
    {
        await using var core = SondaDbContext.Open(database.Connection);
        Assert.Equal(new[] { "20260924023552_DurablePersistence", "20260924032911_InterpretationPolicies", "20260924044645_FileAcquisition" }, await core.Database.GetAppliedMigrationsAsync());
        await using var access = AccessDbContext.Open(database.Connection);
        var before = (await access.Database.GetAppliedMigrationsAsync()).ToArray();
        await access.Database.MigrateAsync(); Assert.Equal(before, await access.Database.GetAppliedMigrationsAsync());
        Assert.Equal(new[] { "20260924130738_AccessFoundation", "20260924141909_AccessSecurityConstraints" }, before);
    }
}
