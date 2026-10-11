import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { pageTitle } from '../lib/pageTitle';
import { SRC_ROOT, stripComments } from './scan';

// Route components named by something else: why each one needs no call of its own
const NAMED_ELSEWHERE: Record<string, string> = {
  'pages/Login default': 'AuthHeader names the sign-in pages',
  'pages/Register default': 'AuthHeader names the sign-in pages',
  'pages/ForgotPassword default': 'AuthHeader names the sign-in pages',
  'pages/SearchResults SearchResults': 'renders SearchView, which names the tab',
  'pages/account/AccountLayout default': 'a layout: every child route names the tab',
};

/** The body of a component of a source file: from its declaration to the next top-level declaration. */
const bodyOf = (src: string, name: string): string => {
  const start = new RegExp(`^(?:export )?(?:const|function) ${name}\\b[^\\n]*\\n`, 'm').exec(src);
  if (!start) return '';
  const rest = src.slice(start.index + start[0].length);
  const end = /^(?:export )?(?:const|function) \w+/m.exec(rest);
  return end ? rest.slice(0, end.index) : rest;
};

// F6: the browser tab names the page — product, shop, category, search, orders — not the static title of index.html
describe('page title', () => {
  it('is "<page> | <platform>", the platform alone without a page', () => {
    expect(pageTitle('Áo thun cotton', 'ShopHub')).toBe('Áo thun cotton | ShopHub');
    expect(pageTitle('  ', 'ShopHub')).toBe('ShopHub');
  });

  // G5-B: every page of the router — a page that sets nothing kept the title of the page before it (a product's name
  // on the home page, after "back")
  it('every route of App.tsx names the browser tab', () => {
    const app = readFileSync(join(SRC_ROOT, 'App.tsx'), 'utf8');
    const routes = [...app.matchAll(/lazy\(\(\) => import\('\.\/([^']+)'\)(?:\.then\(\(m\) => \(\{ default: m\.(\w+) \}\)\))?\)/g)]
      .map((m) => ({ file: m[1], named: m[2] }));
    expect(routes.length).toBeGreaterThan(30);
    const offenders = routes.filter(({ file, named }) => {
      const src = stripComments(readFileSync(join(SRC_ROOT, `${file}.tsx`), 'utf8'));
      const name = named ?? /export default (\w+);/.exec(src)?.[1] ?? '';
      if (NAMED_ELSEWHERE[`${file} ${named ?? 'default'}`] || NAMED_ELSEWHERE[`${file} ${name}`]) return false;
      return !/use(?:Page|Home)Title\(/.test(bodyOf(src, name));
    }).map(({ file, named }) => `${file} ${named ?? 'default'}`);
    expect(offenders).toEqual([]);
  });

  it('every exception still names a route', () => {
    const app = readFileSync(join(SRC_ROOT, 'App.tsx'), 'utf8');
    for (const key of Object.keys(NAMED_ELSEWHERE)) expect(app, key).toContain(`./${key.split(' ')[0]}'`);
  });
});
