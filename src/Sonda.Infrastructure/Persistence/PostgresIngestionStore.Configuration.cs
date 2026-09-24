using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;

namespace Sonda.Infrastructure.Persistence;
public sealed partial class PostgresIngestionStore
{
    public async Task<PolicyReceipt> SubmitControlAsync(OwnerFence fence,string profile,PolicyCommand command,CancellationToken ct=default)
    {
        if(command.Kind is not (PolicyCommandKind.ActivateVersion or PolicyCommandKind.ChangeStatus))throw new PersistenceConflict("Evidence/observations/deadlines require their provenance-specific admission paths.");
        await using(var db=SondaDbContext.Open(connection))
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await RequireFence(db,fence,ct);var runtime=await PostgresProcessingStore.Lock(db,fence.Scope,ct);
            var receipt=await db.Set<ReceiptRow>().SingleOrDefaultAsync(r=>r.TeamId==fence.Scope.TeamId&&r.SessionId==fence.Scope.SessionId&&r.RequestId==command.Id,ct);
            if(receipt is not null)
            {
                if(receipt.ProfileId!=profile||receipt.Fingerprint!=SimulationJson.Hash(command with {Id=Guid.Empty}))throw new PersistenceConflict("Control command identity conflict.");
                return SimulationJson.Deserialize<PolicyReceipt>(receipt.Trace);
            }
            var pending=await db.Set<PendingIngestionRow>().FindAsync([fence.Scope.TeamId,fence.Scope.ApplicationId],ct);
            if(pending is {Command.Length:>0})
            {
                if(pending.CommandId!=command.Id||pending.Fingerprint!=SimulationJson.Hash(command))throw new PersistenceConflict("Earlier pending input must finish before control changes.");
            }
            else
            {
                if(command.Id==Guid.Empty||command.Sequence!=runtime.NextSequence||command.ProcessedAt==default||command.ProcessedAt.UtcTicks<runtime.LastProcessedTicks)throw new PersistenceConflict("Control sequence/time conflict.");
                ReserveControl(db,fence,profile,pending,command,new());await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        await RecoverPendingAsync(fence,ct);
        await using var reader=SondaDbContext.Open(connection);return SimulationJson.Deserialize<PolicyReceipt>((await reader.Set<ReceiptRow>().FindAsync([fence.Scope.TeamId,fence.Scope.SessionId,command.Id],ct))!.Trace);
    }
    public async Task ChangeSourceAsync(OwnerFence fence,SourceConfiguration next,long expectedRevision,CancellationToken ct=default)
    {
        next.Validate();await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);await RequireFence(db,fence,ct);var runtime=await PostgresProcessingStore.Lock(db,fence.Scope,ct);
        var row=await db.Set<AcquisitionSourceRow>().FindAsync([fence.Scope.TeamId,next.ProfileId,next.SourceKey],ct)??throw new PersistenceConflict("Unknown source.");
        if(row.Revision!=expectedRevision||next.Revision!=expectedRevision+1)throw new PersistenceConflict("Source revision conflict.");
        if(await db.Set<PendingIngestionRow>().AnyAsync(p=>p.TeamId==fence.Scope.TeamId&&p.ApplicationId==fence.Scope.ApplicationId&&p.Command!="",ct)||
           await db.Set<RunRow>().AnyAsync(r=>r.TeamId==fence.Scope.TeamId&&r.SessionId==fence.Scope.SessionId&&r.ProfileId==next.ProfileId&&r.Result==null,ct))throw new PersistenceConflict("Source changes require drained processing/runs.");
        var old=SimulationJson.Deserialize<SourceConfiguration>((await db.Set<AcquisitionRevisionRow>().FindAsync([fence.Scope.TeamId,next.ProfileId,next.SourceKey,expectedRevision],ct))!.Configuration);
        // A physical stream cannot be re-framed or rebound after acquisition. A new source identity must retain the old obligations.
        var structural=old with {Revision=next.Revision,Enabled=next.Enabled,PollInterval=next.PollInterval,ReadBytes=next.ReadBytes,VisitBytes=next.VisitBytes,VisitRecords=next.VisitRecords,MaximumFiles=next.MaximumFiles};
        if(SimulationJson.Serialize(structural)!=SimulationJson.Serialize(next)&&await db.Set<FileGenerationRow>().AnyAsync(g=>g.TeamId==fence.Scope.TeamId&&g.ProfileId==next.ProfileId&&g.SourceKey==next.SourceKey,ct))
            throw new PersistenceConflict("Existing generation obligations prevent live source identity/framing/coverage change; use an explicitly reviewed new source boundary.");
        db.Add(new AcquisitionRevisionRow{TeamId=fence.Scope.TeamId,ProfileId=next.ProfileId,SourceKey=next.SourceKey,Revision=next.Revision,Configuration=SimulationJson.Serialize(next),Hash=SimulationJson.Hash(next)});await db.SaveChangesAsync(ct);
        row.Revision=next.Revision;row.Enabled=next.Enabled;row.State=next.Enabled?"Discovering":"Disabled";await db.SaveChangesAsync(ct);
        var previous=await db.Set<SourceMembershipRow>().Where(m=>m.TeamId==fence.Scope.TeamId&&m.ProfileId==next.ProfileId).Select(m=>m.Revision).MaxAsync(ct);
        var sources=await db.Set<AcquisitionSourceRow>().Where(s=>s.TeamId==fence.Scope.TeamId&&s.ProfileId==next.ProfileId).OrderBy(s=>s.SourceKey).Select(s=>new{s.SourceKey,s.Revision,s.Enabled}).ToArrayAsync(ct);
        db.Add(new SourceMembershipRow{TeamId=fence.Scope.TeamId,ProfileId=next.ProfileId,Revision=previous+1,EffectiveSequence=runtime.NextSequence,Sources=SimulationJson.Serialize(sources)});
        foreach(var certificate in await db.Set<FrontierCertificateRow>().Where(c=>c.TeamId==fence.Scope.TeamId&&c.ProfileId==next.ProfileId&&c.Status=="Validated").ToArrayAsync(ct))certificate.Status="Invalidated";
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
