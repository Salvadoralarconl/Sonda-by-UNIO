using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;

namespace Sonda.Infrastructure.Persistence;
public sealed partial class PostgresIngestionStore : IDeadlineDispatcher
{
    public async Task<DateTimeOffset?> ReadFrontierAsync(ProcessingScope scope,string profile,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);
        return (await db.Set<PolicyRuntimeRow>().FindAsync([scope.TeamId,scope.SessionId,profile],ct))?.Frontier;
    }
    public async Task<CompletenessCertificate> CreateCertificateAsync(OwnerFence fence,string profile,string contract,SourceCoverage[] coverage,DateTimeOffset at,CancellationToken ct=default)
    {
        if(coverage.Length==0)throw new PersistenceConflict("No source coverage.");
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);await RequireFence(db,fence,ct);
        var runtime=await PostgresProcessingStore.Lock(db,fence.Scope,ct);
        var member=await db.Set<SourceMembershipRow>().Where(m=>m.TeamId==fence.Scope.TeamId&&m.ProfileId==profile).OrderByDescending(m=>m.Revision).FirstAsync(ct);
        var complete=coverage.Min(c=>c.CompleteThrough);if(complete>at)complete=at;
        var certificate=new CompletenessCertificate(Guid.NewGuid(),profile,member.Revision,runtime.NextSequence-1,complete,contract,coverage);
        await ValidateCoverage(db,fence,certificate,ct);return certificate;
    }
    public async Task RecordManifestAsync(OwnerFence fence,string profile,ProducerCoverageManifest manifest,string contentHash,CancellationToken ct=default)
    {
        if(!manifest.AllEarlierRecordsFlushed||!manifest.NoFutureEarlierRecords||!manifest.CompleteGenerationInventory||manifest.CompleteThrough==default||manifest.Generations.Length==0)
            throw new PersistenceConflict("Manifest does not prove complete coverage and future-event boundary.");
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);await RequireFence(db,fence,ct);await PostgresProcessingStore.Lock(db,fence.Scope,ct);
        var source=await db.Set<AcquisitionSourceRow>().FindAsync([fence.Scope.TeamId,profile,manifest.SourceKey],ct)??throw new PersistenceConflict("Unknown manifest source.");
        var revision=(await db.Set<AcquisitionRevisionRow>().FindAsync([fence.Scope.TeamId,profile,manifest.SourceKey,source.Revision],ct))!;var config=SimulationJson.Deserialize<SourceConfiguration>(revision.Configuration);
        if(config.Completeness!=CompletenessMode.ProducerManifest||config.ProducerContract!=manifest.Contract||contentHash!=SimulationJson.Hash(manifest))throw new PersistenceConflict("Manifest does not match configured completeness contract/hash.");
        var payload=SimulationJson.Serialize(manifest);
        if(!await db.Set<AcquisitionScanRow>().AnyAsync(r=>r.TeamId==fence.Scope.TeamId&&r.ProfileId==profile&&r.SourceKey==manifest.SourceKey&&r.Payload==payload,ct))db.Add(new AcquisitionScanRow{TeamId=fence.Scope.TeamId,Id=Guid.NewGuid(),ProfileId=profile,SourceKey=manifest.SourceKey,ObservedAt=await DatabaseNow(db,ct),Payload=payload});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private static async Task ValidateCoverage(SondaDbContext db,OwnerFence fence,CompletenessCertificate certificate,CancellationToken ct)
    {
        var s=fence.Scope;var runtime=await PostgresProcessingStore.Lock(db,s,ct);
        var lane=await db.Set<LaneRow>().FindAsync([s.TeamId,s.SessionId,certificate.ProfileId],ct)??throw new PersistenceConflict("Unknown Profile lane.");
        var version=(await db.Set<VersionRow>().FindAsync([s.TeamId,certificate.ProfileId,lane.Version],ct))!;
        if(SimulationJson.Deserialize<Sonda.Domain.Profiles.Profile>(version.Snapshot).Policy is null)throw new PersistenceConflict("LegacyV1 has no deterministic deadline dispatcher.");
        if(certificate.ThroughSequence!=runtime.NextSequence-1||certificate.CompleteThrough==default||string.IsNullOrWhiteSpace(certificate.ProducerContract))throw new PersistenceConflict("Stale or incomplete application frontier.");
        var membership=await db.Set<SourceMembershipRow>().Where(m=>m.TeamId==s.TeamId&&m.ProfileId==certificate.ProfileId).OrderByDescending(m=>m.Revision).FirstAsync(ct);
        if(membership.Revision!=certificate.MembershipRevision)throw new PersistenceConflict("Source membership changed.");
        var sources=await db.Set<AcquisitionSourceRow>().Where(x=>x.TeamId==s.TeamId&&x.ProfileId==certificate.ProfileId).ToArrayAsync(ct);
        if(sources.Length==0||certificate.Sources.Length==0||certificate.Sources.Select(c=>c.GenerationId).Distinct().Count()!=certificate.Sources.Length)throw new PersistenceConflict("Coverage is empty or duplicated.");
        foreach(var source in sources)
        {
            if(!source.Enabled||source.State is "Unavailable" or "IdentityUncertain" or "Gap" or "Blocked" or "WaitingForPartial" or "Disabled")throw new PersistenceConflict("Source is not complete.");
            var config=SimulationJson.Deserialize<SourceConfiguration>((await db.Set<AcquisitionRevisionRow>().FindAsync([s.TeamId,source.ProfileId,source.SourceKey,source.Revision],ct))!.Configuration);
            if(config.Completeness!=CompletenessMode.ProducerManifest||config.ProducerContract!=certificate.ProducerContract)throw new PersistenceConflict("Completeness is unproven; EOF is not a watermark.");
            var allGenerations=await db.Set<FileGenerationRow>().Where(g=>g.TeamId==s.TeamId&&g.ProfileId==source.ProfileId&&g.SourceKey==source.SourceKey).ToArrayAsync(ct);
            if(allGenerations.Length==0)throw new PersistenceConflict("Source has no proven generations.");
            var cover=certificate.Sources.Where(c=>c.SourceKey==source.SourceKey).ToArray();
            if(cover.Length!=allGenerations.Length)throw new PersistenceConflict("Incomplete generation inventory.");
            var scans=await db.Set<AcquisitionScanRow>().Where(r=>r.TeamId==s.TeamId&&r.ProfileId==source.ProfileId&&r.SourceKey==source.SourceKey).ToArrayAsync(ct);
            foreach(var coverage in cover)
            {
                var g=allGenerations.SingleOrDefault(g=>g.Id==coverage.GenerationId)??throw new PersistenceConflict("Foreign generation.");
                var checkpoint=(await db.Set<FileCheckpointRow>().FindAsync([s.TeamId,g.Id],ct))!;
                if(g.Gap is not null||g.SourceRevision!=coverage.SourceRevision||checkpoint.Offset<coverage.ThroughOffset||g.ObservedLength>checkpoint.Offset||coverage.CompleteThrough<certificate.CompleteThrough)
                    throw new PersistenceConflict("Unread or uncertain bytes block the frontier.");
                var scan=scans.Where(r=>!IsRotation(r.Payload)).Select(r=>SimulationJson.Deserialize<ProducerCoverageManifest>(r.Payload)).FirstOrDefault(m=>SimulationJson.Hash(m)==coverage.ManifestHash);
                if(scan is null||!scan.AllEarlierRecordsFlushed||!scan.NoFutureEarlierRecords||!scan.CompleteGenerationInventory||scan.Contract!=certificate.ProducerContract||scan.CompleteThrough<certificate.CompleteThrough||!scan.Generations.Any(c=>c.GenerationId==g.Id&&c.ThroughOffset==coverage.ThroughOffset&&c.SourceRevision==coverage.SourceRevision))
                    throw new PersistenceConflict("No matching durable producer proof.");
            }
        }
        if(certificate.Sources.Any(c=>!sources.Any(s=>s.SourceKey==c.SourceKey)))throw new PersistenceConflict("Certificate has unknown source.");
    }
    public async Task<PolicyReceipt?> DispatchAsync(OwnerFence fence,CompletenessCertificate certificate,DateTimeOffset processedAt,CancellationToken ct=default)
    {
        await using(var db=SondaDbContext.Open(connection))
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await RequireFence(db,fence,ct);var runtime=await PostgresProcessingStore.Lock(db,fence.Scope,ct);
            var existing=await db.Set<FrontierCertificateRow>().FindAsync([fence.Scope.TeamId,certificate.Id],ct);
            if(existing is not null&&existing.Payload!=SimulationJson.Serialize(certificate))throw new PersistenceConflict("Certificate identity reused.");
            if(existing is {Status:"Consumed",ReceiptId:{ } receiptId})
                return SimulationJson.Deserialize<PolicyReceipt>((await db.Set<ReceiptRow>().FindAsync([fence.Scope.TeamId,fence.Scope.SessionId,receiptId],ct))!.Trace);
            await ValidateCoverage(db,fence,certificate,ct);
            if(processedAt.UtcTicks<runtime.LastProcessedTicks||runtime.NextSequence>=int.MaxValue)throw new PersistenceConflict("Processing clock or sequence capacity conflict; no clock reserved.");
            if(certificate.CompleteThrough>processedAt)throw new PersistenceConflict("Frontier cannot outrun processing time.");
            var pending=await db.Set<PendingIngestionRow>().FindAsync([fence.Scope.TeamId,fence.Scope.ApplicationId],ct);
            if(pending is {Command.Length:>0})throw new PersistenceConflict("Pending earlier input blocks deadline evaluation.");
            if(existing is null){existing=new(){TeamId=fence.Scope.TeamId,Id=certificate.Id,ProfileId=certificate.ProfileId,MembershipRevision=certificate.MembershipRevision,Payload=SimulationJson.Serialize(certificate)};db.Add(existing);}
            else if(existing.Status!="Validated")throw new PersistenceConflict("Certificate invalidated.");
            var command=new PolicyCommand{Id=certificate.Id,Kind=PolicyCommandKind.AdvanceTime,Sequence=checked((int)certificate.ThroughSequence+1),ProcessedAt=processedAt,EffectiveAt=certificate.CompleteThrough,Frontier=new(certificate.ThroughSequence,certificate.CompleteThrough,0)};
            ReserveControl(db,fence,certificate.ProfileId,pending,command,new(Certificate:certificate));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        await RecoverPendingAsync(fence,ct);
        await using var resultDb=SondaDbContext.Open(connection);return SimulationJson.Deserialize<PolicyReceipt>((await resultDb.Set<ReceiptRow>().FindAsync([fence.Scope.TeamId,fence.Scope.SessionId,certificate.Id],ct))!.Trace);
    }
}
