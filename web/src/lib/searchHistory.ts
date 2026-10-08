// The viewer's own recent searches, kept in this browser only (a convenience for the search box, never sent anywhere).
const KEY = 'sh:search-history';
const LIMIT = 8;

export const readSearchHistory = (): string[] => {
  try {
    const raw = window.localStorage.getItem(KEY);
    const list: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(list) ? list.filter((x): x is string => typeof x === 'string').slice(0, LIMIT) : [];
  } catch {
    return [];
  }
};

/** Puts the keyword first (once, case-insensitively) and keeps the newest few. */
export const pushSearchHistory = (keyword: string, list: string[] = readSearchHistory()): string[] => {
  const k = keyword.trim().replace(/\s+/g, ' ');
  if (!k) return list;
  const next = [k, ...list.filter((x) => x.toLowerCase() !== k.toLowerCase())].slice(0, LIMIT);
  try {
    window.localStorage.setItem(KEY, JSON.stringify(next));
  } catch {
    // storage blocked (private window): the history simply is not kept
  }
  return next;
};

export const clearSearchHistory = (): void => {
  try {
    window.localStorage.removeItem(KEY);
  } catch {
    // nothing kept
  }
};
