using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sonda.Application.Acquisition;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Files;
using Sonda.Infrastructure.Persistence;

if(args.Length<2||args[0] is not ("validate-manifest" or "register" or "run" or "diagnose"))throw new ArgumentException("validate-manifest|register|run|diagnose <explicit-manifest.json> [--once]");
var manifest=SimulationJson.Deserialize<WorkerManifest>(await File.ReadAllTextAsync(Path.GetFullPath(args[1])));
var errors=manifest.Validate();if(errors.Length>0){Console.Error.WriteLine(string.Join(Environment.NewLine,errors));return 2;}
if(args[0]=="validate-manifest")
{
    Console.WriteLine(SimulationJson.Serialize(new{valid=true,sourceCount=manifest.Sources.Length,readsPerformed=0,realPilotAuthorization="Required separately",checksStillRequired=new[]{"encoding/timezone/date context","producer rotation retention","manual engine comparison","SMB provider identity","completeness contract or deadline deferral"}}));return 0;
}
var connection=Environment.GetEnvironmentVariable("SONDA_DATABASE")??throw new ArgumentException("SONDA_DATABASE is required; credentials must not be in the manifest.");
var store=new PostgresIngestionStore(connection);
if(args[0]=="diagnose"){foreach(var profile in manifest.Sources.Select(s=>s.ProfileId).Distinct())Console.WriteLine(SimulationJson.Serialize(await store.DiagnoseAsync(manifest.Scope,profile,DateTimeOffset.UtcNow)));return 0;}
if(args[0]=="register"){foreach(var source in manifest.Sources)await store.RegisterAsync(manifest.Scope,source,manifest.HostAuthority);Console.WriteLine("Registered explicit acquisition revisions; no files read.");return 0;}
var builder=Host.CreateApplicationBuilder();builder.Services.AddWindowsService(o=>o.ServiceName="SONDA Acquisition");
builder.Services.Configure<HostOptions>(o=>o.ShutdownTimeout=TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(manifest);builder.Services.AddSingleton(store);builder.Services.AddSingleton(new WorkerOptions(args.Contains("--once")));builder.Services.AddHostedService<AcquisitionWorker>();
await builder.Build().RunAsync();return Environment.ExitCode;

public sealed record WorkerOptions(bool Once);
public sealed class AcquisitionWorker(WorkerManifest manifest,PostgresIngestionStore store,WorkerOptions options,IHostApplicationLifetime lifetime,ILogger<AcquisitionWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OwnerFence? fence=null;await using var pump=new FileIngestionPump(store,new WindowsFileSource());var scheduler=new DeadlineScheduler(store,new WindowsFileSource());
        var retries=new Dictionary<string,(int Count,DateTimeOffset Next)>();
        try
        {
            fence=await store.ClaimAsync(manifest.Scope,Guid.NewGuid(),TimeSpan.FromMinutes(2),stoppingToken);
            while(!stoppingToken.IsCancellationRequested)
            {
                await store.RecoverPendingAsync(fence,stoppingToken);var sources=await store.SourcesAsync(manifest.Scope,stoppingToken);
                foreach(var source in sources)
                {
                    var key=source.ProfileId+"/"+source.SourceKey;
                    if(retries.TryGetValue(key,out var retry)&&retry.Next>DateTimeOffset.UtcNow)continue;
                    await store.RenewAsync(fence,TimeSpan.FromMinutes(2),stoppingToken);
                    var visit=await pump.VisitAsync(fence,source,DateTimeOffset.UtcNow,stoppingToken);
                    if(visit.State is ReaderState.Unavailable or ReaderState.IdentityUncertain or ReaderState.Gap or ReaderState.Blocked)
                    {var failures=Math.Min(6,retry.Count+1);retries[key]=(failures,DateTimeOffset.UtcNow.AddSeconds(Math.Min(60,Math.Pow(2,failures))));}
                    else retries.Remove(key);
                    log.LogInformation("Source {Source} state {State}: records {Records}, bytes {Bytes}, backlog {Backlog}",source.SourceKey,visit.State,visit.CommittedRecords,visit.ReadBytes,visit.BytesBehind);
                }
                foreach(var group in sources.GroupBy(s=>s.ProfileId))
                {
                    var result=await scheduler.TickAsync(fence,group.Key,group.ToArray(),DateTimeOffset.UtcNow,stoppingToken);
                    log.LogInformation("Deadline evaluation for {Profile}: {Disposition}",group.Key,result.Reason);
                }
                if(options.Once){lifetime.StopApplication();break;}
                await Task.Delay(sources.Length==0?TimeSpan.FromSeconds(1):sources.Min(s=>s.PollInterval),stoppingToken);
            }
        }
        catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
        catch(Exception e){log.LogError("Acquisition stopped safely: {ErrorType}",e.GetType().Name);Environment.ExitCode=1;lifetime.StopApplication();}
        finally
        {
            if(fence is not null)try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await store.ReleaseAsync(fence,timeout.Token);}catch(Exception e){log.LogWarning("Owner release unavailable: {ErrorType}; lease will expire",e.GetType().Name);}
        }
    }
}
