using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonda.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InterpretationPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "evidence_id",
                schema: "sonda",
                table: "processing_receipts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "sonda",
                table: "processing_receipts",
                type: "text",
                nullable: false,
                defaultValue: "Evidence");

            migrationBuilder.AddColumn<string>(
                name: "policy_context",
                schema: "sonda",
                table: "incidents",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "run_id",
                schema: "sonda",
                table: "incident_occurrences",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "policy_runtime",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    frontier = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_runtime", x => new { x.team_id, x.session_id, x.profile_id });
                    table.ForeignKey(
                        name: "FK_policy_runtime_profile_runtime_team_id_session_id_profile_id",
                        columns: x => new { x.team_id, x.session_id, x.profile_id },
                        principalSchema: "sonda",
                        principalTable: "profile_runtime",
                        principalColumns: new[] { "team_id", "session_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });
            using var stream = typeof(InterpretationPolicies).Assembly.GetManifestResourceStream("Sonda.Infrastructure.Persistence.Migrations.002_policy_constraints.sql")!;
            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Restore a pre-upgrade backup; destructive downgrade is not supported.");
    }
}