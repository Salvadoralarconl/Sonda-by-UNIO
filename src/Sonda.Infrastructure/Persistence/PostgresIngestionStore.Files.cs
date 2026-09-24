using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;

namespace Sonda.Infrastructure.Persistence;
public sealed partial class PostgresIngestionStore
{
    public async Task<bool> IsSourceBlockedAsync(ProcessingScope scope,string profile,string source,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);
        return await db.Set<AcquisitionSourceRow>().AnyAsync(s=>s.TeamId==scope.TeamId&&s.ProfileId==profile&&s.SourceKey==source&&s.State=="Blocked",ct);
    }
    public async Task MarkGapAsync(OwnerFence fence,Guid generation,string code,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);await RequireFence(db,fence,ct);await PostgresProcessingStore.Lock(db,fence.Scope,ct);
        var row=await db.Set<FileGenerationRow>().FindAsync([fence.Scope.TeamId,generation],ct)??throw new PersistenceConflict("Unknown generation.");row.Gap=code;row.State="Gap";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<SourceConfiguration[]> SourcesAsync(ProcessingScope scope,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);
        var profiles=await db.Set<LaneRow>().Where(p=>p.TeamId==scope.TeamId&&p.SessionId==scope.SessionId&&p.ApplicationId==scope.ApplicationId).Select(p=>p.ProfileId).ToArrayAsync(ct);
        var rows=await db.Set<AcquisitionSourceRow>().Where(s=>s.TeamId==scope.TeamId&&profiles.Contains(s.ProfileId)).OrderBy(s=>s.ProfileId).ThenBy(s=>s.SourceKey).ToArrayAsync(ct);
        List<SourceConfiguration> sources=[];
        foreach(var source in rows){var revision=(await db.Set<AcquisitionRevisionRow>().FindAsync([scope.TeamId,source.ProfileId,source.SourceKey,source.Revision],ct))!;sources.Add(SimulationJson.Deserialize<SourceConfiguration>(revision.Configuration) with {Enabled=source.Enabled});}
        return sources.ToArray();
    }
    public async Task<GenerationInfo[]> GenerationsAsync(ProcessingScope scope,string profile,string source,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);
        var unordered=await db.Set<FileGenerationRow>().Where(g=>g.TeamId==scope.TeamId&&g.ProfileId==profile&&g.SourceKey==source).Take(10001).ToArrayAsync(ct);
        if(unordered.Length>10000)throw new PersistenceConflict("Generation inventory capacity reached; archive review required, no checkpoint reset.");
        List<FileGenerationRow> rows=[];
        while(rows.Count<unordered.Length)
        {
            var ready=unordered.Where(g=>rows.All(r=>r.Id!=g.Id)&&(g.Predecessor is null||rows.Any(r=>r.Id==g.Predecessor))).OrderBy(g=>g.Id).ToArray();
            if(ready.Length==0)throw new PersistenceConflict("Invalid generation predecessor chain.");
            rows.AddRange(ready);
        }
        List<GenerationInfo> result=[];
        foreach(var g in rows)
        {
            var obj=(await db.Set<FileObjectRow>().FindAsync([scope.TeamId,g.ObjectId],ct))!;
            var c=(await db.Set<FileCheckpointRow>().FindAsync([scope.TeamId,g.Id],ct))!;
            var paths=await db.Set<FilePathRow>().Where(p=>p.TeamId==scope.TeamId&&p.ObjectId==g.ObjectId).Select(p=>p.Path).Distinct().Take(257).ToArrayAsync(ct);
            if(paths.Length>256)throw new PersistenceConflict("Physical file alias capacity reached.");
            result.Add(new(g.Id,SimulationJson.Deserialize<PhysicalFileIdentity>(obj.Identity),g.Epoch,c.Offset,g.ObservedLength,g.PrefixLength,g.PrefixHash,g.State,g.Gap,paths,SimulationJson.Deserialize<SourceConfiguration>(g.Configuration)));
        }
        return result.ToArray();
    }
    public async Task<GenerationCheckpoint> ObserveFileAsync(OwnerFence fence,SourceConfiguration config,FileObservation observation,
        int prefixLength,string? priorPrefixHash,CancellationToken ct=default)
    {
        if(observation.Length<0||prefixLength<0||prefixLength>observation.Length)throw new ArgumentException("Invalid observed byte length.");
        var s=fence.Scope;Guid generationId;
        await using(var db=SondaDbContext.Open(connection))
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await RequireFence(db,fence,ct);await PostgresProcessingStore.Lock(db,s,ct);
            var revision=await db.Set<AcquisitionRevisionRow>().FindAsync([s.TeamId,config.ProfileId,config.SourceKey,config.Revision],ct);
            if(revision is null||revision.Configuration!=SimulationJson.Serialize(config with {Enabled=SimulationJson.Deserialize<SourceConfiguration>(revision.Configuration).Enabled}))throw new PersistenceConflict("Exact registered source revision required.");
            var key=SimulationJson.Hash(observation.Identity);
            var obj=await db.Set<FileObjectRow>().SingleOrDefaultAsync(o=>o.TeamId==s.TeamId&&o.IdentityKey==key,ct);
            if(obj is not null&&(obj.ProfileId!=config.ProfileId||obj.SourceKey!=config.SourceKey))throw new PersistenceConflict("Physical file already belongs to another source.");
            if(obj is null)
            {
                obj=new(){TeamId=s.TeamId,Id=Guid.NewGuid(),ProfileId=config.ProfileId,SourceKey=config.SourceKey,IdentityKey=key,Identity=SimulationJson.Serialize(observation.Identity)};db.Add(obj);await db.SaveChangesAsync(ct);
            }
            var g=await db.Set<FileGenerationRow>().Where(g=>g.TeamId==s.TeamId&&g.ObjectId==obj.Id).OrderByDescending(g=>g.Epoch).FirstOrDefaultAsync(ct);
            var checkpoint=g is null?null:await LockCheckpoint(db,s.TeamId,g.Id,ct);
            var changed=g is not null&&(g.State=="Archived"||observation.Length<g.ObservedLength||observation.Length<checkpoint!.Offset||g.PrefixLength>0&&priorPrefixHash!=g.PrefixHash);
            if(g is not null&&g.Gap is not null)throw new PersistenceConflict("Generation has unresolved continuity gap.");
            if(changed)
            {
                if(g!.State!="Archived"&&(g.ObservedLength>checkpoint!.Offset||observation.Length>=g.ObservedLength))
                {
                    g.Gap="UnprovenMutationOrUnreadTruncation";g.State="Gap";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                    throw new PersistenceConflict("Continuity lost; unread or overwritten bytes require verified archive lineage.");
                }
                if(g.State!="Archived")g.State="Truncated";await db.SaveChangesAsync(ct);
            }
            if(g is null||changed)
            {
                var previous=g;
                var chain=await db.Set<FileGenerationRow>().Where(x=>x.TeamId==s.TeamId&&x.ProfileId==config.ProfileId&&x.SourceKey==config.SourceKey).ToArrayAsync(ct);
                var tail=chain.SingleOrDefault(x=>!chain.Any(y=>y.Predecessor==x.Id));
                g=new(){TeamId=s.TeamId,Id=Guid.NewGuid(),ObjectId=obj.Id,Epoch=(previous?.Epoch??0)+1,ProfileId=config.ProfileId,SourceKey=config.SourceKey,SourceRevision=config.Revision,Predecessor=tail?.Id,Configuration=revision.Configuration,ObservedLength=observation.Length,PrefixLength=prefixLength,PrefixHash=observation.PrefixHash};
                db.Add(g);await db.SaveChangesAsync(ct);db.Add(new FileCheckpointRow{TeamId=s.TeamId,GenerationId=g.Id,FenceEpoch=fence.Epoch});await db.SaveChangesAsync(ct);
            }
            else {g.ObservedLength=observation.Length;if(g.PrefixLength==0&&prefixLength>0){g.PrefixLength=prefixLength;g.PrefixHash=observation.PrefixHash;}await db.SaveChangesAsync(ct);}
            if(!await db.Set<FilePathRow>().AnyAsync(p=>p.TeamId==s.TeamId&&p.ObjectId==obj.Id&&p.Path==observation.Path,ct))db.Add(new FilePathRow{TeamId=s.TeamId,Id=Guid.NewGuid(),ObjectId=obj.Id,Path=observation.Path,ObservedAt=observation.ObservedAt.ToUniversalTime()});
            generationId=g.Id;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        return await CheckpointAsync(s,generationId,ct);
    }
    public async Task SetEnabledAsync(OwnerFence fence,string profile,string source,bool enabled,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);await using var tx=await db.Database.BeginTransactionAsync(ct);await RequireFence(db,fence,ct);var runtime=await PostgresProcessingStore.Lock(db,fence.Scope,ct);
        if(await db.Set<PendingIngestionRow>().AnyAsync(p=>p.TeamId==fence.Scope.TeamId&&p.ApplicationId==fence.Scope.ApplicationId&&p.Command!="",ct))throw new PersistenceConflict("Finish pending input before source state change.");
        var row=await db.Set<AcquisitionSourceRow>().FindAsync([fence.Scope.TeamId,profile,source],ct)??throw new PersistenceConflict("Unknown source.");
        if(row.State=="Blocked")throw new PersistenceConflict("Disable/enable cannot clear a quarantined source.");
        row.Enabled=enabled;row.State=enabled?"Discovering":"Disabled";await db.SaveChangesAsync(ct);
        var prior=await db.Set<SourceMembershipRow>().Where(m=>m.TeamId==fence.Scope.TeamId&&m.ProfileId==profile).MaxAsync(m=>m.Revision,ct);
        var members=await db.Set<AcquisitionSourceRow>().Where(s=>s.TeamId==fence.Scope.TeamId&&s.ProfileId==profile).OrderBy(s=>s.SourceKey).Select(s=>new{s.SourceKey,s.Revision,s.Enabled}).ToArrayAsync(ct);
        db.Add(new SourceMembershipRow{TeamId=fence.Scope.TeamId,ProfileId=profile,Revision=prior+1,EffectiveSequence=runtime.NextSequence,Sources=SimulationJson.Serialize(members)});
        foreach(var certificate in await db.Set<FrontierCertificateRow>().Where(c=>c.TeamId==fence.Scope.TeamId&&c.ProfileId==profile&&c.Status=="Validated").ToArrayAsync(ct))certificate.Status="Invalidated";
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}

