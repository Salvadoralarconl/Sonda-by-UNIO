import { describe, expect, it } from 'vitest';
import { greeting } from './greeting';

describe('authenticated reporting-time greeting', () => {
  it.each([
    ['Maria', '2026-09-24T15:59:59Z', 'Good Morning, Maria'],
    ['David', '2026-09-24T16:00:00Z', 'Good Afternoon, David'],
    ['Maria', '2026-09-24T21:59:59Z', 'Good Afternoon, Maria'],
    ['David', '2026-09-24T22:00:00Z', 'Good Evening, David'],
    ['Maria', '2026-09-25T04:00:00Z', 'Good Morning, Maria'],
  ])('uses %s and server-zone boundaries at %s', (name, instant, expected) => {
    expect(greeting(name, instant, 'America/New_York')).toBe(expected);
  });
  it('accounts for a different server timezone and elapsed time', () => {
    expect(greeting('David', '2026-09-24T15:59:59Z', 'America/New_York', 1000)).toBe('Good Afternoon, David');
    expect(greeting('Maria', '2026-09-24T15:59:59Z', 'Asia/Tokyo')).toBe('Good Morning, Maria');
  });
  it('handles daylight saving rules, rather than a fixed offset', () => {
    expect(greeting('Maria', '2026-01-24T16:30:00Z', 'America/New_York')).toBe('Good Morning, Maria');
    expect(greeting('Maria', '2026-09-24T16:30:00Z', 'America/New_York')).toBe('Good Afternoon, Maria');
  });
  it('never invents a name or substitutes browser timezone', () => {
    expect(() => greeting('', '2026-09-24T16:00:00Z', 'America/New_York')).toThrow();
    expect(() => greeting('Maria', '2026-09-24T16:00:00Z', 'invalid/timezone')).toThrow();
  });
});
