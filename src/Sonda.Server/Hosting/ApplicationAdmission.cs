using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Files;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Server.Hosting;

public enum AdmissionBoundary { BeforeReservation, AfterCoreCommit }
public sealed class ApplicationAdmission(string connection, MonitoringPathPolicy paths, ILogger<ApplicationAdmission> log,
    Action<AdmissionBoundary>? fault = null, Action<AcquisitionBoundary>? acquisitionFault = null, string? hostAuthority = null) : IAsyncDisposable
{
    private sealed class Lane(ProcessingScope scope, PostgresIngestionStore store)
    {
        public ProcessingScope Scope { get; } = scope;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public OwnerFence? Fence { get; set; }
        public FileIngestionPump Pump { get; } = new(store, new WindowsFileSource());
        public DateTimeOffset NextVisit { get; set; }
    }
    private readonly PostgresIngestionStore store = new(connection, acquisitionFault);
    private readonly Guid instance = Guid.NewGuid();
    private readonly ConcurrentDictionary<(string Team, string Application), Lane> lanes = new();
    private readonly TimeSpan lease = TimeSpan.FromMinutes(2);

    private async Task<Lane> Find(string team, string application, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        var row = await db.Set<MonitoringSessionRow>().SingleOrDefaultAsync(x => x.TeamId == team && x.ApplicationId == application, ct) ?? throw new AccessFault(404, "resource_not_found");
        if (hostAuthority is not null && row.HostAuthority != hostAuthority) throw new AccessFault(409, "monitoring_host_authority_mismatch");
        return lanes.GetOrAdd((team, application), _ => new(new(team, row.SessionId, application), store));
    }
    private async Task Own(Lane lane, CancellationToken ct)
    {
        if (lane.Fence is null) lane.Fence = await store.ClaimAsync(lane.Scope, instance, lease, ct);
        else
        {
            try { await store.RenewAsync(lane.Fence, lease, ct); }
            catch (PersistenceConflict) { lane.Fence = null; throw new AccessFault(503, "monitoring_owner_unavailable"); }
        }
        await store.RecoverPendingAsync(lane.Fence, ct);
    }
    public async Task<PolicyReceipt> Submit(Actor actor, string application, string profile, Guid commandId,
        Func<int, DateTimeOffset, PolicyCommand> create, CancellationToken ct)
    {
        var lane = await Find(actor.TeamId, application, ct);
        if (!await lane.Gate.WaitAsync(TimeSpan.FromSeconds(5), ct)) throw new AccessFault(429, "application_busy");
        try
        {
            // Once admitted, request disconnect does not cancel a durable engine operation.
            using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = finish.Token;
            await Own(lane, token);
            await using var db = SondaDbContext.Open(connection);
            var prior = await db.Set<ReceiptRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == lane.Scope.SessionId && x.RequestId == commandId, token);
            if (prior is not null)
            {
                if (prior.ProfileId != profile) throw new AccessFault(409, "operation_identity_conflict");
                if (!prior.Kind.StartsWith("Policy", StringComparison.Ordinal)) throw new AccessFault(409, "operation_identity_conflict");
                var receipt = SimulationJson.Deserialize<PolicyReceipt>(prior.Trace);
                var expected = create(receipt.Command.Sequence, receipt.Command.ProcessedAt);
                if (SimulationJson.Hash(expected with { Id = Guid.Empty }) != prior.Fingerprint) throw new AccessFault(409, "operation_identity_conflict");
                return receipt;
            }
            var runtime = await db.Set<RuntimeRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.SessionId == lane.Scope.SessionId && x.ApplicationId == application, token);
            var current = await db.Set<LaneRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.SessionId == lane.Scope.SessionId && x.ProfileId == profile, token);
            var version = await db.Set<VersionRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile && x.Version == current.Version, token);
            if (SimulationJson.Deserialize<Profile>(version.Snapshot).Policy?.Revision != 2) throw new AccessFault(409, "legacy_live_mutation_unsupported");
            var now = DateTimeOffset.UtcNow;
            if (now.UtcTicks < runtime.LastProcessedTicks) throw new AccessFault(409, "processing_clock_regressed");
            fault?.Invoke(AdmissionBoundary.BeforeReservation);
            var committed = await store.SubmitControlAsync(lane.Fence!, profile, create(checked((int)runtime.NextSequence), now), token);
            fault?.Invoke(AdmissionBoundary.AfterCoreCommit);
            return committed;
        }
        catch (PersistenceConflict) { throw new AccessFault(409, "engine_or_owner_conflict"); }
        finally { lane.Gate.Release(); }
    }
    public async Task VisitAll(CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        var monitors = await db.Set<MonitoringSessionRow>().AsNoTracking().Where(x => hostAuthority == null || x.HostAuthority == hostAuthority).ToArrayAsync(ct);
        foreach (var monitor in monitors)
        {
            var lane = await Find(monitor.TeamId, monitor.ApplicationId, ct);
            if (!await lane.Gate.WaitAsync(0, ct)) continue;
            try
            {
                if (lane.NextVisit > DateTimeOffset.UtcNow) continue;
                await Own(lane, ct);
                var sources = await store.SourcesAsync(lane.Scope, ct);
                foreach (var source in sources)
                {
                    paths.Validate(source); await store.RenewAsync(lane.Fence!, lease, ct);
                    await lane.Pump.VisitAsync(lane.Fence!, source, DateTimeOffset.UtcNow, ct);
                }
                var scheduler = new DeadlineScheduler(store, new WindowsFileSource());
                foreach (var group in sources.GroupBy(x => x.ProfileId)) await scheduler.TickAsync(lane.Fence!, group.Key, group.ToArray(), DateTimeOffset.UtcNow, ct);
                lane.NextVisit = DateTimeOffset.UtcNow + (sources.Length == 0 ? TimeSpan.FromSeconds(1) : sources.Min(x => x.PollInterval));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                lane.NextVisit = DateTimeOffset.UtcNow.AddSeconds(5);
                log.LogWarning("Monitoring application {Application} deferred: {ErrorType}", monitor.ApplicationId, e.GetType().Name);
            }
            finally { lane.Gate.Release(); }
        }
    }
    public async Task<object> AttachProfile(Actor actor, string application, Func<OwnerFence, Task<object>> action, CancellationToken ct)
    {
        var lane = await Find(actor.TeamId, application, ct);
        if (!await lane.Gate.WaitAsync(TimeSpan.FromSeconds(5), ct)) throw new AccessFault(429, "application_busy");
        try { await Own(lane, ct); return await action(lane.Fence!); }
        finally { lane.Gate.Release(); }
    }
    public Task<object> ChangeSource(Actor actor, string application, SourceConfiguration next, long expectedRevision, Guid operation, CancellationToken ct) =>
        AttachProfile(actor, application, async fence =>
        {
            using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var revision = await store.ChangeSourceWithReceiptAsync(fence, next, expectedRevision, operation, finish.Token);
            return new { id = next.SourceKey, revision, state = next.Enabled ? "Enabled" : "Disabled" };
        }, ct);
    public async ValueTask DisposeAsync()
    {
        foreach (var lane in lanes.Values)
        {
            await lane.Pump.DisposeAsync();
            if (lane.Fence is not null)
                try { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await store.ReleaseAsync(lane.Fence, ct.Token); }
                catch (Exception e) { log.LogWarning("Owner release deferred to lease expiration: {ErrorType}", e.GetType().Name); }
        }
    }
}
public sealed class SharedMonitoringWorker(ApplicationAdmission admission, IConfiguration configuration, ILogger<SharedMonitoringWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Monitoring:RunEnabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await admission.VisitAll(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { log.LogWarning("Monitoring discovery unavailable: {ErrorType}", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
