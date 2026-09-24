using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.RegularExpressions;

namespace Sonda.Infrastructure.Persistence;

public sealed class SondaDbContext(DbContextOptions<SondaDbContext> options) : DbContext(options)
{
    public static SondaDbContext Open(string connection) => new(new DbContextOptionsBuilder<SondaDbContext>().UseNpgsql(connection).Options);
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("sonda");
        Map<TeamRow>("teams", "TeamId"); Map<ApplicationRow>("applications", "TeamId", "ApplicationId");
        Map<ProfileRow>("profiles", "TeamId", "ProfileId"); Map<VersionRow>("profile_versions", "TeamId", "ProfileId", "Version");
        Map<RuleRow>("profile_rules", "TeamId", "ProfileId", "Version", "Key");
        Map<PatternRow>("profile_patterns", "TeamId", "ProfileId", "Version", "RuleKey", "Ordinal");
        Map<ParsingRow>("profile_parsing_configurations", "TeamId", "ProfileId", "Version");
        Map<IdentifierRow>("profile_identifier_configurations", "TeamId", "ProfileId", "Version");
        Map<SourceRow>("log_sources", "TeamId", "ProfileId", "SourceKey");
        Map<SessionRow>("processing_sessions", "TeamId", "SessionId");
        Map<RuntimeRow>("application_runtime", "TeamId", "SessionId", "ApplicationId");
        Map<LaneRow>("profile_runtime", "TeamId", "SessionId", "ProfileId");
        Map<ActivationRow>("profile_activations", "TeamId", "SessionId", "ProfileId", "Revision");
        Map<EvidenceRow>("raw_evidence", "TeamId", "SessionId", "Id");
        Map<ReceiptRow>("processing_receipts", "TeamId", "SessionId", "Id");
        Map<NormalizedRow>("normalized_evidence", "TeamId", "SessionId", "ReceiptId");
        Map<RunRow>("runs", "TeamId", "SessionId", "Id");
        Map<ProblemRow>("problem_identities", "TeamId", "SessionId", "Id");
        Map<IncidentRow>("incidents", "TeamId", "SessionId", "Id");
        Map<OccurrenceRow>("incident_occurrences", "TeamId", "SessionId", "Id");
        Map<RecoveryRow>("recoveries", "TeamId", "SessionId", "IncidentId", "RunId");
        Map<HistoryRow>("incident_status_history", "TeamId", "SessionId", "IncidentId", "Ordinal");
        Map<FactRow>("run_metric_facts", "TeamId", "SessionId", "RunId");
        Map<CommandRow>("command_receipts", "TeamId", "Id");
        Map<ObservationRow>("commit_observations", "TeamId", "SessionId", "ReceiptId");
        Map<RunEvidenceRow>("run_evidence", "TeamId", "SessionId", "RunId", "ReceiptId");
        Map<OccurrenceEvidenceRow>("occurrence_evidence", "TeamId", "SessionId", "OccurrenceId", "ReceiptId");
        Map<RecoveryEvidenceRow>("recovery_evidence", "TeamId", "SessionId", "IncidentId", "RunId", "ReceiptId");
        Map<SimulationReportRow>("simulation_reports", "TeamId", "Id");
        Map<ValidationRow>("profile_validations", "TeamId", "ProfileId", "Version");
        Map<VersionSourceRow>("profile_version_sources", "TeamId", "ProfileId", "Version", "SourceKey");
        Map<RequestKeyRow>("processing_request_keys", "TeamId", "SessionId", "RequestId");
        Map<PolicyRuntimeRow>("policy_runtime", "TeamId", "SessionId", "ProfileId");
        Map<MonitoringSessionRow>("monitoring_sessions", "TeamId", "ApplicationId");
        Map<AcquisitionRevisionRow>("source_acquisition_revisions", "TeamId", "ProfileId", "SourceKey", "Revision");
        Map<AcquisitionSourceRow>("source_acquisition_runtime", "TeamId", "ProfileId", "SourceKey");
        Map<SourceMembershipRow>("source_membership_sets", "TeamId", "ProfileId", "Revision");
        Map<IngestionOwnerRow>("ingestion_owners", "TeamId", "ApplicationId");
        Map<FileObjectRow>("file_objects", "TeamId", "Id"); Map<FilePathRow>("file_path_observations", "TeamId", "Id");
        Map<FileGenerationRow>("file_generations", "TeamId", "Id"); Map<FileCheckpointRow>("file_checkpoints", "TeamId", "GenerationId");
        Map<PendingIngestionRow>("ingestion_pending_commands", "TeamId", "ApplicationId");
        Map<FileRecordRow>("file_record_evidence", "TeamId", "GenerationId", "StartOffset");
        Map<AcquisitionScanRow>("source_scan_batches", "TeamId", "Id"); Map<SourceOperationalRow>("source_operational_events", "TeamId", "Id");
        Map<FrontierCertificateRow>("frontier_certificates", "TeamId", "Id");
        Foreign<MonitoringSessionRow, RuntimeRow>("TeamId", "SessionId", "ApplicationId");
        b.Entity<MonitoringSessionRow>().HasIndex(x => new { x.TeamId, x.SessionId }).IsUnique();
        Foreign<AcquisitionRevisionRow, SourceRow>("TeamId", "ProfileId", "SourceKey");
        Foreign<AcquisitionSourceRow, AcquisitionRevisionRow>("TeamId", "ProfileId", "SourceKey", "Revision");
        Foreign<SourceMembershipRow, ProfileRow>("TeamId", "ProfileId");
        Foreign<IngestionOwnerRow, MonitoringSessionRow>("TeamId", "ApplicationId");
        Foreign<FileObjectRow, SourceRow>("TeamId", "ProfileId", "SourceKey");
        b.Entity<FileObjectRow>().HasIndex(x => new { x.TeamId, x.IdentityKey }).IsUnique();
        Foreign<FilePathRow, FileObjectRow>("TeamId", "ObjectId");
        Foreign<FileGenerationRow, FileObjectRow>("TeamId", "ObjectId");
        Foreign<FileGenerationRow, AcquisitionRevisionRow>("TeamId", "ProfileId", "SourceKey", "SourceRevision");
        Foreign<FileGenerationRow, FileGenerationRow>("TeamId", "Predecessor");
        b.Entity<FileGenerationRow>().HasIndex(x => new { x.TeamId, x.ObjectId, x.Epoch }).IsUnique();
        Foreign<FileCheckpointRow, FileGenerationRow>("TeamId", "GenerationId");
        Foreign<PendingIngestionRow, MonitoringSessionRow>("TeamId", "ApplicationId");
        Foreign<PendingIngestionRow, FileGenerationRow>("TeamId", "GenerationId");
        b.Entity<PendingIngestionRow>().HasIndex(x => new { x.TeamId, x.CommandId }).IsUnique();
        Foreign<FileRecordRow, FileGenerationRow>("TeamId", "GenerationId");
        Foreign<FileRecordRow, EvidenceRow>("TeamId", "SessionId", "EvidenceId");
        Foreign<FileRecordRow, ReceiptRow>("TeamId", "SessionId", "ReceiptId");
        b.Entity<FileRecordRow>().HasIndex(x => new { x.TeamId, x.SessionId, x.EvidenceId }).IsUnique();
        Foreign<AcquisitionScanRow, SourceRow>("TeamId", "ProfileId", "SourceKey");
        Foreign<SourceOperationalRow, SourceRow>("TeamId", "ProfileId", "SourceKey");
        Foreign<FrontierCertificateRow, SourceMembershipRow>("TeamId", "ProfileId", "MembershipRevision");
        Foreign<PolicyRuntimeRow, LaneRow>("TeamId", "SessionId", "ProfileId");
        b.Entity<ReceiptRow>().Property(r => r.Kind).HasDefaultValue("Evidence");
        Foreign<RequestKeyRow, ReceiptRow>("TeamId", "SessionId", "ReceiptId");
        Foreign<ApplicationRow, TeamRow>("TeamId"); Foreign<ProfileRow, ApplicationRow>("TeamId", "ApplicationId");
        Foreign<VersionRow, ProfileRow>("TeamId", "ProfileId"); Foreign<RuleRow, VersionRow>("TeamId", "ProfileId", "Version");
        Foreign<PatternRow, RuleRow>("TeamId", "ProfileId", "Version", "RuleKey");
        Foreign<ParsingRow, VersionRow>("TeamId", "ProfileId", "Version"); Foreign<IdentifierRow, VersionRow>("TeamId", "ProfileId", "Version");
        Foreign<SourceRow, ProfileRow>("TeamId", "ProfileId"); Foreign<SessionRow, TeamRow>("TeamId");
        Foreign<RuntimeRow, SessionRow>("TeamId", "SessionId"); Foreign<RuntimeRow, ApplicationRow>("TeamId", "ApplicationId");
        Foreign<LaneRow, RuntimeRow>("TeamId", "SessionId", "ApplicationId"); Foreign<LaneRow, VersionRow>("TeamId", "ProfileId", "Version");
        Foreign<ActivationRow, LaneRow>("TeamId", "SessionId", "ProfileId"); Foreign<ActivationRow, VersionRow>("TeamId", "ProfileId", "Version");
        Foreign<EvidenceRow, LaneRow>("TeamId", "SessionId", "ProfileId"); Foreign<EvidenceRow, SourceRow>("TeamId", "ProfileId", "SourceKey");
        Foreign<ReceiptRow, EvidenceRow>("TeamId", "SessionId", "EvidenceId"); Foreign<ReceiptRow, VersionRow>("TeamId", "ProfileId", "Version");
        Foreign<NormalizedRow, ReceiptRow>("TeamId", "SessionId", "ReceiptId");
        Foreign<RunRow, VersionRow>("TeamId", "ProfileId", "Version"); Foreign<RunRow, ReceiptRow>("TeamId", "SessionId", "CreatedReceipt");
        Foreign<RunRow, ReceiptRow>("TeamId", "SessionId", "CompletedReceipt"); Foreign<RunRow, RunRow>("TeamId", "SessionId", "ParentId");
        Foreign<ProblemRow, SessionRow>("TeamId", "SessionId"); Foreign<IncidentRow, ProblemRow>("TeamId", "SessionId", "ProblemId");
        Foreign<OccurrenceRow, IncidentRow>("TeamId", "SessionId", "IncidentId"); Foreign<OccurrenceRow, RunRow>("TeamId", "SessionId", "RunId");
        Foreign<RecoveryRow, IncidentRow>("TeamId", "SessionId", "IncidentId"); Foreign<RecoveryRow, RunRow>("TeamId", "SessionId", "RunId");
        Foreign<HistoryRow, IncidentRow>("TeamId", "SessionId", "IncidentId"); Foreign<FactRow, RunRow>("TeamId", "SessionId", "RunId");
        Foreign<FactRow, ReceiptRow>("TeamId", "SessionId", "ReceiptId"); Foreign<ObservationRow, ReceiptRow>("TeamId", "SessionId", "ReceiptId");
        Foreign<RunEvidenceRow, RunRow>("TeamId", "SessionId", "RunId"); Foreign<RunEvidenceRow, NormalizedRow>("TeamId", "SessionId", "ReceiptId");
        Foreign<OccurrenceEvidenceRow, OccurrenceRow>("TeamId", "SessionId", "OccurrenceId"); Foreign<OccurrenceEvidenceRow, NormalizedRow>("TeamId", "SessionId", "ReceiptId");
        Foreign<RecoveryEvidenceRow, RecoveryRow>("TeamId", "SessionId", "IncidentId", "RunId"); Foreign<RecoveryEvidenceRow, NormalizedRow>("TeamId", "SessionId", "ReceiptId");
        Foreign<SimulationReportRow, VersionRow>("TeamId", "ProfileId", "Version"); Foreign<ValidationRow, SimulationReportRow>("TeamId", "ReportId"); Foreign<VersionSourceRow, VersionRow>("TeamId", "ProfileId", "Version");
        b.Entity<ReceiptRow>().Property(r => r.RecordedAt).HasDefaultValueSql("clock_timestamp()");
        b.Entity<ReceiptRow>().Property(r => r.TransactionId).HasSentinel("").HasDefaultValueSql("pg_current_xact_id()::text");
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
                if (property.ClrType == typeof(string)) property.SetColumnType("text");
                if (property.Name == "Revision" && !property.IsPrimaryKey()) property.IsConcurrencyToken = true;
            }
        void Map<T>(string table, params string[] keys) where T : class
        {
            b.Entity<T>().ToTable(table).HasKey(keys);
        }
        void Foreign<T, U>(params string[] names) where T : class where U : class => b.Entity<T>().HasOne<U>().WithMany().HasForeignKey(names).OnDelete(DeleteBehavior.Restrict);
    }
}
