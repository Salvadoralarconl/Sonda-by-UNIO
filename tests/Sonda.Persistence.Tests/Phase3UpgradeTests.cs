using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed partial class PersistenceTests
{
    private static readonly string[] LegacyTables = ["teams","applications","profiles","log_sources","profile_versions","profile_rules","profile_patterns","profile_parsing_configurations","profile_identifier_configurations","simulation_reports","profile_validations","profile_version_sources","processing_sessions","application_runtime","profile_runtime","profile_activations","raw_evidence","processing_receipts","normalized_evidence","runs","problem_identities","incidents","incident_occurrences","recoveries","incident_status_history","run_metric_facts","command_receipts","commit_observations","run_evidence","occurrence_evidence","recovery_evidence","processing_request_keys"];

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Delivered_phase2_data_upgrades_without_changing_legacy_facts_or_replay(bool completed)
    {
        var(sample,scope,inputs)=await Setup("repeated-recovery");
        // A failed incident plus the open retry is present at the upgrade boundary.
        var split=completed?inputs.Length:inputs.Length-2;await Apply(inputs.Take(split));
        var expected=await new PostgresProcessingStore(pg.Connection).ReadStateAsync(scope,sample.Profiles[0].Id);
        var admin=Environment.GetEnvironmentVariable("SONDA_TEST_ADMIN")!;
        var name="sonda_upgrade_"+Guid.NewGuid().ToString("N");
        await using var server=new NpgsqlConnection(admin);await server.OpenAsync();await new NpgsqlCommand($"CREATE DATABASE {name}",server).ExecuteNonQueryAsync();
        var target=new NpgsqlConnectionStringBuilder(admin){Database=name}.ConnectionString;
        try
        {
            await using(var migration=SondaDbContext.Open(target))
            {
                await migration.GetService<IMigrator>().MigrateAsync("20260924023552_DurablePersistence");
                Assert.Equal(new[]{"20260924023552_DurablePersistence"},await migration.Database.GetAppliedMigrationsAsync());
            }
            await using(var source=new NpgsqlConnection(pg.OwnerConnection))
            await using(var destination=new NpgsqlConnection(target))
            {
                await source.OpenAsync();await destination.OpenAsync();await using var tx=await destination.BeginTransactionAsync();
                foreach(var table in LegacyTables)
                {
                    var order=table=="runs"?"ORDER BY CASE WHEN scope='Application' THEN 0 ELSE 1 END,cycle_sequence,id":"";
                    await using var read=new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM sonda.{table} t WHERE team_id=@team {order}",source);read.Parameters.AddWithValue("team",scope.TeamId);
                    await using var reader=await read.ExecuteReaderAsync();
                    while(await reader.ReadAsync())
                    {
                        var json=JsonNode.Parse(reader.GetString(0))!.AsObject();if(table=="processing_receipts")json.Remove("kind");if(table=="incidents")json.Remove("policy_context");
                        if(table=="profile_versions")json["sealed"]=false;
                        // Populate only columns present in the delivered v1 schema; no triggers are disabled.
                        await using var insert=new NpgsqlCommand($"INSERT INTO sonda.{table} SELECT * FROM json_populate_record(NULL::sonda.{table},@payload::json)",destination,tx);
                        insert.Parameters.AddWithValue("payload",json.ToJsonString());await insert.ExecuteNonQueryAsync();
                    }
                }
                await new NpgsqlCommand("UPDATE sonda.profile_versions SET sealed=true",destination,tx).ExecuteNonQueryAsync();await tx.CommitAsync();
            }
            var before=await LegacySnapshot(target,scope.TeamId);
            await using(var migration=SondaDbContext.Open(target))
            {
                await migration.GetService<IMigrator>().MigrateAsync("20260924032911_InterpretationPolicies");
                Assert.Equal(new[]{"20260924023552_DurablePersistence","20260924032911_InterpretationPolicies"},await migration.Database.GetAppliedMigrationsAsync());
                await migration.GetService<IMigrator>().MigrateAsync("20260924032911_InterpretationPolicies");
            }
            Assert.Equal(before,await LegacySnapshot(target,scope.TeamId));
            var store=new PostgresProcessingStore(target);
            Assert.Equal(SimulationJson.Serialize(expected),SimulationJson.Serialize(await store.ReadStateAsync(scope,sample.Profiles[0].Id)));
            foreach(var input in inputs.Take(split))Assert.True((await store.ProcessAsync(input)).Replayed);
            foreach(var input in inputs.Skip(split)){await store.ProcessAsync(input);await new PostgresProcessingStore(pg.Connection).ProcessAsync(input);}
            Assert.Equal(SimulationJson.Serialize(await new PostgresProcessingStore(pg.Connection).ReadStateAsync(scope,sample.Profiles[0].Id)),SimulationJson.Serialize(await store.ReadStateAsync(scope,sample.Profiles[0].Id)));
            var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/phase3/upgrade-"+(completed?"completed":"open")+".json"));
            await File.WriteAllTextAsync(path,SimulationJson.Serialize(new {initialMigration="20260924023552_DurablePersistence",additiveMigration="20260924032911_InterpretationPolicies",legacyTablesCompared=LegacyTables.Length,allLegacyRowsUnchanged=true,reapplyNoOp=true,replayUnchanged=true,continuationEqual=true,snapshotHash=SimulationJson.Hash(before)}));
        }
        finally {NpgsqlConnection.ClearAllPools();await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)",server).ExecuteNonQueryAsync();}
    }
    private static async Task<string> LegacySnapshot(string connection,string team)
    {
        await using var db=new NpgsqlConnection(connection);await db.OpenAsync();List<string> rows=[];
        foreach(var table in LegacyTables)
        {
            var projection=table=="processing_receipts"?"to_jsonb(t)-'kind'":table=="incidents"?"to_jsonb(t)-'policy_context'":"to_jsonb(t)";
            await using var command=new NpgsqlCommand($"SELECT ({projection})::text FROM sonda.{table} t WHERE team_id=@team ORDER BY ({projection})::text",db);command.Parameters.AddWithValue("team",team);
            await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())rows.Add(table+":"+reader.GetString(0));
        }
        return string.Join("\n",rows);
    }
}
