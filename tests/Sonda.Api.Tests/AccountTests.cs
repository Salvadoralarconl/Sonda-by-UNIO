using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class AccountTests(AccessDatabase database)
{
    private const string Password = "Synthetic long account passphrase";
    [Fact]
    public async Task Reset_grant_revokes_sessions_and_absolute_expiry_cannot_be_extended_by_activity()
    {
        await using var factory = new ServerFactory(database.Connection); var (actor, login) = await Admin(factory);
        Guid session;
        using (var scope = factory.Services.CreateScope()) session = (await scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, Password)).Session;
        GrantResult grant;
        using (var scope = factory.Services.CreateScope()) grant = await scope.ServiceProvider.GetRequiredService<AccountService>().ReplaceGrantAsync(actor, Guid.NewGuid(), actor.AccountId, "Reset", 1);
        const string replacement = "Synthetic replacement password for reset";
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AccountService>().RedeemAsync(grant.Token!, replacement);
        using (var scope = factory.Services.CreateScope()) Assert.Null(await scope.ServiceProvider.GetRequiredService<AccountService>().ValidateSessionAsync(session));
        using (var scope = factory.Services.CreateScope()) session = (await scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, replacement)).Session;
        await using (var db = AccessDbContext.Open(database.Connection)) await db.Set<WebSession>().Where(x => x.Id == session).ExecuteUpdateAsync(s => s.SetProperty(x => x.AbsoluteExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)).SetProperty(x => x.IdleExpiresAt, DateTimeOffset.UtcNow.AddMinutes(20)));
        using (var scope = factory.Services.CreateScope()) Assert.Null(await scope.ServiceProvider.GetRequiredService<AccountService>().ValidateSessionAsync(session));
    }
    private async Task<(Actor Actor, string Login)> Admin(ServerFactory factory)
    {
        var team = Guid.NewGuid().ToString("N"); var login = Guid.NewGuid().ToString("N");
        await using (var db = SondaDbContext.Open(database.Connection)) { db.Add(new TeamRow { TeamId = team, Name = "Account fixture" }); await db.SaveChangesAsync(); }
        GrantResult grant;
        using (var scope = factory.Services.CreateScope()) grant = await scope.ServiceProvider.GetRequiredService<AccountService>().BootstrapAsync(team, login);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AccountService>().RedeemAsync(grant.Token!, Password);
        return (new(grant.AccountId, team, "Admin", 1), login);
    }

    [Fact]
    public async Task Concurrent_bootstrap_creates_one_admin_without_duplicate_identity()
    {
        await using var factory = new ServerFactory(database.Connection);
        var team = Guid.NewGuid().ToString("N");
        await using (var db = SondaDbContext.Open(database.Connection)) { db.Add(new TeamRow { TeamId = team, Name = "Bootstrap race" }); await db.SaveChangesAsync(); }
        async Task<bool> Attempt()
        {
            using var scope = factory.Services.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<AccountService>().BootstrapAsync(team, Guid.NewGuid().ToString("N")); return true; }
            catch (AccessFault e) when (e.Status == 409) { return false; }
        }
        Assert.Single(await Task.WhenAll(Attempt(), Attempt()), x => x);
        await using var access = AccessDbContext.Open(database.Connection);
        Assert.Single(await access.Set<Membership>().Where(x => x.TeamId == team && x.Role == "Admin").ToArrayAsync());
    }
    [Fact]
    public async Task Concurrent_admin_self_demotions_leave_one_enabled_admin()
    {
        await using var factory = new ServerFactory(database.Connection);
        var first = await database.ActorAsync(); var second = await database.ActorAsync(team: first.TeamId);
        async Task<bool> Change(Actor actor)
        {
            using var scope = factory.Services.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<AccountService>().ChangeMemberAsync(actor, Guid.NewGuid(), actor.AccountId, 0, "Member", false); return true; }
            catch (AccessFault e) when (e.Status == 409) { return false; }
        }
        Assert.Single(await Task.WhenAll(Change(first), Change(second)), x => x);
        await using var access = AccessDbContext.Open(database.Connection);
        Assert.Single(await access.Set<Membership>().Where(x => x.TeamId == first.TeamId && x.Role == "Admin" && x.Enabled).ToArrayAsync());
    }
    [Fact]
    public async Task Concurrent_invitation_redemption_consumes_token_once()
    {
        await using var factory = new ServerFactory(database.Connection); var (actor, _) = await Admin(factory);
        GrantResult grant;
        using (var scope = factory.Services.CreateScope()) grant = await scope.ServiceProvider.GetRequiredService<AccountService>().InviteAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("N"), "Member");
        async Task<bool> Redeem()
        {
            using var scope = factory.Services.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<AccountService>().RedeemAsync(grant.Token!, Password); return true; }
            catch (AccessFault e) when (e.Code == "grant_invalid") { return false; }
        }
        Assert.Single(await Task.WhenAll(Redeem(), Redeem()), x => x);
    }
    [Fact]
    public async Task Replaced_and_expired_grants_are_not_redeemable_and_tokens_are_not_stored()
    {
        await using var factory = new ServerFactory(database.Connection); var (actor, _) = await Admin(factory);
        GrantResult first; GrantResult second;
        using (var scope = factory.Services.CreateScope()) first = await scope.ServiceProvider.GetRequiredService<AccountService>().InviteAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("N"), "Member");
        using (var scope = factory.Services.CreateScope()) second = await scope.ServiceProvider.GetRequiredService<AccountService>().ReplaceGrantAsync(actor, Guid.NewGuid(), first.AccountId, "Invitation", 0);
        using (var scope = factory.Services.CreateScope()) await Assert.ThrowsAsync<AccessFault>(() => scope.ServiceProvider.GetRequiredService<AccountService>().RedeemAsync(first.Token!, Password));
        await using (var access = AccessDbContext.Open(database.Connection))
        {
            await access.Set<AccountGrant>().Where(x => x.Id == second.GrantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.DoesNotContain(await access.Set<AccountGrant>().Where(x => x.TeamId == actor.TeamId).Select(x => x.Digest).ToArrayAsync(), x => x == first.Token || x == second.Token);
            Assert.DoesNotContain(await access.Set<ApiOperation>().Where(x => x.TeamId == actor.TeamId).Select(x => x.Result).ToArrayAsync(), x => x.Contains(first.Token!, StringComparison.Ordinal) || x.Contains(second.Token!, StringComparison.Ordinal));
        }
        using (var scope = factory.Services.CreateScope()) await Assert.ThrowsAsync<AccessFault>(() => scope.ServiceProvider.GetRequiredService<AccountService>().RedeemAsync(second.Token!, Password));
    }
    [Fact]
    public async Task Password_change_revokes_all_sessions_and_old_password()
    {
        await using var factory = new ServerFactory(database.Connection); var (actor, login) = await Admin(factory);
        Guid session;
        using (var scope = factory.Services.CreateScope()) session = (await scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, Password)).Session;
        const string next = "A different synthetic long passphrase";
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AccountService>().ChangePasswordAsync(actor, Password, next);
        using (var scope = factory.Services.CreateScope()) Assert.Null(await scope.ServiceProvider.GetRequiredService<AccountService>().ValidateSessionAsync(session));
        using (var scope = factory.Services.CreateScope()) await Assert.ThrowsAsync<AccessFault>(() => scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, Password));
        using (var scope = factory.Services.CreateScope()) Assert.Equal(actor.AccountId, (await scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, next)).Actor.AccountId);
    }
    [Fact]
    public async Task Lockout_is_durable_and_returns_same_failure_shape()
    {
        await using var factory = new ServerFactory(database.Connection); var (_, login) = await Admin(factory);
        for (var i = 0; i < 5; i++)
        {
            using var scope = factory.Services.CreateScope();
            Assert.Equal("invalid_credentials", (await Assert.ThrowsAsync<AccessFault>(() => scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, "Wrong password"))).Code);
        }
        using (var scope = factory.Services.CreateScope()) Assert.Equal("invalid_credentials", (await Assert.ThrowsAsync<AccessFault>(() => scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, Password))).Code);
    }
    [Fact]
    public async Task Revocation_between_cookie_validation_and_command_admission_is_not_hidden_by_tracking()
    {
        await using var factory = new ServerFactory(database.Connection); var (actor, login) = await Admin(factory);
        Guid session;
        using (var scope = factory.Services.CreateScope()) session = (await scope.ServiceProvider.GetRequiredService<AccountService>().LoginAsync(login, Password)).Session;
        using var requestScope = factory.Services.CreateScope(); var service = requestScope.ServiceProvider.GetRequiredService<AccountService>();
        Assert.NotNull(await service.ValidateSessionAsync(session));
        await using (var access = AccessDbContext.Open(database.Connection))
            await access.Set<Membership>().Where(x => x.AccountId == actor.AccountId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, false).SetProperty(x => x.Revision, 2));
        Assert.Equal(401, (await Assert.ThrowsAsync<AccessFault>(() => service.InviteAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("N"), "Member"))).Status);
    }
}
