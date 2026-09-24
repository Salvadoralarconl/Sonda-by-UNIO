import { useEffect, useState } from 'react';
import { RunModal } from '../runs/RunModal';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, command, submit, type PendingCommand } from '../../api/client';
import type { Evidence, Incident, Page, Run } from '../../api/types';
import { Modal } from '../../components/Modal';

// Retain uncertain intents across modal close/reopen; never persist log payloads in browser storage.
const uncertain = new Map<string, PendingCommand<object>>();
window.addEventListener('sonda:session-expired', () => uncertain.clear());

type Presentation = { applicationName: string; summary: string; summaryTruncated: boolean; firstDetectedProcessedAt: string; lastUpdatedProcessedAt: string; profileVersion: number };
type Occurrence = { id: string; runId: string; applicationRunId: string; result: string | null; severity: string; detectedAt: string };
type Recovery = { runId: string; applicationRunId: string; recoveredAt: string; method: string };
type History = { from: string | null; to: string; at: string; reason: string; actor?: string };
type EvidenceReference = { evidenceId: string; processedAt: string; profileVersion: number };
type OperationResult = { disposition?: string; diagnostics?: unknown[] };

function Fields({ values }: { values: Record<string, string | number | null | undefined> }) {
  return <dl>{Object.entries(values).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value ?? 'Not available'}</dd></div>)}</dl>;
}
function Failure({ error }: { error: unknown }) {
  return <p role="alert">{error instanceof ApiError ? `Unable to load: ${error.code}` : 'Temporarily unavailable. Please retry.'}</p>;
}

function Paged<T>({ identity, path, children }: { identity: string; path: string; children: (item: T, index: number) => React.ReactNode }) {
  const [cursors, setCursors] = useState<(string | null)[]>([null]);
  const cursor = cursors.at(-1);
  const query = useQuery({ queryKey: [identity, path, cursor], queryFn: ({ signal }) => api<Page<T>>(`${path}&size=20${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`, { signal }) });
  if (query.isPending) return <p role="status">Loading…</p>;
  if (query.isError) return <Failure error={query.error} />;
  return <>{query.data.items.length ? query.data.items.map(children) : <p>No records.</p>}
    <nav aria-label="Detail pages"><button disabled={cursors.length === 1} onClick={() => setCursors(cursors.slice(0, -1))}>Previous</button>
    <button disabled={!query.data.hasMore || !query.data.nextCursor} onClick={() => setCursors([...cursors, query.data.nextCursor])}>Next</button></nav></>;
}

function RunDetail({ identity, session, occurrence }: { identity: string; session: string; occurrence: Occurrence }) {
  const [selected, setSelected] = useState<string|null>(null);
  const scope = occurrence.runId === occurrence.applicationRunId ? 'application-runs' : 'order-runs';
  const query = useQuery({ queryKey: [identity, scope, session, occurrence.runId], enabled: !!occurrence.runId,
    queryFn: ({ signal }) => api<Run>(`/${scope}/${encodeURIComponent(occurrence.runId)}?session=${session}`, { signal }) });
  if (!occurrence.runId) return <p>This diagnostic is not associated with a run.</p>;
  if (query.isPending) return <p>Loading run…</p>;
  if (query.isError) return <Failure error={query.error} />;
  const run = query.data;
  return <><article><button onClick={() => setSelected(run.id)}>Open run details</button>{run.parentId && <button onClick={() => setSelected(run.parentId!)}>Open parent Application Run</button>}{run.previousAttemptId && <button onClick={() => setSelected(run.previousAttemptId!)}>Open previous attempt</button>}<Fields values={{ 'Parent Application Run': run.parentId ?? occurrence.applicationRunId,
    'Run': run.id, 'Scope': run.scope, 'Identifier': run.identifier, 'Attempt': run.attempt,
    'Detection result': run.result ?? 'In progress', 'Previous attempt': run.previousAttemptId,
    'Profile Version': run.profileVersion, 'Started event time': run.startedEventAt,
    'Started processing time': run.startedProcessedAt, 'Completion event time': run.completedEventAt,
    'Completion processing time': run.completedProcessedAt }} /></article>{selected && <RunModal sessionId={session} runId={selected} onClose={() => setSelected(null)}/>}</>;
}

