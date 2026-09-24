using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Infrastructure.Files;
public sealed record DeadlineDispatch(string ProfileId,bool Dispatched,string Reason);
/// <summary>Converts explicit producer proof into an existing AdvanceTime command. It contains no outcome logic.</summary>
public sealed class DeadlineScheduler(PostgresIngestionStore store,WindowsFileSource files)
{
    public async Task<DeadlineDispatch> TickAsync(OwnerFence fence,string profile,SourceConfiguration[] sources,DateTimeOffset at,CancellationToken ct=default)
    {
        if(sources.Length==0||sources.Any(s=>!s.Enabled||s.Completeness==CompletenessMode.None))return new(profile,false,"CompletenessUnproven");
        if(sources.Any(s=>s.ProducerManifestPath.Length==0)||sources.Select(s=>s.ProducerContract).Distinct(StringComparer.Ordinal).Count()!=1)return new(profile,false,"ProducerContractMissing");
        List<SourceCoverage> coverage=[];
        try
        {
            foreach(var source in sources)
            {
                await using var stream=files.Open(source.ProducerManifestPath,source.Root);
                if(stream.Length>1048576)throw new PersistenceConflict("Completeness manifest exceeds the bound.");
                var bytes=new byte[1048577];var count=0;int n;
                while((n=await stream.ReadAsync(bytes.AsMemory(count),ct))>0){count+=n;if(count>1048576)throw new PersistenceConflict("Completeness manifest changed beyond the bound.");}
                var manifest=SimulationJson.Deserialize<ProducerCoverageManifest>(new System.Text.UTF8Encoding(false,true).GetString(bytes,0,count));
                if(manifest.SourceKey!=source.SourceKey)throw new PersistenceConflict("Manifest source mismatch.");
                var hash=SimulationJson.Hash(manifest);await store.RecordManifestAsync(fence,profile,manifest,hash,ct);
                coverage.AddRange(manifest.Generations.Select(g=>g with {ManifestHash=hash,CompleteThrough=manifest.CompleteThrough}));
            }
            if((await store.ReadFrontierAsync(fence.Scope,profile,ct)) is { } frontier&&coverage.Min(c=>c.CompleteThrough)<=frontier)return new(profile,false,"FrontierAlreadyConsumed");
            var certificate=await store.CreateCertificateAsync(fence,profile,sources[0].ProducerContract,coverage.ToArray(),at,ct);
            var receipt=await store.DispatchAsync(fence,certificate,at,ct);return new(profile,receipt?.Disposition=="Applied",receipt?.Disposition??"Deferred");
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Text.DecoderFallbackException or PersistenceConflict)
        {return new(profile,false,e is PersistenceConflict?e.Message:"CompletenessProofUnavailable");}
    }
}
