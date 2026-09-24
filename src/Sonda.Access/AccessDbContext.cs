using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sonda.Access;

public sealed class Account : IdentityUser<Guid>;
public sealed class Membership
{
    public string TeamId { get; set; } = "";
    public Guid AccountId { get; set; }
    public string Role { get; set; } = "Member";
    public bool Enabled { get; set; }
    public long Revision { get; set; }
}
public sealed class TeamGuard { public string TeamId { get; set; } = ""; public long Revision { get; set; } }
public sealed class WebSession
{
    public Guid Id { get; set; }
    public string TeamId { get; set; } = "";
    public Guid AccountId { get; set; }
    public long AccessRevision { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class AccountGrant
{
    public Guid Id { get; set; }
    public string TeamId { get; set; } = "";
    public Guid AccountId { get; set; }
    public string Purpose { get; set; } = "Invitation";
    public string Digest { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class ApiOperation
{
    public string TeamId { get; set; } = "";
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string State { get; set; } = "Submitted";
    public string Result { get; set; } = "";
    public string Envelope { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
}
public sealed class AccessAudit
{
    public Guid Id { get; set; }
    public string TeamId { get; set; } = "";
    public Guid AccountId { get; set; }
    public Guid OperationId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Disposition { get; set; } = "";
    public long AccessRevision { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
public sealed class ProfilePreview
{
    public string TeamId { get; set; } = "";
    public Guid Id { get; set; }
    public string ProfileId { get; set; } = "";
    public long DraftRevision { get; set; }
    public Guid AccountId { get; set; }
    public string ProfileHash { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Request { get; set; } = "";
    public string Report { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ApplicationMetadata
{
    public string TeamId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options)
    : IdentityDbContext<Account, IdentityRole<Guid>, Guid>(options)
{
    public static AccessDbContext Open(string connection) => new(new DbContextOptionsBuilder<AccessDbContext>()
        .UseNpgsql(connection, x => x.MigrationsHistoryTable("__AccessMigrationsHistory", "sonda_access")).Options);

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("sonda_access");
        b.Entity<Membership>().ToTable("team_memberships", t => t.HasCheckConstraint("membership_role", "\"Role\" IN ('Admin','Member')"));
        b.Entity<Membership>().HasKey(x => new { x.TeamId, x.AccountId });
        b.Entity<Membership>().HasIndex(x => x.AccountId).IsUnique();
        b.Entity<Membership>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Membership>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TeamGuard>().ToTable("team_access_guards").HasKey(x => x.TeamId);
        b.Entity<TeamGuard>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<WebSession>().ToTable("web_sessions").HasKey(x => x.Id);
        b.Entity<WebSession>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TeamId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccountGrant>().ToTable("account_grants", t => t.HasCheckConstraint("grant_purpose", "\"Purpose\" IN ('Invitation','Reset')")).HasKey(x => x.Id);
        b.Entity<AccountGrant>().HasIndex(x => x.Digest).IsUnique();
        b.Entity<AccountGrant>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TeamId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApiOperation>().ToTable("api_operations", t => t.HasCheckConstraint("operation_state", "\"State\" IN ('Submitted','Committed','Rejected','OutcomeUnknown')")).HasKey(x => new { x.TeamId, x.Id });
        b.Entity<ApiOperation>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TeamId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessAudit>().ToTable("access_audit").HasKey(x => x.Id);
        b.Entity<AccessAudit>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TeamId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessAudit>().HasIndex(x => new { x.TeamId, x.RecordedAt, x.Id });
        b.Entity<ProfilePreview>().ToTable("profile_previews").HasKey(x => new { x.TeamId, x.Id });
        b.Entity<ProfilePreview>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TeamId, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApplicationMetadata>().ToTable("application_metadata").HasKey(x => new { x.TeamId, x.ApplicationId });
    }
}

public sealed class AccessDesignFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args) => AccessDbContext.Open(
        Environment.GetEnvironmentVariable("SONDA_DATABASE") ?? "Host=localhost;Database=sonda_design");
}