function EvidenceDetail({ identity, session, reference }: { identity: string; session: string; reference: EvidenceReference }) {
  const [selected, setSelected] = useState<string|null>(null);
  const query = useQuery({ queryKey: [identity, 'evidence', session, reference.evidenceId], queryFn: ({ signal }) =>
    api<{ evidence: Evidence; linksTruncated: boolean }>(`/evidence/${reference.evidenceId}?session=${session}`, { signal }) });
  if (query.isPending) return <p>Loading evidence…</p>;
  if (query.isError) return <Failure error={query.error} />;
  const evidence = query.data.evidence;
  return <><article><Fields values={{ 'Evidence ID': evidence.id, 'Source timestamp': evidence.sourceTimestamp,
    'Normalized event time': evidence.normalizedEventAt, 'Processed': evidence.processedAt,
    'Database commit': evidence.databaseCommitAt, 'Acknowledged': evidence.acknowledgedAt,
    'Profile Version': evidence.profileVersion }} /><pre>{evidence.raw}</pre>
    <p>Persisted run groups: {evidence.runIds.length ? evidence.runIds.map(id => <button key={id} onClick={() => setSelected(id)}>{id}</button>) : 'Unassigned evidence'}</p>{query.data.linksTruncated && <p>Run links truncated; this list is incomplete.</p>}</article>{selected && <RunModal sessionId={session} runId={selected} onClose={() => setSelected(null)}/>}</>;
}

