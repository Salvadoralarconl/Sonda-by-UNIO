using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Api.Contracts;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Server.Queries;

// Read-only presentation of existing facts. No interpretation, episode or workflow decisions.
public static class IncidentPresentation
{
    public static async Task<IncidentPresentationDto> Read(SondaDbContext db, Actor actor, Guid session, string id, CancellationToken ct)
    {
        await InvestigationQueries.RequireMonitoring(db, actor, session, ct);
        var seed = await (from i in db.Set<IncidentRow>()
                          join r in db.Set<ReceiptRow>() on new { i.TeamId, i.SessionId, Id = i.CreatedReceipt } equals new { r.TeamId, r.SessionId, r.Id }
                          join a in db.Set<ApplicationRow>() on new { r.TeamId, r.ApplicationId } equals new { a.TeamId, a.ApplicationId }
                          where i.TeamId == actor.TeamId && i.SessionId == session && i.Id == id
                          select new { r.ApplicationId, a.Name, r.EvidenceId, r.ProfileId, r.Version, r.ProcessedAt, r.Id }).SingleOrDefaultAsync(ct)
                          ?? throw new AccessFault(404, "resource_not_found");
        var raw = seed.EvidenceId is null ? null : await db.Set<EvidenceRow>()
            .Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.Id == seed.EvidenceId)
            .Select(x => x.Raw.Substring(0, Math.Min(x.Raw.Length, 4001))).SingleOrDefaultAsync(ct);
        var occurrenceReceipts = db.Set<OccurrenceRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.IncidentId == id).Select(x => x.UpdatedReceipt);
        var recoveryReceipts = db.Set<RecoveryRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.IncidentId == id).Select(x => x.ReceiptId);
        var historyReceipts = db.Set<HistoryRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session && x.IncidentId == id).Select(x => x.Origin);
        var updated = await db.Set<ReceiptRow>().Where(x => x.TeamId == actor.TeamId && x.SessionId == session &&
            (x.Id == seed.Id || occurrenceReceipts.Contains(x.Id) || recoveryReceipts.Contains(x.Id) || historyReceipts.Contains(x.Id)))
            .MaxAsync(x => x.ProcessedAt, ct);
        return new(session, id, seed.ApplicationId, seed.Name,
            raw is null ? "No raw evidence for this processing event." : raw[..Math.Min(raw.Length, 4000)],
            raw?.Length > 4000, raw is null ? "NoRawEvidence" : "IncidentCreationEvidence",
            seed.ProcessedAt, updated, seed.EvidenceId, seed.ProfileId, seed.Version);
    }
}
