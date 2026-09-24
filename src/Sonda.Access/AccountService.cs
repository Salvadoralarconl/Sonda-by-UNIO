using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Simulation;

namespace Sonda.Access;

public sealed record GrantResult(Guid AccountId, Guid GrantId, string? Token, bool SecretUnavailable, long Revision);
public sealed record MembershipResult(Guid AccountId, string Role, bool Enabled, long Revision);

public sealed class AccountService(AccessDbContext db, UserManager<Account> users, TimeProvider clock)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static void Password(string value)
    {
        if (value is null || value.Length is < 15 or > 128) throw new AccessFault(422, "password_policy");
    }
    private async Task LockTeam(string team, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({team},0))", ct);

    public async Task<GrantResult> BootstrapAsync(string team, string login, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(team, ct);
        if (await db.Set<Membership>().AnyAsync(x => x.TeamId == team && x.Role == "Admin", ct)) throw new AccessFault(409, "bootstrap_already_completed");
        if (!await db.Set<TeamGuard>().AnyAsync(x => x.TeamId == team, ct)) db.Add(new TeamGuard { TeamId = team });
        var account = await CreateAccount(login);
        db.Add(new Membership { TeamId = team, AccountId = account.Id, Role = "Admin", Enabled = false });
        var grant = Grant(team, account.Id, "Invitation");
        Audit(new(account.Id, team, "Admin", 0), Guid.NewGuid(), "Bootstrap", account.Id.ToString(), "Committed");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return grant;
    }

    public async Task<GrantResult> OperatorRecoveryAsync(string team, string login, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(team, ct);
        var account = await users.FindByNameAsync(login) ?? throw new AccessFault(404, "resource_not_found");
        var member = await Member(team, account.Id, ct);
        if (member.Role != "Admin" || (!member.Enabled && account.PasswordHash is not null)) throw new AccessFault(409, "operator_recovery_requires_admin");
        foreach (var old in await db.Set<AccountGrant>().Where(x => x.TeamId == team && x.AccountId == account.Id && x.ConsumedAt == null && x.RevokedAt == null).ToArrayAsync(ct)) old.RevokedAt = Now;
        var grant = Grant(team, account.Id, account.PasswordHash is null ? "Invitation" : "Reset") with { Revision = member.Revision };
        Audit(new(account.Id, team, "Admin", member.Revision), Guid.NewGuid(), "LocalOperatorRecoveryGrant", account.Id.ToString(), "Committed");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return grant;
    }

    public async Task<GrantResult> InviteAsync(Actor actor, Guid operation, string login, string role, CancellationToken ct = default)
    {
        if (role is not ("Admin" or "Member")) throw new AccessFault(422, "invalid_role");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(actor.TeamId, ct);
        await AccessAuthorization.RequireAsync(db, actor, true, ct);
        var hash = SimulationJson.Hash(new { kind = "Invite", login, role, actor.AccountId });
        var previous = await Prior(actor, operation, hash, ct);
        if (previous is not null) return SimulationJson.Deserialize<GrantResult>(previous);
        var account = await CreateAccount(login);
        db.Add(new Membership { TeamId = actor.TeamId, AccountId = account.Id, Role = role });
        var grant = Grant(actor.TeamId, account.Id, "Invitation");
        SaveOperation(actor, operation, hash, "Invite", account.Id.ToString(), grant with { Token = null, SecretUnavailable = true });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return grant;
    }

    public async Task<GrantResult> ReplaceGrantAsync(Actor actor, Guid operation, Guid accountId, string purpose, long expectedRevision, CancellationToken ct = default)
    {
        if (purpose is not ("Invitation" or "Reset")) throw new AccessFault(422, "invalid_grant_purpose");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(actor.TeamId, ct);
        await AccessAuthorization.RequireAsync(db, actor, true, ct);
        var hash = SimulationJson.Hash(new { kind = "Grant", accountId, purpose, expectedRevision, actorId = actor.AccountId });
        var previous = await Prior(actor, operation, hash, ct);
        if (previous is not null) return SimulationJson.Deserialize<GrantResult>(previous);
        var member = await Member(actor.TeamId, accountId, ct);
        if (member.Revision != expectedRevision) throw new AccessFault(409, "membership_revision_conflict");
        var account = await users.FindByIdAsync(accountId.ToString()) ?? throw new AccessFault(404, "resource_not_found");
        if (purpose == "Invitation" && account.PasswordHash is not null) throw new AccessFault(409, "account_already_activated");
        foreach (var old in await db.Set<AccountGrant>().Where(x => x.TeamId == actor.TeamId && x.AccountId == accountId && x.ConsumedAt == null && x.RevokedAt == null).ToArrayAsync(ct)) old.RevokedAt = Now;
        var grant = Grant(actor.TeamId, accountId, purpose) with { Revision = member.Revision };
        SaveOperation(actor, operation, hash, "ReplaceGrant", accountId.ToString(), grant with { Token = null, SecretUnavailable = true });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return grant;
    }

    public async Task RedeemAsync(string token, string password, CancellationToken ct = default)
    {
        Password(password);
        if (token is null || token.Length != 64) throw new AccessFault(400, "grant_invalid");
        var digest = Digest(token);
        // Team lookup is internal; error responses never disclose the account/team.
        var candidate = await db.Set<AccountGrant>().AsNoTracking().SingleOrDefaultAsync(x => x.Digest == digest, ct);
        if (candidate is null) throw new AccessFault(400, "grant_invalid");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(candidate.TeamId, ct);
        var grant = await db.Set<AccountGrant>().SingleAsync(x => x.Id == candidate.Id, ct);
        if (grant.ConsumedAt is not null || grant.RevokedAt is not null || grant.ExpiresAt <= Now) throw new AccessFault(400, "grant_invalid");
        var account = await users.FindByIdAsync(grant.AccountId.ToString()) ?? throw new AccessFault(400, "grant_invalid");
        var member = await Member(grant.TeamId, grant.AccountId, ct);
        if (grant.Purpose == "Reset" && !member.Enabled) throw new AccessFault(400, "grant_invalid");
        var identityToken = await users.GeneratePasswordResetTokenAsync(account);
        Check(await users.ResetPasswordAsync(account, identityToken, password));
        if (grant.Purpose == "Invitation") member.Enabled = true;
        member.Revision++; grant.ConsumedAt = Now;
        await Revoke(member.AccountId, ct);
        Audit(new(member.AccountId, member.TeamId, member.Role, member.Revision), Guid.NewGuid(), "RedeemGrant", grant.Id.ToString(), "Committed");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<MembershipResult> ChangeMemberAsync(Actor actor, Guid operation, Guid id, long expectedRevision, string role, bool enabled, CancellationToken ct = default)
    {
        if (role is not ("Admin" or "Member")) throw new AccessFault(422, "invalid_role");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(actor.TeamId, ct);
        await AccessAuthorization.RequireAsync(db, actor, true, ct);
        var hash = SimulationJson.Hash(new { kind = "Membership", id, expectedRevision, role, enabled, actor.AccountId });
        var previous = await Prior(actor, operation, hash, ct);
        if (previous is not null) return SimulationJson.Deserialize<MembershipResult>(previous);
        var member = await Member(actor.TeamId, id, ct);
        if (member.Revision != expectedRevision) throw new AccessFault(409, "membership_revision_conflict");
        if (member.Role == "Admin" && member.Enabled && (role != "Admin" || !enabled) &&
            await db.Set<Membership>().CountAsync(x => x.TeamId == actor.TeamId && x.Role == "Admin" && x.Enabled, ct) <= 1)
            throw new AccessFault(409, "last_admin_required");
        var account = await users.FindByIdAsync(id.ToString()) ?? throw new AccessFault(404, "resource_not_found");
        if (enabled && account.PasswordHash is null) throw new AccessFault(409, "invitation_redemption_required");
        member.Role = role; member.Enabled = enabled; member.Revision++; await Revoke(id, ct);
        // Outstanding grants must not undo an administrative disable or privilege change.
        foreach (var grant in await db.Set<AccountGrant>().Where(x => x.AccountId == id && x.ConsumedAt == null && x.RevokedAt == null).ToArrayAsync(ct)) grant.RevokedAt = Now;
        var result = new MembershipResult(id, role, enabled, member.Revision);
        SaveOperation(actor, operation, hash, "ChangeMember", id.ToString(), result);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }

    public async Task<(Actor Actor, Guid Session)> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        if (login is null || password is null || login.Length > 256 || password.Length > 128) throw new AccessFault(401, "invalid_credentials");
        var account = await users.FindByNameAsync(login);
        if (account is null) { _ = new PasswordHasher<Account>().HashPassword(new(), "synthetic constant-work unknown account"); throw new AccessFault(401, "invalid_credentials"); }
        var team = await db.Set<Membership>().Where(x => x.AccountId == account.Id).Select(x => x.TeamId).SingleOrDefaultAsync(ct);
        if (team is null) throw new AccessFault(401, "invalid_credentials");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(team, ct);
        await db.Entry(account).ReloadAsync(ct);
        var member = await Member(team, account.Id, ct);
        if (!member.Enabled || await users.IsLockedOutAsync(account)) throw new AccessFault(401, "invalid_credentials");
        if (!await users.CheckPasswordAsync(account, password))
        {
            Check(await users.AccessFailedAsync(account)); await tx.CommitAsync(ct); throw new AccessFault(401, "invalid_credentials");
        }
        Check(await users.ResetAccessFailedCountAsync(account));
        var id = Guid.NewGuid(); var now = Now;
        db.Add(new WebSession { Id = id, TeamId = team, AccountId = account.Id, AccessRevision = member.Revision,
            IssuedAt = now, LastActivityAt = now, IdleExpiresAt = now.AddMinutes(30), AbsoluteExpiresAt = now.AddHours(8) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return (new(account.Id, team, member.Role, member.Revision), id);
    }

    public async Task<Actor?> ValidateSessionAsync(Guid id, CancellationToken ct = default)
    {
        var session = await db.Set<WebSession>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (session is null || session.RevokedAt is not null || session.IdleExpiresAt <= Now || session.AbsoluteExpiresAt <= Now) return null;
        var member = await db.Set<Membership>().SingleOrDefaultAsync(x => x.TeamId == session.TeamId && x.AccountId == session.AccountId, ct);
        if (member is null || !member.Enabled || member.Revision != session.AccessRevision) return null;
        var now = Now;
        // Conditional update cannot undo revocation in a concurrent request.
        await db.Set<WebSession>().Where(x => x.Id == id && x.RevokedAt == null).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.LastActivityAt, now).SetProperty(x => x.IdleExpiresAt, now.AddMinutes(30)), ct);
        return new(member.AccountId, member.TeamId, member.Role, member.Revision);
    }

    public async Task LogoutAsync(Actor actor, Guid session, bool all, CancellationToken ct = default)
    {
        await db.Set<WebSession>().Where(x => x.AccountId == actor.AccountId && (all || x.Id == session) && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
    }

    public async Task ChangePasswordAsync(Actor actor, string current, string next, CancellationToken ct = default)
    {
        Password(next); if (current is null || current.Length > 128) throw new AccessFault(422, "password_policy");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTeam(actor.TeamId, ct);
        var member = await AccessAuthorization.RequireAsync(db, actor, false, ct);
        var account = await users.FindByIdAsync(actor.AccountId.ToString()) ?? throw new AccessFault(401, "session_invalid");
        Check(await users.ChangePasswordAsync(account, current, next)); member.Revision++; await Revoke(account.Id, ct);
        Audit(actor, Guid.NewGuid(), "ChangePassword", account.Id.ToString(), "Committed");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private async Task<Account> CreateAccount(string login)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length > 256) throw new AccessFault(422, "invalid_login");
        var account = new Account { Id = Guid.NewGuid(), UserName = login.Trim(), LockoutEnabled = true };
        Check(await users.CreateAsync(account)); return account;
    }
    private GrantResult Grant(string team, Guid account, string purpose)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var id = Guid.NewGuid();
        db.Add(new AccountGrant { Id = id, TeamId = team, AccountId = account, Purpose = purpose, Digest = Digest(token), ExpiresAt = Now.Add(purpose == "Invitation" ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(30)) });
        return new(account, id, token, false, 0);
    }
    private async Task<Membership> Member(string team, Guid id, CancellationToken ct) =>
        await db.Set<Membership>().SingleOrDefaultAsync(x => x.TeamId == team && x.AccountId == id, ct) ?? throw new AccessFault(404, "resource_not_found");
    private async Task Revoke(Guid id, CancellationToken ct) => await db.Set<WebSession>().Where(x => x.AccountId == id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
    private async Task<string?> Prior(Actor actor, Guid operation, string hash, CancellationToken ct)
    {
        if (operation == Guid.Empty) throw new AccessFault(422, "operation_id_required");
        var row = await db.Set<ApiOperation>().FindAsync([actor.TeamId, operation], ct);
        if (row is null) return null;
        if (row.AccountId != actor.AccountId || row.Fingerprint != hash) throw new AccessFault(409, "operation_identity_conflict");
        return row.Result;
    }
    private void SaveOperation(Actor actor, Guid operation, string hash, string action, string target, object result)
    {
        db.Add(new ApiOperation { TeamId = actor.TeamId, AccountId = actor.AccountId, Id = operation, Action = action, Target = target, Fingerprint = hash, State = "Committed", Result = SimulationJson.Serialize(result), SubmittedAt = Now });
        Audit(actor, operation, action, target, "Committed");
    }
    private void Audit(Actor actor, Guid operation, string action, string target, string disposition) => db.Add(new AccessAudit
    { Id = Guid.NewGuid(), TeamId = actor.TeamId, AccountId = actor.AccountId, OperationId = operation, Action = action, Target = target, Disposition = disposition, AccessRevision = actor.Revision, RecordedAt = Now });
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw new AccessFault(422, "account_operation_rejected"); }
}
