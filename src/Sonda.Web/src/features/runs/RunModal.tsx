import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../../api/client';
import type { Run, Page } from '../../api/types';
import { identity, useSession } from '../../session';
import { Modal } from '../../components/Modal';
import { Result } from '../accounts/Account';
export function RunModal({sessionId,runId,onClose}:{sessionId:string;runId:string;onClose:()=>void}) {
 const actor=useSession(),[selected,setSelected]=useState(runId),[cursor,setCursor]=useState<string|null>(null);
 const q=useQuery({queryKey:[identity(actor),'run-detail',sessionId,selected],queryFn:async({signal})=>{try{return await api<Run>(`/order-runs/${encodeURIComponent(selected)}?session=${sessionId}`,{signal});}catch(e){if(!(e instanceof ApiError)||e.status!==404)throw e;return api<Run>(`/application-runs/${encodeURIComponent(selected)}?session=${sessionId}`,{signal});}}});
 const children=useQuery({queryKey:[identity(actor),'run-children',sessionId,selected,cursor],enabled:q.data?.scope==='Application',queryFn:({signal})=>api<Page<Run>>(`/order-runs?session=${sessionId}&parentId=${encodeURIComponent(selected)}&size=20${cursor?'&cursor='+encodeURIComponent(cursor):''}`,{signal})});
 function open(id:string){setSelected(id);setCursor(null);}
 return <Modal title="Run details" onClose={onClose}><div className="detail-body"><h2>{q.data?.scope??''} Run</h2>{q.isPending&&<p>Loading run…</p>}{q.isError&&<p role="alert">Run details unavailable.</p>}{q.data&&<><Result value={q.data}/>{q.data.parentId&&<button onClick={()=>open(q.data!.parentId!)}>Parent Application Run</button>}{q.data.previousAttemptId&&<button onClick={()=>open(q.data!.previousAttemptId!)}>Previous attempt</button>}{q.data.scope==='Application'&&<><h3>Child Order Runs</h3>{children.isError&&<p role="alert">Child runs unavailable.</p>}{children.data?.items.map(r=><article key={r.id}><button onClick={()=>open(r.id)}>{r.identifier??r.id} · Attempt {r.attempt} · {r.result??'In progress'}</button></article>)}<nav className="pagination"><button disabled={!cursor} onClick={()=>setCursor(null)}>First page</button><button disabled={!children.data?.hasMore} onClick={()=>setCursor(children.data!.nextCursor)}>Next</button></nav></>}</>}</div></Modal>;
}
