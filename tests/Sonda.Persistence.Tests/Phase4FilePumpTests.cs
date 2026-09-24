using System.Text;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Runs;
using Sonda.Infrastructure.Files;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Theory][InlineData(FileEncoding.Utf8)][InlineData(FileEncoding.Utf16LittleEndian)][InlineData(FileEncoding.Utf16BigEndian)]
    public async Task Real_byte_ingestion_restart_partial_append_and_simulator_parity(FileEncoding kind)
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-pump-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var(sample,scope)=await PolicySetup();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=root,Enabled=true,Encoding=kind,ReadBytes=7};
            var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,config,"test-host");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));
            Encoding encoding=kind switch{FileEncoding.Utf8=>new UTF8Encoding(true,true),FileEncoding.Utf16LittleEndian=>new UnicodeEncoding(false,true,true),_=>new UnicodeEncoding(true,true,true)};
            var path=Path.Combine(root,"a.log");var text="CAM process started\r\nFinding order OrderID=x\nUnable to send order OrderID=x\nFinding order OrderID=x\nOrder sent OrderID=x\nCAM process completed";
            var bytes=encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();await File.WriteAllBytesAsync(path,bytes);
            await using(var pump=new FileIngestionPump(store,new WindowsFileSource()))
            {
                var visit=await pump.VisitAsync(fence,config,sample.AsOf);Assert.Equal(ReaderState.WaitingForPartial,visit.State);Assert.Equal(5,visit.CommittedRecords);
            }
            var generation=Assert.Single(await store.GenerationsAsync(scope,"cam","file"));Assert.True(generation.Offset<bytes.Length);
            await using(var append=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))await append.WriteAsync(encoding.GetBytes("\n"));
            await using(var restarted=new FileIngestionPump(store,new WindowsFileSource()))
            {
                var visit=await restarted.VisitAsync(fence,config,sample.AsOf.AddSeconds(1));Assert.Equal(ReaderState.Following,visit.State);Assert.Equal(1,visit.CommittedRecords);
                Assert.Equal(0,(await restarted.VisitAsync(fence,config,sample.AsOf.AddSeconds(2))).ReadBytes);
            }
            var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,"cam");var memory=new PolicySession(sample.Profiles[0],sample.Seed);
            foreach(var receipt in state.Receipts)memory.Execute(receipt.Command);
            Assert.Equal(SimulationJson.Serialize(memory.Export()),SimulationJson.Serialize(state));
            Assert.Equal(new[]{DetectionResult.Success,DetectionResult.Failure,DetectionResult.Success},state.Interpreter.Runs.Select(r=>r.Result!.Value));
            await using var db=SondaDbContext.Open(pg.Connection);var records=await db.Set<FileRecordRow>().Where(r=>r.GenerationId==generation.Id).OrderBy(r=>r.StartOffset).ToArrayAsync();
            Assert.Equal(await File.ReadAllBytesAsync(path),records.SelectMany(r=>r.Bytes).ToArray());
            Assert.Equal(new FileInfo(path).Length,(await store.CheckpointAsync(scope,generation.Id)).Offset);
            var artifact=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/phase4/byte-parity"));Directory.CreateDirectory(artifact);
            await File.WriteAllTextAsync(Path.Combine(artifact,kind+".json"),SimulationJson.Serialize(new{encoding=kind,parity=true,records=records.Select(r=>new{r.StartOffset,r.EndOffset,r.Hash,r.ReceiptId}),state}));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public async Task Reader_failure_and_recovery_do_not_fabricate_business_failure()
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-missing-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var(sample,scope)=await PolicySetup();var config=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=root,Enabled=true};
            var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,config,"test-host");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(1));
            await using var pump=new FileIngestionPump(store,new WindowsFileSource());Assert.Equal(ReaderState.Unavailable,(await pump.VisitAsync(fence,config,sample.AsOf)).State);
            await File.WriteAllTextAsync(Path.Combine(root,"a.log"),"trace\n",new UTF8Encoding(false));Assert.Equal(ReaderState.Following,(await pump.VisitAsync(fence,config,sample.AsOf.AddSeconds(1))).State);
            var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,"cam");Assert.Empty(state.Interpreter.Runs);Assert.Empty(state.Interpreter.Incidents);Assert.Equal(new[]{false,true},state.Observations.Select(o=>o.Success));
        }
        finally{Directory.Delete(root,true);}
    }
}
