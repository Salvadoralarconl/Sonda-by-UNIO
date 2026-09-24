using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Availability;
namespace Sonda.Infrastructure.Persistence;
public sealed record SourceAcquisitionDiagnostic(string Source,bool Enabled,bool Required,string State,string? Reason,long CommittedBytes,long? BytesBehind);
public sealed record AcquisitionDiagnostic(string Profile,AvailabilityResult Availability,SourceAcquisitionDiagnostic[] Sources,bool PendingCommand,DateTimeOffset? LeaseExpiresAt,long FenceEpoch,string DeadlineStatus);
public sealed partial class PostgresIngestionStore
{
    public async Task<AcquisitionDiagnostic> DiagnoseAsync(ProcessingScope scope,string profile,DateTimeOffset asOf,CancellationToken ct=default)
    {
        await using var db=SondaDbContext.Open(connection);var configurations=(await SourcesAsync(scope,ct)).Where(s=>s.ProfileId==profile).ToArray();List<SourceAcquisitionDiagnostic> sources=[];
        foreach(var config in configurations)
        {
            var source=(await db.Set<AcquisitionSourceRow>().FindAsync([scope.TeamId,profile,config.SourceKey],ct))!;
            var ranges=await (from g in db.Set<FileGenerationRow>() join c in db.Set<FileCheckpointRow>() on new{g.TeamId,Id=g.Id} equals new{c.TeamId,Id=c.GenerationId} where g.TeamId==scope.TeamId&&g.ProfileId==profile&&g.SourceKey==config.SourceKey select new{c.Offset,g.ObservedLength}).ToArrayAsync(ct);
            var uncertain=source.State is "Unavailable" or "IdentityUncertain" or "Gap" or "Blocked" or "Disabled";
            sources.Add(new(config.SourceKey,source.Enabled,config.Required,source.State,source.Error,ranges.Sum(r=>r.Offset),uncertain?null:ranges.Sum(r=>Math.Max(0,r.ObservedLength-r.Offset))));
        }
        var health=await new PostgresProcessingStore(connection).ReadHealthAsync(scope,ct);var lane=(await db.Set<LaneRow>().FindAsync([scope.TeamId,scope.SessionId,profile],ct))!;var version=(await db.Set<VersionRow>().FindAsync([scope.TeamId,profile,lane.Version],ct))!;
        var policy=SimulationJson.Deserialize<Sonda.Domain.Profiles.Profile>(version.Snapshot).Policy;AvailabilityResult availability;
        if(policy is not null)
        {
            var state=await new PostgresPolicyStore(connection).ReadAsync(scope,profile,ct);var last=state.Interpreter.Runs.Where(r=>r.CompletedProcessedAt<=asOf).Select(r=>r.CompletedProcessedAt).Max();
            availability=AvailabilityProjection.Calculate(health,configurations.Where(s=>s.Required).Select(s=>s.SourceKey).ToArray(),state.Observations,policy,last,asOf);
            if(sources.Any(s=>s.Required&&(!s.Enabled||s.State is "Gap" or "IdentityUncertain" or "Blocked")))availability=availability with{Availability=AvailabilityState.Unknown,Reason="Required acquisition source is disabled, blocked or uncertain."};
        }
        else availability=new(health,AvailabilityState.Unknown,null,null,asOf,"Legacy profile has no configured availability policy.");
        var owner=await db.Set<IngestionOwnerRow>().FindAsync([scope.TeamId,scope.ApplicationId],ct);var pending=await db.Set<PendingIngestionRow>().FindAsync([scope.TeamId,scope.ApplicationId],ct);
        return new(profile,availability,sources.ToArray(),pending is{Command.Length:>0},owner?.ExpiresAt,owner?.Epoch??0,configurations.Any(s=>s.Completeness==Sonda.Application.Acquisition.CompletenessMode.None)?"Deferred: completeness unproven":"Requires validated durable producer certificate");
    }
}
