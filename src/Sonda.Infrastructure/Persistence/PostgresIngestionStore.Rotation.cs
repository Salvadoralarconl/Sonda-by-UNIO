using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
namespace Sonda.Infrastructure.Persistence;
public sealed partial class PostgresIngestionStore
{
    public async IAsyncEnumerable<PhysicalRecord> ReadPhysicalRecordsAsync(ProcessingScope scope,Guid generation,[EnumeratorCancellation] CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);
        await foreach(var row in db.Set<FileRecordRow>().AsNoTracking().Where(r=>r.TeamId==scope.TeamId&&r.SessionId==scope.SessionId&&r.GenerationId==generation).OrderBy(r=>r.StartOffset).AsAsyncEnumerable().WithCancellation(ct))yield return SimulationJson.Deserialize<PhysicalRecord>(row.Record);
    }
    public async Task<VerifiedRotation[]> RotationProofsAsync(ProcessingScope scope,string profile,string source,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);var rows=await db.Set<AcquisitionScanRow>().Where(r=>r.TeamId==scope.TeamId&&r.ProfileId==profile&&r.SourceKey==source).Select(r=>r.Payload).ToArrayAsync(ct);
        return rows.Where(IsRotation).Select(SimulationJson.Deserialize<VerifiedRotation>).ToArray();
    }
    private static bool IsRotation(string json){using var doc=System.Text.Json.JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("kind",out var kind)&&kind.GetString()=="Rotation";}
    public async Task AcceptRotationAsync(OwnerFence fence,SourceConfiguration config,VerifiedRotation proof,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);await RequireFence(db,fence,ct);await PostgresProcessingStore.Lock(db,fence.Scope,ct);
        var g=await db.Set<FileGenerationRow>().FindAsync([fence.Scope.TeamId,proof.Rotation.OldGeneration],ct)??throw new PersistenceConflict("Unknown rotation predecessor.");
        var checkpoint=await LockCheckpoint(db,fence.Scope.TeamId,g.Id,ct);
        if(config.Rotation!=RotationMode.CopyTruncate||proof.Kind!="Rotation"||config.RotationContract.Length==0||proof.Rotation.Contract!=config.RotationContract||g.ProfileId!=config.ProfileId||g.SourceKey!=config.SourceKey||checkpoint.Offset!=proof.VerifiedOffset||checkpoint.Revision!=proof.CheckpointRevision||proof.Rotation.ArchiveLength<g.ObservedLength)
            throw new PersistenceConflict("Rotation proof is stale or incomplete.");
        if(await db.Set<PendingIngestionRow>().AnyAsync(p=>p.TeamId==fence.Scope.TeamId&&p.ApplicationId==fence.Scope.ApplicationId&&p.Command!="",ct))throw new PersistenceConflict("Pending command must finish before rotation reconciliation.");
        g.State="Archived";g.Gap=null;g.ObservedLength=proof.Rotation.ArchiveLength;
        db.Add(new AcquisitionScanRow{TeamId=fence.Scope.TeamId,Id=Guid.NewGuid(),ProfileId=config.ProfileId,SourceKey=config.SourceKey,ObservedAt=await DatabaseNow(db,ct),Payload=SimulationJson.Serialize(proof)});
        db.Add(new FilePathRow{TeamId=fence.Scope.TeamId,Id=Guid.NewGuid(),ObjectId=g.ObjectId,Path=proof.Rotation.ArchivePath,ObservedAt=await DatabaseNow(db,ct)});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
