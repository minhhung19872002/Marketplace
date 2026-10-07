import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { SRC_ROOT, rel, sourceFiles, stripComments } from './scan';

// Pages whose tab is read from ?tab= — the keys of their TABS list are the only valid values (D1)
const TAB_PAGES: Record<string, string> = {
  '/san-pham': 'pages/ProductsPage.tsx',
  '/don-hang': 'pages/OrdersPage.tsx',
};

const tabsOf = (page: string): string[] => {
  const src = stripComments(readFileSync(join(SRC_ROOT, page), 'utf8'));
  const start = src.indexOf('= [', src.indexOf('const TABS'));
  const list = src.slice(start, src.indexOf('\n]', start));
  return [...list.matchAll(/key: '(\w+)'/g)].map((m) => m[1]);
};

describe('tab links', () => {
  it('every in-app link to "?tab=" names a tab that page really has', () => {
    const offenders: string[] = [];
    for (const file of sourceFiles(['.ts', '.tsx'])) {
      const src = stripComments(readFileSync(file, 'utf8'));
      for (const m of src.matchAll(/['"`](\/[\w-]+)\?tab=(\w+)/g)) {
        const page = TAB_PAGES[m[1]];
        if (!page) offenders.push(`${rel(file)}: ${m[1]}?tab=${m[2]} — trang này không đọc ?tab=`);
        else if (!tabsOf(page).includes(m[2])) offenders.push(`${rel(file)}: ${m[1]}?tab=${m[2]} — không có tab "${m[2]}"`);
      }
    }
    expect(offenders).toEqual([]);
  });

  it('the pages listed read their tab from the URL', () => {
    for (const page of Object.values(TAB_PAGES)) {
      expect(readFileSync(join(SRC_ROOT, page), 'utf8'), page).toMatch(/useSearchParams\(\)/);
    }
  });
});
