import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { SRC_ROOT } from './scan';

// F1 (spec 6.5): Be Vietnam Pro is both declared first and really bundled (weights 400–700), so Vietnamese text never
// falls back to whatever the operating system has
describe('font', () => {
  it('declares Be Vietnam Pro first', () => {
    const declared = readFileSync(join(SRC_ROOT, 'index.css'), 'utf8');
    expect(declared).toMatch(/font-?family:\s*["']?\s*["']Be Vietnam Pro["']/i);
  });

  it('imports the bundled font files, every weight in use', () => {
    const main = readFileSync(join(SRC_ROOT, 'main.tsx'), 'utf8');
    for (const weight of [400, 500, 600, 700]) expect(main, String(weight)).toContain(`@fontsource/be-vietnam-pro/${weight}.css`);
  });
});
