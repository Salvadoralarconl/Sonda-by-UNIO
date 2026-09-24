import type { Session } from './types';
export function reportingTime(value:string,session:Session):string {
 if(!session.reportingTimeZoneIanaId)return `${value} (reporting timezone unavailable)`;
 return new Intl.DateTimeFormat('en-US',{timeZone:session.reportingTimeZoneIanaId,hour:'numeric',minute:'2-digit'}).format(new Date(value));
}
