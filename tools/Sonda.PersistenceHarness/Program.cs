using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Acquisition;

var connection = Environment.GetEnvironmentVariable("SONDA_DATABASE") ?? throw new InvalidOperationException("Set SONDA_DATABASE to a disposable PostgreSQL connection.");
if (args.Length == 0) throw new ArgumentException("migrate | simulate <fixture> | process <request.json> [crash-after-commit]");
if(args[0]=="ingest-record")
{
    var request=SimulationJson.Deserialize<IngestionHarnessRequest>(await File.ReadAllTextAsync(args[1]));
    if(request.ByteFile is { } path)
    {
        await using var file=new Sonda.Infrastructure.Files.WindowsFileSource().Open(path,Path.GetDirectoryName(path)!);
        var bytes=new byte[request.Record.Bytes.Length];var count=0;
        while(count<bytes.Length){var read=await RandomAccess.ReadAsync(file.SafeFileHandle,bytes.AsMemory(count),request.Record.Start+count);if(read==0)throw new IOException("Incomplete synthetic byte range.");count+=read;}
        if(!bytes.SequenceEqual(request.Record.Bytes))throw new IOException("Synthetic bytes differ from the requested range.");
    }
    if(args.Contains("crash-after-read"))Environment.Exit(76);
    var store=new PostgresIngestionStore(connection,b=>
    {
        if(args.Contains("crash-after-commit")&&b==AcquisitionBoundary.AfterCommit)Environment.Exit(73);
        if(args.Contains("crash-before-commit")&&b==AcquisitionBoundary.AfterCheckpoint)Environment.Exit(74);
        if(args.Contains("crash-after-reservation")&&b==AcquisitionBoundary.AfterReservation)Environment.Exit(75);
    });
    Console.WriteLine(SimulationJson.Serialize(await store.CommitAsync(request.Fence,request.Generation,request.Record,request.ProcessedAt)));return;
}
if (args[0] == "migrate")
{
    await using var db = SondaDbContext.Open(connection); await db.Database.MigrateAsync(); Console.WriteLine("Migrations applied."); return;
}
if (args[0] == "process")
{
    var request = SimulationJson.Deserialize<ProcessingRequest>(await File.ReadAllTextAsync(args[1]));
    var store = new PostgresProcessingStore(connection, b =>
    {
        if (args.Contains("crash-after-commit") && b == PersistenceBoundary.AfterCommit) Environment.Exit(73);
        if (args.Contains("crash-before-commit") && b == PersistenceBoundary.BeforeCommit) Environment.Exit(74);
    });
    Console.WriteLine(SimulationJson.Serialize(await store.ProcessAsync(request))); return;
}
if(args[0]=="policy-process")
{
    var request=SimulationJson.Deserialize<PolicyHarnessRequest>(await File.ReadAllTextAsync(args[1]));
    var store=new PostgresPolicyStore(connection,b=>
    {
        if(args.Contains("crash-after-commit")&&b==PersistenceBoundary.AfterCommit)Environment.Exit(73);
        if(args.Contains("crash-before-commit")&&b==PersistenceBoundary.BeforeCommit)Environment.Exit(74);
    });
    Console.WriteLine(SimulationJson.Serialize(await store.ExecuteAsync(request.Scope,request.ProfileId,request.Command)));return;
}
if (args[0] != "simulate") throw new ArgumentException("Unknown command.");
var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(args[1]));
var session = await new PostgresConfigurationStore(connection).CreateSessionAsync(sample);
var counters = new Dictionary<string, long>();
var adapter = new PostgresProcessingStore(connection);
for (var n = 0; n < sample.Entries.Count; n++)
{
    var entry = sample.Entries[n]; var profile = sample.Profiles.Single(p => p.Id == entry.ProfileId);
    counters.TryGetValue(profile.ApplicationId, out var seq); counters[profile.ApplicationId] = ++seq;
    var request = new ProcessingRequest(new(profile.TeamId, session, profile.ApplicationId), profile.Id, Guid.NewGuid(), "sample", "fixture", n, n + 1, seq, entry.Raw, entry.ProcessedAt, sample.SampleDate);
    await adapter.ProcessAsync(request);
}
foreach (var p in sample.Profiles)
    Console.WriteLine(SimulationJson.Serialize(await adapter.ReadStateAsync(new(p.TeamId, session, p.ApplicationId), p.Id)));
Console.WriteLine($"Session: {session}");

public sealed record PolicyHarnessRequest(ProcessingScope Scope,string ProfileId,PolicyCommand Command);
public sealed record IngestionHarnessRequest(OwnerFence Fence,Guid Generation,PhysicalRecord Record,DateTimeOffset ProcessedAt,string? ByteFile=null);
