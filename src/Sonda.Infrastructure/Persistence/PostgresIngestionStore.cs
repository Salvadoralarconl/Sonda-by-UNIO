using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;

namespace Sonda.Infrastructure.Persistence;

public enum AcquisitionBoundary { AfterReservation, BeforeInterpretation, AfterCheckpoint, AfterCommit }
public sealed partial class PostgresIngestionStore(string connection, Action<AcquisitionBoundary>? fault = null,
    Action<PersistenceBoundary>? interpretationFault = null) : IIngestionCommitStore
{
    public async Task RegisterAsync(ProcessingScope scope, SourceConfiguration configuration, string authority, CancellationToken ct = default)
    {
        configuration.Validate();
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);
        if(await db.Set<IngestionOwnerRow>().AnyAsync(o=>o.TeamId==scope.TeamId&&o.ApplicationId==scope.ApplicationId,ct))
        {
            var owner=await LockOwner(db,scope,ct);
            if(owner.Epoch>0)
            {
                var published=await db.Set<AcquisitionRevisionRow>().FindAsync([scope.TeamId,configuration.ProfileId,configuration.SourceKey,configuration.Revision],ct);
                if(published?.Configuration==SimulationJson.Serialize(configuration)){await tx.CommitAsync(ct);return;}
                throw new PersistenceConflict("Active monitoring configuration changes require a fenced, drained change.");
            }
        }
        await PostgresProcessingStore.Lock(db,scope,ct);
        if(await db.Set<RuntimeRow>().CountAsync(r=>r.TeamId==scope.TeamId&&r.SessionId==scope.SessionId,ct)!=1)throw new PersistenceConflict("Monitoring requires one persistent session per Application.");
        var lane=await db.Set<LaneRow>().FindAsync([scope.TeamId,scope.SessionId,configuration.ProfileId],ct);
        if(lane?.ApplicationId!=scope.ApplicationId)throw new PersistenceConflict("Source is outside the Application.");
        var monitoring=await db.Set<MonitoringSessionRow>().FindAsync([scope.TeamId,scope.ApplicationId],ct);
        if(monitoring is null)
        {
            db.Add(new MonitoringSessionRow{TeamId=scope.TeamId,ApplicationId=scope.ApplicationId,SessionId=scope.SessionId,HostAuthority=authority});await db.SaveChangesAsync(ct);
            db.Add(new IngestionOwnerRow{TeamId=scope.TeamId,ApplicationId=scope.ApplicationId,ExpiresAt=DateTimeOffset.UnixEpoch});await db.SaveChangesAsync(ct);
        }
        else if(monitoring.SessionId!=scope.SessionId||monitoring.HostAuthority!=authority)throw new PersistenceConflict("Monitoring session/host identity cannot be reset.");
        var source=await db.Set<SourceRow>().FindAsync([scope.TeamId,configuration.ProfileId,configuration.SourceKey],ct);
        if(source is null){db.Add(new SourceRow{TeamId=scope.TeamId,ProfileId=configuration.ProfileId,SourceKey=configuration.SourceKey});await db.SaveChangesAsync(ct);}
        var prior=await db.Set<AcquisitionRevisionRow>().FindAsync([scope.TeamId,configuration.ProfileId,configuration.SourceKey,configuration.Revision],ct);
        var json=SimulationJson.Serialize(configuration);
        if(prior is not null){if(prior.Configuration!=json)throw new PersistenceConflict("Acquisition revision immutable.");await tx.CommitAsync(ct);return;}
        if(await db.Set<AcquisitionSourceRow>().AnyAsync(s=>s.TeamId==scope.TeamId&&s.ProfileId==configuration.ProfileId&&s.SourceKey==configuration.SourceKey,ct))
            throw new PersistenceConflict("Reconfiguration requires the fenced, drained configuration path.");
        db.Add(new AcquisitionRevisionRow{TeamId=scope.TeamId,ProfileId=configuration.ProfileId,SourceKey=configuration.SourceKey,Revision=configuration.Revision,Configuration=json,Hash=SimulationJson.Hash(configuration)});
        await db.SaveChangesAsync(ct);
        db.Add(new AcquisitionSourceRow{TeamId=scope.TeamId,ProfileId=configuration.ProfileId,SourceKey=configuration.SourceKey,Revision=configuration.Revision,Enabled=configuration.Enabled,State=configuration.Enabled?"Discovering":"Disabled"});
        await db.SaveChangesAsync(ct);
        var members=await db.Set<AcquisitionSourceRow>().Where(s=>s.TeamId==scope.TeamId&&s.ProfileId==configuration.ProfileId).OrderBy(s=>s.SourceKey).Select(s=>new {s.SourceKey,s.Revision,s.Enabled}).ToArrayAsync(ct);
        var previous=await db.Set<SourceMembershipRow>().Where(s=>s.TeamId==scope.TeamId&&s.ProfileId==configuration.ProfileId).Select(s=>(long?)s.Revision).MaxAsync(ct)??0;
        if(previous>0&&await db.Set<RunRow>().AnyAsync(r=>r.TeamId==scope.TeamId&&r.SessionId==scope.SessionId&&r.ProfileId==configuration.ProfileId&&r.Result==null,ct))throw new PersistenceConflict("Source membership changes require drained runs.");
        db.Add(new SourceMembershipRow{TeamId=scope.TeamId,ProfileId=configuration.ProfileId,Revision=previous+1,Sources=SimulationJson.Serialize(members),EffectiveSequence=(await db.Set<RuntimeRow>().FindAsync([scope.TeamId,scope.SessionId,scope.ApplicationId],ct))!.NextSequence});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private static void ValidateLease(TimeSpan lease){if(lease<TimeSpan.FromSeconds(1)||lease>TimeSpan.FromMinutes(5))throw new ArgumentException("Lease must be 1 second to 5 minutes.");}
    internal static Task<DateTimeOffset> DatabaseNow(SondaDbContext db,CancellationToken ct)=>db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private static Task<IngestionOwnerRow> LockOwner(SondaDbContext db,ProcessingScope scope,CancellationToken ct)=>db.Set<IngestionOwnerRow>().FromSqlInterpolated($"SELECT * FROM sonda.ingestion_owners WHERE team_id={scope.TeamId} AND application_id={scope.ApplicationId} FOR UPDATE").SingleAsync(ct);
    public async Task<OwnerFence> ClaimAsync(ProcessingScope scope,Guid instanceId,TimeSpan lease,CancellationToken ct=default)
    {
        ValidateLease(lease);if(instanceId==Guid.Empty)throw new ArgumentException("Worker identity required.");
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var owner=await LockOwner(db,scope,ct);var now=await DatabaseNow(db,ct);
        var session=await db.Set<MonitoringSessionRow>().FindAsync([scope.TeamId,scope.ApplicationId],ct);
        if(session?.SessionId!=scope.SessionId)throw new PersistenceConflict("Wrong monitoring session.");
        if(owner.ExpiresAt>now&&owner.InstanceId!=instanceId)throw new PersistenceConflict("Application reader already owned.");
        if(owner.InstanceId!=instanceId||owner.ExpiresAt<=now)owner.Epoch=checked(owner.Epoch+1);
        owner.InstanceId=instanceId;owner.ExpiresAt=now+lease;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(scope,instanceId,owner.Epoch);
    }
    internal static async Task<IngestionOwnerRow> RequireFence(SondaDbContext db,OwnerFence fence,CancellationToken ct)
    {
        var owner=await LockOwner(db,fence.Scope,ct);
        if(owner.InstanceId!=fence.InstanceId||owner.Epoch!=fence.Epoch||owner.ExpiresAt<=await DatabaseNow(db,ct))throw new PersistenceConflict("Lost ingestion fence.");
        var session=await db.Set<MonitoringSessionRow>().FindAsync([fence.Scope.TeamId,fence.Scope.ApplicationId],ct);
        if(session?.SessionId!=fence.Scope.SessionId)throw new PersistenceConflict("Wrong monitoring session.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('sonda.fence_epoch',{fence.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)},true),set_config('sonda.fence_instance',{fence.InstanceId.ToString()},true)",ct);
        return owner;
    }
    public async Task RenewAsync(OwnerFence fence,TimeSpan lease,CancellationToken ct=default)
    {
        ValidateLease(lease);await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var owner=await RequireFence(db,fence,ct);owner.ExpiresAt=await DatabaseNow(db,ct)+lease;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task ReleaseAsync(OwnerFence fence,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var owner=await RequireFence(db,fence,ct);owner.ExpiresAt=DateTimeOffset.UnixEpoch;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private static Task<FileCheckpointRow> LockCheckpoint(SondaDbContext db,string team,Guid generation,CancellationToken ct)=>
        db.Set<FileCheckpointRow>().FromSqlInterpolated($"SELECT * FROM sonda.file_checkpoints WHERE team_id={team} AND generation_id={generation} FOR UPDATE").SingleAsync(ct);
    public async Task<GenerationCheckpoint> CheckpointAsync(ProcessingScope scope,Guid generation,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);var g=await db.Set<FileGenerationRow>().FindAsync([scope.TeamId,generation],ct)??throw new PersistenceConflict("Unknown generation.");
        var c=(await db.Set<FileCheckpointRow>().FindAsync([scope.TeamId,generation],ct))!;
        return new(generation,c.Offset,c.Revision,g.ProfileId,g.SourceKey,SimulationJson.Deserialize<SourceConfiguration>(g.Configuration));
    }
    public async Task<IngestionResult> CommitAsync(OwnerFence fence,Guid generation,PhysicalRecord record,DateTimeOffset processedAt,CancellationToken ct=default)
    {
        await using(var db=SondaDbContext.Open(connection))
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await RequireFence(db,fence,ct);var s=fence.Scope;var runtime=await PostgresProcessingStore.Lock(db,s,ct);
            var ownerGeneration=await db.Set<FileGenerationRow>().FindAsync([s.TeamId,generation],ct)??throw new PersistenceConflict("Unknown generation.");
            if(!await db.Set<LaneRow>().AnyAsync(l=>l.TeamId==s.TeamId&&l.SessionId==s.SessionId&&l.ApplicationId==s.ApplicationId&&l.ProfileId==ownerGeneration.ProfileId,ct))throw new PersistenceConflict("Generation belongs to a different Application.");
            var existing=await db.Set<FileRecordRow>().FindAsync([s.TeamId,generation,record.Start],ct);
            if(existing is not null)
            {
                if(existing.EndOffset!=record.End||!existing.Bytes.SequenceEqual(record.Bytes)||existing.Hash!=record.Hash)throw new PersistenceConflict("Physical identity reused with different bytes.");
                var checkpoint=await LockCheckpoint(db,s.TeamId,generation,ct);return new(existing.ReceiptId,checkpoint.Offset,true,"Replayed");
            }
            var g=await db.Set<FileGenerationRow>().FindAsync([s.TeamId,generation],ct)??throw new PersistenceConflict("Unknown generation.");
            var source=(await db.Set<AcquisitionSourceRow>().FindAsync([s.TeamId,g.ProfileId,g.SourceKey],ct))!;
            if(!source.Enabled||source.State=="Blocked"||g.Gap is not null)throw new PersistenceConflict("Source is disabled, blocked or has an unresolved gap.");
            var checkpointRow=await LockCheckpoint(db,s.TeamId,generation,ct);
            if(record.Start!=checkpointRow.Offset)throw new PersistenceConflict("Record must start at committed offset.");
            var configuration=SimulationJson.Deserialize<SourceConfiguration>(g.Configuration);
            var framed=new ByteLineFramer(configuration.Encoding,record.Start,configuration.MaximumRecordBytes).Append(record.Bytes);
            if(framed.Error is not null||framed.PartialBytes!=0||framed.Records.Length!=1||SimulationJson.Serialize(framed.Records[0])!=SimulationJson.Serialize(record))throw new PersistenceConflict("Candidate is not exactly one complete physical record.");
            var pending=await db.Set<PendingIngestionRow>().FindAsync([s.TeamId,s.ApplicationId],ct);
            if(pending is {Command.Length:>0})
            {
                if(pending.GenerationId!=generation||pending.StartOffset!=record.Start||pending.Bytes is null||!pending.Bytes.SequenceEqual(record.Bytes))throw new PersistenceConflict("An earlier command is pending.");
            }
            else
            {
                if(processedAt==default||processedAt.UtcTicks<runtime.LastProcessedTicks||runtime.NextSequence>=int.MaxValue)throw new PersistenceConflict("Processing clock or sequence capacity conflict.");
                var command=new PolicyCommand{Id=Guid.NewGuid(),Sequence=checked((int)runtime.NextSequence),Kind=PolicyCommandKind.Evidence,ProcessedAt=processedAt,Raw=record.Text,EvidenceKey=record.EvidenceKey(g.SourceKey,g.Id),SampleDate=configuration.SampleDate};
                pending??=new(){TeamId=s.TeamId,ApplicationId=s.ApplicationId};if(db.Entry(pending).State==EntityState.Detached)db.Add(pending);
                pending.CommandId=command.Id;pending.ProfileId=g.ProfileId;pending.GenerationId=g.Id;pending.StartOffset=record.Start;pending.EndOffset=record.End;pending.Bytes=record.Bytes.ToArray();pending.Record=SimulationJson.Serialize(record);pending.Command=SimulationJson.Serialize(command);pending.Fingerprint=SimulationJson.Hash(command);pending.Certificate=null;
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        fault?.Invoke(AcquisitionBoundary.AfterReservation);
        return await RecoverPendingAsync(fence,ct)??throw new PersistenceConflict("Pending command disappeared.");
    }
    public async Task<IngestionResult?> RecoverPendingAsync(OwnerFence fence,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);
        await RequireFence(db,fence,ct);var s=fence.Scope;await PostgresProcessingStore.Lock(db,s,ct);
        var pending=await db.Set<PendingIngestionRow>().FindAsync([s.TeamId,s.ApplicationId],ct);if(pending is null||pending.Command.Length==0)return null;
        var command=SimulationJson.Deserialize<PolicyCommand>(pending.Command);if(SimulationJson.Hash(command)!=pending.Fingerprint)throw new PersistenceConflict("Pending fingerprint mismatch.");
        FileGenerationRow? generation=null;FileCheckpointRow? checkpoint=null;PhysicalRecord? physical=null;
        if(pending.GenerationId is { } generationId)
        {
            generation=(await db.Set<FileGenerationRow>().FindAsync([s.TeamId,generationId],ct))!;checkpoint=await LockCheckpoint(db,s.TeamId,generationId,ct);
            physical=SimulationJson.Deserialize<PhysicalRecord>(pending.Record!);if(checkpoint.Offset!=physical.Start)throw new PersistenceConflict("Pending range no longer follows checkpoint.");
        }
        fault?.Invoke(AcquisitionBoundary.BeforeInterpretation);
        if(pending.Certificate is not null && SimulationJson.Deserialize<PendingControl>(pending.Certificate).Certificate is { } proof)
            await ValidateCoverage(db,fence,proof,ct);
        var lane=(await db.Set<LaneRow>().FindAsync([s.TeamId,s.SessionId,pending.ProfileId],ct))!;
        var version=(await db.Set<VersionRow>().FindAsync([s.TeamId,pending.ProfileId,lane.Version],ct))!;
        var profile=SimulationJson.Deserialize<Profile>(version.Snapshot);
        Guid receiptId;string disposition;ReceiptRow? receipt=null;
        if(profile.Policy is not null)
        {
            var result=await new PostgresPolicyStore(connection,interpretationFault).ExecuteInTransactionAsync(db,s,pending.ProfileId,command,generation is null?null:new(generation.SourceKey,generation.Id.ToString(),physical!.Start),r=>receipt=r,ct);
            receiptId=result.Id;disposition=result.Disposition;
        }
        else
        {
            if(generation is null||physical is null||command.Kind!=PolicyCommandKind.Evidence)throw new PersistenceConflict("LegacyV1 accepts evidence only.");
            var result=await new PostgresProcessingStore(connection,interpretationFault).ProcessInTransactionAsync(db,new(s,pending.ProfileId,command.Id,generation.SourceKey,generation.Id.ToString(),physical.Start,command.Sequence,command.Sequence,command.Raw,command.ProcessedAt,command.SampleDate),ct);
            receiptId=result.Id;disposition=result.Trace.Disposition;
        }
        receipt??=(await db.Set<ReceiptRow>().FindAsync([s.TeamId,s.SessionId,receiptId],ct))!;
        if(pending.Certificate is not null) await CompleteControl(db,fence,pending,command,receiptId,ct);
        if(generation is not null&&physical is not null&&checkpoint is not null)
        {
            db.Add(new FileRecordRow{TeamId=s.TeamId,GenerationId=generation.Id,StartOffset=physical.Start,EndOffset=physical.End,Bytes=physical.Bytes,Hash=physical.Hash,SessionId=s.SessionId,EvidenceId=receipt.EvidenceId!.Value,ReceiptId=receiptId,FenceEpoch=fence.Epoch,Record=SimulationJson.Serialize(physical)});
            await db.SaveChangesAsync(ct);
            checkpoint.Offset=physical.End;checkpoint.Revision++;checkpoint.ReceiptId=receiptId;checkpoint.FenceEpoch=fence.Epoch;
            if(disposition is "Rejected" or "Quarantined" or "BlockedByPriorError")
            {
                var source=(await db.Set<AcquisitionSourceRow>().FindAsync([s.TeamId,generation.ProfileId,generation.SourceKey],ct))!;source.State="Blocked";source.Error=disposition;
            }
            if(disposition=="LateEvidence"&&await db.Set<FrontierCertificateRow>().AnyAsync(c=>c.TeamId==s.TeamId&&c.ProfileId==generation.ProfileId&&c.Status=="Consumed",ct))
            {
                var source=(await db.Set<AcquisitionSourceRow>().FindAsync([s.TeamId,generation.ProfileId,generation.SourceKey],ct))!;source.State="Blocked";source.Error="ProducerContractViolation:LateEvidence";
            }
        }
        pending.Command="";pending.Record=null;pending.Bytes=null;pending.GenerationId=null;pending.StartOffset=null;pending.EndOffset=null;
        await db.SaveChangesAsync(ct);fault?.Invoke(AcquisitionBoundary.AfterCheckpoint);
        await tx.CommitAsync(ct);fault?.Invoke(AcquisitionBoundary.AfterCommit);
        await new PostgresProcessingStore(connection).ObserveCommit(receipt,ct);
        return new(receiptId,checkpoint?.Offset??0,false,disposition);
    }
}
