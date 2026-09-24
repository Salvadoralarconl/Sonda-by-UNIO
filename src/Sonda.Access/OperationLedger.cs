using Microsoft.EntityFrameworkCore;
using Sonda.Application.Simulation;

namespace Sonda.Access;

public sealed class OperationLedger(AccessDbContext db)
{
    public async Task<ApiOperation> Begin(Actor actor, Guid id, string action, string target, object payload, bool admin, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new AccessFault(422, "operation_id_required");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.TeamId},0))", ct);
        await AccessAuthorization.RequireAsync(db, actor, admin, ct);
        var hash = SimulationJson.Hash(new { actor.AccountId, action, target, payload });
        var row = await db.Set<ApiOperation>().FindAsync([actor.TeamId, id], ct);
        if (row is not null)
        {
            if (row.AccountId != actor.AccountId || row.Fingerprint != hash) throw new AccessFault(409, "operation_identity_conflict");
            return row;
        }
        row = new ApiOperation { TeamId = actor.TeamId, AccountId = actor.AccountId, Id = id, Action = action, Target = target, Fingerprint = hash, Envelope = SimulationJson.Serialize(payload), SubmittedAt = DateTimeOffset.UtcNow };
        db.Add(row); Audit(actor, row, "Submitted"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return row;
    }
    public async Task Finish(Actor actor, Guid id, object result, bool rejected, CancellationToken ct, bool reconciled = false)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.TeamId},0))", ct);
        var row = await db.Set<ApiOperation>().FindAsync([actor.TeamId, id], ct) ?? throw new InvalidOperationException("Durable intent required.");
        await db.Entry(row).ReloadAsync(ct);
        if (row.State is "Committed" or "Rejected") return;
        row.Result = SimulationJson.Serialize(result); row.State = rejected ? "Rejected" : "Committed";
        Audit(actor, row, reconciled ? "Reconciled" + row.State : row.State); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private void Audit(Actor actor, ApiOperation row, string state) => db.Add(new AccessAudit
    { Id = Guid.NewGuid(), TeamId = actor.TeamId, AccountId = actor.AccountId, OperationId = row.Id, Action = row.Action, Target = row.Target,
        Disposition = state, AccessRevision = actor.Revision, RecordedAt = DateTimeOffset.UtcNow });
}
