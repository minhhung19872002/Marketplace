// Strip Vietnamese tones for accent-insensitive matching ("ao thun" matches "Áo Thun")
const COMBINING = new RegExp('[\u0300-\u036f]', 'g');

export const removeTones = (str: string): string =>
  str.normalize('NFD').replace(COMBINING, '').replace(/đ/g, 'd').replace(/Đ/g, 'D');

/** Counter on a header icon: "99+" past 99 (P1). */
export const badgeCount = (n: number): string => (n > 99 ? '99+' : String(n));

/**
 * The page buttons of a pager: always the first and last page, two either side of the current one, and "…" for each
 * gap (1 … 4 5 6 7 8 … 20).
 */
export const pageWindow = (page: number, total: number): (number | '…')[] => {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const from = Math.max(2, Math.min(page - 2, total - 5));
  const to = Math.min(total - 1, Math.max(page + 2, 6));
  const middle = Array.from({ length: to - from + 1 }, (_, i) => from + i);
  return [1, ...(from > 2 ? ['…' as const] : []), ...middle, ...(to < total - 1 ? ['…' as const] : []), total];
};

/**
 * Splits `text` into the parts that match `query` and the rest, ignoring accents and case ("dien" marks "Điện"), so a
 * suggestion can show the typed part in bold. Each word of the query is matched on its own.
 */
export const highlightParts = (text: string, query: string): { text: string; match: boolean }[] => {
  const chars = Array.from(text);
  const folded = chars.map((c) => removeTones(c).toLowerCase().charAt(0) || c).join('');
  const marks = new Array<boolean>(chars.length).fill(false);
  for (const word of removeTones(query).toLowerCase().split(/\s+/).filter(Boolean)) {
    for (let at = folded.indexOf(word); at >= 0; at = folded.indexOf(word, at + word.length))
      for (let i = at; i < at + word.length; i++) marks[i] = true;
  }
  const parts: { text: string; match: boolean }[] = [];
  chars.forEach((c, i) => {
    const last = parts[parts.length - 1];
    if (last && last.match === marks[i]) last.text += c;
    else parts.push({ text: c, match: marks[i] });
  });
  return parts;
};
