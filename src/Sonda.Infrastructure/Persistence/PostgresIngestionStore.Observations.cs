using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;

namespace Sonda.Infrastructure.Persistence;
internal sealed record PendingControl(OperationalObservation? Observation=null,CompletenessCertificate? Certificate=null);
public sealed partial class PostgresIngestionStore
{
    public async Task ObserveSourceAsync(OwnerFence fence,string profile,OperationalObservation observation,CancellationToken ct=default)
    {
        if(observation.ProcessedAt==default||observation.EffectiveAt>observation.ProcessedAt)throw new ArgumentException("Explicit valid observation clocks required.");
        await using(var db=SondaDbContext.Open(connection))
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await RequireFence(db,fence,ct);var runtime=await PostgresProcessingStore.Lock(db,fence.Scope,ct);
            if(observation.ProcessedAt.UtcTicks<runtime.LastProcessedTicks||runtime.NextSequence>=int.MaxValue)throw new PersistenceConflict("Processing clock or sequence capacity conflict; no observation reserved.");
            var source=await db.Set<AcquisitionSourceRow>().FindAsync([fence.Scope.TeamId,profile,observation.SourceKey],ct)??throw new PersistenceConflict("Unknown source.");
            var lane=(await db.Set<LaneRow>().FindAsync([fence.Scope.TeamId,fence.Scope.SessionId,profile],ct))!;
            var version=(await db.Set<VersionRow>().FindAsync([fence.Scope.TeamId,profile,lane.Version],ct))!;
            if(SimulationJson.Deserialize<Profile>(version.Snapshot).Policy is null||observation.ReadSuccess is null)
            {
                await SaveObservation(db,fence,profile,observation,null,ct);await tx.CommitAsync(ct);return;
            }
            var pending=await db.Set<PendingIngestionRow>().FindAsync([fence.Scope.TeamId,fence.Scope.ApplicationId],ct);
            if(pending is {Command.Length:>0})throw new PersistenceConflict("Earlier reserved command must finish before observation.");
            var command=new PolicyCommand{Id=Guid.NewGuid(),Kind=PolicyCommandKind.SourceObservation,Sequence=checked((int)runtime.NextSequence),ProcessedAt=observation.ProcessedAt,EffectiveAt=observation.EffectiveAt,Source=observation.SourceKey,ReadSuccess=observation.ReadSuccess.Value};
            ReserveControl(db,fence,profile,pending,command,new(Observation:observation));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        await RecoverPendingAsync(fence,ct);
    }
    private static void ReserveControl(SondaDbContext db,OwnerFence fence,string profile,PendingIngestionRow? pending,PolicyCommand command,PendingControl metadata)
    {
        pending??=new(){TeamId=fence.Scope.TeamId,ApplicationId=fence.Scope.ApplicationId};if(db.Entry(pending).State==EntityState.Detached)db.Add(pending);
        pending.CommandId=command.Id;pending.ProfileId=profile;pending.GenerationId=null;pending.StartOffset=null;pending.EndOffset=null;pending.Bytes=null;pending.Record=null;
        pending.Command=SimulationJson.Serialize(command);pending.Fingerprint=SimulationJson.Hash(command);pending.Certificate=SimulationJson.Serialize(metadata);
    }
    private static async Task SaveObservation(SondaDbContext db,OwnerFence fence,string profile,OperationalObservation observation,Guid? receipt,CancellationToken ct)
    {
        var id=Guid.NewGuid();db.Add(new SourceOperationalRow{TeamId=fence.Scope.TeamId,Id=id,ProfileId=profile,SourceKey=observation.SourceKey,ProcessedAt=observation.ProcessedAt.ToUniversalTime(),Payload=SimulationJson.Serialize(observation),ReceiptId=receipt});
        var source=(await db.Set<AcquisitionSourceRow>().FindAsync([fence.Scope.TeamId,profile,observation.SourceKey],ct))!;
        if(source.State!="Blocked")source.State=observation.State.ToString();source.Error=observation.Code;source.LastObservation=id;
        if(observation.ReadSuccess==false||observation.State is ReaderState.Gap or ReaderState.IdentityUncertain or ReaderState.Blocked)
        {
            var proofs=await db.Set<FrontierCertificateRow>().Where(p=>p.TeamId==fence.Scope.TeamId&&p.ProfileId==profile&&p.Status=="Validated").ToArrayAsync(ct);
            foreach(var proof in proofs)proof.Status="Invalidated";
        }
        await db.SaveChangesAsync(ct);
    }
    private static async Task CompleteControl(SondaDbContext db,OwnerFence fence,PendingIngestionRow pending,PolicyCommand command,Guid receipt,CancellationToken ct)
    {
        var control=SimulationJson.Deserialize<PendingControl>(pending.Certificate!);
        if(control.Observation is { } observation)await SaveObservation(db,fence,pending.ProfileId,observation,receipt,ct);
        if(control.Certificate is { } certificate)
        {
            var row=await db.Set<FrontierCertificateRow>().FindAsync([fence.Scope.TeamId,certificate.Id],ct)??throw new PersistenceConflict("Missing certificate.");
            if(row.Status!="Validated"||row.Payload!=SimulationJson.Serialize(certificate))throw new PersistenceConflict("Certificate no longer valid.");
            row.Status="Consumed";row.ReceiptId=receipt;
        }
    }
}
