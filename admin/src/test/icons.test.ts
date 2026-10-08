import { describe, expect, it } from 'vitest';
import { findLines, sourceFiles } from './scan';

// Icons are SVG (@ant-design/icons) — never emoji or dingbat characters (UI upgrade G0). Covers pictographs, dingbats,
// misc symbols (★ ♡ ⚡ ⚑ ✓ ✕ ⚠ ☰) and geometric shapes used as arrows (▲ ▾ ▶).
const ICON_CHARS = /[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}\u{25A0}-\u{25FF}\u{2B50}\u{2B06}\u{2B07}\u{21C5}]/u;

describe('icons', () => {
  it('has no emoji / symbol characters used as icons in TS/TSX', () => {
    expect(findLines(sourceFiles(['.tsx', '.ts']), ICON_CHARS)).toEqual([]);
  });
});
