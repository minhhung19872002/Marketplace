import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { SRC_ROOT } from './scan';

// WCAG 2.1 relative luminance and contrast ratio
const luminance = (hex: string) => {
  const h = hex.replace('#', '');
  const full = h.length === 3 ? h.split('').map((c) => c + c).join('') : h;
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
export const contrast = (a: string, b: string) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

const css = readFileSync(join(SRC_ROOT, 'index.css'), 'utf8');
const token = (name: string) => {
  const m = css.match(new RegExp(`--${name}:\s*([^;]+);`));
  if (!m) throw new Error(`thiếu token --${name}`);
  return m[1].trim();
};
const stops = (gradient: string) => gradient.match(/#[0-9a-fA-F]{3,6}\b/g) ?? [];

// Spec 6.5: text / background pairs of the design tokens reach WCAG AA (4.5:1 normal text)
describe('contrast', () => {
  const white = '#ffffff';
  const page = token('bg');

  it('brand colour as text on white and on the page background', () => {
    expect(contrast(token('sh-primary'), white)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token('sh-primary'), page)).toBeGreaterThanOrEqual(4.5);
  });

  it('white text on the brand colour, the brand bar and the banner tones', () => {
    expect(contrast(token('sh-on-primary'), token('sh-primary'))).toBeGreaterThanOrEqual(4.5);
    for (const name of ['sh-gradient-brand', 'sh-tone-primary', 'sh-tone-mall'])
      for (const stop of stops(token(name))) expect(contrast(white, stop), `${name} ${stop}`).toBeGreaterThanOrEqual(4.5);
  });

  it('muted and body text on white and on the page background', () => {
    for (const name of ['sh-text-muted', 'text-light', 'text']) {
      expect(contrast(token(name), white), name).toBeGreaterThanOrEqual(4.5);
      expect(contrast(token(name), page), name).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('notice text on the soft brand background (vacation banner, E5)', () => {
    expect(contrast(token('sh-primary-dark'), token('sh-primary-soft'))).toBeGreaterThanOrEqual(4.5);
  });

  it('the keyboard focus ring stands out from white (non-text 3:1)', () => {
    expect(contrast(token('sh-focus'), white)).toBeGreaterThanOrEqual(3);
  });
});
