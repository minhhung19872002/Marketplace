// The ONLY place that formats or splits dates/times. Storage is UTC; display is Asia/Ho_Chi_Minh.
export const VN_TIME_ZONE = 'Asia/Ho_Chi_Minh';

// Vietnam has no daylight saving time: wall clock is always UTC+7
const VN_OFFSET_MS = 7 * 60 * 60 * 1000;

const dateTimeFormat = new Intl.DateTimeFormat('vi-VN', {
  timeZone: VN_TIME_ZONE,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
});

const dateFormat = new Intl.DateTimeFormat('vi-VN', {
  timeZone: VN_TIME_ZONE,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
});

type DateInput = string | number | Date;

const toDate = (value: DateInput): Date => (value instanceof Date ? value : new Date(value));

/** 06/10/2026 14:05 (Vietnam time) */
export const formatDateTime = (value: DateInput): string => dateTimeFormat.format(toDate(value));

/** 06/10/2026 (Vietnam calendar day) */
export const formatDate = (value: DateInput): string => dateFormat.format(toDate(value));

/**
 * Seconds until the next slot boundary in Vietnam time, slots starting every `slotHours` hours from 00:00.
 * Never returns 0 so a countdown always has something to show.
 */
export const secondsToNextSlot = (nowMs: number, slotHours: number): number => {
  const slotMs = slotHours * 60 * 60 * 1000;
  const vnWallMs = nowMs + VN_OFFSET_MS;
  const nextBoundary = Math.floor(vnWallMs / slotMs) * slotMs + slotMs;
  return Math.max(1, Math.floor((nextBoundary - vnWallMs) / 1000));
};

/**
 * ISO instants bounding Vietnam calendar days "YYYY-MM-DD" (inclusive), e.g. for report/audit filters —
 * independent of the browser's own time zone.
 */
export const vnDayBoundsIso = (fromDay?: string, toDay?: string): { from?: string; to?: string } => ({
  from: fromDay ? new Date(`${fromDay}T00:00:00+07:00`).toISOString() : undefined,
  to: toDay ? new Date(`${toDay}T23:59:59.999+07:00`).toISOString() : undefined,
});

/** Today's Vietnam calendar day as "YYYY-MM-DD" (report filters), whatever the browser's time zone. */
export const vnTodayIso = (nowMs: number = Date.now()): string => new Date(nowMs + VN_OFFSET_MS).toISOString().slice(0, 10);

/** "YYYY-MM-DD" shifted by n days. */
export const addDaysIso = (day: string, n: number): string => new Date(Date.parse(`${day}T00:00:00Z`) + n * 86_400_000).toISOString().slice(0, 10);

/** "YYYY-MM-DD" → "06/10/2026". */
export const formatIsoDay = (day: string): string => `${day.slice(8, 10)}/${day.slice(5, 7)}/${day.slice(0, 4)}`;
