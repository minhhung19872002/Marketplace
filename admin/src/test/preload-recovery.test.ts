import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { PRELOAD_RELOAD_KEY, shouldReloadAfterPreloadError } from '../lib/preloadRecovery';
import { SRC_ROOT } from './scan';

const memory = () => {
  const data = new Map<string, string>();
  return { getItem: (k: string) => data.get(k) ?? null, setItem: (k: string, v: string) => void data.set(k, v), data };
};

// G5-A §3: a chunk that fails to load (new deploy, a proxy error) reloads the page once instead of leaving it blank
describe('preload recovery', () => {
  it('reloads on the first failure, not again within a minute, again after it', () => {
    const store = memory();
    expect(shouldReloadAfterPreloadError(store, 1_000_000)).toBe(true);
    expect(store.data.get(PRELOAD_RELOAD_KEY)).toBe('1000000');
    expect(shouldReloadAfterPreloadError(store, 1_030_000)).toBe(false);
    expect(shouldReloadAfterPreloadError(store, 1_061_000)).toBe(true);
  });

  it('still reloads once when the storage cannot be used', () => {
    const blocked = { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); } };
    expect(shouldReloadAfterPreloadError(blocked, 5)).toBe(true);
    expect(shouldReloadAfterPreloadError(null, 5)).toBe(true);
  });

  it('is installed by main.tsx', () => {
    expect(readFileSync(join(SRC_ROOT, 'main.tsx'), 'utf8')).toMatch(/installPreloadRecovery\(\)/);
  });
});
