import { describe, expect, it } from 'vitest';
import { findLines, sourceFiles } from './scan';

// D6 (L098): the bank list comes from the server (GET /api/site/banks) — never a copy written into a form
describe('bank catalogue', () => {
  it('no bank code or bank name is written in the source', () => {
    expect(findLines(sourceFiles(['.ts', '.tsx']), /['"`](VCB|TCB|BIDV|VTB|ACB|TPB|VPB|AGR|STB|HDB)['"`]|Vietcombank|Techcombank|VietinBank|Agribank|Sacombank/)).toEqual([]);
  });
});
