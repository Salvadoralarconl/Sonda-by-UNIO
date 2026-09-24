using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Incidents;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Server.Hosting;

public sealed record StatusRequest(Guid OperationId, Guid SessionId, long? ExpectedRevision, IncidentStatus Status, string Reason);
public sealed record ActivationRequest(Guid OperationId, int Version, long? ExpectedRevision, int? ExpectedVersion);
public sealed class WorkflowService(string connection, OperationLedger ledger, ApplicationAdmission admission, InitialActivationStore initial)
{
    public async Task<object> Status(Actor actor, string id, StatusRequest input, CancellationToken ct)
    {
        var expected = input.ExpectedRevision ?? throw new AccessFault(428, "expected_revision_required");
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000 || !Enum.IsDefined(input.Status)) throw new AccessFault(422, "invalid_status_request");
        await using var db = SondaDbContext.Open(connection);
        var incident = await db.Set<IncidentRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == input.SessionId && x.Id == id, ct) ?? throw new AccessFault(404, "resource_not_found");
        var monitor = await db.Set<MonitoringSessionRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == input.SessionId, ct) ?? throw new AccessFault(404, "resource_not_found");
        var op = await ledger.Begin(actor, input.OperationId, "IncidentStatus", input.SessionId + "/" + id, input, false, ct);
        if (op.State is "Committed" or "Rejected") return JsonDocument.Parse(op.Result).RootElement.Clone();
        var receipt = await admission.Submit(actor, monitor.ApplicationId, incident.ProfileId, input.OperationId,
            (sequence, now) => new PolicyCommand { Id = input.OperationId, Kind = PolicyCommandKind.ChangeStatus, Sequence = sequence,
                ProcessedAt = now, EffectiveAt = now, IncidentId = id, ExpectedRevision = expected, TargetStatus = input.Status,
                Actor = actor.AccountId.ToString(), Reason = input.Reason }, ct);
        var result = new { receiptId = receipt.Id, receipt.Disposition, receipt.Diagnostics, processedAt = receipt.Command.ProcessedAt };
        await ledger.Finish(actor, input.OperationId, result, receipt.Disposition == "Rejected", CancellationToken.None); return result;
    }
    public async Task<object> Activate(Actor actor, string profile, ActivationRequest input, CancellationToken ct)
    {
        var expected = input.ExpectedRevision ?? throw new AccessFault(428, "expected_revision_required");
        if (input.ExpectedVersion is null) throw new AccessFault(428, "expected_version_required");
        await using var db = SondaDbContext.Open(connection);
        var row = await db.Set<ProfileRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile, ct) ?? throw new AccessFault(404, "resource_not_found");
        var published = await db.Set<VersionRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ProfileId == profile && x.Version == input.Version && x.Sealed, ct) ?? throw new AccessFault(409, "published_version_required");
        var candidate = SimulationJson.Deserialize<Profile>(published.Snapshot);
        var op = await ledger.Begin(actor, input.OperationId, "ActivateProfile", profile, input, true, ct);
        if (op.State is "Committed" or "Rejected") return JsonDocument.Parse(op.Result).RootElement.Clone();
        var monitor = await db.Set<MonitoringSessionRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ApplicationId == row.ApplicationId, ct);
        var lane = monitor is null ? null : await db.Set<LaneRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.SessionId == monitor.SessionId && x.ProfileId == profile, ct);
        if (lane is null)
        {
            if (input.ExpectedVersion != 0 || input.ExpectedRevision != 0) throw new AccessFault(409, "initial_activation_revision_conflict");
            return monitor is null ? await initial.Activate(actor, profile, input.Version, input.OperationId, null, ct) :
                await admission.AttachProfile(actor, row.ApplicationId, fence => initial.Activate(actor, profile, input.Version, input.OperationId, fence, ct), ct);
        }
        // Lane.Revision advances for every input. Revision-2 activation uses the count of
        // successful activation commands, excluding the initial registration record.
        var activationRevision = await db.Set<ActivationRow>().LongCountAsync(x => x.TeamId == actor.TeamId && x.SessionId == monitor!.SessionId && x.ProfileId == profile, ct) - 1;
        if (lane.Version != input.ExpectedVersion || activationRevision != input.ExpectedRevision)
        {
            // A previously committed core receipt must still reconcile after an acknowledgment loss.
            if (!await db.Set<ReceiptRow>().AnyAsync(x => x.TeamId == actor.TeamId && x.SessionId == monitor!.SessionId && x.RequestId == input.OperationId, ct))
                throw new AccessFault(409, "activation_revision_conflict");
        }
        var receipt = await admission.Submit(actor, row.ApplicationId, profile, input.OperationId,
            (sequence, now) => new PolicyCommand { Id = input.OperationId, Kind = PolicyCommandKind.ActivateVersion, Sequence = sequence,
                ProcessedAt = now, EffectiveAt = now, ExpectedRevision = expected, Profile = candidate, Actor = actor.AccountId.ToString() }, ct);
        var result = new { receiptId = receipt.Id, receipt.Disposition, receipt.Diagnostics, processedAt = receipt.Command.ProcessedAt };
        await ledger.Finish(actor, input.OperationId, result, receipt.Disposition == "Rejected", CancellationToken.None); return result;
    }
}
