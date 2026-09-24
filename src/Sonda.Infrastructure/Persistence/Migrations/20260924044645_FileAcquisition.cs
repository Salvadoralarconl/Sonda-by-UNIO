using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonda.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FileAcquisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_objects",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    identity_key = table.Column<string>(type: "text", nullable: false),
                    identity = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_objects", x => new { x.team_id, x.id });
                    table.ForeignKey(
                        name: "FK_file_objects_log_sources_team_id_profile_id_source_key",
                        columns: x => new { x.team_id, x.profile_id, x.source_key },
                        principalSchema: "sonda",
                        principalTable: "log_sources",
                        principalColumns: new[] { "team_id", "profile_id", "source_key" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_sessions",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_authority = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_sessions", x => new { x.team_id, x.application_id });
                    table.ForeignKey(
                        name: "FK_monitoring_sessions_application_runtime_team_id_session_id_~",
                        columns: x => new { x.team_id, x.session_id, x.application_id },
                        principalSchema: "sonda",
                        principalTable: "application_runtime",
                        principalColumns: new[] { "team_id", "session_id", "application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_acquisition_revisions",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    configuration = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_acquisition_revisions", x => new { x.team_id, x.profile_id, x.source_key, x.revision });
                    table.ForeignKey(
                        name: "FK_source_acquisition_revisions_log_sources_team_id_profile_id~",
                        columns: x => new { x.team_id, x.profile_id, x.source_key },
                        principalSchema: "sonda",
                        principalTable: "log_sources",
                        principalColumns: new[] { "team_id", "profile_id", "source_key" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_membership_sets",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    sources = table.Column<string>(type: "text", nullable: false),
                    effective_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_membership_sets", x => new { x.team_id, x.profile_id, x.revision });
                    table.ForeignKey(
                        name: "FK_source_membership_sets_profiles_team_id_profile_id",
                        columns: x => new { x.team_id, x.profile_id },
                        principalSchema: "sonda",
                        principalTable: "profiles",
                        principalColumns: new[] { "team_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_operational_events",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_operational_events", x => new { x.team_id, x.id });
                    table.ForeignKey(
                        name: "FK_source_operational_events_log_sources_team_id_profile_id_so~",
                        columns: x => new { x.team_id, x.profile_id, x.source_key },
                        principalSchema: "sonda",
                        principalTable: "log_sources",
                        principalColumns: new[] { "team_id", "profile_id", "source_key" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_scan_batches",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_scan_batches", x => new { x.team_id, x.id });
                    table.ForeignKey(
                        name: "FK_source_scan_batches_log_sources_team_id_profile_id_source_k~",
                        columns: x => new { x.team_id, x.profile_id, x.source_key },
                        principalSchema: "sonda",
                        principalTable: "log_sources",
                        principalColumns: new[] { "team_id", "profile_id", "source_key" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_path_observations",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_path_observations", x => new { x.team_id, x.id });
                    table.ForeignKey(
                        name: "FK_file_path_observations_file_objects_team_id_object_id",
                        columns: x => new { x.team_id, x.object_id },
                        principalSchema: "sonda",
                        principalTable: "file_objects",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_owners",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_owners", x => new { x.team_id, x.application_id });
                    table.ForeignKey(
                        name: "FK_ingestion_owners_monitoring_sessions_team_id_application_id",
                        columns: x => new { x.team_id, x.application_id },
                        principalSchema: "sonda",
                        principalTable: "monitoring_sessions",
                        principalColumns: new[] { "team_id", "application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_generations",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    predecessor = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    observed_length = table.Column<long>(type: "bigint", nullable: false),
                    prefix_hash = table.Column<string>(type: "text", nullable: false),
                    prefix_length = table.Column<int>(type: "integer", nullable: false),
                    gap = table.Column<string>(type: "text", nullable: true),
                    configuration = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_generations", x => new { x.team_id, x.id });
                    table.ForeignKey(
                        name: "FK_file_generations_file_generations_team_id_predecessor",
                        columns: x => new { x.team_id, x.predecessor },
                        principalSchema: "sonda",
                        principalTable: "file_generations",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_generations_file_objects_team_id_object_id",
                        columns: x => new { x.team_id, x.object_id },
                        principalSchema: "sonda",
                        principalTable: "file_objects",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_generations_source_acquisition_revisions_team_id_profi~",
                        columns: x => new { x.team_id, x.profile_id, x.source_key, x.source_revision },
                        principalSchema: "sonda",
                        principalTable: "source_acquisition_revisions",
                        principalColumns: new[] { "team_id", "profile_id", "source_key", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_acquisition_runtime",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    last_observation = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_acquisition_runtime", x => new { x.team_id, x.profile_id, x.source_key });
                    table.ForeignKey(
                        name: "FK_source_acquisition_runtime_source_acquisition_revisions_tea~",
                        columns: x => new { x.team_id, x.profile_id, x.source_key, x.revision },
                        principalSchema: "sonda",
                        principalTable: "source_acquisition_revisions",
                        principalColumns: new[] { "team_id", "profile_id", "source_key", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "frontier_certificates",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    membership_revision = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frontier_certificates", x => new { x.team_id, x.id });
                    table.ForeignKey(
                        name: "FK_frontier_certificates_source_membership_sets_team_id_profil~",
                        columns: x => new { x.team_id, x.profile_id, x.membership_revision },
                        principalSchema: "sonda",
                        principalTable: "source_membership_sets",
                        principalColumns: new[] { "team_id", "profile_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_checkpoints",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offset = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fence_epoch = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_checkpoints", x => new { x.team_id, x.generation_id });
                    table.ForeignKey(
                        name: "FK_file_checkpoints_file_generations_team_id_generation_id",
                        columns: x => new { x.team_id, x.generation_id },
                        principalSchema: "sonda",
                        principalTable: "file_generations",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_record_evidence",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_offset = table.Column<long>(type: "bigint", nullable: false),
                    end_offset = table.Column<long>(type: "bigint", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fence_epoch = table.Column<long>(type: "bigint", nullable: false),
                    record = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_record_evidence", x => new { x.team_id, x.generation_id, x.start_offset });
                    table.ForeignKey(
                        name: "FK_file_record_evidence_file_generations_team_id_generation_id",
                        columns: x => new { x.team_id, x.generation_id },
                        principalSchema: "sonda",
                        principalTable: "file_generations",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_record_evidence_processing_receipts_team_id_session_id~",
                        columns: x => new { x.team_id, x.session_id, x.receipt_id },
                        principalSchema: "sonda",
                        principalTable: "processing_receipts",
                        principalColumns: new[] { "team_id", "session_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_record_evidence_raw_evidence_team_id_session_id_eviden~",
                        columns: x => new { x.team_id, x.session_id, x.evidence_id },
                        principalSchema: "sonda",
                        principalTable: "raw_evidence",
                        principalColumns: new[] { "team_id", "session_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_pending_commands",
                schema: "sonda",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_offset = table.Column<long>(type: "bigint", nullable: true),
                    end_offset = table.Column<long>(type: "bigint", nullable: true),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    record = table.Column<string>(type: "text", nullable: true),
                    command = table.Column<string>(type: "text", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    certificate = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_pending_commands", x => new { x.team_id, x.application_id });
                    table.ForeignKey(
                        name: "FK_ingestion_pending_commands_file_generations_team_id_generat~",
                        columns: x => new { x.team_id, x.generation_id },
                        principalSchema: "sonda",
                        principalTable: "file_generations",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ingestion_pending_commands_monitoring_sessions_team_id_appl~",
                        columns: x => new { x.team_id, x.application_id },
                        principalSchema: "sonda",
                        principalTable: "monitoring_sessions",
                        principalColumns: new[] { "team_id", "application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_generations_team_id_object_id_epoch",
                schema: "sonda",
                table: "file_generations",
                columns: new[] { "team_id", "object_id", "epoch" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_generations_team_id_predecessor",
                schema: "sonda",
                table: "file_generations",
                columns: new[] { "team_id", "predecessor" });

            migrationBuilder.CreateIndex(
                name: "IX_file_generations_team_id_profile_id_source_key_source_revis~",
                schema: "sonda",
                table: "file_generations",
                columns: new[] { "team_id", "profile_id", "source_key", "source_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_file_objects_team_id_identity_key",
                schema: "sonda",
                table: "file_objects",
                columns: new[] { "team_id", "identity_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_objects_team_id_profile_id_source_key",
                schema: "sonda",
                table: "file_objects",
                columns: new[] { "team_id", "profile_id", "source_key" });

            migrationBuilder.CreateIndex(
                name: "IX_file_path_observations_team_id_object_id",
                schema: "sonda",
                table: "file_path_observations",
                columns: new[] { "team_id", "object_id" });

            migrationBuilder.CreateIndex(
                name: "IX_file_record_evidence_team_id_session_id_evidence_id",
                schema: "sonda",
                table: "file_record_evidence",
                columns: new[] { "team_id", "session_id", "evidence_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_record_evidence_team_id_session_id_receipt_id",
                schema: "sonda",
                table: "file_record_evidence",
                columns: new[] { "team_id", "session_id", "receipt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_frontier_certificates_team_id_profile_id_membership_revision",
                schema: "sonda",
                table: "frontier_certificates",
                columns: new[] { "team_id", "profile_id", "membership_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_pending_commands_team_id_command_id",
                schema: "sonda",
                table: "ingestion_pending_commands",
                columns: new[] { "team_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_pending_commands_team_id_generation_id",
                schema: "sonda",
                table: "ingestion_pending_commands",
                columns: new[] { "team_id", "generation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_sessions_team_id_session_id",
                schema: "sonda",
                table: "monitoring_sessions",
                columns: new[] { "team_id", "session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_sessions_team_id_session_id_application_id",
                schema: "sonda",
                table: "monitoring_sessions",
                columns: new[] { "team_id", "session_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "IX_source_acquisition_runtime_team_id_profile_id_source_key_re~",
                schema: "sonda",
                table: "source_acquisition_runtime",
                columns: new[] { "team_id", "profile_id", "source_key", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_source_operational_events_team_id_profile_id_source_key",
                schema: "sonda",
                table: "source_operational_events",
                columns: new[] { "team_id", "profile_id", "source_key" });

            migrationBuilder.CreateIndex(
                name: "IX_source_scan_batches_team_id_profile_id_source_key",
                schema: "sonda",
                table: "source_scan_batches",
                columns: new[] { "team_id", "profile_id", "source_key" });
            using var stream = typeof(FileAcquisition).Assembly.GetManifestResourceStream("Sonda.Infrastructure.Persistence.Migrations.003_acquisition_constraints.sql")!;
            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Restore a pre-upgrade backup; destructive downgrade is not supported.");
    }
}