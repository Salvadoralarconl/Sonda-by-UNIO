using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
using Sonda.Server.Hosting;
using Xunit;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class WorkflowTests(AccessDatabase database)
{
    private readonly MonitoringPathPolicy paths = new([@"C:\synthetic-logs"]);
    private async Task<(Actor Actor, ProcessingScope Scope, string Profile, string Incident)> Fixture()
    {
        var actor = await database.ActorAsync();
        var request = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        var profile = request.Profiles.Single() with { TeamId = actor.TeamId, Policy = new InterpretationPolicy { RoutingContract = "fixture-route", RecoveryCompatibility = "fixture-recovery" } };
        request = request with { Profiles = [profile], ServerTimeZoneId = TimeZoneInfo.Local.Id };
        var session = await new PostgresConfigurationStore(database.Connection).CreateSessionAsync(request);
        var scope = new ProcessingScope(actor.TeamId, session, profile.ApplicationId); var processor = new PostgresPolicyStore(database.Connection);
        for (var i = 0; i < request.Entries.Count; i++) await processor.ExecuteAsync(scope, profile.Id, new PolicyCommand
        { Id = Guid.NewGuid(), Kind = PolicyCommandKind.Evidence, Sequence = i + 1, ProcessedAt = request.Entries[i].ProcessedAt, Raw = request.Entries[i].Raw, EvidenceKey = "synthetic-" + i, SampleDate = request.SampleDate });
        var state = await processor.ReadAsync(scope, profile.Id); var incident = Assert.Single(state.Interpreter.Incidents);
        await new PostgresIngestionStore(database.Connection).RegisterAsync(scope, new SourceConfiguration { ProfileId = profile.Id, SourceKey = "sample", Root = @"C:\synthetic-logs", SampleDate = request.SampleDate, Enabled = true }, "synthetic");
        return (actor, scope, profile.Id, incident.Id);
    }
    private WorkflowService Service(AccessDbContext db, ApplicationAdmission admission) => new(database.Connection,
        new OperationLedger(db), admission, new InitialActivationStore(database.Connection, paths, "synthetic"));

    [Theory]
    [InlineData("before-reservation")]
    [InlineData("pending-before-interpretation")]
    [InlineData("after-core-commit")]
    public async Task Crash_retry_uses_durable_intent_pending_and_receipt_without_duplicate_history(string boundary)
    {
        var (actor, scope, _, incident) = await Fixture();
        var command = new StatusRequest(Guid.NewGuid(), scope.SessionId, 0, IncidentStatus.Investigating, "Synthetic review");
        await using (var access = AccessDbContext.Open(database.Connection))
        await using (var crashing = new ApplicationAdmission(database.Connection, paths, NullLogger<ApplicationAdmission>.Instance,
            b => { if ((boundary == "before-reservation" && b == AdmissionBoundary.BeforeReservation) || (boundary == "after-core-commit" && b == AdmissionBoundary.AfterCoreCommit)) throw new IOException("Synthetic process interruption"); },
            b => { if (boundary == "pending-before-interpretation" && b == AcquisitionBoundary.BeforeInterpretation) throw new IOException("Synthetic pending interruption"); }))
            await Assert.ThrowsAsync<IOException>(() => Service(access, crashing).Status(actor, incident, command, default));
        await using var restarted = new ApplicationAdmission(database.Connection, paths, NullLogger<ApplicationAdmission>.Instance);
        if (boundary == "after-core-commit")
        {
            await using var access = AccessDbContext.Open(database.Connection);
            var op = (await access.Set<ApiOperation>().FindAsync(actor.TeamId, command.OperationId))!;
            await OperationReconciler.Reconcile(database.Connection, access, op, default);
            Assert.Equal("Committed", op.State);
        }
        await using (var access = AccessDbContext.Open(database.Connection)) await Service(access, restarted).Status(actor, incident, command, default);
        await using (var access = AccessDbContext.Open(database.Connection)) await Service(access, restarted).Status(actor, incident, command, default);
        await using var core = SondaDbContext.Open(database.Connection);
        Assert.Single(await core.Set<ReceiptRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == scope.SessionId && x.RequestId == command.OperationId).ToArrayAsync());
        var updated = await core.Set<IncidentRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.Id == incident);
        Assert.Equal("Investigating", updated.Status); Assert.Equal(1, updated.Revision);
        Assert.Equal(4, await core.Set<FactRow>().CountAsync(x => x.TeamId == actor.TeamId));
        await using var ledger = AccessDbContext.Open(database.Connection);
        Assert.Equal("Committed", (await ledger.Set<ApiOperation>().FindAsync(actor.TeamId, command.OperationId))!.State);
        Assert.Equal(2, await ledger.Set<AccessAudit>().CountAsync(x => x.TeamId == actor.TeamId && x.OperationId == command.OperationId));
    }

    [Fact]
    public async Task Concurrent_workflow_commands_do_not_bypass_expected_revision()
    {
        var (actor, scope, _, incident) = await Fixture();
        await using var admission = new ApplicationAdmission(database.Connection, paths, NullLogger<ApplicationAdmission>.Instance);
        async Task Send(IncidentStatus status)
        {
            await using var access = AccessDbContext.Open(database.Connection);
            await Service(access, admission).Status(actor, incident, new(Guid.NewGuid(), scope.SessionId, 0, status, "Synthetic concurrent review"), default);
        }
        await Task.WhenAll(Send(IncidentStatus.Investigating), Send(IncidentStatus.Resolved));
        await using var core = SondaDbContext.Open(database.Connection);
        Assert.Equal(1, (await core.Set<IncidentRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.Id == incident)).Revision);
        await using var access = AccessDbContext.Open(database.Connection);
        Assert.Single(await access.Set<ApiOperation>().Where(x => x.TeamId == actor.TeamId && x.State == "Committed").ToArrayAsync());
        Assert.Single(await access.Set<ApiOperation>().Where(x => x.TeamId == actor.TeamId && x.State == "Rejected").ToArrayAsync());
    }

    [Fact]
    public async Task Source_commit_ack_loss_replays_once_and_conflicting_revision_rolls_back()
    {
        var (actor, scope, profile, _) = await Fixture();
        var store = new PostgresIngestionStore(database.Connection);
        var fence = await store.ClaimAsync(scope, Guid.NewGuid(), TimeSpan.FromMinutes(2));
        try
        {
            var old = Assert.Single(await store.SourcesAsync(scope));
            var next = old with { Revision = old.Revision + 1, Enabled = false }; var id = Guid.NewGuid();
            var crash = new PostgresIngestionStore(database.Connection, b => { if (b == AcquisitionBoundary.AfterCommit) throw new IOException("lost ack"); });
            await Assert.ThrowsAsync<IOException>(() => crash.ChangeSourceWithReceiptAsync(fence, next, old.Revision, id));
            Assert.Equal(next.Revision, await store.ChangeSourceWithReceiptAsync(fence, next, old.Revision, id));
            await Assert.ThrowsAsync<PersistenceConflict>(() => store.ChangeSourceWithReceiptAsync(fence, next with { Enabled = true }, old.Revision, id));
            await Assert.ThrowsAsync<PersistenceConflict>(() => store.ChangeSourceWithReceiptAsync(fence, next, old.Revision, Guid.NewGuid()));
            await using var db = SondaDbContext.Open(database.Connection);
            Assert.Equal(2, await db.Set<AcquisitionRevisionRow>().CountAsync(x => x.TeamId == actor.TeamId));
            Assert.Single(await db.Set<CommandRow>().Where(x => x.TeamId == actor.TeamId && x.Id == id).ToArrayAsync());
            Assert.Equal(4, await db.Set<FactRow>().CountAsync(x => x.TeamId == actor.TeamId));
        }
        finally { await store.ReleaseAsync(fence); }
    }

    [Fact]
    public async Task Standalone_owner_fence_and_wrong_host_cannot_be_bypassed_by_workflow()
    {
        var (actor, scope, _, incident) = await Fixture();
        var store = new PostgresIngestionStore(database.Connection); var fence = await store.ClaimAsync(scope, Guid.NewGuid(), TimeSpan.FromMinutes(2));
        try
        {
            await using var admission = new ApplicationAdmission(database.Connection, paths, NullLogger<ApplicationAdmission>.Instance);
            await using var access = AccessDbContext.Open(database.Connection);
            Assert.Equal(409, (await Assert.ThrowsAsync<AccessFault>(() => Service(access, admission).Status(actor, incident, new(Guid.NewGuid(), scope.SessionId, 0, IncidentStatus.Investigating, "review"), default))).Status);
            await using var wrongHost = new ApplicationAdmission(database.Connection, paths, NullLogger<ApplicationAdmission>.Instance, hostAuthority: "other-host");
            Assert.Equal(409, (await Assert.ThrowsAsync<AccessFault>(() => Service(access, wrongHost).Status(actor, incident, new(Guid.NewGuid(), scope.SessionId, 0, IncidentStatus.Investigating, "review"), default))).Status);
            await using var db = SondaDbContext.Open(database.Connection);
            Assert.Equal(0, (await db.Set<IncidentRow>().SingleAsync(x => x.TeamId == actor.TeamId)).Revision);
        }
        finally { await store.ReleaseAsync(fence); }
    }

    [Fact]
    public async Task Activation_recovers_reserved_file_input_and_pins_open_cycle_to_old_version()
    {
        var (actor, scope, profile, _) = await Fixture(); var store = new PostgresIngestionStore(database.Connection);
        var source = Assert.Single(await store.SourcesAsync(scope)); var fence = await store.ClaimAsync(scope, Guid.NewGuid(), TimeSpan.FromMinutes(2));
        var now = DateTimeOffset.UtcNow;
        var generation = (await store.ObserveFileAsync(fence, source, new(@"C:\synthetic-logs\a.log", new("synthetic", "volume", "file", "birth"), 0, now, "empty"), 0, null)).GenerationId;
        PhysicalRecord Line(string text, long offset = 0) { var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\n"); return new(offset, offset + bytes.Length, bytes, text, "LF", 0); }
        var begin = Line("10:05:00 CAM process started");
        await Assert.ThrowsAsync<IOException>(() => new PostgresIngestionStore(database.Connection, b => { if (b == AcquisitionBoundary.AfterReservation) throw new IOException("reserved"); }).CommitAsync(fence, generation, begin, now));
        await store.ReleaseAsync(fence);
        await using var core = SondaDbContext.Open(database.Connection);
        var old = SimulationJson.Deserialize<Profile>((await core.Set<VersionRow>().FindAsync(actor.TeamId, profile, 1))!.Snapshot); var next = old with { Version = 2 };
        var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json"))) with { Profiles = [next] };
        var config = new PostgresConfigurationStore(database.Connection); await config.EditDraftAsync(next, 0, Guid.NewGuid()); await config.PublishAsync(sample, 1, Guid.NewGuid());
        await using (var admission = new ApplicationAdmission(database.Connection, paths, NullLogger<ApplicationAdmission>.Instance))
        await using (var access = AccessDbContext.Open(database.Connection))
            await Service(access, admission).Activate(actor, profile, new(Guid.NewGuid(), 2, 0, 1), default);
        var state = await new PostgresPolicyStore(database.Connection).ReadAsync(scope, profile);
        Assert.Equal(1, Assert.Single(state.Interpreter.Runs, x => x.Result is null).PolicyContext!.ProfileVersion);
        Assert.Equal(2, (await core.Set<LaneRow>().AsNoTracking().SingleAsync(x => x.TeamId == actor.TeamId)).Version);
        fence = await store.ClaimAsync(scope, Guid.NewGuid(), TimeSpan.FromMinutes(2));
        try
        {
            var end = Line("10:05:01 CAM process completed", begin.End); await store.CommitAsync(fence, generation, end, DateTimeOffset.UtcNow);
            await store.CommitAsync(fence, generation, Line("10:05:02 CAM process started", end.End), DateTimeOffset.UtcNow);
            state = await new PostgresPolicyStore(database.Connection).ReadAsync(scope, profile);
            Assert.Equal(2, Assert.Single(state.Interpreter.Runs, x => x.Result is null).PolicyContext!.ProfileVersion);
            await Assert.ThrowsAsync<PersistenceConflict>(() => store.ChangeSourceWithReceiptAsync(fence, source with { Revision = 2, Enabled = false }, 1, Guid.NewGuid()));
        }
        finally { await store.ReleaseAsync(fence); }
    }
}
