import { describe, expect, it } from 'vitest';
import { findLines, sourceFiles } from './scan';

// Every HTTP path lives in src/api — components and pages never spell '/api/...'
describe('api-paths', () => {
  it('writes /api/ strings only under src/api', () => {
    const files = sourceFiles(['.ts', '.tsx']).filter((f) => !/[\\/]src[\\/]api[\\/]/.test(f));
    const offenders = findLines(files, /['"`]\/api\//);
    expect(offenders).toEqual([]);
  });
});
