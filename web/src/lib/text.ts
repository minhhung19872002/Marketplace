// Strip Vietnamese tones for accent-insensitive matching ("ao thun" matches "Áo Thun")
const COMBINING = new RegExp('[\u0300-\u036f]', 'g');

export const removeTones = (str: string): string =>
  str.normalize('NFD').replace(COMBINING, '').replace(/đ/g, 'd').replace(/Đ/g, 'D');

/** Counter on a header icon: "99+" past 99 (P1). */
export const badgeCount = (n: number): string => (n > 99 ? '99+' : String(n));
