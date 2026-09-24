using System.Security.Cryptography;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
namespace Sonda.Infrastructure.Files;

public sealed class CopyTruncateReconciler(PostgresIngestionStore store,WindowsFileSource files)
{
    public async Task<VerifiedRotation> ReconcileAsync(OwnerFence fence,SourceConfiguration source,DateTimeOffset at,CancellationToken ct)
    {
        if(source.RotationContract.Length==0||source.RotationManifestPath.Length==0)throw new PersistenceConflict("CopyTruncateRequiresVerifiedLineage");
        await using var input=files.Open(source.RotationManifestPath,source.Root);
        if(input.Length>65536)throw new PersistenceConflict("Rotation manifest exceeds bound.");
        var data=new byte[65537];var count=0;int read;
        while((read=await input.ReadAsync(data.AsMemory(count),ct))>0){count+=read;if(count>65536)throw new PersistenceConflict("Rotation manifest exceeds bound.");}
        var manifest=SimulationJson.Deserialize<RotationManifest>(new System.Text.UTF8Encoding(false,true).GetString(data,0,count));
        if(manifest.Contract!=source.RotationContract||manifest.SourceKey!=source.SourceKey||!manifest.ArchiveImmutable||!manifest.CoversEntireOldGeneration)
            throw new PersistenceConflict("Rotation manifest does not prove archive lineage.");
        var existing=(await store.RotationProofsAsync(fence.Scope,source.ProfileId,source.SourceKey,ct)).FirstOrDefault(p=>p.Rotation==manifest);
        if(existing is not null)return existing;
        var checkpoint=await store.CheckpointAsync(fence.Scope,manifest.OldGeneration,ct);
        if(checkpoint.ProfileId!=source.ProfileId||checkpoint.SourceKey!=source.SourceKey||manifest.ArchiveLength<checkpoint.Offset)throw new PersistenceConflict("Archive does not cover source checkpoint.");
        await using var archive=files.Open(manifest.ArchivePath,source.Root);var observation=files.Inspect(archive,manifest.ArchivePath,at);
        if(observation.Identity!=manifest.ArchiveIdentity||observation.Length!=manifest.ArchiveLength)throw new PersistenceConflict("Archive identity or sealed length differs from manifest.");
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await foreach(var record in store.ReadPhysicalRecordsAsync(fence.Scope,manifest.OldGeneration,ct))
        {
            var bytes=new byte[record.Bytes.Length];var position=0;
            while(position<bytes.Length){var n=await RandomAccess.ReadAsync(archive.SafeFileHandle,bytes.AsMemory(position),record.Start+position,ct);if(n==0)throw new PersistenceConflict("Archive prefix is incomplete.");position+=n;}
            if(!bytes.SequenceEqual(record.Bytes))throw new PersistenceConflict("Archive prefix differs from committed evidence.");digest.AppendData(bytes);
        }
        var final=files.Inspect(archive,manifest.ArchivePath,at);if(final.Identity!=observation.Identity||final.Length!=observation.Length)throw new PersistenceConflict("Archive changed during verification.");
        var proof=new VerifiedRotation("Rotation",manifest,checkpoint.Offset,checkpoint.Revision,Convert.ToHexString(digest.GetHashAndReset()));
        await store.AcceptRotationAsync(fence,source,proof,ct);return proof;
    }
}
