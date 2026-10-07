import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { SRC_ROOT } from './scan';

// F5: an unknown address shows "Không tìm thấy" (with a search box), never a blank layout
describe('not found', () => {
  it('the router ends with a catch-all route to the not-found page', () => {
    const app = readFileSync(join(SRC_ROOT, 'App.tsx'), 'utf8');
    expect(app).toMatch(/<Route path="\*" element={<NotFoundPage \/>} \/>/);
    expect(readFileSync(join(SRC_ROOT, 'pages', 'NotFoundPage.tsx'), 'utf8')).toMatch(/role="search"/);
  });
});
