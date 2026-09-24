using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sonda.Access.Migrations
{
    /// <inheritdoc />
    public partial class AccessFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sonda_access");

            migrationBuilder.CreateTable(
                name: "access_audit",
                schema: "sonda_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Target = table.Column<string>(type: "text", nullable: false),
                    Disposition = table.Column<string>(type: "text", nullable: false),
                    AccessRevision = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "application_metadata",
                schema: "sonda_access",
                columns: table => new
                {
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_metadata", x => new { x.TeamId, x.ApplicationId });
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "sonda_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "sonda_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "profile_previews",
                schema: "sonda_access",
                columns: table => new
                {
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "text", nullable: false),
                    DraftRevision = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileHash = table.Column<string>(type: "text", nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    Request = table.Column<string>(type: "text", nullable: false),
                    Report = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_previews", x => new { x.TeamId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "team_access_guards",
                schema: "sonda_access",
                columns: table => new
                {
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_access_guards", x => x.TeamId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "sonda_access",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "sonda_access",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "sonda_access",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sonda_access",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "sonda_access",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sonda_access",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "sonda_access",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "sonda_access",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sonda_access",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "sonda_access",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sonda_access",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_memberships",
                schema: "sonda_access",
                columns: table => new
                {
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_memberships", x => new { x.TeamId, x.AccountId });
                    table.CheckConstraint("membership_role", "\"Role\" IN ('Admin','Member')");
                    table.ForeignKey(
                        name: "FK_team_memberships_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "sonda_access",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_grants",
                schema: "sonda_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_grants", x => x.Id);
                    table.CheckConstraint("grant_purpose", "\"Purpose\" IN ('Invitation','Reset')");
                    table.ForeignKey(
                        name: "FK_account_grants_team_memberships_TeamId_AccountId",
                        columns: x => new { x.TeamId, x.AccountId },
                        principalSchema: "sonda_access",
                        principalTable: "team_memberships",
                        principalColumns: new[] { "TeamId", "AccountId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_operations",
                schema: "sonda_access",
                columns: table => new
                {
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Target = table.Column<string>(type: "text", nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Result = table.Column<string>(type: "text", nullable: false),
                    Envelope = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_operations", x => new { x.TeamId, x.Id });
                    table.CheckConstraint("operation_state", "\"State\" IN ('Submitted','Committed','Rejected','OutcomeUnknown')");
                    table.ForeignKey(
                        name: "FK_api_operations_team_memberships_TeamId_AccountId",
                        columns: x => new { x.TeamId, x.AccountId },
                        principalSchema: "sonda_access",
                        principalTable: "team_memberships",
                        principalColumns: new[] { "TeamId", "AccountId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "web_sessions",
                schema: "sonda_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessRevision = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdleExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_web_sessions_team_memberships_TeamId_AccountId",
                        columns: x => new { x.TeamId, x.AccountId },
                        principalSchema: "sonda_access",
                        principalTable: "team_memberships",
                        principalColumns: new[] { "TeamId", "AccountId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_audit_TeamId_RecordedAt_Id",
                schema: "sonda_access",
                table: "access_audit",
                columns: new[] { "TeamId", "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_account_grants_Digest",
                schema: "sonda_access",
                table: "account_grants",
                column: "Digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_grants_TeamId_AccountId",
                schema: "sonda_access",
                table: "account_grants",
                columns: new[] { "TeamId", "AccountId" });
            migrationBuilder.Sql("""
                ALTER TABLE sonda_access.team_access_guards ADD CONSTRAINT guard_core_team FOREIGN KEY ("TeamId") REFERENCES sonda.teams(team_id);
                ALTER TABLE sonda_access.team_memberships ADD CONSTRAINT membership_core_team FOREIGN KEY ("TeamId") REFERENCES sonda.teams(team_id);
                ALTER TABLE sonda_access.application_metadata ADD CONSTRAINT metadata_core_application FOREIGN KEY ("TeamId","ApplicationId") REFERENCES sonda.applications(team_id,application_id);
                ALTER TABLE sonda_access.profile_previews ADD CONSTRAINT preview_core_profile FOREIGN KEY ("TeamId","ProfileId") REFERENCES sonda.profiles(team_id,profile_id);
                CREATE FUNCTION sonda_access.immutable_record() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Access audit and preview records are immutable'; END $$;
                CREATE TRIGGER audit_immutable BEFORE UPDATE OR DELETE ON sonda_access.access_audit FOR EACH ROW EXECUTE FUNCTION sonda_access.immutable_record();
                CREATE TRIGGER preview_immutable BEFORE UPDATE OR DELETE ON sonda_access.profile_previews FOR EACH ROW EXECUTE FUNCTION sonda_access.immutable_record();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_api_operations_TeamId_AccountId",
                schema: "sonda_access",
                table: "api_operations",
                columns: new[] { "TeamId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "sonda_access",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "sonda_access",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "sonda_access",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "sonda_access",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "sonda_access",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "sonda_access",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "sonda_access",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_AccountId",
                schema: "sonda_access",
                table: "team_memberships",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_web_sessions_TeamId_AccountId",
                schema: "sonda_access",
                table: "web_sessions",
                columns: new[] { "TeamId", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION sonda_access.immutable_record() CASCADE;");
            migrationBuilder.DropTable(
                name: "access_audit",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "account_grants",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "api_operations",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "application_metadata",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "profile_previews",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "team_access_guards",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "web_sessions",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "team_memberships",
                schema: "sonda_access");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "sonda_access");
        }
    }
}
