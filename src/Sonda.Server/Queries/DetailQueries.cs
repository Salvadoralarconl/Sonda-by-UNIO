using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Api.Contracts;
using Sonda.Infrastructure.Persistence;
using Sonda.Server.Hosting;

namespace Sonda.Server.Queries;

public sealed class DetailQueries(string connection, AccessDbContext access, CursorCodec cursors, TimeProvider clock)
{
    public async Task<object> Activation(Actor actor, string profile, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        if (!await db.Set<ProfileRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile, ct)) throw new AccessFault(404, "resource_not_found");
        var lane = await (from l in db.Set<LaneRow>() join m in db.Set<MonitoringSessionRow>() on new { l.TeamId, l.SessionId, l.ApplicationId } equals new { m.TeamId, m.SessionId, m.ApplicationId }
                          where l.TeamId == actor.TeamId && l.ProfileId == profile select l).SingleOrDefaultAsync(ct);
        if (lane is null) return new { activeVersion = 0, expectedRevision = 0L, sessionId = (Guid?)null };
        var count = await db.Set<ActivationRow>().LongCountAsync(x => x.TeamId == actor.TeamId && x.SessionId == lane.SessionId && x.ProfileId == profile, ct);
        return new { activeVersion = lane.Version, expectedRevision = count - 1, sessionId = (Guid?)lane.SessionId };
    }
    public async Task<object> Members(Actor actor, int? size, string? cursor, CancellationToken ct)
    {
        var take = CursorCodec.Size(size); var filter = new { resource = "members", take }; var after = cursors.Decode(cursor, actor.TeamId, filter);
        var query = access.Set<Membership>().Where(x => x.TeamId == actor.TeamId);
        if (after is not null) { var id = Guid.Parse(after.Key); query = query.Where(x => x.AccountId.CompareTo(id) > 0); }
        var rows = await (from member in query join account in access.Users on member.AccountId equals account.Id
                          orderby member.AccountId
                          select new { member.AccountId, login = account.UserName, member.Role, member.Enabled, member.Revision }).Take(take + 1).ToArrayAsync(ct);
        var items = rows.Take(take).ToArray();
        return new { items, hasMore = rows.Length > take, nextCursor = rows.Length > take ? cursors.Encode(actor.TeamId, filter, items[^1].AccountId.ToString()) : null };
    }
    public async Task<object> Sources(Actor actor, string profile, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        if (!await db.Set<ProfileRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile, ct)) throw new AccessFault(404, "resource_not_found");
        var rows = await db.Set<SourceRow>().Where(x => x.TeamId == actor.TeamId && x.ProfileId == profile).OrderBy(x => x.SourceKey).Take(201).ToArrayAsync(ct);
        if (rows.Length > 200) throw new AccessFault(422, "profile_source_limit");
        return rows.Select(x => new { x.SourceKey, configuration = JsonDocument.Parse(x.Configuration).RootElement.Clone() }).ToArray();
    }
    public async Task<object> Evidence(Actor actor, Guid session, Guid id, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection); db.Database.SetCommandTimeout(5);
        await InvestigationQueries.RequireMonitoring(db, actor, session, ct);
        var evidence = await db.Set<EvidenceRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Id == id, ct) ?? throw new AccessFault(404, "resource_not_found");
        var receipt = await db.Set<ReceiptRow>().SingleAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.EvidenceId == id, ct);
        var normalized = await db.Set<NormalizedRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.ReceiptId == receipt.Id, ct);
        var commit = await db.Set<ObservationRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.ReceiptId == receipt.Id, ct);
        var links = await db.Set<RunEvidenceRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.ReceiptId == receipt.Id).OrderBy(x => x.RunId).Take(201).Select(x => x.RunId).ToArrayAsync(ct);
        return new { evidence = new EvidenceDto(session, id, receipt.ApplicationId, evidence.ProfileId, receipt.Version, evidence.Raw,
            normalized?.SourceTimestamp, normalized?.EventAt, normalized?.Quality, receipt.ProcessedAt, commit?.DatabaseCommitAt, commit?.AcknowledgedAt, links.Take(200).ToArray()),
            linksTruncated = links.Length > 200, interpretation = JsonDocument.Parse(receipt.Trace).RootElement.Clone() };
    }
    public async Task<object> IncidentEvidence(Actor actor, Guid session, string id, int? size, string? cursor, CancellationToken ct, string? order = null)
    {
        if (order is not (null or "processed")) throw new AccessFault(422, "invalid_evidence_order");
        var take = CursorCodec.Size(size);
        object filter = order is null ? new { resource = "incident-evidence", session, id, take } : new { resource = "incident-evidence", session, id, take, order };
        var after = cursors.Decode(cursor, actor.TeamId, filter);
        await using var db = SondaDbContext.Open(connection); db.Database.SetCommandTimeout(5); await InvestigationQueries.RequireMonitoring(db, actor, session, ct);
        if (!await db.Set<IncidentRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Id == id, ct)) throw new AccessFault(404, "resource_not_found");
        var occurrenceReceipts = from o in db.Set<OccurrenceRow>() join e in db.Set<OccurrenceEvidenceRow>() on new { o.TeamId, o.SessionId, Id = o.Id } equals new { e.TeamId, e.SessionId, Id = e.OccurrenceId }
                                 where o.TeamId == actor.TeamId && o.SessionId == session && o.IncidentId == id select e.ReceiptId;
        var recoveryReceipts = db.Set<RecoveryEvidenceRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.IncidentId == id).Select(x => x.ReceiptId);
        var query = db.Set<ReceiptRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.EvidenceId != null && (occurrenceReceipts.Contains(x.Id) || recoveryReceipts.Contains(x.Id)));
        if (after is not null)
        {
            var key = Guid.Parse(after.Key);
            if (order == "processed") query = query.Where(x => x.ProcessedAt > after.Timestamp || (x.ProcessedAt == after.Timestamp && x.Id.CompareTo(key) > 0));
            else query = query.Where(x => x.Id.CompareTo(key) > 0);
        }
        var ordered = order == "processed" ? query.OrderBy(x => x.ProcessedAt).ThenBy(x => x.Id) : query.OrderBy(x => x.Id);
        var rows = await ordered.Take(take + 1).Select(x => new { receiptId = x.Id, evidenceId = x.EvidenceId, x.ProcessedAt, profileVersion = x.Version }).ToArrayAsync(ct);
        var items = rows.Take(take).ToArray(); return new { sessionId = session, items, hasMore = rows.Length > take, nextCursor = rows.Length > take ? cursors.Encode(actor.TeamId, filter, items[^1].receiptId.ToString(), order == "processed" ? items[^1].ProcessedAt : null) : null };
    }
    public async Task<object> Team(Actor actor, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        return await db.Set<TeamRow>().Where(x => x.TeamId == actor.TeamId).Select(x => new { id = x.TeamId, x.Name }).SingleAsync(ct);
    }
    public async Task<object> Profiles(Actor actor, int? size, string? cursor, CancellationToken ct)
    {
        var take = CursorCodec.Size(size); var filter = new { resource = "profiles", take }; var after = cursors.Decode(cursor, actor.TeamId, filter);
        await using var db = SondaDbContext.Open(connection); db.Database.SetCommandTimeout(5);
        var query = db.Set<ProfileRow>().Where(x => x.TeamId == actor.TeamId);
        if (after is not null) query = query.Where(x => string.Compare(x.ProfileId, after.Key) > 0);
        var rows = await query.OrderBy(x => x.ProfileId).Take(take + 1).Select(x => new { id = x.ProfileId, x.ApplicationId, draftRevision = actor.Role == "Admin" ? (long?)x.Revision : null }).ToArrayAsync(ct);
        var items = rows.Take(take).ToArray(); return new { items, hasMore = rows.Length > take, nextCursor = rows.Length > take ? cursors.Encode(actor.TeamId, filter, items[^1].id) : null, asOf = clock.GetUtcNow() };
    }
    public async Task<object> Versions(Actor actor, string profile, int? before, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        if (!await db.Set<ProfileRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile, ct)) throw new AccessFault(404, "resource_not_found");
        var rows = await db.Set<VersionRow>().Where(x => x.TeamId == actor.TeamId && x.ProfileId == profile && x.Sealed && (before == null || x.Version < before)).OrderByDescending(x => x.Version).Take(51)
            .Select(x => new { x.Version, x.Hash }).ToArrayAsync(ct);
        return new { items = rows.Take(50), hasMore = rows.Length > 50, nextBefore = rows.Length > 50 ? (int?)rows[49].Version : null };
    }
    public async Task<object> Version(Actor actor, string profile, int version, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        var row = await db.Set<VersionRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile && x.Version == version && x.Sealed, ct) ?? throw new AccessFault(404, "resource_not_found");
        return new { row.Version, row.Hash, configuration = JsonDocument.Parse(row.Snapshot).RootElement.Clone() };
    }
    public async Task<object> IncidentChildren(Actor actor, Guid session, string id, string resource, int? size, string? cursor, CancellationToken ct)
    {
        var take = CursorCodec.Size(size); var filter = new { resource, session, id, take }; var after = cursors.Decode(cursor, actor.TeamId, filter);
        await using var db = SondaDbContext.Open(connection); db.Database.SetCommandTimeout(5);
        await InvestigationQueries.RequireMonitoring(db, actor, session, ct);
        if (!await db.Set<IncidentRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Id == id, ct)) throw new AccessFault(404, "resource_not_found");
        List<(string Key, JsonElement Value)> values;
        if (resource == "occurrences")
        {
            var q = db.Set<OccurrenceRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.IncidentId == id);
            if (after is not null) q = q.Where(x => string.Compare(x.Id, after.Key) > 0);
            values = (await q.OrderBy(x => x.Id).Take(take + 1).Select(x => new { x.Id, x.Payload }).ToArrayAsync(ct)).Select(x => (x.Id, JsonDocument.Parse(x.Payload).RootElement.Clone())).ToList();
        }
        else if (resource == "recoveries")
        {
            var q = db.Set<RecoveryRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.IncidentId == id);
            if (after is not null) q = q.Where(x => string.Compare(x.RunId, after.Key) > 0);
            values = (await q.OrderBy(x => x.RunId).Take(take + 1).Select(x => new { x.RunId, x.Payload }).ToArrayAsync(ct)).Select(x => (x.RunId, JsonDocument.Parse(x.Payload).RootElement.Clone())).ToList();
        }
        else if (resource == "history")
        {
            var ordinal = after is null ? -1 : int.Parse(after.Key, System.Globalization.CultureInfo.InvariantCulture);
            values = (await db.Set<HistoryRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.IncidentId == id && x.Ordinal > ordinal).OrderBy(x => x.Ordinal).Take(take + 1)
                .Select(x => new { x.Ordinal, x.Payload }).ToArrayAsync(ct)).Select(x => (x.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonDocument.Parse(x.Payload).RootElement.Clone())).ToList();
        }
        else throw new AccessFault(404, "resource_not_found");
        var items = values.Take(take).ToArray(); return new { items = items.Select(x => x.Value), hasMore = values.Count > take, nextCursor = values.Count > take ? cursors.Encode(actor.TeamId, filter, items[^1].Key) : null };
    }
    public async Task<object> Operation(Actor actor, Guid id, CancellationToken ct)
    {
        var row = await access.Set<ApiOperation>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.Id == id && (x.AccountId == actor.AccountId || actor.Role == "Admin"), ct) ?? throw new AccessFault(404, "resource_not_found");
        await OperationReconciler.Reconcile(connection, access, row, ct);
        return new { row.Id, row.Action, row.Target, row.State, row.SubmittedAt, result = row.Result.Length == 0 ? (JsonElement?)null : JsonDocument.Parse(row.Result).RootElement.Clone() };
    }
    public async Task<object> Audit(Actor actor, int? size, string? cursor, CancellationToken ct)
    {
        var take = CursorCodec.Size(size); var filter = new { resource = "audit", take }; var after = cursors.Decode(cursor, actor.TeamId, filter);
        var query = access.Set<AccessAudit>().Where(x => x.TeamId == actor.TeamId);
        if (after is not null) { var id = Guid.Parse(after.Key); query = query.Where(x => x.RecordedAt > after.Timestamp || (x.RecordedAt == after.Timestamp && x.Id.CompareTo(id) > 0)); }
        var rows = await query.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).Take(take + 1).Select(x => new { x.Id, x.AccountId, x.OperationId, x.Action, x.Target, x.Disposition, x.AccessRevision, x.RecordedAt }).ToArrayAsync(ct);
        var items = rows.Take(take).ToArray(); return new { items, hasMore = rows.Length > take, nextCursor = rows.Length > take ? cursors.Encode(actor.TeamId, filter, items[^1].Id.ToString(), items[^1].RecordedAt) : null };
    }
    public async Task<object> Monitoring(Actor actor, string application, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection);
        var monitor = await db.Set<MonitoringSessionRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ApplicationId == application, ct) ?? throw new AccessFault(404, "resource_not_found");
        var profiles = await db.Set<LaneRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == monitor.SessionId).Select(x => x.ProfileId).Take(201).ToArrayAsync(ct);
        if (profiles.Length > 200) throw new AccessFault(422, "diagnostic_profile_limit");
        var store = new PostgresIngestionStore(connection); List<object> items = [];
        foreach (var profile in profiles)
        {
            var diagnostic = await store.DiagnoseAsync(new(actor.TeamId, monitor.SessionId, application), profile, clock.GetUtcNow(), ct);
            if (actor.Role == "Admin") items.Add(diagnostic);
            else items.Add(new { diagnostic.Profile, diagnostic.Availability, diagnostic.DeadlineStatus,
                sources = diagnostic.Sources.Select(x => new { x.Source, x.Enabled, x.Required, x.State }) });
        }
        return new { applicationId = application, asOf = clock.GetUtcNow(), profiles = items,
            hostAuthority = actor.Role == "Admin" ? monitor.HostAuthority : null,
            configuredSources = actor.Role == "Admin" ? await store.SourcesAsync(new(actor.TeamId, monitor.SessionId, application), ct) : null };
    }
}
