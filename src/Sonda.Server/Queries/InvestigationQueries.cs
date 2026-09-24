using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Api.Contracts;
using Sonda.Infrastructure.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Processing;

namespace Sonda.Server.Queries;

public sealed class InvestigationQueries(string connection, CursorCodec cursors, TimeProvider clock)
{
    public async Task<Page<ApplicationDto>> Applications(Actor actor, int? size, string? cursor, CancellationToken ct)
    {
        var take = CursorCodec.Size(size); var filter = new { resource = "applications", take };
        var after = cursors.Decode(cursor, actor.TeamId, filter);
        await using var db = SondaDbContext.Open(connection); db.Database.SetCommandTimeout(5);
        var query = db.Set<ApplicationRow>().Where(x => x.TeamId == actor.TeamId);
        if (after is not null) query = query.Where(x => string.Compare(x.ApplicationId, after.Key) > 0);
        var rows = await query.OrderBy(x => x.ApplicationId).Take(take + 1).Select(x => new ApplicationDto(x.ApplicationId, x.Name, x.Revision)).ToArrayAsync(ct);
        var more = rows.Length > take; var items = rows.Take(take).ToArray();
        return new(items, more, more ? cursors.Encode(actor.TeamId, filter, items[^1].Id) : null, clock.GetUtcNow());
    }
    public async Task<ApplicationDto> Application(Actor actor, string id, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        return await db.Set<ApplicationRow>().Where(x => x.TeamId == actor.TeamId && x.ApplicationId == id).Select(x => new ApplicationDto(x.ApplicationId, x.Name, x.Revision)).SingleOrDefaultAsync(ct) ?? throw new AccessFault(404, "resource_not_found");
    }
    public async Task<Page<RunDto>> Runs(Actor actor, string scope, Guid session, int? size, string? cursor, CancellationToken ct, string? parentId = null)
    {
        if (parentId is not null && scope != "Order") throw new AccessFault(422, "parent_filter_requires_order_scope");
        var take = CursorCodec.Size(size);
        object filter = parentId is null ? new { resource = "runs", scope, session, take } : new { resource = "runs", scope, session, take, parentId };
        var after = cursors.Decode(cursor, actor.TeamId, filter);
        await using var db = SondaDbContext.Open(connection); db.Database.SetCommandTimeout(5);
        await RequireMonitoring(db, actor, session, ct);
        var query = db.Set<RunRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Scope == scope);
        if (parentId is not null)
        {
            if (!await db.Set<RunRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Scope == "Application" && x.Id == parentId, ct))
                throw new AccessFault(404, "resource_not_found");
            query = query.Where(x => x.ParentId == parentId);
        }
        if (after is not null) query = query.Where(x => string.Compare(x.Id, after.Key) > 0);
        var raw = await query.OrderBy(x => x.Id).Take(take + 1).AsNoTracking().ToArrayAsync(ct);
        var receiptIds = raw.Select(x => x.CreatedReceipt).ToArray();
        var receipts = await db.Set<ReceiptRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && receiptIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.ProcessedAt, ct);
        var rows = raw.Select(x => RunDto(x, receipts[x.CreatedReceipt])).ToArray();
        var more = rows.Length > take; var items = rows.Take(take).ToArray();
        return new(items, more, more ? cursors.Encode(actor.TeamId, filter, items[^1].Id) : null, clock.GetUtcNow());
    }
    public async Task<RunDto> Run(Actor actor, string scope, Guid session, string id, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection); await RequireMonitoring(db, actor, session, ct);
        var row = await db.Set<RunRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Id == id && x.Scope == scope, ct) ?? throw new AccessFault(404, "resource_not_found");
        var receipt = await db.Set<ReceiptRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Id == row.CreatedReceipt, ct);
        return RunDto(row, receipt.ProcessedAt);
    }
    public async Task<Page<IncidentDto>> Incidents(Actor actor, Guid session, int? size, string? cursor, CancellationToken ct)
    {
        var take = CursorCodec.Size(size); var filter = new { resource = "incidents", session, take };
        var after = cursors.Decode(cursor, actor.TeamId, filter);
        await using var db = SondaDbContext.Open(connection); await RequireMonitoring(db, actor, session, ct); db.Database.SetCommandTimeout(5);
        var query = db.Set<IncidentRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session);
        if (after is not null) query = query.Where(x => string.Compare(x.Id, after.Key) > 0);
        var rows = await query.OrderBy(x => x.Id).Take(take + 1).Select(x => new IncidentDto(x.SessionId, x.Id, x.ProfileId, x.Problem, x.Severity, x.Status, x.Revision, x.PolicyContext != null)).ToArrayAsync(ct);
        var more = rows.Length > take; var items = rows.Take(take).ToArray();
        return new(items, more, more ? cursors.Encode(actor.TeamId, filter, items[^1].Id) : null, clock.GetUtcNow());
    }
    public async Task<IncidentDto> Incident(Actor actor, Guid session, string id, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection); await RequireMonitoring(db, actor, session, ct);
        return await db.Set<IncidentRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Id == id)
            .Select(x => new IncidentDto(x.SessionId, x.Id, x.ProfileId, x.Problem, x.Severity, x.Status, x.Revision, x.PolicyContext != null)).SingleOrDefaultAsync(ct) ?? throw new AccessFault(404, "resource_not_found");
    }
    public static async Task RequireMonitoring(SondaDbContext db, Actor actor, Guid session, CancellationToken ct)
    {
        if (!await db.Set<MonitoringSessionRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.SessionId == session, ct)) throw new AccessFault(404, "resource_not_found");
    }
    private static RunDto RunDto(RunRow row, DateTimeOffset processed)
    {
        var state = SimulationJson.Deserialize<RunState>(row.Payload);
        return new(row.SessionId, row.Id, row.ProfileId, row.Version, row.Scope, row.ParentId, row.Identifier, row.Attempt, row.Result,
            state.StartedEventAt, processed, state.CompletedEventAt, state.CompletedProcessedAt, state.PolicyContext?.PreviousAttemptId, state.TerminalReason);
    }
}
