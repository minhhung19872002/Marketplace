import { describe, expect, it } from 'vitest';
import { formatDate, formatDateTime, secondsToNextSlot } from '../lib/datetime';
import { findLines, sourceFiles } from './scan';

const ALLOWED = ['lib/datetime.ts', 'lib/money.ts'];

describe('datetime', () => {
  it('formats and splits dates only inside lib/datetime', () => {
    const files = sourceFiles(['.ts', '.tsx']).filter((f) => !ALLOWED.some((a) => f.replace(/\\/g, '/').endsWith(a)));
    const offenders = findLines(
      files,
      /toLocale(Date|Time)?String\(|Intl\.DateTimeFormat|\.get(Hours|Minutes|Seconds|Date|Day|Month|FullYear)\(|\.set(Hours|Minutes|Date)\(|\.(startOf|endOf)\(/,
    );
    expect(offenders).toEqual([]);
  });

  it('displays in Vietnam time regardless of the machine time zone', () => {
    // 2026-10-06T17:30:00Z is 00:30 on 7 Oct in Vietnam
    expect(formatDateTime('2026-10-06T17:30:00Z')).toBe('00:30 07/10/2026');
    expect(formatDate('2026-10-06T17:30:00Z')).toBe('07/10/2026');
  });

  it('counts down to the next Vietnam-time slot boundary', () => {
    // 13:59:30 VN (06:59:30Z) → next 2-hour slot at 14:00 VN = 30 s
    expect(secondsToNextSlot(Date.parse('2026-10-06T06:59:30Z'), 2)).toBe(30);
    // Exactly on a boundary → a full slot remains
    expect(secondsToNextSlot(Date.parse('2026-10-06T07:00:00Z'), 2)).toBe(7200);
  });
});
