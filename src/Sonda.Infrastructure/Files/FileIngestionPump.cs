using System.Text.RegularExpressions;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Infrastructure.Files;

public sealed record SourceVisit(string Source,ReaderState State,long ReadBytes,int CommittedRecords,long? BytesBehind,string Code);
/// <summary>One bounded acquisition visit. Scheduling does not interpret outcomes or infer deadlines.</summary>
public sealed class FileIngestionPump(PostgresIngestionStore store,WindowsFileSource files) : IAsyncDisposable
{
    private readonly Dictionary<Guid,(FileStream Stream,string Path)> handles=[];
    public async Task<SourceVisit> VisitAsync(OwnerFence fence,SourceConfiguration config,DateTimeOffset processedAt,CancellationToken ct=default)
    {
        if(!config.Enabled)return new(config.SourceKey,ReaderState.Disabled,0,0,null,"Disabled");
        long bytesRead=0;int committed=0;
        try
        {
            await store.RecoverPendingAsync(fence,ct);
            if(await store.IsSourceBlockedAsync(fence.Scope,config.ProfileId,config.SourceKey,ct))return new(config.SourceKey,ReaderState.Blocked,0,0,null,"PersistedSourceBlock");
            VerifiedRotation[] rotations=[];
            if(config.Rotation==RotationMode.CopyTruncate)
            {
                if(config.RotationContract.Length==0||config.RotationManifestPath.Length==0)throw new PersistenceConflict("CopyTruncateRequiresVerifiedLineage");
                if(File.Exists(config.RotationManifestPath))await new CopyTruncateReconciler(store,files).ReconcileAsync(fence,config,processedAt,ct);
                rotations=await store.RotationProofsAsync(fence.Scope,config.ProfileId,config.SourceKey,ct);
            }
            var known=await store.GenerationsAsync(fence.Scope,config.ProfileId,config.SourceKey,ct);
            var candidates=files.Enumerate(config).Concat(known.SelectMany(g=>g.Paths).Where(File.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).Take(config.MaximumFiles+1).ToArray();
            if(candidates.Length>config.MaximumFiles)throw new PersistenceConflict("File inventory exceeds the configured bound.");
            if(config.Rotation==RotationMode.OrderedFiles)
            {
                if(string.IsNullOrWhiteSpace(config.FileOrderExpression))throw new PersistenceConflict("Ordered files require a producer filename sequence contract.");
                var regex=new Regex(config.FileOrderExpression,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
                var ordered=candidates.Select(p=>(Path:p,Key:regex.Match(Path.GetFileName(p)).Groups["sequence"].Value)).ToArray();
                if(ordered.Any(x=>x.Key.Length==0)||ordered.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=ordered.Length)throw new PersistenceConflict("Ambiguous file series sequence.");
                candidates=ordered.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Path).ToArray();
            }
            else if(candidates.Length>1&&known.Length==0)
                throw new PersistenceConflict("Multiple initial files require an explicit ordering contract.");
            List<(GenerationCheckpoint Checkpoint,FileObservation Observation)> observed=[];
            foreach(var path in candidates)
            {
                ct.ThrowIfCancellationRequested();var stream=files.Open(path,config.Root);
                try
                {
                    var file=files.Inspect(stream,path,processedAt);var prior=known.Where(g=>g.Identity==file.Identity).MaxBy(g=>g.Epoch);
                    var continuity=prior is {PrefixLength:>0}&&file.Length>=prior.PrefixLength?WindowsFileSource.PrefixHash(stream,prior.PrefixLength):null;
                    var rotation=rotations.FirstOrDefault(r=>Path.GetFullPath(r.Rotation.ArchivePath).Equals(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));
                    GenerationCheckpoint checkpoint;
                    if(rotation is not null)
                    {
                        if(file.Identity!=rotation.Rotation.ArchiveIdentity||file.Length!=rotation.Rotation.ArchiveLength)throw new PersistenceConflict("Verified archive was modified.");
                        checkpoint=await store.CheckpointAsync(fence.Scope,rotation.Rotation.OldGeneration,ct);
                        if(handles.Remove(checkpoint.GenerationId,out var replaced))await replaced.Stream.DisposeAsync();
                    }
                    else
                    {
                        if(config.Rotation==RotationMode.CopyTruncate&&rotations.Length>0&&!rotations.Any(r=>Path.GetFullPath(r.Rotation.CurrentPath).Equals(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)))throw new PersistenceConflict("Unknown copy/truncate file has no lineage.");
                        checkpoint=await store.ObserveFileAsync(fence,config,file,(int)Math.Min(file.Length,64),continuity,ct);
                    }
                    if(handles.TryGetValue(checkpoint.GenerationId,out var open)){await stream.DisposeAsync();handles[checkpoint.GenerationId]=(open.Stream,path);}
                    else handles.Add(checkpoint.GenerationId,(stream,path));
                    if(!observed.Any(o=>o.Checkpoint.GenerationId==checkpoint.GenerationId))observed.Add((checkpoint,file));
                }
                catch{await stream.DisposeAsync();throw;}
            }
            foreach(var old in known.Where(g=>!observed.Any(o=>o.Checkpoint.GenerationId==g.Id)&&g.State is not ("Truncated" or "Archived")))
            {
                if(handles.TryGetValue(old.Id,out var open))observed.Insert(0,(await store.CheckpointAsync(fence.Scope,old.Id,ct),files.Inspect(open.Stream,open.Path,processedAt)));
                else if(old.Gap is not null||old.ObservedLength>old.Offset)
                {
                    await store.MarkGapAsync(fence,old.Id,"MissingUnreadGeneration",ct);return await Finish(ReaderState.Gap,"MissingUnreadGeneration",null,false);
                }
            }
            if(observed.Count==0)return await Finish(config.Required?ReaderState.Unavailable:ReaderState.Following,"NoMatchingFile",null,config.Required?false:null);
            // Retained generations precede replacements; new independent series uses the validated name ordering above.
            var rank=known.Select((g,n)=>(g.Id,n)).ToDictionary(x=>x.Id,x=>x.n);
            if(config.Rotation!=RotationMode.OrderedFiles)observed=observed.OrderBy(o=>rank.GetValueOrDefault(o.Checkpoint.GenerationId,int.MaxValue)).ToList();
            if(config.Rotation==RotationMode.Rename&&known.Length>0&&observed.Count(o=>!rank.ContainsKey(o.Checkpoint.GenerationId))>1)
            {
                foreach(var ambiguous in observed.Where(o=>!rank.ContainsKey(o.Checkpoint.GenerationId)))await store.MarkGapAsync(fence,ambiguous.Checkpoint.GenerationId,"MultipleUnorderedSuccessors",ct);
                return await Finish(ReaderState.Gap,"MultipleUnorderedSuccessors",null,false);
            }
            for(var index=0;index<observed.Count;index++)
            {
                var item=observed[index];
                if(item.Observation.Length>item.Checkpoint.Offset&&observed.Skip(index+1).Any(later=>later.Checkpoint.Offset>0))
                {
                    await store.MarkGapAsync(fence,item.Checkpoint.GenerationId,"EarlierBytesAfterCommittedSuccessor",ct);
                    return await Finish(ReaderState.Gap,"EarlierBytesAfterCommittedSuccessor",null,false);
                }
            }
            long behind=0;bool partial=false;
            foreach(var item in observed)
            {
                if(bytesRead>=config.VisitBytes||committed>=config.VisitRecords){behind+=Math.Max(0,item.Observation.Length-item.Checkpoint.Offset);continue;}
                var stream=handles[item.Checkpoint.GenerationId].Stream;var offset=item.Checkpoint.Offset;var readAt=offset;
                var framer=new ByteLineFramer(item.Checkpoint.Configuration.Encoding,offset,item.Checkpoint.Configuration.MaximumRecordBytes);
                var buffer=new byte[Math.Min(config.ReadBytes,config.VisitBytes)];
                while(readAt<item.Observation.Length&&bytesRead<config.VisitBytes&&committed<config.VisitRecords)
                {
                    var count=(int)Math.Min(buffer.Length,Math.Min(item.Observation.Length-readAt,config.VisitBytes-bytesRead));
                    var n=await RandomAccess.ReadAsync(stream.SafeFileHandle,buffer.AsMemory(0,count),readAt,ct);
                    if(n==0){await store.MarkGapAsync(fence,item.Checkpoint.GenerationId,"ChangedDuringRead",ct);return await Finish(ReaderState.Gap,"ChangedDuringRead",null,false);}
                    readAt+=n;bytesRead+=n;var batch=framer.Append(buffer.AsSpan(0,n));
                    var after=files.Inspect(stream,handles[item.Checkpoint.GenerationId].Path,processedAt);
                    if(after.Identity!=item.Observation.Identity||after.Length<item.Observation.Length)throw new IOException("Generation changed during read.");
                    foreach(var record in batch.Records)
                    {
                        if(committed==config.VisitRecords)break;
                        var result=await store.CommitAsync(fence,item.Checkpoint.GenerationId,record,processedAt,ct);offset=result.CommittedOffset;committed++;
                        if(result.Disposition is "Rejected" or "Quarantined" or "BlockedByPriorError"||await store.IsSourceBlockedAsync(fence.Scope,config.ProfileId,config.SourceKey,ct))return await Finish(ReaderState.Blocked,result.Disposition,null,null);
                    }
                    if(batch.Error is { } error)return await Finish(ReaderState.Blocked,error.Code,null,false);
                }
                behind+=Math.Max(0,item.Observation.Length-offset);partial|=framer.PartialBytes>0&&readAt==item.Observation.Length;
                if(partial){behind+=observed.SkipWhile(o=>o.Checkpoint.GenerationId!=item.Checkpoint.GenerationId).Skip(1).Sum(o=>Math.Max(0,o.Observation.Length-o.Checkpoint.Offset));break;} // No successor may outrun an incomplete predecessor.
            }
            return await Finish(partial?ReaderState.WaitingForPartial:behind>0?ReaderState.CatchingUp:ReaderState.Following,
                partial?"PartialRecord":config.Completeness==CompletenessMode.None?"DeadlineDeferred:CompletenessUnproven":"ReadComplete",behind,true);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PersistenceConflict or RegexMatchTimeoutException)
        {
            var state=e is PersistenceConflict?ReaderState.IdentityUncertain:ReaderState.Unavailable;
            return await Finish(state,e is UnauthorizedAccessException?"AccessDenied":e is PersistenceConflict?e.Message:"ReadFailed",null,false);
        }
        async Task<SourceVisit> Finish(ReaderState state,string code,long? backlog,bool? success)
        {
            await store.ObserveSourceAsync(fence,config.ProfileId,new(config.SourceKey,state,code,processedAt,processedAt,ReadSuccess:success),ct);
            return new(config.SourceKey,state,bytesRead,committed,backlog,code);
        }
    }
    public async ValueTask DisposeAsync(){foreach(var value in handles.Values)await value.Stream.DisposeAsync();handles.Clear();}
}
