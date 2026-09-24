using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact] public async Task Delivered_phase3_database_upgrades_with_all_prior_rows_and_hashes_unchanged()
    {
        var name="sonda_p4upgrade_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(pg.OwnerConnection);await admin.OpenAsync();await new NpgsqlCommand("CREATE DATABASE "+name,admin).ExecuteNonQueryAsync();var target=new NpgsqlConnectionStringBuilder(pg.OwnerConnection){Database=name}.ConnectionString;
        try
        {
            await using(var migration=SondaDbContext.Open(target))await migration.GetService<IMigrator>().MigrateAsync("20260924032911_InterpretationPolicies");
            var(sample,_)=await PolicySetup();var configuration=new PostgresConfigurationStore(target);var id=await configuration.CreateSessionAsync(sample);var profile=sample.Profiles[0];var scope=new ProcessingScope(profile.TeamId,id,profile.ApplicationId);var adapter=new PostgresPolicyStore(target);
            var inputs=new[]{PolicyInput(1,"CAM process started"),PolicyInput(2,"Finding order OrderID=x"),PolicyInput(3,"Unable to send order OrderID=x"),PolicyInput(4,"Finding order OrderID=x")};foreach(var input in inputs)await adapter.ExecuteAsync(scope,profile.Id,input);
            var expected=await adapter.ReadAsync(scope,profile.Id);var before=await EntireSchemaSnapshot(target);
            await using(var migration=SondaDbContext.Open(target))
            {
                await migration.Database.MigrateAsync();Assert.Equal(new[]{"20260924023552_DurablePersistence","20260924032911_InterpretationPolicies","20260924044645_FileAcquisition"},await migration.Database.GetAppliedMigrationsAsync());
                await migration.Database.MigrateAsync();Assert.False(migration.Database.HasPendingModelChanges());
            }
            var after=await EntireSchemaSnapshot(target);foreach(var pair in before)Assert.Equal(pair.Value,after[pair.Key]);
            Assert.Equal(SimulationJson.Serialize(expected),SimulationJson.Serialize(await adapter.ReadAsync(scope,profile.Id)));
            foreach(var input in inputs)await adapter.ExecuteAsync(scope,profile.Id,input);
            Assert.Equal(SimulationJson.Serialize(expected),SimulationJson.Serialize(await adapter.ReadAsync(scope,profile.Id)));
            await adapter.ExecuteAsync(scope,profile.Id,PolicyInput(5,"Order sent OrderID=x"));Assert.Single(Assert.Single((await adapter.ReadAsync(scope,profile.Id)).Interpreter.Incidents).Recoveries);
            await File.WriteAllTextAsync(Phase4Artifact("upgrade.json"),SimulationJson.Serialize(new{oldTableHashes=before.ToDictionary(p=>p.Key,p=>SimulationJson.Hash(p.Value)),unchanged=true,replayUnchanged=true,continuationRecovered=true,modelMatches=true}));
        }
        finally{NpgsqlConnection.ClearAllPools();await new NpgsqlCommand("DROP DATABASE "+name+" WITH (FORCE)",admin).ExecuteNonQueryAsync();}
    }
    private static async Task<Dictionary<string,string>> EntireSchemaSnapshot(string connection)
    {
        await using var db=new NpgsqlConnection(connection);await db.OpenAsync();List<string> tables=[];
        await using(var list=new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='sonda' ORDER BY tablename",db))await using(var reader=await list.ExecuteReaderAsync())while(await reader.ReadAsync())tables.Add(reader.GetString(0));
        Dictionary<string,string> result=[];
        foreach(var table in tables){List<string> rows=[];await using var query=new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM sonda.\"{table.Replace("\"","\"\"")}\" t ORDER BY to_jsonb(t)::text",db);await using var reader=await query.ExecuteReaderAsync();while(await reader.ReadAsync())rows.Add(reader.GetString(0));result[table]=string.Join("\n",rows);}
        return result;
    }
    [Fact] public async Task Acquisition_dump_restore_recovers_reserved_bytes_open_run_checkpoint_and_proof()
    {
        using var directory=new AcquisitionDirectory();var(sample,scope)=await PolicySetup();var source=new SourceConfiguration{ProfileId="cam",SourceKey="file",Root=directory.Root,Enabled=true,Completeness=CompletenessMode.ProducerManifest,ProducerContract="restore-test"};var store=new PostgresIngestionStore(pg.Connection);await store.RegisterAsync(scope,source,"test");var fence=await store.ClaimAsync(scope,Guid.NewGuid(),TimeSpan.FromMinutes(2));var at=DateTimeOffset.UtcNow;var begin=Line("CAM process started");await File.WriteAllBytesAsync(directory.PathFor("a.log"),begin.Bytes);
        var files=new Sonda.Infrastructure.Files.WindowsFileSource();FileObservation observation;using(var handle=files.Open(directory.PathFor("a.log"),directory.Root))observation=files.Inspect(handle,directory.PathFor("a.log"),at);
        var generation=(await store.ObserveFileAsync(fence,source,observation,(int)observation.Length,null)).GenerationId;await store.CommitAsync(fence,generation,begin,at);
        var certificate=await ReadyCertificate(store,fence,generation,source,begin.End,at.AddSeconds(1));await store.DispatchAsync(fence,certificate,at.AddSeconds(1));var order=Line("Finding order OrderID=x",begin.End);
        await File.AppendAllTextAsync(directory.PathFor("a.log"),order.Text+"\n");
        await Assert.ThrowsAsync<IOException>(()=>new PostgresIngestionStore(pg.Connection,b=>{if(b==AcquisitionBoundary.AfterReservation)throw new IOException();}).CommitAsync(fence,generation,order,at.AddSeconds(2)));
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));var bin=Path.Combine(root,".tools/postgresql/pgsql/bin");var path=Phase4Artifact("acquisition-restore.dump");var builder=new NpgsqlConnectionStringBuilder(pg.OwnerConnection);await RunPgTool(bin,"pg_dump",builder,["--format=custom","--no-owner","--no-privileges","--file",path]);
        var name="sonda_p4restore_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(pg.OwnerConnection);await admin.OpenAsync();await new NpgsqlCommand("CREATE DATABASE "+name,admin).ExecuteNonQueryAsync();builder.Database=name;
        try
        {
            await RunPgTool(bin,"pg_restore",builder,["--no-owner","--no-privileges","--dbname",name,path]);var restored=new PostgresIngestionStore(builder.ConnectionString);
            Assert.Equal(begin.End,(await restored.CheckpointAsync(fence.Scope,generation)).Offset);
            await restored.RecoverPendingAsync(fence);await store.RecoverPendingAsync(fence);
            var expected=await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam");Assert.Equal(SimulationJson.Serialize(expected),SimulationJson.Serialize(await new PostgresPolicyStore(builder.ConnectionString).ReadAsync(fence.Scope,"cam")));
            var success=Line("Order sent OrderID=x",order.End);await File.AppendAllTextAsync(directory.PathFor("a.log"),success.Text+"\n");
            await using(var restoredPump=new Sonda.Infrastructure.Files.FileIngestionPump(restored,files))Assert.Equal(1,(await restoredPump.VisitAsync(fence,source,at.AddSeconds(3))).CommittedRecords);
            await using(var originalPump=new Sonda.Infrastructure.Files.FileIngestionPump(store,files))Assert.Equal(1,(await originalPump.VisitAsync(fence,source,at.AddSeconds(3))).CommittedRecords);
            var actual=await new PostgresPolicyStore(builder.ConnectionString).ReadAsync(fence.Scope,"cam");Assert.Equal(SimulationJson.Serialize((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter),SimulationJson.Serialize(actual.Interpreter));
            Assert.Equal(success.End,(await restored.CheckpointAsync(fence.Scope,generation)).Offset);await using var db=SondaDbContext.Open(builder.ConnectionString);Assert.Equal("Consumed",(await db.Set<FrontierCertificateRow>().FindAsync(fence.Scope.TeamId,certificate.Id))!.Status);
            await File.WriteAllTextAsync(Phase4Artifact("restore.json"),SimulationJson.Serialize(new{reservedBytesRecovered=true,checkpoint=success.End,openRunPreserved=true,consumedProofPreserved=true,continuationEqual=true}));
        }
        finally{NpgsqlConnection.ClearAllPools();await new NpgsqlCommand("DROP DATABASE "+name+" WITH (FORCE)",admin).ExecuteNonQueryAsync();}
    }
}
