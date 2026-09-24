using System.Diagnostics;
using Npgsql;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Phase3.Fixtures;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact]
    public async Task Policy_dump_restore_preserves_pinned_versions_deadlines_and_manual_confirmation()
    {
        var p=PolicyFixtureCatalog.Profile() with {TeamId="restore-policy-"+Guid.NewGuid().ToString("N"),Version=7,CycleTiming=new(){FallbackTimeout=TimeSpan.FromMinutes(10)}};
        p=p with {Policy=p.Policy! with {CycleMode=CycleMode.Correlated,CorrelationExpression=@"cycle=(?<cycle>\w+)",CorrelationEpoch="test"}};
        var sample=new SimulationRequest{Profiles=[p],Entries=[],Seed="policy-restore",AsOf=PolicyFixtureCatalog.Start.AddHours(1),ServerTimeZoneId="UTC"};
        var config=new PostgresConfigurationStore(pg.Connection);var id=await config.CreateSessionAsync(sample);var scope=new ProcessingScope(p.TeamId,id,p.ApplicationId);
        var store=new PostgresPolicyStore(pg.Connection);
        await store.ExecuteAsync(scope,p.Id,PolicyInput(1,"APP BEGIN cycle=A"));await store.ExecuteAsync(scope,p.Id,PolicyInput(2,"APP FAILURE cycle=A"));
        var incident=Assert.Single((await store.ReadAsync(scope,p.Id)).Interpreter.Incidents);
        await store.ExecuteAsync(scope,p.Id,new(){Id=Guid.NewGuid(),Sequence=3,Kind=PolicyCommandKind.ChangeStatus,ProcessedAt=PolicyFixtureCatalog.Start.AddSeconds(3),IncidentId=incident.Id,TargetStatus=IncidentStatus.Resolved,ExpectedRevision=0,Actor="reviewer",Reason="Reviewed"});
        await store.ExecuteAsync(scope,p.Id,PolicyInput(4,"APP BEGIN cycle=B"));
        var next=p with {Version=8};var revision=await config.EditDraftAsync(next,0,Guid.NewGuid());await config.PublishAsync(sample with {Profiles=[next]},revision,Guid.NewGuid());
        await store.ExecuteAsync(scope,p.Id,new(){Id=Guid.NewGuid(),Sequence=5,Kind=PolicyCommandKind.ActivateVersion,ProcessedAt=PolicyFixtureCatalog.Start.AddSeconds(5),Profile=next});
        await store.ExecuteAsync(scope,p.Id,PolicyInput(6,"APP BEGIN cycle=C"));
        var before=await store.ReadAsync(scope,p.Id);Assert.Equal(new[]{7,8},before.Interpreter.Runs.Where(r=>r.Result is null).Select(r=>r.PolicyContext!.ProfileVersion));
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));var bin=Environment.GetEnvironmentVariable("SONDA_PG_BIN")??Path.Combine(root,".tools","postgresql","pgsql","bin");
        var path=Path.Combine(root,"artifacts","test-results",$"policy-restore-{Guid.NewGuid():N}.dump");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder=new NpgsqlConnectionStringBuilder(pg.OwnerConnection);await RunPgTool(bin,"pg_dump",builder,["--format=custom","--no-owner","--no-privileges","--file",path]);
        var name="sonda_restore_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(pg.OwnerConnection);await admin.OpenAsync();await new NpgsqlCommand("CREATE DATABASE "+name,admin).ExecuteNonQueryAsync();builder.Database=name;
        try
        {
            await RunPgTool(bin,"pg_restore",builder,["--no-owner","--no-privileges","--dbname",name,path]);var restored=new PostgresPolicyStore(builder.ConnectionString);
            Assert.Equal(SimulationJson.Serialize(before),SimulationJson.Serialize(await restored.ReadAsync(scope,p.Id)));
            var success=PolicyInput(7,"APP SUCCESS cycle=C");await restored.ExecuteAsync(scope,p.Id,success);await store.ExecuteAsync(scope,p.Id,success);
            var clock=new PolicyCommand{Id=Guid.NewGuid(),Sequence=8,Kind=PolicyCommandKind.AdvanceTime,ProcessedAt=sample.AsOf,EffectiveAt=sample.AsOf,Frontier=new(7,sample.AsOf,0)};
            await restored.ExecuteAsync(scope,p.Id,clock);await store.ExecuteAsync(scope,p.Id,clock);
            var after=await restored.ReadAsync(scope,p.Id);Assert.Equal(SimulationJson.Serialize(await store.ReadAsync(scope,p.Id)),SimulationJson.Serialize(after));
            Assert.Single(after.Interpreter.Incidents[0].Recoveries);Assert.Equal("Manual",after.Interpreter.Incidents[0].PolicyContext!.ResolutionKind);
            Assert.Equal(2,after.Interpreter.Incidents.Length);Assert.All(after.Interpreter.Runs,r=>Assert.NotNull(r.Result));
            var output=Path.Combine(root,"artifacts","phase3","restore.json");await File.WriteAllTextAsync(output,SimulationJson.Serialize(new{pinnedVersions=new[]{7,8},stateEqual=true,continuationEqual=true,manualResolutionPreserved=true,deadlineCompleted=true}));
        }
        finally{NpgsqlConnection.ClearAllPools();await new NpgsqlCommand("DROP DATABASE "+name+" WITH (FORCE)",admin).ExecuteNonQueryAsync();}
    }
    private static async Task RunPgTool(string bin,string name,NpgsqlConnectionStringBuilder settings,string[] arguments)
    {
        var start=new ProcessStartInfo(Path.Combine(bin,name+(OperatingSystem.IsWindows()?".exe":""))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        start.Environment["PGHOST"]=settings.Host;start.Environment["PGPORT"]=settings.Port.ToString();start.Environment["PGUSER"]=settings.Username;start.Environment["PGPASSWORD"]=settings.Password;start.Environment["PGDATABASE"]=settings.Database;
        using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();Assert.True(child.ExitCode==0,$"{name}: {await error}; {await output}");
    }
}
