using Npgsql;
using NpgsqlTypes;
using Sonda.Access;
using Sonda.Api.Contracts;

namespace Sonda.Server.Queries;

public sealed class SearchQueries(string connection, CursorCodec cursors, TimeProvider clock)
{
    public async Task<Page<EvidenceDto>> Search(Actor actor, SearchFilter filter, int? size, string? cursor, CancellationToken ct)
    {
        filter = filter with { From = filter.From.ToUniversalTime(), To = filter.To.ToUniversalTime() };
        var take = CursorCodec.Size(size);
        if (filter.From >= filter.To || filter.To - filter.From > TimeSpan.FromDays(31) || filter.TimeBasis is not ("event" or "processed")) throw new AccessFault(422, "invalid_search_window");
        if (filter.Text is { Length: < 3 or > 256 }) throw new AccessFault(422, "invalid_search_text");
        if (filter.Identifier is not null && (filter.Identifier.Length > 256 || string.IsNullOrWhiteSpace(filter.ProfileId) || string.IsNullOrWhiteSpace(filter.IdentifierNamespace))) throw new AccessFault(422, "identifier_scope_required");
        if (filter.Result is not (null or "Success" or "Failure" or "Undefined") || filter.Classification is not (null or "Error" or "Warning" or "Ignore") || filter.IncidentStatus is not (null or "Active" or "Investigating" or "Resolved")) throw new AccessFault(422, "invalid_search_filter");
        var bound = new { resource = "evidence", filter, take }; var after = cursors.Decode(cursor, actor.TeamId, bound);
        await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync(ct);
        await using var command = new NpgsqlCommand(Sql, conn) { CommandTimeout = 5 };
        Text(command, "team", actor.TeamId); Text(command, "basis", filter.TimeBasis); Text(command, "app", filter.ApplicationId); Text(command, "profile", filter.ProfileId);
        Text(command, "text", filter.Text); Text(command, "identifier", filter.Identifier); Text(command, "namespace", filter.IdentifierNamespace);
        Text(command, "result", filter.Result); Text(command, "classification", filter.Classification); Text(command, "status", filter.IncidentStatus);
        command.Parameters.AddWithValue("case", filter.CaseSensitive);
        command.Parameters.AddWithValue("from", filter.From.ToUniversalTime()); command.Parameters.AddWithValue("to", filter.To.ToUniversalTime());
        command.Parameters.AddWithValue("take", take + 1);
        command.Parameters.Add(new NpgsqlParameter("after_time", NpgsqlDbType.TimestampTz) { Value = (object?)after?.Timestamp ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("after_session", NpgsqlDbType.Uuid) { Value = (object?)after?.Session ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("after_id", NpgsqlDbType.Uuid) { Value = after is null ? DBNull.Value : Guid.Parse(after.Key) });
        List<(EvidenceDto Evidence, DateTimeOffset Timestamp)> rows = [];
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            DateTimeOffset? OptionalTime(int i) => reader.IsDBNull(i) ? null : reader.GetFieldValue<DateTimeOffset>(i);
            rows.Add((new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), OptionalTime(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9),
                OptionalTime(10), OptionalTime(11), reader.GetFieldValue<string[]>(12).Take(200).ToArray(), reader.GetBoolean(14), reader.GetFieldValue<string[]>(12).Length > 200), reader.GetFieldValue<DateTimeOffset>(13)));
        }
        var more = rows.Count > take; var items = rows.Take(take).ToArray();
        return new(items.Select(x => x.Evidence).ToArray(), more, more ? cursors.Encode(actor.TeamId, bound, items[^1].Evidence.Id.ToString(), items[^1].Timestamp, items[^1].Evidence.SessionId) : null, clock.GetUtcNow());
    }
    private static void Text(NpgsqlCommand command, string key, string? value) => command.Parameters.Add(new NpgsqlParameter(key, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });
    // All dynamic values are parameters. Matching is literal strpos, never SQL LIKE/regex input.
    private const string Sql = """
        SELECT e.session_id,e.id,r.application_id,e.profile_id,r.version,left(e.raw,4000),n.source_timestamp,n.event_at,n.quality,r.processed_at,
               o.database_commit_at,o.acknowledged_at,
               ARRAY(SELECT re.run_id FROM sonda.run_evidence re WHERE re.team_id=e.team_id AND re.session_id=e.session_id AND re.receipt_id=r.id ORDER BY re.run_id LIMIT 201),
               CASE WHEN @basis='event' THEN n.event_at ELSE r.processed_at END AS stamp,length(e.raw)>4000
        FROM sonda.raw_evidence e
        JOIN sonda.processing_receipts r ON (r.team_id,r.session_id,r.evidence_id)=(e.team_id,e.session_id,e.id)
        JOIN sonda.monitoring_sessions m ON (m.team_id,m.session_id,m.application_id)=(r.team_id,r.session_id,r.application_id)
        LEFT JOIN sonda.normalized_evidence n ON (n.team_id,n.session_id,n.receipt_id)=(r.team_id,r.session_id,r.id)
        LEFT JOIN sonda.commit_observations o ON (o.team_id,o.session_id,o.receipt_id)=(r.team_id,r.session_id,r.id)
        WHERE e.team_id=@team
          AND (@app IS NULL OR r.application_id=@app) AND (@profile IS NULL OR e.profile_id=@profile)
          AND (CASE WHEN @basis='event' THEN n.event_at ELSE r.processed_at END)>=@from
          AND (CASE WHEN @basis='event' THEN n.event_at ELSE r.processed_at END)<@to
          AND (@text IS NULL OR CASE WHEN @case THEN strpos(COALESCE(n.payload::jsonb->>'message',e.raw),@text)>0 ELSE strpos(lower(COALESCE(n.payload::jsonb->>'message',e.raw)),lower(@text))>0 END)
          AND (@result IS NULL OR EXISTS(SELECT 1 FROM sonda.run_evidence re JOIN sonda.runs run ON (run.team_id,run.session_id,run.id)=(re.team_id,re.session_id,re.run_id) WHERE (re.team_id,re.session_id,re.receipt_id)=(r.team_id,r.session_id,r.id) AND run.result=@result))
          AND (@identifier IS NULL OR EXISTS(SELECT 1 FROM sonda.run_evidence re JOIN sonda.runs run ON (run.team_id,run.session_id,run.id)=(re.team_id,re.session_id,re.run_id) JOIN sonda.profile_versions v ON (v.team_id,v.profile_id,v.version)=(run.team_id,run.profile_id,run.version) WHERE (re.team_id,re.session_id,re.receipt_id)=(r.team_id,r.session_id,r.id) AND run.identifier=@identifier AND v.snapshot::jsonb->'identifier'->>'namespace'=@namespace))
          AND (@status IS NULL OR EXISTS(SELECT 1 FROM sonda.occurrence_evidence oe JOIN sonda.incident_occurrences occ ON (occ.team_id,occ.session_id,occ.id)=(oe.team_id,oe.session_id,oe.occurrence_id) JOIN sonda.incidents i ON (i.team_id,i.session_id,i.id)=(occ.team_id,occ.session_id,occ.incident_id) WHERE (oe.team_id,oe.session_id,oe.receipt_id)=(r.team_id,r.session_id,r.id) AND i.status=@status))
          AND (@classification IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(COALESCE(r.trace::jsonb->'interpretation'->'matches',r.trace::jsonb->'matches','[]'::jsonb)) match WHERE match->>'selection'='Winner' AND match->>'classification'=@classification)
               OR EXISTS(SELECT 1 FROM sonda.occurrence_evidence oe JOIN sonda.incident_occurrences occ ON (occ.team_id,occ.session_id,occ.id)=(oe.team_id,oe.session_id,oe.occurrence_id) JOIN sonda.incidents i ON (i.team_id,i.session_id,i.id)=(occ.team_id,occ.session_id,occ.incident_id) WHERE (oe.team_id,oe.session_id,oe.receipt_id)=(r.team_id,r.session_id,r.id) AND occ.payload::jsonb->>'severity'=@classification))
          AND (@after_time IS NULL OR (CASE WHEN @basis='event' THEN n.event_at ELSE r.processed_at END,e.session_id,e.id)>(@after_time,@after_session,@after_id))
        ORDER BY stamp,e.session_id,e.id LIMIT @take
        """;
}

