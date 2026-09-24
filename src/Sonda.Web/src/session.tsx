import { Link, useLocation } from 'react-router-dom';
import { Redeem } from './features/accounts/Redeem';
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, clearIdentity } from './api/client';
import type { Dashboard, Session } from './api/types';
const Context = createContext<Session | null>(null);
export function useSession() { const s=useContext(Context); if(!s) throw new Error('Authenticated session required'); return s; }
export function identity(s: Session) { return `${s.teamId}:${s.accountId}:${s.revision}`; }
export function SessionBoundary({children}: {children: ReactNode}) {
 const cache=useQueryClient(); const location=useLocation();
 const [expired,setExpired]=useState(false);
 const q=useQuery({queryKey:['session'],enabled:!expired && location.pathname!=='/redeem',queryFn:({signal})=>api<Session>('/session',{signal}),retry:false,refetchOnWindowFocus:false});
 useEffect(()=>{const expired=()=>{setExpired(true);clearIdentity();cache.clear();};window.addEventListener('sonda:session-expired',expired);return()=>window.removeEventListener('sonda:session-expired',expired);},[cache]);
 if(location.pathname==='/redeem')return <Redeem/>;
 if(expired)return <Login onSuccess={()=>{clearIdentity();cache.clear();setExpired(false);}}/>;
 if(q.isPending)return <main className="auth-panel" role="status">Opening SONDA…</main>;
 if(q.isError)return <Login onSuccess={()=>{clearIdentity();cache.clear();void cache.invalidateQueries({queryKey:['session']});}}/>;
 return <Context.Provider value={q.data}>{children}</Context.Provider>;
}
function Login({onSuccess}:{onSuccess:()=>void}) {
 const [error,setError]=useState('');const [busy,setBusy]=useState(false);
 return <main className="auth-panel"><h1>SONDA</h1><h2>Sign in</h2><form onSubmit={async e=>{e.preventDefault();const data=new FormData(e.currentTarget);setBusy(true);setError('');try{await api('/session/login',{method:'POST',body:{login:data.get('login'),password:data.get('password')}});onSuccess();}catch{setError('Sign-in could not be completed. Check your credentials and connection.');}finally{setBusy(false);}}}>
 <label>Login<input name="login" autoComplete="username" required/></label><label>Password<input name="password" type="password" autoComplete="current-password" required/></label><button disabled={busy}>Sign in</button>{error&&<p role="alert">{error}</p>}</form><Link to="/redeem">Use an invitation or reset token</Link></main>;
}
// Poll only while visible and recently interacted with; authenticated reads must not keep an idle session alive indefinitely.
export function useDashboard() {
 const session=useSession();const [active,setActive]=useState(true);
 useEffect(()=>{let last=performance.now();const activity=()=>{last=performance.now();setActive(!document.hidden);};const check=()=>setActive(!document.hidden&&performance.now()-last<60000);window.addEventListener('pointerdown',activity);window.addEventListener('keydown',activity);document.addEventListener('visibilitychange',check);const timer=setInterval(check,5000);return()=>{clearInterval(timer);window.removeEventListener('pointerdown',activity);window.removeEventListener('keydown',activity);document.removeEventListener('visibilitychange',check);};},[]);
 const query=useQuery({queryKey:[identity(session),'dashboard'],queryFn:({signal})=>api<Dashboard>('/dashboard',{signal}),refetchInterval:active?30000:false,refetchOnWindowFocus:false,retry:false});return {...query,pollingActive:active};
}


