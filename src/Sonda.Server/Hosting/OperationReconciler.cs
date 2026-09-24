using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Server.Hosting;

/// <summary>Projects already committed receipts. Never dispatches commands or advances input.</summary>
public static class OperationReconciler
{
    public static async Task Reconcile(string connection, AccessDbContext access, ApiOperation operation, CancellationToken ct)
    {
        if (operation.State is "Committed" or "Rejected" || operation.Envelope.Length == 0) return;
        await using var core = SondaDbContext.Open(connection);
        object? result = null; var rejected = false;
        if (operation.Action is "EditDraft" or "PublishProfile")
        {
            var receipt = await core.Set<CommandRow>().FindAsync([operation.TeamId, operation.Id], ct);
            if (receipt?.Fingerprint != operation.Envelope) return;
            var revision = long.Parse(receipt.Result, System.Globalization.CultureInfo.InvariantCulture);
            result = operation.Action == "EditDraft" ? new { revision } : new { version = revision, state = "Published" };
        }
        else if (operation.Action == "ConfigureAcquisitionSource")
        {
            var request = SimulationJson.Deserialize<SourceCommandEnvelope>(operation.Envelope);
            var profile = await core.Set<ProfileRow>().FindAsync([operation.TeamId, request.ProfileId], ct);
            if (profile is null) return;
            var monitor = await core.Set<MonitoringSessionRow>().FindAsync([operation.TeamId, profile.ApplicationId], ct);
            if (monitor is null) return;
            var receipt = await core.Set<CommandRow>().FindAsync([operation.TeamId, operation.Id], ct);
            var expected = PostgresIngestionStore.SourceCommandFingerprint(new(operation.TeamId, monitor.SessionId, profile.ApplicationId), request.Configuration, request.ExpectedRevision);
            if (receipt?.Fingerprint != expected) return;
            result = new { id = request.SourceId, revision = request.Configuration.Revision, state = request.Configuration.Enabled ? "Enabled" : "Disabled" };
        }
        else if (operation.Action is "IncidentStatus" or "ActivateProfile")
        {
            var receipts = await core.Set<ReceiptRow>().Where(x => x.TeamId == operation.TeamId && x.RequestId == operation.Id && x.Kind == (operation.Action == "IncidentStatus" ? "PolicyChangeStatus" : "PolicyActivateVersion")).Take(2).ToArrayAsync(ct);
            if (receipts.Length != 1) return;
            var row = receipts[0]; var receipt = SimulationJson.Deserialize<PolicyReceipt>(row.Trace);
            if (receipt.Command.Actor != operation.AccountId.ToString()) return;
            if (operation.Action == "IncidentStatus")
            {
                var request = SimulationJson.Deserialize<StatusRequest>(operation.Envelope);
                if (row.SessionId != request.SessionId || operation.Target != request.SessionId + "/" + receipt.Command.IncidentId || request.ExpectedRevision != receipt.Command.ExpectedRevision || request.Status != receipt.Command.TargetStatus || request.Reason != receipt.Command.Reason) return;
            }
            else
            {
                var request = SimulationJson.Deserialize<ActivationRequest>(operation.Envelope);
                if (row.ProfileId != operation.Target || request.Version != receipt.Command.Profile?.Version || request.ExpectedRevision != receipt.Command.ExpectedRevision) return;
            }
            result = new { receiptId = receipt.Id, receipt.Disposition, receipt.Diagnostics, processedAt = receipt.Command.ProcessedAt };
            rejected = receipt.Disposition == "Rejected";
        }
        if (result is null) return;
        var revisionAtAdmission = await access.Set<AccessAudit>().Where(x => x.TeamId == operation.TeamId && x.OperationId == operation.Id && x.Disposition == "Submitted").Select(x => x.AccessRevision).SingleAsync(ct);
        await new OperationLedger(access).Finish(new(operation.AccountId, operation.TeamId, "", revisionAtAdmission), operation.Id, result, rejected, ct, reconciled: true);
    }
}
