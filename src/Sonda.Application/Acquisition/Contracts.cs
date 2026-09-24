using Sonda.Application.Persistence;
using Sonda.Application.Processing;

namespace Sonda.Application.Acquisition;

public enum FileEncoding { Utf8, Utf16LittleEndian, Utf16BigEndian }
public enum CompletenessMode { None, ProducerManifest }
public enum RotationMode { Rename, OrderedFiles, CopyTruncate }
public enum ReaderState { Disabled, Discovering, CatchingUp, Following, WaitingForPartial, Unavailable, IdentityUncertain, Gap, Blocked }
public sealed record SourceConfiguration
{
    public string ProfileId { get; init; } = "";
    public string SourceKey { get; init; } = "";
    public long Revision { get; init; } = 1;
    public string Root { get; init; } = "";
    public string[] Include { get; init; } = ["*.log"];
    public string[] Exclude { get; init; } = [];
    public bool Recursive { get; init; }
    public bool Enabled { get; init; }
    public bool Required { get; init; } = true;
    public FileEncoding Encoding { get; init; }
    public RotationMode Rotation { get; init; }
    public CompletenessMode Completeness { get; init; }
    public string ProducerContract { get; init; } = "";
    public string ProducerManifestPath { get; init; } = "";
    public string RotationContract { get; init; } = "";
    public string RotationManifestPath { get; init; } = "";
    public string FileOrderExpression { get; init; } = "";
    public DateOnly? SampleDate { get; init; }
    public int ReadBytes { get; init; } = 65536;
    public int VisitBytes { get; init; } = 1048576;
    public int VisitRecords { get; init; } = 256;
    public int MaximumRecordBytes { get; init; } = 1048576;
    public int MaximumFiles { get; init; } = 1024;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId) || string.IsNullOrWhiteSpace(SourceKey) || Revision < 1 ||
            !Path.IsPathFullyQualified(Root) || Include.Length == 0 || !Enum.IsDefined(Encoding) || !Enum.IsDefined(Rotation) || !Enum.IsDefined(Completeness))
            throw new ArgumentException("Source identity, absolute root, patterns and explicit supported modes are required.");
        if (ReadBytes < 1 || ReadBytes > 1048576 || VisitBytes < ReadBytes || VisitBytes > 16777216 || VisitRecords < 1 || VisitRecords > 10000 ||
            MaximumRecordBytes < 4 || MaximumRecordBytes > VisitBytes || MaximumFiles<1 || MaximumFiles>10000 || PollInterval < TimeSpan.FromMilliseconds(100))
            throw new ArgumentException("Source limits must be bounded.");
        if (Completeness != CompletenessMode.None && string.IsNullOrWhiteSpace(ProducerContract)) throw new ArgumentException("Completeness requires an explicit producer contract.");
        if (Include.Concat(Exclude).Any(p => string.IsNullOrWhiteSpace(p) || p.Contains("..", StringComparison.Ordinal) || p.Contains(':') || Path.IsPathFullyQualified(p)))
            throw new ArgumentException("Patterns must stay inside the configured root.");
    }
}
public sealed record PhysicalFileIdentity(string Authority, string Volume, string FileId, string Incarnation);
public sealed record FileObservation(string Path, PhysicalFileIdentity Identity, long Length, DateTimeOffset ObservedAt, string PrefixHash);
public sealed record GenerationInfo(Guid Id, PhysicalFileIdentity Identity, long Epoch, long Offset, long ObservedLength,
    int PrefixLength, string PrefixHash, string State, string? Gap, string[] Paths, SourceConfiguration Configuration);
public sealed record PhysicalRecord(long Start, long End, byte[] Bytes, string Text, string Terminator, int BomBytes)
{
    public string Hash => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Bytes));
    public string EvidenceKey(string source, Guid generation) => System.Text.Json.JsonSerializer.Serialize(new { schema = 1, source, generation, offset = Start });
}
public sealed record FramingError(string Code, long Offset, string Message);
public sealed record FramingBatch(PhysicalRecord[] Records, FramingError? Error, int PartialBytes);
public sealed record OwnerFence(ProcessingScope Scope, Guid InstanceId, long Epoch);
public sealed record GenerationCheckpoint(Guid GenerationId, long Offset, long Revision, string ProfileId, string SourceKey, SourceConfiguration Configuration);
public sealed record IngestionResult(Guid ReceiptId, long CommittedOffset, bool Replayed, string Disposition);
public sealed record OperationalObservation(string SourceKey, ReaderState State, string Code, DateTimeOffset EffectiveAt, DateTimeOffset ProcessedAt,
    long? ObservedLength = null, long? CommittedOffset = null, bool? ReadSuccess = null);
public sealed record SourceCoverage(string SourceKey, Guid GenerationId, long ThroughOffset, long SourceRevision, string ManifestHash, DateTimeOffset CompleteThrough);
public sealed record ProducerCoverageManifest(string Contract, string SourceKey, DateTimeOffset CompleteThrough, SourceCoverage[] Generations,
    bool AllEarlierRecordsFlushed, bool NoFutureEarlierRecords, bool CompleteGenerationInventory);
public sealed record RotationManifest(string Contract,string SourceKey,Guid OldGeneration,string CurrentPath,string ArchivePath,
    long ArchiveLength,PhysicalFileIdentity ArchiveIdentity,bool ArchiveImmutable,bool CoversEntireOldGeneration);
public sealed record VerifiedRotation(string Kind,RotationManifest Rotation,long VerifiedOffset,long CheckpointRevision,string PrefixDigest);
public sealed record CompletenessCertificate(Guid Id, string ProfileId, long MembershipRevision, long ThroughSequence, DateTimeOffset CompleteThrough,
    string ProducerContract, SourceCoverage[] Sources);

public interface IIngestionCommitStore
{
    Task<OwnerFence> ClaimAsync(ProcessingScope scope, Guid instanceId, TimeSpan lease, CancellationToken ct = default);
    Task RenewAsync(OwnerFence fence, TimeSpan lease, CancellationToken ct = default);
    Task ReleaseAsync(OwnerFence fence, CancellationToken ct = default);
    Task<IngestionResult> CommitAsync(OwnerFence fence, Guid generation, PhysicalRecord record, DateTimeOffset processedAt, CancellationToken ct = default);
    Task<IngestionResult?> RecoverPendingAsync(OwnerFence fence, CancellationToken ct = default);
}
public interface IFileIdentityProvider { FileObservation Inspect(FileStream stream, string path, DateTimeOffset observedAt); }
public interface IDeadlineDispatcher { Task<PolicyReceipt?> DispatchAsync(OwnerFence fence, CompletenessCertificate certificate, DateTimeOffset processedAt, CancellationToken ct = default); }
