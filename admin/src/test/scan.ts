import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

// Source root of this package (src/)
export const SRC_ROOT = join(__dirname, '..');

/** Every source file under src/ with one of the given extensions (tests excluded). */
export function sourceFiles(exts: string[]): string[] {
  const out: string[] = [];
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const full = join(dir, name);
      if (statSync(full).isDirectory()) {
        if (name !== 'test') walk(full);
      } else if (exts.some((e) => name.endsWith(e)) && !name.includes('.test.')) {
        out.push(full);
      }
    }
  };
  walk(SRC_ROOT);
  return out;
}

export const rel = (file: string): string => relative(SRC_ROOT, file).split(sep).join('/');

// Drop // and /* */ comments so documentation never trips a rule
export const stripComments = (src: string): string =>
  src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1');

/** "file:line: text" for every line matching the pattern. */
export function findLines(files: string[], pattern: RegExp): string[] {
  return files.flatMap((file) =>
    stripComments(readFileSync(file, 'utf8'))
      .split('\n')
      .flatMap((line, i) => (pattern.test(line) ? [`${rel(file)}:${i + 1}: ${line.trim()}`] : [])),
  );
}
