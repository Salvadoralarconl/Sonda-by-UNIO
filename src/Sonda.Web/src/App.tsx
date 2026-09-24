import { reportingTime } from './api/time';
import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import type { Dashboard, ApplicationState } from './api/types';
import { useSession, useDashboard, identity } from './session';
import { Icon } from './components/Icon';
import { IncidentModal } from './features/incidents/IncidentModal';
import { greeting } from './features/home/greeting';
import { Search } from './features/search/Search';
import { ApplicationModal } from './features/monitoring/ApplicationModal';
import { Configuration } from './features/configuration/Configuration';
import { Account } from './features/accounts/Account';

export function Badge({value}:{value:string|null}) { return <span className={`badge tone-${value?.toLowerCase() ?? 'unknown'}`}>{value??'Not observed'}</span>; }
export function App() {
 const session=useSession(), dashboard=useDashboard(), location=useLocation(), navigate=useNavigate();
 const [incident,setIncident]=useState<{session:string;id:string}|null>(null),[application,setApplication]=useState<string|null>(null);
 const monitoring=location.pathname==='/monitoring', fullscreen=monitoring&&new URLSearchParams(location.search).has('expanded');
 const compact=location.pathname==='/'||location.pathname==='/search';
 const exit=()=>{if(document.fullscreenElement)void document.exitFullscreen().catch(()=>{});navigate('/monitoring');};
 useEffect(()=>{if(!fullscreen)return;const key=(e:KeyboardEvent)=>{if(e.key==='Escape')exit();};window.addEventListener('keydown',key);return()=>window.removeEventListener('keydown',key);},[fullscreen]);
 const expand=()=>{navigate('/monitoring?expanded=1');if(document.documentElement.requestFullscreen)void document.documentElement.requestFullscreen().catch(()=>{});};
 const title=monitoring?'Monitoring':location.pathname==='/search'?'Search':location.pathname.startsWith('/configuration')?'Configuration':location.pathname==='/account'?'Settings':'Home';
 return <div className={`app-shell ${compact?'with-monitor':''} ${fullscreen?'expanded-monitor':''}`}>
 {!fullscreen&&<nav className="rail" aria-label="Primary"><span className="brand" role="img" aria-label="SONDA"><Icon name="logo" size={30}/></span><div className="rail-main"><NavLink to="/" aria-label="Home"><Icon name="home"/></NavLink><NavLink to="/monitoring" aria-label="Monitoring"><Icon name="monitor"/></NavLink><NavLink to="/search" aria-label="Search"><Icon name="search"/></NavLink></div><div className="rail-bottom">{session.role==='Admin'&&<NavLink to="/configuration" aria-label="Configuration"><Icon name="config"/></NavLink>}<NavLink to="/account" aria-label="Settings"><Icon name="settings" size={25}/></NavLink><Link to="/account" className="avatar" aria-label="Your account"><Icon name="user" size={41}/></Link></div></nav>}
 <main className={`workspace page-${title.toLowerCase()}`}>
 <header className="utility">{!fullscreen&&<Icon name={monitoring?'monitor':title==='Search'?'search':'home'}/>}<span className="page-title">{title}</span><span className="utility-spacer"/>{fullscreen?<button onClick={exit}>Exit fullscreen</button>:monitoring?<button className="icon-button" aria-label="Fullscreen monitoring" onClick={expand}><Icon name="expand" size={18}/></button>:<><button className="icon-button muted" aria-disabled="true" title="Notifications are not available yet." aria-label="Notifications are not available yet."><Icon name="bell" size={16}/></button><Link className="icon-button" to="/search?filters=1" aria-label="Open Search"><Icon name="search" size={16}/></Link></>}</header>
 {!dashboard.pollingActive&&<p className="load-state" role="status">Live updates paused while inactive. <button onClick={()=>void dashboard.refetch()}>Refresh monitoring</button></p>}
 {dashboard.isError&&<p className="load-state" role="alert">Monitoring data is unavailable. <button onClick={()=>void dashboard.refetch()}>Retry</button></p>}
 <Routes><Route path="/" element={dashboard.data?<Home data={dashboard.data} openIncident={(session,id)=>setIncident({session,id})}/>:<p className="load-state">Loading Home…</p>}/><Route path="/monitoring" element={dashboard.data?<Monitoring data={dashboard.data} onOpen={setApplication}/>:<p className="load-state">Loading Monitoring…</p>}/><Route path="/search" element={<Search labels={dashboard.data?.applicationLabels??[]}/>}/><Route path="/configuration/*" element={session.role==='Admin'?<Configuration/>:<p className="load-state">Administrator access required.</p>}/><Route path="/account" element={<Account/>}/><Route path="*" element={<p className="load-state">Page not found. <Link to="/">Home</Link></p>}/></Routes>
 </main>
 {compact&&<aside className="monitor-panel" aria-label="Logs Monitored"><header><span>Logs Monitored</span><button className="icon-button muted" aria-label="Fullscreen monitoring" onClick={expand}><Icon name="expand" size={17}/></button></header>{dashboard.data?<MonitorRows data={dashboard.data} onOpen={setApplication}/>:<p className="load-state">Monitoring unavailable</p>}</aside>}
 {incident&&<IncidentModal identity={identity(session)} session={incident.session} incidentId={incident.id} onClose={()=>setIncident(null)}/>}
 {application&&<ApplicationModal applicationId={application} state={dashboard.data?.applications.find(x=>x.applicationId===application)} name={dashboard.data?.applicationLabels.find(x=>x.id===application)?.name??application} onClose={()=>setApplication(null)}/>}
 </div>;
}
function Home({data,openIncident}:{data:Dashboard;openIncident:(session:string,id:string)=>void}) {
 const session=useSession(),[elapsed,setElapsed]=useState(0),origin=useRef(performance.now());
 useEffect(()=>{origin.current=performance.now();setElapsed(0);const timer=setInterval(()=>setElapsed(performance.now()-origin.current),30000);return()=>clearInterval(timer);},[session.asOf]);
 let hello=`Hello, ${session.displayName}`;try{if(session.reportingTimeZoneIanaId)hello=greeting(session.displayName,session.asOf,session.reportingTimeZoneIanaId,elapsed);}catch{/* unavailable timezone does not fall back to the viewer's zone */}
 const max=Math.max(1,...data.fiveDayCompletedOrderHistory.map(x=>x.completedOrderRunsProcessedCount));
 return <><div className="greeting"><h1 title={hello}>{hello}</h1><p>Here’s what’s happening across Sonda today.</p></div><section className="metric-cards" aria-label="Today's summary">
 <article className="metric-card"><span className="metric-icon"><Icon name="pulse" size={18}/></span><h2>System Health</h2><div className="card-badge"><Badge value={data.currentSystemBadge.businessHealth}/></div><strong>{data.systemHealth.percentage===null?'—':`${data.systemHealth.percentage}%`}</strong><div className="health-track" role="img" aria-label={data.systemHealth.percentage===null?'No evaluated runs':`${data.systemHealth.percentage}% of evaluated runs successful`}><span style={{width:`${data.systemHealth.percentage??0}%`}}/></div>{data.currentSystemBadge.isQualified&&<span className="health-qualifier" title={data.currentSystemBadge.qualification}>Availability unconfirmed</span>}</article>
 <article className="metric-card"><span className="metric-icon"><Icon name="log" size={17}/></span><h2>Log’s Today</h2><strong>{data.completedOrderRunsProcessedToday.toLocaleString('en-US')}</strong><div className="volume-chart" role="img" aria-label="Five-day completed Order Run history">{data.fiveDayCompletedOrderHistory.map(d=><span key={d.date} style={{height:`${Math.max(2,d.completedOrderRunsProcessedCount/max*36)}px`}} title={`${d.date}: ${d.completedOrderRunsProcessedCount}`}/>)}</div></article>
 <article className="metric-card"><span className="metric-icon"><Icon name="warning" size={18}/></span><h2>Active Incidents</h2><div className="card-badge"><Badge value={data.activeIncidentsBadge}/></div><strong>{data.unresolvedIncidentCount.toLocaleString('en-US')}</strong><p className="incident-caption">{data.unresolvedIncidentCount?'Attention may be needed soon':'No unresolved incidents'}</p></article>
 </section><h2 className="error-heading">Error Logs</h2><div className="home-table-wrap"><table className="home-table"><colgroup>{[15,11.1,12.8,39.8,12.7,8.6].map((n,i)=><col key={i} style={{width:`${n}%`}}/>)}</colgroup><thead><tr>{['Application','Time','Severity','Message','Status',''].map((v,i)=><th key={i}>{v}</th>)}</tr></thead><tbody>{data.recentIncidents.map(i=>{const d=data.incidentDisplays.find(x=>x.sessionId===i.sessionId&&x.incidentId===i.id);return <tr key={`${i.sessionId}:${i.id}`}><td>{d?.applicationName??'Unavailable'}</td><td>{d?reportingTime(d.firstDetectedProcessedAt,session):'—'}</td><td><Badge value={i.severity}/></td><td className="message-cell" title={d?.summary}>{d?.summary??'Summary unavailable'}</td><td><Badge value={i.status}/></td><td><button className="info-button" onClick={()=>openIncident(i.sessionId,i.id)}>Info</button></td></tr>;})}</tbody></table>{!data.recentIncidents.length&&<p className="empty-state">No incidents to display.</p>}</div></>;
}
function MonitorRows({data,onOpen}:{data:Dashboard;onOpen:(id:string)=>void}) {return <div className="monitor-rows">{data.applications.map(s=><button key={s.applicationId} className={`monitor-row tone-${s.businessHealth?.toLowerCase()??'unknown'}`} onClick={()=>onOpen(s.applicationId)}><span>{data.applicationLabels.find(a=>a.id===s.applicationId)?.name??s.applicationId}</span><Availability state={s}/><span className={`state-dot tone-${s.businessHealth?.toLowerCase()??'unknown'}`} aria-hidden="true"/><span className="sr-only">{s.businessHealth??'Not observed'}</span></button>)}{!data.applications.length&&<p className="empty-state">No applications configured.</p>}</div>;}
function Availability({state}:{state:ApplicationState}) {return state.availability==='Fresh'?null:<small className="availability" title={state.reason}>{state.availability}</small>;}
function Monitoring({data,onOpen}:{data:Dashboard;onOpen:(id:string)=>void}) {return <MonitorRows data={data} onOpen={onOpen}/>;}






