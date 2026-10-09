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
    for (const name of ['sh-gradient-brand', 'sh-tone-primary', 'sh-tone-mall', 'sh-gradient-voucher', 'sh-gradient-freeship'])
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

  it('discount tag text on the yellow tag', () => {
    expect(contrast(token('sh-deal-tag-text'), token('sh-deal-tag'))).toBeGreaterThanOrEqual(4.5);
  });

  it('Flash Sale bar text on every stop of its fill', () => {
    for (const stop of stops(token('sh-flash-fill'))) expect(contrast(token('sh-flash-text'), stop), stop).toBeGreaterThanOrEqual(4.5);
  });

  // ---- Documented exceptions (G-VIS, user decision docs/00 #203: look like the reference marketplace) ----

  // The bright header gradient: white reads 3.76:1 at the top and 2.92:1 at the very bottom. Text never sits in the bottom
  // 13 %: the top bar (14 px, weight 500) lies in the first 34 of 120 px and the 12 px hot keywords end at 104 px (header
  // layout in Header.css, G-VIS block). Exception: ≥ 3:1 instead of 4.5:1 for that text (top bar ≥ 14 px, bolder;
  // hot keywords kept at 12 px by the user's spec).
  it('EXCEPTION: white header text on the bright gradient is ≥ 3:1 where text sits', () => {
    const [top = '#000000', bottom = '#000000'] = stops(token('sh-gradient-header'));
    const mix = (t: number) => '#' + [1, 3, 5].map((i) => Math.round(parseInt(top.slice(i, i + 2), 16) * (1 - t) + parseInt(bottom.slice(i, i + 2), 16) * t)
      .toString(16).padStart(2, '0')).join('');
    for (const t of [0, 34 / 120, 104 / 120]) expect(contrast(white, mix(t)), `t=${t.toFixed(2)}`).toBeGreaterThanOrEqual(3);
  });

  // Prices: the spec's #ee4d2d is only 3.66:1 — axe (e2e a11y) refused it on every card; #d43b21 looks the same and is AA
  // on white and on the #fafafa price block of the product page
  it('the price colour is AA on white and on the price block', () => {
    expect(contrast(token('sh-price'), white)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(token('sh-price'), '#fafafa')).toBeGreaterThanOrEqual(4.5);
  });

  // Flash Sale bar (G-VIS round 2): the label is drawn twice — dark orange on the pale track, white clipped to the fill
  it('Flash Sale bar label: dark on the track, white on the fill', () => {
    expect(contrast(token('sh-flash-text-track'), token('sh-flash-track'))).toBeGreaterThanOrEqual(4.5);
    const flashCss = readFileSync(join(SRC_ROOT, 'components/FlashSaleBlock.css'), 'utf8');
    expect(flashCss).toMatch(/\.flash-item-bar-text \{[^}]*color: var\(--sh-flash-text-track\)/);
    expect(flashCss).toMatch(/\.flash-item-bar-text--on-fill \{[^}]*color: var\(--sh-flash-text\)/);
  });

  it('home section titles and the Flash Sale wordmark (large text, 3:1) read on white', () => {
    expect(contrast(token('sh-section-title'), white)).toBeGreaterThanOrEqual(4.5);
    for (const stop of stops(token('sh-flash-wordmark'))) expect(contrast(stop, white), stop).toBeGreaterThanOrEqual(3);
  });

  it('the keyboard focus ring stands out from white (non-text 3:1)', () => {
    expect(contrast(token('sh-focus'), white)).toBeGreaterThanOrEqual(3);
  });
});
