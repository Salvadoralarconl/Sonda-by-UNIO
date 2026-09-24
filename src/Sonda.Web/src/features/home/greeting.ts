// Presentation only. Time comes from the server snapshot plus monotonic elapsed time,
// never the viewer's selected timezone or adjustable wall clock.
export function greeting(displayName: string, serverInstant: string, reportingIanaZone: string, elapsedMs = 0): string {
  const date = new Date(Date.parse(serverInstant) + Math.max(0, elapsedMs));
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: reportingIanaZone, hour: 'numeric', hourCycle: 'h23' }).formatToParts(date);
  const hour = Number(parts.find(part => part.type === 'hour')?.value);
  if (!Number.isInteger(hour) || !displayName.trim()) throw new Error('Valid authenticated name and reporting time are required');
  const period = hour < 12 ? 'Morning' : hour < 18 ? 'Afternoon' : 'Evening';
  return `Good ${period}, ${displayName}`;
}
