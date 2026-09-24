using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonda.Access.Migrations
{
    /// <inheritdoc />
    public partial class AccessSecurityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE sonda_access.team_memberships ADD CONSTRAINT membership_nonnegative_revision CHECK ("Revision">=0);
                ALTER TABLE sonda_access.api_operations ADD CONSTRAINT operation_nonempty_id CHECK ("Id"<>'00000000-0000-0000-0000-000000000000'::uuid);
                ALTER TABLE sonda_access.account_grants ADD CONSTRAINT grant_digest_shape CHECK ("Digest" ~ '^[0-9A-F]{64}$');
                CREATE FUNCTION sonda_access.operation_identity_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF ROW(NEW."TeamId",NEW."Id",NEW."AccountId",NEW."Action",NEW."Target",NEW."Fingerprint",NEW."Envelope",NEW."SubmittedAt") IS DISTINCT FROM
                     ROW(OLD."TeamId",OLD."Id",OLD."AccountId",OLD."Action",OLD."Target",OLD."Fingerprint",OLD."Envelope",OLD."SubmittedAt") THEN
                    RAISE EXCEPTION 'Operation identity is immutable';
                  END IF;
                  IF OLD."State" IN ('Committed','Rejected') AND ROW(NEW."State",NEW."Result") IS DISTINCT FROM ROW(OLD."State",OLD."Result") THEN
                    RAISE EXCEPTION 'Final operation outcome is immutable';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER operation_identity_guard BEFORE UPDATE ON sonda_access.api_operations FOR EACH ROW EXECUTE FUNCTION sonda_access.operation_identity_guard();
                """);
            migrationBuilder.CreateIndex(
                name: "IX_profile_previews_TeamId_AccountId",
                schema: "sonda_access",
                table: "profile_previews",
                columns: new[] { "TeamId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_access_audit_TeamId_AccountId",
                schema: "sonda_access",
                table: "access_audit",
                columns: new[] { "TeamId", "AccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_access_audit_team_memberships_TeamId_AccountId",
                schema: "sonda_access",
                table: "access_audit",
                columns: new[] { "TeamId", "AccountId" },
                principalSchema: "sonda_access",
                principalTable: "team_memberships",
                principalColumns: new[] { "TeamId", "AccountId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_profile_previews_team_memberships_TeamId_AccountId",
                schema: "sonda_access",
                table: "profile_previews",
                columns: new[] { "TeamId", "AccountId" },
                principalSchema: "sonda_access",
                principalTable: "team_memberships",
                principalColumns: new[] { "TeamId", "AccountId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION sonda_access.operation_identity_guard() CASCADE;
                ALTER TABLE sonda_access.team_memberships DROP CONSTRAINT membership_nonnegative_revision;
                ALTER TABLE sonda_access.api_operations DROP CONSTRAINT operation_nonempty_id;
                ALTER TABLE sonda_access.account_grants DROP CONSTRAINT grant_digest_shape;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_access_audit_team_memberships_TeamId_AccountId",
                schema: "sonda_access",
                table: "access_audit");

            migrationBuilder.DropForeignKey(
                name: "FK_profile_previews_team_memberships_TeamId_AccountId",
                schema: "sonda_access",
                table: "profile_previews");

            migrationBuilder.DropIndex(
                name: "IX_profile_previews_TeamId_AccountId",
                schema: "sonda_access",
                table: "profile_previews");

            migrationBuilder.DropIndex(
                name: "IX_access_audit_TeamId_AccountId",
                schema: "sonda_access",
                table: "access_audit");
        }
    }
}
