using Microsoft.EntityFrameworkCore;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Runs;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Persistence.Tests;
public sealed partial class PersistenceTests
{
    [Fact] public async Task EOF_without_producer_proof_never_authorizes_deadline()
    {
        var(fence,generation,source)=await IngestionSetup(timeout:true);var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.Parse("2026-09-23T14:00:00Z");var begin=Line("CAM process started");
        await store.CommitAsync(fence,generation,begin,at);
        var coverage=new SourceCoverage(source.SourceKey,generation,begin.End,1,"not-proof",at.AddDays(1));
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.CreateCertificateAsync(fence,"cam","fixture-contract",[coverage],at.AddDays(1)));
        Assert.Null(Assert.Single((await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")).Interpreter.Runs).Result);
        await using var db=SondaDbContext.Open(pg.Connection);Assert.Equal(0,await db.Set<FactRow>().CountAsync(f=>f.SessionId==fence.Scope.SessionId));
    }
    [Fact] public async Task Proven_frontier_submits_existing_deadline_once_and_preserves_physical_checkpoint()
    {
        var(fence,generation,source)=await IngestionSetup(proof:true,timeout:true);var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.Parse("2026-09-23T14:00:00Z");var begin=Line("CAM process started");await store.CommitAsync(fence,generation,begin,at);
        var coverage=new SourceCoverage(source.SourceKey,generation,begin.End,1,"",at.AddMinutes(2));
        var manifest=new ProducerCoverageManifest("fixture-contract",source.SourceKey,coverage.CompleteThrough,[coverage],true,true,true);var hash=SimulationJson.Hash(manifest);
        await store.RecordManifestAsync(fence,"cam",manifest,hash);coverage=coverage with {ManifestHash=hash};
        var certificate=await store.CreateCertificateAsync(fence,"cam","fixture-contract",[coverage],coverage.CompleteThrough);
        Assert.Equal("Applied",(await store.DispatchAsync(fence,certificate,coverage.CompleteThrough))!.Disposition);
        var state=await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam");Assert.Equal(DetectionResult.Undefined,Assert.Single(state.Interpreter.Runs).Result);
        await store.DispatchAsync(fence,certificate,coverage.CompleteThrough.AddHours(1));Assert.Equal(SimulationJson.Serialize(state),SimulationJson.Serialize(await new PostgresPolicyStore(pg.Connection).ReadAsync(fence.Scope,"cam")));
        Assert.Equal(begin.End,(await store.CheckpointAsync(fence.Scope,generation)).Offset);
        await using var db=SondaDbContext.Open(pg.Connection);Assert.Equal(1,await db.Set<EvidenceRow>().CountAsync(e=>e.SessionId==fence.Scope.SessionId));Assert.Equal(1,await db.Set<FactRow>().CountAsync(f=>f.SessionId==fence.Scope.SessionId));
    }
    [Fact] public async Task Unread_backlog_and_source_failure_invalidate_completeness()
    {
        var(fence,generation,source)=await IngestionSetup(proof:true);var store=new PostgresIngestionStore(pg.Connection);var at=DateTimeOffset.Parse("2026-09-23T14:00:00Z");var record=Line("trace");await store.CommitAsync(fence,generation,record,at);
        var coverage=new SourceCoverage(source.SourceKey,generation,record.End+1,1,"",at.AddMinutes(2));var manifest=new ProducerCoverageManifest("fixture-contract",source.SourceKey,coverage.CompleteThrough,[coverage],true,true,true);var hash=SimulationJson.Hash(manifest);
        await store.RecordManifestAsync(fence,"cam",manifest,hash);coverage=coverage with {ManifestHash=hash};
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.CreateCertificateAsync(fence,"cam","fixture-contract",[coverage],coverage.CompleteThrough));
        await store.ObserveSourceAsync(fence,"cam",new(source.SourceKey,ReaderState.Unavailable,"ShareUnavailable",at.AddMinutes(1),at.AddMinutes(1),ReadSuccess:false));
        await Assert.ThrowsAsync<PersistenceConflict>(()=>store.CreateCertificateAsync(fence,"cam","fixture-contract",[coverage with {ThroughOffset=record.End}],coverage.CompleteThrough));
    }
}
