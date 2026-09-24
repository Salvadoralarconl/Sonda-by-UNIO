using System.Text;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Files;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Real_rotation_rename_or_truncate_creates_no_duplicate_evidence(bool truncate)
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-rotate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var(sample,scope)=await PolicySetup();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=root,Enabled=true};var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,config,"host");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
            var path=Path.Combine(root,"current.log");await File.WriteAllTextAsync(path,"trace-one-long\n",new UTF8Encoding(false));
            await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(1,(await pump.VisitAsync(fence,config,sample.AsOf)).CommittedRecords);
            var old=Assert.Single(await store.GenerationsAsync(scope,"cam","file"));
            if(truncate)await File.WriteAllTextAsync(path,"x\n",new UTF8Encoding(false));
            else{File.Move(path,Path.Combine(root,"old.log"));await File.WriteAllTextAsync(path,"x\n",new UTF8Encoding(false));}
            var visit=await pump.VisitAsync(fence,config,sample.AsOf.AddSeconds(1));Assert.Equal(ReaderState.Following,visit.State);Assert.Equal(1,visit.CommittedRecords);
            var generations=await store.GenerationsAsync(scope,"cam","file");Assert.Equal(2,generations.Length);Assert.Equal(old.Offset,generations.Single(g=>g.Id==old.Id).Offset);
            Assert.Equal(0,(await pump.VisitAsync(fence,config,sample.AsOf.AddSeconds(2))).CommittedRecords);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public async Task Verified_copy_truncate_recovers_unread_archive_without_recounting_prefix()
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-copy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var(sample,scope)=await PolicySetup();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=root,Enabled=true,Rotation=RotationMode.CopyTruncate,RotationContract="test-rotation",RotationManifestPath=Path.Combine(root,"rotation.json"),VisitRecords=1};
            var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,config,"host");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
            var path=Path.Combine(root,"current.log");await File.WriteAllTextAsync(path,"trace-one\ntrace-two\n",new UTF8Encoding(false));
            await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(1,(await pump.VisitAsync(fence,config,sample.AsOf)).CommittedRecords);
            var old=Assert.Single(await store.GenerationsAsync(scope,"cam","file"));Assert.True(old.Offset<old.ObservedLength);
            var archive=Path.Combine(root,"archive.log");File.Copy(path,archive);await File.WriteAllTextAsync(path,"new\n",new UTF8Encoding(false));
            var files=new WindowsFileSource();FileObservation observation;using(var handle=files.Open(archive,root))observation=files.Inspect(handle,archive,sample.AsOf);
            var manifest=new RotationManifest("test-rotation","file",old.Id,path,archive,observation.Length,observation.Identity,true,true);await File.WriteAllTextAsync(config.RotationManifestPath,SimulationJson.Serialize(manifest));
            var visit=await pump.VisitAsync(fence,config,sample.AsOf.AddSeconds(1));Assert.Equal(1,visit.CommittedRecords);Assert.Equal(old.ObservedLength,(await store.CheckpointAsync(scope,old.Id)).Offset);
            Assert.Equal(1,(await pump.VisitAsync(fence,config,sample.AsOf.AddSeconds(2))).CommittedRecords);
            Assert.Equal(0,(await pump.VisitAsync(fence,config,sample.AsOf.AddSeconds(3))).CommittedRecords);
            var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,"cam");Assert.Equal(3,state.Receipts.Count(r=>r.Command.Kind==Sonda.Application.Processing.PolicyCommandKind.Evidence));
        }
        finally{Directory.Delete(root,true);}
    }
}
