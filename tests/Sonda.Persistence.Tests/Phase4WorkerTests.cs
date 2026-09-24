using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact] public async Task Actual_worker_console_starts_stops_restarts_and_validates_without_source_access()
    {
        using var directory=new AcquisitionDirectory();var source=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true};await directory.Write("a.log","CAM process started\n");var(sample,scope)=await PolicySetup();var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,source,"test-host");
        var manifest=Phase4Artifact("synthetic-worker-manifest.json");await File.WriteAllTextAsync(manifest,SimulationJson.Serialize(new WorkerManifest(scope,"test-host",[source])));
        var first=await RunWorker("run",manifest);Assert.Equal(0,first.Exit);var generation=Assert.Single(await store.GenerationsAsync(scope,"cam","file"));Assert.Equal(new FileInfo(directory.PathFor("a.log")).Length,generation.Offset);
        await directory.Write("a.log","CAM process started\nCAM process completed\n");var second=await RunWorker("run",manifest);Assert.Equal(0,second.Exit);Assert.Equal(generation.Id,Assert.Single(await store.GenerationsAsync(scope,"cam","file")).Id);
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(scope,"cam");Assert.Equal(Sonda.Domain.Runs.DetectionResult.Success,Assert.Single(state.Interpreter.Runs).Result);Assert.Equal(2,state.Receipts.Count(r=>r.Command.Kind==Sonda.Application.Processing.PolicyCommandKind.Evidence));
        var noAccess=source with{Root=Path.Combine(directory.Root,"not-created"),Enabled=false};await File.WriteAllTextAsync(manifest,SimulationJson.Serialize(new WorkerManifest(scope,"test-host",[noAccess])));
        var validation=await RunWorker("validate-manifest",manifest);Assert.Equal(0,validation.Exit);Assert.Contains("\"readsPerformed\": 0",validation.Output);
        await using var db=SondaDbContext.Open(pg.Connection);var owner=(await db.Set<IngestionOwnerRow>().FindAsync(scope.TeamId,scope.ApplicationId))!;Assert.True(owner.ExpiresAt<DateTimeOffset.UtcNow);Assert.Equal(2,owner.Epoch);
        await File.WriteAllTextAsync(Phase4Artifact("worker-lifecycle.json"),SimulationJson.Serialize(new{consoleStartStopRestart=true,leaseReleased=true,epochs=owner.Epoch,checkpoint=(await store.CheckpointAsync(scope,generation.Id)).Offset,manifestValidationReads=0,installedService="Environmental blocker: non-elevated session; not tested",first=first.Output,second=second.Output}));
    }
    private async Task<(int Exit,string Output)> RunWorker(string mode,string manifest)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));var start=new ProcessStartInfo(Path.Combine(root,".tools/dotnet/dotnet.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{Path.Combine(root,"src/Sonda.Worker/bin/Release/net10.0/Sonda.Worker.dll"),mode,manifest,"--once"})start.ArgumentList.Add(arg);start.Environment["SONDA_DATABASE"]=pg.Connection;
        using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));await child.WaitForExitAsync(timeout.Token);var message=(await output)+(await error);Assert.True(child.ExitCode==0,message);return(child.ExitCode,message);
    }
}
