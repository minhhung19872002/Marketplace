import { describe, expect, it } from 'vitest';
import { findLines, sourceFiles } from './scan';

// E9: an in-app path through <a href> reloads the whole page (and loses the cart / query cache) — use <Link> or
// BannerLink. Links set by the platform (banner.link, shortcut.link…) may be internal, so they go through BannerLink too.
describe('internal links', () => {
  it('no <a> points at an internal path or at a platform-set link', () => {
    const offenders = findLines(sourceFiles(['.tsx']), /<a\b[^>]*\bhref=(["']\/(?!\/)|\{\s*["'`]\/(?!\/)|\{[\w?.]*\.link\b)/)
      // BannerLink itself is where the https case falls back to <a>
      .filter((l) => !l.startsWith('components/Banner.tsx:'));
    expect(offenders).toEqual([]);
  });
});
