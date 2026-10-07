import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { pageTitle } from '../lib/pageTitle';
import { SRC_ROOT } from './scan';

// F6: the browser tab names the page — product, shop, category, search, orders — not the static title of index.html
describe('page title', () => {
  it('is "<page> | <platform>", the platform alone without a page', () => {
    expect(pageTitle('Áo thun cotton', 'ShopHub')).toBe('Áo thun cotton | ShopHub');
    expect(pageTitle('  ', 'ShopHub')).toBe('ShopHub');
  });

  it('every page that names something sets it', () => {
    const pages: Record<string, number> = {
      'pages/ProductDetail.tsx': 1,
      'pages/ShopPage.tsx': 1,
      // search + category
      'pages/SearchResults.tsx': 2,
      // order list + order detail
      'pages/account/CommercePages.tsx': 2,
      'pages/NotFoundPage.tsx': 1,
    };
    for (const [file, calls] of Object.entries(pages)) {
      const src = readFileSync(join(SRC_ROOT, file), 'utf8');
      expect((src.match(/usePageTitle\(/g) ?? []).length, file).toBeGreaterThanOrEqual(calls);
    }
  });
});
