import { describe, expect, it } from 'vitest';
import { groupEvidencePage, orderResultLabel } from './grouping';
import { scopedKey, type Evidence, type Run } from '../../api/types';
const evidence = (id: string, runIds: string[], sessionId = 'session'): Evidence => ({ id, runIds, sessionId, applicationId: 'app', profileId: 'profile', profileVersion: 7, raw: 'Connecting to payer...', sourceTimestamp: null, normalizedEventAt: null, timestampQuality: null, processedAt: '2026-09-24T12:00:00Z', databaseCommitAt: null, acknowledgedAt: null, linksTruncated: false, rawTruncated: false });
const run = (id: string, attempt = 1): Run => ({ id, sessionId: 'session', profileId: 'profile', profileVersion: 7, scope: 'Order', parentId: 'cycle', identifier: '93822', attempt, result: attempt === 1 ? 'Failure' : 'Success', startedEventAt: '', startedProcessedAt: '', completedEventAt: null, completedProcessedAt: null, previousAttemptId: null, terminalReason: null });
describe('evidence-centric Order Run blocks', () => {
  it('renders shared evidence once with references to both attempts', () => {
    const first = run('a'), second = run('b', 2);
    const groups = groupEvidencePage([evidence('e', ['a', 'b']), evidence('f', ['b'])], new Map([[scopedKey('session', 'a'), first], [scopedKey('session', 'b'), second]]));
    expect(groups).toHaveLength(2);
    expect(groups.flatMap(g => g.rows).map(r => r.evidence.id)).toEqual(['e', 'f']);
    expect(groups[0].rows[0].relatedOrders.map(r => r.attempt)).toEqual([1, 2]);
    expect(groups[1].sharedReferences).toEqual([{ evidenceKey: scopedKey('session', 'e'), primaryGroupKey: scopedKey('session', 'a') }]);
  });
  it('preserves unassigned evidence without inventing a failed order', () => {
    const [group] = groupEvidencePage([evidence('e', [])], new Map());
    expect(group.kind).toBe('Unassigned'); expect(group.order).toBeUndefined();
  });
  it('does not label incomplete run lookups as unassigned', () => {
    expect(groupEvidencePage([evidence('e', ['not-loaded'])], new Map())[0].kind).toBe('Incomplete');
  });
  it('uses session-scoped evidence identity and prevents duplicate-looking rows', () => {
    const groups = groupEvidencePage([evidence('e', []), evidence('e', []), evidence('e', [], 'other')], new Map());
    expect(groups.flatMap(g => g.rows)).toHaveLength(2);
  });
  it('never substitutes Incident status for Order result or unfinished for Undefined', () => {
    expect(['Success', 'Failure', 'Undefined', null].map(r => orderResultLabel(r as Run['result']))).toEqual(['Successful Run', 'Failed Run', 'Undefined', 'In progress']);
  });
});