export function IncidentModal({ identity, session, incidentId, onClose }: { identity: string; session: string; incidentId: string; onClose: () => void }) {
  const [section, setSection] = useState('Overview');
  const [reason, setReason] = useState('');
  const intentKey = JSON.stringify([identity, session, incidentId]);
  const [pending, updatePending] = useState<PendingCommand<object> | null>(() => uncertain.get(intentKey) ?? null);
  const setPending = (intent: PendingCommand<object> | null) => { if(intent) uncertain.set(intentKey, intent); else uncertain.delete(intentKey); updatePending(intent); };
  useEffect(() => { if(!pending) return; const warn = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue=''; }; window.addEventListener('beforeunload', warn); return () => window.removeEventListener('beforeunload', warn); }, [pending]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const cache = useQueryClient();
  const base = `/incidents/${encodeURIComponent(incidentId)}`;
  const suffix = `?session=${encodeURIComponent(session)}`;
  const incident = useQuery({ queryKey: [identity, base, session], queryFn: ({ signal }) => api<Incident>(base + suffix, { signal }) });
  const display = useQuery({ queryKey: [identity, base, session, 'presentation'], queryFn: ({ signal }) => api<Presentation>(base + '/presentation' + suffix, { signal }) });
  async function send(intent: PendingCommand<object>) {
    setBusy(true); setPending(intent); setMessage('Submitting…');
    try {
      const result = await submit<OperationResult>(intent);
      setMessage(result.disposition === 'Applied' ? 'Status updated.' : `Server returned ${result.disposition ?? 'a result'}. Refresh before another action.`);
      setPending(null); await cache.invalidateQueries({ predicate: query => query.queryKey[0] === identity });
    } catch (error) {
      if (error instanceof ApiError && error.status < 500) {
        setPending(null); setMessage(error.status === 409 ? 'The Incident changed. Refresh, review it and choose an action again.' : error.code);
      } else setMessage('Outcome unknown. Check the operation before retrying; the original operation ID is retained.');
    } finally { setBusy(false); }
  }
  async function reconcile() {
    if (!pending) return;
    setBusy(true);
    try {
      const result = await api<{ state: string }>(`/operations/${pending.operationId}`);
      setMessage(`Operation ${result.state}.`);
      if (result.state === 'Committed' || result.state === 'Rejected') { setPending(null); await cache.invalidateQueries({ predicate: query => query.queryKey[0] === identity }); }
    } catch (error) { setMessage(error instanceof ApiError && error.status === 404 ? 'No receipt found yet. You may retry the same request.' : 'Unable to check the operation yet.'); }
    finally { setBusy(false); }
  }
  return <Modal title="Incident information" onClose={onClose}><div className="modal-layout">
    <nav className="modal-sections" aria-label="Incident sections">{['Overview', 'Order / Run', 'Evidence', 'History'].map(name =>
      <button key={name} aria-current={section === name} onClick={() => setSection(name)}>{name}</button>)}</nav>
    <section className="modal-content"><h2>{section}</h2>
      {incident.isPending ? <p role="status">Loading…</p> : incident.isError ? <Failure error={incident.error} /> : <>
        {section === 'Overview' && <>
          <Fields values={{ 'Severity': incident.data.severity, 'Incident status': incident.data.status, 'Problem Identity': incident.data.problemIdentity }} />
          {display.isPending ? <p>Loading summary…</p> : display.isError ? <Failure error={display.error} /> : <Fields values={{
            'Application': display.data.applicationName, 'Message / summary': display.data.summary,
            'First detected (processing time)': display.data.firstDetectedProcessedAt,
            'Last updated (processing time)': display.data.lastUpdatedProcessedAt,
            'Creation Profile Version': display.data.profileVersion }} />}
          {display.data?.summaryTruncated && <p>Summary truncated. Open Evidence for the complete stored entry.</p>}
          <Paged<Occurrence> key="overview-occurrences" identity={identity} path={base + '/occurrences' + suffix}>{o => <RunDetail key={o.id} identity={identity} session={session} occurrence={o} />}</Paged>
        </>}
        {section === 'Order / Run' && <>
          <Paged<Occurrence> key="occurrences" identity={identity} path={base + '/occurrences' + suffix}>{o => <RunDetail key={o.id} identity={identity} session={session} occurrence={o} />}</Paged>
          <h3>Recovery</h3><Paged<Recovery> identity={identity} path={base + '/recoveries' + suffix}>{(r, i) => <Fields key={i} values={{ 'Run': r.runId, 'Application Run': r.applicationRunId, 'Recovered at': r.recoveredAt, 'Method': r.method }} />}</Paged>
        </>}
        {section === 'Evidence' && <><p>Related evidence, oldest processing time first. Run references identify the persisted grouping.</p>
          <Paged<EvidenceReference> identity={identity} path={base + '/evidence' + suffix + '&order=processed'}>{e => <EvidenceDetail key={e.evidenceId} identity={identity} session={session} reference={e} />}</Paged></>}
        {section === 'History' && <><Paged<History> identity={identity} path={base + '/history' + suffix}>{(h, i) => <Fields key={i} values={{ 'From': h.from, 'To': h.to, 'Time': h.at, 'Reason': h.reason, 'Actor': h.actor }} />}</Paged>
          <h3>Automatic recovery</h3><Paged<Recovery> identity={identity} path={base + '/recoveries' + suffix}>{(r, i) => <Fields key={i} values={{ 'Run': r.runId, 'Recovered at': r.recoveredAt, 'Method': r.method }} />}</Paged></>}
        {!!incident.data.allowedActions.length && <fieldset disabled={busy || !!pending}><legend>Incident workflow</legend>
          <label>Reason<textarea value={reason} maxLength={1000} onChange={e => setReason(e.target.value)} /></label>
          {incident.data.allowedActions.map(action => <button key={action} disabled={!reason.trim()} onClick={() => void send(command(base + '/status', { sessionId: session, expectedRevision: incident.data.revision, status: action, reason }))}>{action}</button>)}
        </fieldset>}
        <p role="status">{message}</p>{pending && <><p>Operation ID: {pending.operationId}</p><button disabled={busy} onClick={() => void reconcile()}>Check operation</button><button disabled={busy} onClick={() => void send(pending)}>Retry same request</button></>}
        <button disabled={busy || !!pending} onClick={() => void cache.invalidateQueries({ predicate: query => query.queryKey[0] === identity })}>Refresh Incident</button>
      </>}
    </section></div></Modal>;
}

