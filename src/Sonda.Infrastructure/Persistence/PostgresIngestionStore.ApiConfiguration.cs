using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;

namespace Sonda.Infrastructure.Persistence;

public sealed partial class PostgresIngestionStore
{
    public static string SourceCommandFingerprint(ProcessingScope scope, SourceConfiguration next, long expectedRevision) =>
        SimulationJson.Hash(new { kind = "ApiSourceConfiguration", scope, next, expectedRevision });

    /// <summary>Phase 5 command receipt around the accepted fenced/drained source configuration boundary.
    /// Existing Phase 4 methods and checkpoints remain unchanged. No source creation or repair is performed here.</summary>
    public async Task<long> ChangeSourceWithReceiptAsync(OwnerFence fence, SourceConfiguration next, long expectedRevision, Guid commandId, CancellationToken ct = default)
    {
        next.Validate(); if (commandId == Guid.Empty) throw new ArgumentException("Command identity required.");
        var fingerprint = SourceCommandFingerprint(fence.Scope, next, expectedRevision);
        await using var db = SondaDbContext.Open(connection); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RequireFence(db, fence, ct); var runtime = await PostgresProcessingStore.Lock(db, fence.Scope, ct);
        var prior = await db.Set<CommandRow>().FindAsync([fence.Scope.TeamId, commandId], ct);
        if (prior is not null) { PostgresConfigurationStore.Check(prior, fingerprint); return long.Parse(prior.Result, System.Globalization.CultureInfo.InvariantCulture); }
        var lane = await db.Set<LaneRow>().FindAsync([fence.Scope.TeamId, fence.Scope.SessionId, next.ProfileId], ct);
        if (lane?.ApplicationId != fence.Scope.ApplicationId) throw new PersistenceConflict("Source Profile is outside this application.");
        var row = await db.Set<AcquisitionSourceRow>().FindAsync([fence.Scope.TeamId, next.ProfileId, next.SourceKey], ct) ?? throw new PersistenceConflict("Unknown source.");
        if (row.Revision != expectedRevision || next.Revision != expectedRevision + 1) throw new PersistenceConflict("Source revision conflict.");
        if (await db.Set<PendingIngestionRow>().AnyAsync(p => p.TeamId == fence.Scope.TeamId && p.ApplicationId == fence.Scope.ApplicationId && p.Command != "", ct) ||
            await db.Set<RunRow>().AnyAsync(r => r.TeamId == fence.Scope.TeamId && r.SessionId == fence.Scope.SessionId && r.ProfileId == next.ProfileId && r.Result == null, ct))
            throw new PersistenceConflict("Source changes require drained processing/runs.");
        var old = SimulationJson.Deserialize<SourceConfiguration>((await db.Set<AcquisitionRevisionRow>().FindAsync([fence.Scope.TeamId, next.ProfileId, next.SourceKey, expectedRevision], ct))!.Configuration);
        var permitted = old with { Revision = next.Revision, Enabled = next.Enabled, PollInterval = next.PollInterval, ReadBytes = next.ReadBytes,
            VisitBytes = next.VisitBytes, VisitRecords = next.VisitRecords, MaximumFiles = next.MaximumFiles };
        if (SimulationJson.Serialize(permitted) != SimulationJson.Serialize(next) &&
            await db.Set<FileGenerationRow>().AnyAsync(g => g.TeamId == fence.Scope.TeamId && g.ProfileId == next.ProfileId && g.SourceKey == next.SourceKey, ct))
            throw new PersistenceConflict("Existing generation obligations prevent live source identity/framing/coverage change.");
        db.Add(new AcquisitionRevisionRow { TeamId = fence.Scope.TeamId, ProfileId = next.ProfileId, SourceKey = next.SourceKey, Revision = next.Revision,
            Configuration = SimulationJson.Serialize(next), Hash = SimulationJson.Hash(next) }); await db.SaveChangesAsync(ct);
        row.Revision = next.Revision; row.Enabled = next.Enabled; row.State = next.Enabled ? "Discovering" : "Disabled";
        await db.SaveChangesAsync(ct);
        var previous = await db.Set<SourceMembershipRow>().Where(m => m.TeamId == fence.Scope.TeamId && m.ProfileId == next.ProfileId).Select(m => m.Revision).MaxAsync(ct);
        var sources = await db.Set<AcquisitionSourceRow>().Where(s => s.TeamId == fence.Scope.TeamId && s.ProfileId == next.ProfileId).OrderBy(s => s.SourceKey).Select(s => new { s.SourceKey, s.Revision, s.Enabled }).ToArrayAsync(ct);
        db.Add(new SourceMembershipRow { TeamId = fence.Scope.TeamId, ProfileId = next.ProfileId, Revision = previous + 1,
            EffectiveSequence = runtime.NextSequence, Sources = SimulationJson.Serialize(sources) });
        foreach (var certificate in await db.Set<FrontierCertificateRow>().Where(c => c.TeamId == fence.Scope.TeamId && c.ProfileId == next.ProfileId && c.Status == "Validated").ToArrayAsync(ct)) certificate.Status = "Invalidated";
        // Draft source metadata follows this accepted change; prior published version-source snapshots stay immutable.
        var metadata = (await db.Set<SourceRow>().FindAsync([fence.Scope.TeamId, next.ProfileId, next.SourceKey], ct))!;
        metadata.Configuration = SimulationJson.Serialize(next); metadata.Revision++;
        db.Add(new CommandRow { TeamId = fence.Scope.TeamId, Id = commandId, Fingerprint = fingerprint, Result = next.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); fault?.Invoke(AcquisitionBoundary.AfterCommit); return next.Revision;
    }
}
