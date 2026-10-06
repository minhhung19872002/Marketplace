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

const clockFormat = new Intl.DateTimeFormat('vi-VN', {
  timeZone: VN_TIME_ZONE,
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
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

/** Remaining time as ["02", "05", "09"] (hours may exceed 24) — for Flash Sale countdowns. */
export const countdownParts = (remainingMs: number): [string, string, string] => {
  const total = Math.max(0, Math.floor(remainingMs / 1000));
  const pad = (n: number) => String(n).padStart(2, '0');
  return [pad(Math.floor(total / 3600)), pad(Math.floor((total % 3600) / 60)), pad(total % 60)];
};

/** "14:00" — the Vietnam wall-clock hour of an instant (Flash Sale slot labels). */
export const formatClock = (value: DateInput): string => clockFormat.format(toDate(value));
