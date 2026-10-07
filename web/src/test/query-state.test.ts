import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { rel, sourceFiles, stripComments } from './scan';

// F3 (spec 6.5): a page that loads data shows loading / error + "Thử lại" / empty through QueryState — a network error
// must never look like an empty list
describe('query state', () => {
  it('every page that calls useQuery renders QueryState', () => {
    const offenders = sourceFiles(['.tsx'])
      .filter((f) => rel(f).startsWith('pages/'))
      .filter((f) => {
        const src = stripComments(readFileSync(f, 'utf8'));
        return src.includes('useQuery(') && !src.includes('<QueryState');
      })
      .map(rel);
    expect(offenders).toEqual([]);
  });
});
