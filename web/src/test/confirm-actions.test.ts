import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { rel, sourceFiles, stripComments } from './scan';

// F2 (spec 6.5): every "Xoá" / "Huỷ" asks first — through ConfirmButton. A button that only opens its own dialog (with a reason
// box…) says data-confirm="dialog"; one that only drops a row of a form not saved yet says data-confirm="local".
const BUTTON = /<(button|Button)\b((?:=>|[^>])*?)>\s*(Xoá|Xóa|Huỷ|Hủy)/g;

describe('confirm before delete / cancel', () => {
  it('no delete or cancel button acts without asking', () => {
    const offenders: string[] = [];
    for (const file of sourceFiles(['.tsx'])) {
      const src = stripComments(readFileSync(file, 'utf8'));
      for (const m of src.matchAll(BUTTON)) {
        if (/data-confirm="(dialog|local)"/.test(m[2])) continue;
        const before = src.slice(Math.max(0, (m.index ?? 0) - 400), m.index);
        const open = before.lastIndexOf('<Popconfirm');
        if (open >= 0 && before.indexOf('</Popconfirm>', open) < 0) continue;
        offenders.push(`${rel(file)}:${src.slice(0, m.index).split('\n').length}: ${m[3]}`);
      }
    }
    expect(offenders).toEqual([]);
  });
});
