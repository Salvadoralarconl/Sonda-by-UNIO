import { RunModal } from '../runs/RunModal';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { api } from '../../api/client';
import type { ApplicationState, Incident } from '../../api/types';
import { identity, useSession } from '../../session';
import { Modal } from '../../components/Modal';
import { IncidentModal } from '../incidents/IncidentModal';
type RecentRun={id:string;scope?:string;identifier:string|null;attempt:number;result:string|null;version:number};
type Overview={sessionId:string;unresolvedErrorCount:number;unresolvedWarningCount:number;investigatingCount:number;recentIncidents:Incident[];investigatingIncidents:Incident[];recentRuns:RecentRun[];recentFailedOrders:RecentRun[];limit:number};
export function ApplicationModal({applicationId,name,state,onClose}:{applicationId:string;name:string;state?:ApplicationState;onClose:()=>void}) {
 const session=useSession();const [section,setSection]=useState('Overview'),[incident,setIncident]=useState<Incident|null>(null),[runId,setRunId]=useState<string|null>(null);
 const q=useQuery({queryKey:[identity(session),'application-overview',applicationId],queryFn:({signal})=>api<Overview>(`/monitoring/applications/${encodeURIComponent(applicationId)}/overview`,{signal})});
 return <><Modal title={name} onClose={onClose}><div className="modal-layout"><nav className="modal-sections" aria-label="Application details">{['Overview','Incidents','Runs'].map(s=><button key={s} aria-current={s===section?'page':undefined} onClick={()=>setSection(s)}>{s}</button>)}</nav><div className="modal-content"><h2>{name}</h2>{section==='Overview'&&<dl>{Object.entries({'Current business health':state?.businessHealth??'Not observed','Availability':state?.availability??'Unknown','Availability detail':state?.reason,'Last successful read':state?.lastSuccessfulRead,'Last completed run':state?.lastRunCompleted,'Unresolved Errors':q.data?.unresolvedErrorCount,'Unresolved Warnings':q.data?.unresolvedWarningCount,'Investigating':q.data?.investigatingCount}).map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v??'Unavailable'}</dd></div>)}</dl>}{q.isPending&&<p>Loading application details…</p>}{q.isError&&<p role="alert">Application details unavailable. The application may not yet be activated.</p>}{section==='Incidents'&&q.data&&<><h3>Investigating incidents</h3>{(q.data.investigatingIncidents??[]).map(i=><article key={i.id}><span>{i.severity} · {i.status} · {i.problemIdentity}</span> <button onClick={()=>setIncident(i)}>Info</button></article>)}<h3>Recent incidents and resolved history</h3><p>Each list shows up to {q.data.limit} records. Counts above cover all unresolved incidents.</p>{q.data.recentIncidents.map(i=><article key={i.id}><span>{i.severity} · {i.status} · {i.problemIdentity}</span> <button onClick={()=>setIncident(i)}>Info</button></article>)}</>}{section==='Runs'&&q.data&&<><h3>Recent runs</h3>{q.data.recentRuns.map(r=><button key={r.id} onClick={()=>setRunId(r.id)}><RunRow run={r}/></button>)}<h3>Recent failed Order Runs</h3>{q.data.recentFailedOrders.map(r=><button key={r.id} onClick={()=>setRunId(r.id)}><RunRow run={r}/></button>)}<p>Each list is limited to the latest {q.data.limit} records.</p></>}</div></div></Modal>{incident&&<IncidentModal identity={identity(session)} session={incident.sessionId} incidentId={incident.id} onClose={()=>setIncident(null)}/ >}{runId&&q.data&&<RunModal sessionId={q.data.sessionId} runId={runId} onClose={()=>setRunId(null)}/>}</>;
}
function RunRow({run}:{run:RecentRun}) {return <span><strong>{run.identifier??run.id}</strong> · {run.scope??'Order'} · Attempt {run.attempt} · {run.result??'In progress'} · Profile v{run.version}</span>;}




