using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Application.Simulation;
using Sonda.Domain.Evidence;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;

namespace Sonda.Server.Profiles;

public sealed record DraftEdit(Guid OperationId, long? ExpectedRevision, Profile Configuration);
public sealed record PreviewRequest(Guid OperationId, long? ExpectedRevision, DateTimeOffset AsOf, DateOnly? SampleDate, SampleEntry[] Entries);
public sealed record PublishRequest(Guid OperationId, long? ExpectedRevision, Guid ReportId, bool AcknowledgeWarnings);

public sealed class ProfileService(string connection, AccessDbContext access, SimulationProcess simulation)
{
    public async Task<object> Draft(Actor actor, string id, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection); var row = await Profile(db, actor, id, ct);
        return new { row.ProfileId, row.Revision, configuration = JsonDocument.Parse(row.Draft).RootElement.Clone() };
    }
    public async Task<object> Edit(Actor actor, string id, DraftEdit request, CancellationToken ct)
    {
        var expected = request.ExpectedRevision ?? throw new AccessFault(428, "expected_revision_required");
        ValidateShape(request.Configuration);
        await using var db = SondaDbContext.Open(connection); var row = await Profile(db, actor, id, ct);
        // Explicitly reject conflicting identity; server owns team/application/Profile binding.
        if (request.Configuration.TeamId != actor.TeamId || request.Configuration.ApplicationId != row.ApplicationId || request.Configuration.Id != id) throw new AccessFault(422, "profile_identity_mismatch");
        var hash = SimulationJson.Hash(new { action = "Draft", id, request });
        await Intent(actor, request.OperationId, "EditDraft", id, hash, ct,
            SimulationJson.Hash(new { profile = request.Configuration, expectedRevision = expected, kind = "Draft" }));
        var revision = await new PostgresConfigurationStore(connection).EditDraftAsync(request.Configuration, expected, request.OperationId, ct);
        await Complete(actor, request.OperationId, new { revision }, ct); return new { revision };
    }
    public async Task<object> Validate(Actor actor, string id, CancellationToken ct)
    {
        await using var db = SondaDbContext.Open(connection); var row = await Profile(db, actor, id, ct);
        return new { row.Revision, profileHash = SimulationJson.Hash(SimulationJson.Deserialize<Profile>(row.Draft)), diagnostics = ProfileValidator.Validate(SimulationJson.Deserialize<Profile>(row.Draft)) };
    }
    public async Task<object> Preview(Actor actor, string id, PreviewRequest input, CancellationToken ct)
    {
        if (input.ExpectedRevision is null) throw new AccessFault(428, "expected_revision_required");
        if (input.Entries is null || input.Entries.Any(x => x is null || x.Raw is null)) throw new AccessFault(422, "invalid_sample");
        var hash = SimulationJson.Hash(new { action = "Preview", id, input });
        var prior = await access.Set<ApiOperation>().FindAsync([actor.TeamId, input.OperationId], ct);
        if (prior is not null)
        {
            await Intent(actor, input.OperationId, "SimulateProfile", id, hash, ct);
            if (prior.State == "Committed") return JsonDocument.Parse(prior.Result).RootElement.Clone();
            var saved = await access.Set<ProfilePreview>().FindAsync([actor.TeamId, input.OperationId], ct);
            if (saved is not null)
            {
                var recovered = new { reportId = saved.Id, draftRevision = saved.DraftRevision, report = JsonDocument.Parse(saved.Report).RootElement.Clone() };
                await Complete(actor, input.OperationId, recovered, ct); return recovered;
            }
        }
        await using var db = SondaDbContext.Open(connection); var row = await Profile(db, actor, id, ct);
        if (row.Revision != input.ExpectedRevision) throw new AccessFault(409, "draft_revision_conflict");
        if (input.Entries.Any(x => x.ProfileId != id)) throw new AccessFault(422, "sample_profile_mismatch");
        var profile = SimulationJson.Deserialize<Profile>(row.Draft);
        var request = new SimulationRequest { Seed = "preview-" + input.OperationId.ToString("N"), ServerTimeZoneId = TimeZoneInfo.Local.Id,
            AsOf = input.AsOf, SampleDate = input.SampleDate, Profiles = [profile], Entries = input.Entries.ToList() };
        var operation = await Intent(actor, input.OperationId, "SimulateProfile", id, hash, ct);
        if (operation.State == "Committed") return JsonDocument.Parse(operation.Result).RootElement.Clone();
        var report = await simulation.Run(request, ct);
        // Deterministic report ID allows recovery after report commit but before access outcome update.
        await using var tx = await access.Database.BeginTransactionAsync(ct);
        await access.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.TeamId},0))", ct);
        await AccessAuthorization.RequireAsync(access, actor, true, ct);
        var existing = await access.Set<ProfilePreview>().FindAsync([actor.TeamId, input.OperationId], ct);
        if (existing is null)
        {
            access.Add(new ProfilePreview { TeamId = actor.TeamId, Id = input.OperationId, ProfileId = id, DraftRevision = row.Revision,
                AccountId = actor.AccountId, ProfileHash = SimulationJson.Hash(profile), RequestHash = SimulationJson.Hash(request),
                Request = SimulationJson.Serialize(request), Report = report, CreatedAt = DateTimeOffset.UtcNow });
            await access.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        var result = new { reportId = input.OperationId, draftRevision = row.Revision, report = JsonDocument.Parse(existing?.Report ?? report).RootElement.Clone() };
        await Complete(actor, input.OperationId, result, ct); return result;
    }
    public async Task<object> Report(Actor actor, string id, Guid reportId, CancellationToken ct)
    {
        var row = await access.Set<ProfilePreview>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ProfileId == id && x.Id == reportId, ct) ?? throw new AccessFault(404, "resource_not_found");
        return new { row.Id, row.DraftRevision, row.ProfileHash, row.RequestHash, row.CreatedAt, report = JsonDocument.Parse(row.Report).RootElement.Clone() };
    }
    public async Task<object> Publish(Actor actor, string id, PublishRequest input, CancellationToken ct)
    {
        var expected = input.ExpectedRevision ?? throw new AccessFault(428, "expected_revision_required");
        var preview = await access.Set<ProfilePreview>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ProfileId == id && x.Id == input.ReportId, ct) ?? throw new AccessFault(404, "resource_not_found");
        using var report = JsonDocument.Parse(preview.Report);
        if (!report.RootElement.GetProperty("complete").GetBoolean()) throw new AccessFault(422, "complete_simulation_required");
        var warning = report.RootElement.GetProperty("diagnostics").EnumerateArray().Any(x => x.GetProperty("level").GetString() == "Warning");
        if (warning && !input.AcknowledgeWarnings) throw new AccessFault(422, "warning_acknowledgment_required");
        if (preview.DraftRevision != input.ExpectedRevision) throw new AccessFault(409, "simulation_revision_mismatch");
        var request = JsonSerializer.Deserialize<SimulationRequest>(preview.Request, SimulationJson.Options)!;
        var hash = SimulationJson.Hash(new { action = "Publish", id, input });
        await Intent(actor, input.OperationId, "PublishProfile", id, hash, ct,
            SimulationJson.Hash(new { request, expectedRevision = expected, kind = "Publish" }));
        await simulation.Publish(() => new PostgresConfigurationStore(connection).PublishAsync(request, expected, input.OperationId, ct), ct);
        var result = new { version = request.Profiles.Single().Version, state = "Published" };
        await Complete(actor, input.OperationId, result, ct); return result;
    }
    private static async Task<ProfileRow> Profile(SondaDbContext db, Actor actor, string id, CancellationToken ct) =>
        await db.Set<ProfileRow>().SingleOrDefaultAsync(x => x.TeamId == actor.TeamId && x.ProfileId == id, ct) ?? throw new AccessFault(404, "resource_not_found");
    private static void ValidateShape(Profile? profile)
    {
        if (profile is null || profile.Parsing is null || profile.Rules is null || profile.CycleTiming is null || profile.OrderTiming is null ||
            profile.Rules.Any(x => x is null || x.Alternatives is null || x.Alternatives.Any(p => p is null || p.Expression is null)))
            throw new AccessFault(422, "invalid_profile_shape");
        if (profile.Parsing.EntryPattern is null || profile.Parsing.TimestampFormat is null || profile.Parsing.SourceTimeZoneId is null ||
            profile.Policy is { } policy && (policy.ApplicationWideRules is null || policy.LateObservationRules is null))
            throw new AccessFault(422, "invalid_profile_shape");
        if (profile.Rules.Count > 128 || profile.Rules.Sum(x => x.Alternatives.Count) > 512) throw new AccessFault(422, "profile_size_limit");
    }
    private async Task<ApiOperation> Intent(Actor actor, Guid id, string action, string target, string hash, CancellationToken ct, string coreFingerprint = "")
    {
        if (id == Guid.Empty) throw new AccessFault(422, "operation_id_required");
        await using var tx = await access.Database.BeginTransactionAsync(ct);
        await access.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.TeamId},0))", ct);
        await AccessAuthorization.RequireAsync(access, actor, true, ct);
        var prior = await access.Set<ApiOperation>().FindAsync([actor.TeamId, id], ct);
        if (prior is not null)
        {
            if (prior.AccountId != actor.AccountId || prior.Fingerprint != hash) throw new AccessFault(409, "operation_identity_conflict");
            return prior;
        }
        var row = new ApiOperation { TeamId = actor.TeamId, AccountId = actor.AccountId, Id = id, Action = action, Target = target, Fingerprint = hash, Envelope = coreFingerprint, SubmittedAt = DateTimeOffset.UtcNow };
        access.Add(row); access.Add(new AccessAudit { Id = Guid.NewGuid(), TeamId = actor.TeamId, AccountId = actor.AccountId, OperationId = id, Action = action, Target = target, Disposition = "Submitted", AccessRevision = actor.Revision, RecordedAt = DateTimeOffset.UtcNow });
        await access.SaveChangesAsync(ct); await tx.CommitAsync(ct); return row;
    }
    private async Task Complete(Actor actor, Guid id, object result, CancellationToken ct)
    {
        await using var tx = await access.Database.BeginTransactionAsync(ct);
        await access.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({actor.TeamId},0))", ct);
        var row = await access.Set<ApiOperation>().FindAsync([actor.TeamId, id], ct) ?? throw new InvalidOperationException();
        await access.Entry(row).ReloadAsync(ct);
        if (row.State == "Committed") return;
        row.State = "Committed"; row.Result = SimulationJson.Serialize(result);
        access.Add(new AccessAudit { Id = Guid.NewGuid(), TeamId = actor.TeamId, AccountId = actor.AccountId, OperationId = id, Action = row.Action, Target = row.Target, Disposition = "Committed", AccessRevision = actor.Revision, RecordedAt = DateTimeOffset.UtcNow });
        await access.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
