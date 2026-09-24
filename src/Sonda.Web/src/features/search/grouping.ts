import { scopedKey, type Evidence, type Run } from '../../api/types';

export type EvidenceDisplay = { evidence: Evidence; relatedOrders: Run[]; linksIncomplete: boolean };
export type SearchGroup = { key: string; kind: 'Order' | 'Unassigned' | 'Incomplete'; order?: Run; rows: EvidenceDisplay[]; sharedReferences: { evidenceKey: string; primaryGroupKey: string }[] };

// Presentation of one bounded evidence page. This does not infer an order from
// message/identifier text, combine attempts, or claim a complete run history.
export function groupEvidencePage(evidencePage: Evidence[], runs: ReadonlyMap<string, Run>): SearchGroup[] {
  const groups = new Map<string, SearchGroup>();
  const seen = new Set<string>();
  function group(key: string, kind: SearchGroup['kind'], order?: Run) {
    if (!groups.has(key)) groups.set(key, { key, kind, order, rows: [], sharedReferences: [] });
    return groups.get(key)!;
  }
  for (const evidence of evidencePage) {
    const evidenceKey = scopedKey(evidence.sessionId, evidence.id);
    if (seen.has(evidenceKey)) continue;
    seen.add(evidenceKey);
    const linked = evidence.runIds.map(id => runs.get(scopedKey(evidence.sessionId, id)));
    const orders = linked.filter((run): run is Run => run?.scope === 'Order');
    const incomplete = evidence.linksTruncated || linked.some(run => !run);
    const primary = orders[0]; // display placement only; every relationship remains visible
    const key = primary ? scopedKey(primary.sessionId, primary.id) : `${incomplete ? 'incomplete' : 'unassigned'}:${evidence.applicationId}`;
    group(key, primary ? 'Order' : incomplete ? 'Incomplete' : 'Unassigned', primary).rows.push({ evidence, relatedOrders: orders, linksIncomplete: incomplete });
    for (const order of orders.slice(1)) {
      group(scopedKey(order.sessionId, order.id), 'Order', order).sharedReferences.push({ evidenceKey, primaryGroupKey: key });
    }
  }
  return [...groups.values()];
}

export function orderResultLabel(result: Run['result']): string {
  return result === 'Success' ? 'Successful Run' : result === 'Failure' ? 'Failed Run' : result === 'Undefined' ? 'Undefined' : 'In progress';
}

