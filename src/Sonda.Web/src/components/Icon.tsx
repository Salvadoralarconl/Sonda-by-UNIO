import type { CSSProperties } from 'react';
export function Icon({ name, size = 22 }: { name: string; size?: number }) {
 const artwork:Record<string,[number,number,number,number]>={logo:[17,14,30,30],user:[12,678,42,42],bell:[890,34,16,18],expand:[1299,33,19,20],pulse:[115,173,18,16],log:[415,171,18,18],warning:[711,170,19,19]};
 if(artwork[name]){const [x,y,w,h]=artwork[name];return <span aria-hidden="true" style={{display:'inline-block',width:size,height:size,overflow:'hidden',position:'relative',flexShrink:0}}><img alt="" src="/design/home-artwork.png" style={{position:'absolute',maxWidth:'none',width:1366*size/Math.max(w,h),left:-x*size/Math.max(w,h),top:-y*size/Math.max(w,h)}}/></span>;} 
 const crop: Record<string,[number,number,number]>={home:[59,150,117],monitor:[430,358,120],search:[620,354,117],settings:[243,554,120],config:[40,48,72]};
 if(crop[name]){const [x,y,w]=crop[name];const scale=size/w;return <span aria-hidden="true" style={{display:'inline-block',width:size,height:size,overflow:'hidden',position:'relative',flexShrink:0}}><img alt="" src={name==='config'?'/design/configuration.png':'/design/navigation.webp'} style={{position:'absolute',maxWidth:'none',width:(name==='config'?150:800)*scale,left:-x*scale,top:-y*scale}}/></span>;}
 const paths: Record<string, React.ReactNode> = {
 home: <><path d="m3 10 9-7 9 7"/><path d="M5 9v11h14V9"/></>,
 search: <><circle cx="10" cy="10" r="7"/><path d="m15 15 6 6"/></>,
 monitor: <><path d="M3 14h4v7H3zM10 8h4v13h-4zM17 3h4v18h-4z"/></>,
 bell: <><path d="M6 16h12l-2-3V8a4 4 0 0 0-8 0v5zM10 19h4"/></>,
 expand: <path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5"/>,
 config: <><rect x="2" y="3" width="6" height="18" rx="1"/><rect x="12" y="3" width="10" height="7" rx="1"/><rect x="12" y="14" width="4" height="7" rx="1"/><rect x="19" y="14" width="3" height="7" rx="1"/></>,
 settings: <><path d="m10 2 4 0 1 3 3 1 3 3-2 3 2 3-3 3-3 1-1 3h-4l-1-3-3-1-3-3 2-3-2-3 3-3 3-1z"/><circle cx="12" cy="12" r="3"/></>,
 pulse: <path d="M2 12h5l2-5 4 10 2-5h7"/>,
 log: <><rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 7h8M8 11h8M8 15h8"/></>,
 warning: <><path d="m12 3 10 18H2zM12 9v5M12 17v1"/></>,
 user: <><circle cx="12" cy="8" r="4"/><path d="M3 22c0-10 18-10 18 0"/></>,
 logo: <>{[0,60,120,180,240,300].map(a=><circle key={a} cx="12" cy="5" r="4" transform={`rotate(${a} 12 12)`}/>)}<circle cx="12" cy="12" r="3"/></>
 };
 return <svg width={size} height={size} viewBox="0 0 24 24" fill={name==='logo'?'currentColor':'none'} stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{flexShrink:0} as CSSProperties}>{paths[name] ?? paths.log}</svg>;
}


