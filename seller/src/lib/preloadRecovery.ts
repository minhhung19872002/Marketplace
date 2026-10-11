/**
 * A lazy page whose code fails to load — a deploy replaced the hashed chunks under a tab opened before it, or a proxy
 * answered with an error / the wrong type (G5-A §3) — fires `vite:preloadError`; unhandled, the page stays blank.
 * Reload once to pick up the new index.html. At most one reload per minute per tab, so a lasting failure shows its
 * error instead of looping.
 */
export const PRELOAD_RELOAD_KEY = 'sh:preload-reload-at';
const MIN_GAP_MS = 60_000;

interface Store {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

/** true when the page should reload now (and the time is noted); false when it reloaded under a minute ago. */
export const shouldReloadAfterPreloadError = (store: Store | null, now: number): boolean => {
  try {
    if (!store) return true;
    const last = store.getItem(PRELOAD_RELOAD_KEY);
    if (last !== null && now - Number(last) < MIN_GAP_MS) return false;
    store.setItem(PRELOAD_RELOAD_KEY, String(now));
    return true;
  } catch {
    // Storage blocked (private mode): one reload is still better than a blank page
    return true;
  }
};

/** Listens for `vite:preloadError` and reloads the page once (see above). */
export const installPreloadRecovery = (): void => {
  window.addEventListener('vite:preloadError', (event) => {
    let store: Store | null = null;
    try {
      store = window.sessionStorage;
    } catch {
      store = null;
    }
    if (!shouldReloadAfterPreloadError(store, Date.now())) return;
    event.preventDefault();
    window.location.reload();
  });
};
