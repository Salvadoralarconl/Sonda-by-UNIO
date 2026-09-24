import { Result } from '../accounts/Account';
import { RunModal } from '../runs/RunModal';
import { reportingTime } from '../../api/time';
import { Link, useSearchParams } from 'react-router-dom';
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../../api/client';
import { scopedKey, type Evidence, type Page, type Run } from '../../api/types';
import { identity, useSession } from '../../session';
import { groupEvidencePage, orderResultLabel } from './grouping';
import { Modal } from '../../components/Modal';
export function Search({labels}:{labels:{id:string;name:string}[]}) {
 const [urlParams]=useSearchParams(); const session=useSession();const [filters,setFilters]=useState(''),[cursors,setCursors]=useState<(string|null)[]>([null]);const [selected,setSelected]=useState<Evidence|null>(null);const [selectedRun,setSelectedRun]=useState<Run|null>(null);
 const q=useQuery({queryKey:[identity(session),'search',filters,cursors.at(-1)],queryFn:({signal})=>api<Page<Evidence>>(`/search?size=50${filters?'&'+filters:''}${cursors.at(-1)?'&cursor='+encodeURIComponent(cursors.at(-1)!):''}`,{signal})});
 const runs=useQuery({queryKey:[identity(session),'search-links',q.data?.items.map(x=>scopedKey(x.sessionId,x.id)),q.data?.asOf],enabled:!!q.data,queryFn:async({signal})=>{
  const map=new Map<string,Run>();const pairs=[...new Map(q.data!.items.flatMap(e=>e.runIds.map(id=>[scopedKey(e.sessionId,id),{session:e.sessionId,id}] as const))).entries()].slice(0,200);
  // Bound concurrency; only resolve links on the current evidence page. Failed links remain explicitly incomplete.
  let index=0;await Promise.all(Array.from({length:Math.min(6,pairs.length)},async()=>{while(index<pairs.length){const [key,pair]=pairs[index++];try{let run:Run;try{run=await api<Run>(`/order-runs/${encodeURIComponent(pair.id)}?session=${pair.session}`,{signal});}catch(e){if(!(e instanceof ApiError)||e.status!==404)throw e;run=await api<Run>(`/application-runs/${encodeURIComponent(pair.id)}?session=${pair.session}`,{signal});}map.set(key,run);}catch(e){if(signal.aborted)throw e;}}}));return map;
 }});
 const groups=q.data?groupEvidencePage(q.data.items,runs.data??new Map()):[];
 return <section className="search-page"><details className="search-filters" open={urlParams.has('filters')}><summary className="sr-only">Search filters</summary><Link to="/search">Close filters</Link><form onSubmit={e=>{e.preventDefault();const form=new FormData(e.currentTarget),params=new URLSearchParams();for(const [k,v]of form.entries())if(String(v).trim())params.set(k,String(v));setFilters(params.toString());setCursors([null]);}}><label>Application<select name="applicationId"><option value="">All</option>{labels.map(a=><option value={a.id} key={a.id}>{a.name}</option>)}</select></label><label>Message<input name="text" minLength={3} maxLength={256}/></label><label>Profile<input name="profileId"/></label><label>Identifier<input name="identifier"/></label><label>Identifier namespace<input name="identifierNamespace"/></label><label>From (ISO timestamp)<input name="from" placeholder="2026-09-24T00:00:00-04:00"/></label><label>To (ISO timestamp)<input name="to"/></label><label>Time basis<select name="timeBasis"><option value="processed">Processing time</option><option value="event">Event time</option></select></label><label>Order result<select name="result"><option value="">Any</option>{['Success','Failure','Undefined'].map(v=><option key={v}>{v}</option>)}</select></label><label>Classification<select name="classification"><option value="">Any</option>{['Error','Warning','Ignore'].map(v=><option key={v}>{v}</option>)}</select></label><label>Incident status<select name="incidentStatus"><option value="">Any</option>{['Active','Investigating','Resolved'].map(v=><option key={v}>{v}</option>)}</select></label><label><input type="checkbox" name="caseSensitive" value="true"/>Case sensitive</label><button>Search</button></form></details>
 <div className="search-columns" aria-hidden="true"><span>Application</span><span>Time</span><span>Message</span><span>Status</span></div>
 {q.isPending&&<p role="status">Loading evidence…</p>}{q.isError&&<p role="alert">Search unavailable: {q.error instanceof ApiError?q.error.code:'connection error'} <button onClick={()=>void q.refetch()}>Retry</button></p>}
 {groups.map(group=><article className={`search-group ${group.kind==='Order'?'':'unassigned'}`} key={group.key}>
 {group.kind!=='Order'&&<h2>{group.kind==='Unassigned'?'Unassigned evidence':'Evidence — run links incomplete'}</h2>}
 {group.rows.map(({evidence,relatedOrders,linksIncomplete},rowIndex)=><div className="search-evidence" key={evidence.id}><span>{rowIndex===0||group.kind!=='Order'?(labels.find(a=>a.id===evidence.applicationId)?.name??evidence.applicationId):''}</span><time title={evidence.processedAt}>{reportingTime(evidence.processedAt,session)}</time><div><button className="evidence-message" onClick={()=>setSelected(evidence)}>{evidence.raw}</button>{linksIncomplete&&<small>Some run links are unavailable or outside the lookup limit.</small>}{evidence.rawTruncated&&<small>Preview truncated</small>}{relatedOrders.length>1&&<small>Related Order Runs: {relatedOrders.map(r=>`${r.identifier??r.id} · Attempt ${r.attempt}`).join(', ')}</small>}</div></div>)}
 {group.sharedReferences.map(ref=><p className="shared-reference" key={ref.evidenceKey}>Shared evidence → shown once in its linked Order Run group. <button onClick={()=>setSelected(q.data!.items.find(e=>scopedKey(e.sessionId,e.id)===ref.evidenceKey)!)}>View evidence and all run links</button></p>)}
 {group.order&&<footer><span>Order {group.order.identifier??group.order.id} · Attempt {group.order.attempt}</span><button className={`order-result result-${group.order.result?.toLowerCase()??'open'}`} onClick={()=>setSelectedRun(group.order!)} aria-label={`Open Order Run ${group.order.identifier??group.order.id} Attempt ${group.order.attempt}: ${orderResultLabel(group.order.result)}`}>{orderResultLabel(group.order.result)}</button></footer>}
 </article>)}
 {q.data&&!q.data.items.length&&<p className="empty-state">No matching evidence.</p>}{runs.isFetching&&<p role="status">Resolving persisted run links…</p>}
 <nav className="pagination" aria-label="Search pages"><button disabled={cursors.length===1} onClick={()=>setCursors(cursors.slice(0,-1))}>Previous</button><button disabled={!q.data?.hasMore} onClick={()=>setCursors([...cursors,q.data!.nextCursor])}>Next</button><small>Groups contain matching evidence from this page.</small></nav>
 {selected&&<EvidenceModal evidence={selected} onClose={()=>setSelected(null)}/>} {selectedRun&&<RunModal sessionId={selectedRun.sessionId} runId={selectedRun.id} onClose={()=>setSelectedRun(null)}/>}
 </section>;
}
function EvidenceModal({evidence,onClose}:{evidence:Evidence;onClose:()=>void}) {
 const session=useSession();const [runId,setRunId]=useState<string|null>(null);const q=useQuery({queryKey:[identity(session),'evidence-detail',evidence.sessionId,evidence.id],queryFn:({signal})=>api<{evidence:Evidence;linksTruncated:boolean;interpretation:unknown}>(`/evidence/${evidence.id}?session=${evidence.sessionId}`,{signal})});const row=q.data?.evidence??evidence;
 return <><Modal title="Evidence" onClose={onClose}><div className="detail-body"><h2>Evidence</h2>{q.isError&&<p role="alert">Full evidence unavailable; displaying the search preview.</p>}<pre>{row.raw}</pre><dl>{Object.entries({'Application':row.applicationId,'Profile Version':`${row.profileId} v${row.profileVersion}`,'Source timestamp':row.sourceTimestamp,'Normalized timestamp':row.normalizedEventAt,'Processing timestamp':row.processedAt,'Database commit timestamp':row.databaseCommitAt}).map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v??'Not recorded'}</dd></div>)}</dl>{q.data?.interpretation!=null&&<details><summary>Recorded interpretation and classification</summary><Result value={q.data.interpretation}/></details>}<h3>All returned linked runs</h3><ul>{row.runIds.map(id=><li key={id}><button onClick={()=>setRunId(id)}>{id}</button></li>)}</ul>{q.data?.linksTruncated&&<p>Run links are truncated; this is not a complete list.</p>}</div></Modal>{runId&&<RunModal sessionId={evidence.sessionId} runId={runId} onClose={()=>setRunId(null)}/>}</>;
}





