import { describe, expect, it } from 'vitest';
import { findLines, sourceFiles } from './scan';

// Colours go through tokens (CSS variables / theme) — never literal colour codes in TSX
describe('palette', () => {
  it('has no hard-coded colour codes in TSX', () => {
    const offenders = findLines(sourceFiles(['.tsx']), /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/);
    expect(offenders).toEqual([]);
  });
});
