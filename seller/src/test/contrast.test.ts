import { describe, expect, it } from 'vitest'
import { cssVars, darkTheme, palette, theme } from '../theme'

const luminance = (hex: string) => {
  const h = hex.replace('#', '')
  const full = h.length === 3 ? h.split('').map((c) => c + c).join('') : h
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}
const contrast = (a: string, b: string) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}
const hex = (n: number) => Math.round(n).toString(16).padStart(2, '0')
const full = (c: string) => (c.length === 4 ? '#' + [...c.slice(1)].map((x) => x + x).join('') : c)
const channels = (c: string) => [1, 3, 5].map((i) => parseInt(full(c).slice(i, i + 2), 16))
// rgba(r, g, b, a) text drawn on an opaque background → the colour the eye sees
const over = (rgba: string, bg: string) => {
  const m = /rgba\((\d+), (\d+), (\d+), ([0-9.]+)\)/.exec(rgba)
  if (!m) return rgba
  const a = Number(m[4])
  return '#' + channels(bg).map((b, i) => hex(Number(m[i + 1]) * a + b * (1 - a))).join('')
}
const mix = (top: string, bottom: string, t: number) => {
  const [a, b] = [channels(top), channels(bottom)]
  return '#' + a.map((v, i) => hex(v * (1 - t) + b[i] * t)).join('')
}
const AA = 4.5

// Spec 6.5: the theme's text / background pairs reach WCAG AA (4.5:1), measured in both modes
describe('contrast — light', () => {
  const white = '#ffffff'
  const v = cssVars.light

  it('brand colour on white and white on the brand colour (buttons)', () => {
    expect(contrast(palette.primary, white)).toBeGreaterThanOrEqual(AA)
  })

  it('chart / change colours used as text', () => {
    for (const c of [palette.secondary, palette.up, palette.down]) expect(contrast(c, white), c).toBeGreaterThanOrEqual(AA)
  })

  it('secondary, tertiary and placeholder text', () => {
    const t = theme.token!
    for (const c of [t.colorTextSecondary, t.colorTextTertiary, t.colorTextDescription])
      expect(contrast(over(String(c), white), white), String(c)).toBeGreaterThanOrEqual(AA)
  })

  it('price, success, warning and info text on white and on their soft backgrounds', () => {
    expect(contrast(v['--sh-price'], white)).toBeGreaterThanOrEqual(AA)
    expect(contrast(v['--sh-price'], v['--sh-surface-muted'])).toBeGreaterThanOrEqual(AA)
    for (const [fg, bg] of [['--sh-up', '--sh-up-soft'], ['--sh-warning', '--sh-warning-soft'], ['--sh-info', '--sh-info-soft']])
      expect(contrast(v[fg], v[bg]), `${fg} / ${bg}`).toBeGreaterThanOrEqual(AA)
    expect(contrast(String(theme.token!.colorSuccess), white)).toBeGreaterThanOrEqual(AA)
  })

  it('selected menu item on its tint', () => {
    expect(contrast(palette.primary, String(theme.components!.Menu!.itemSelectedBg))).toBeGreaterThanOrEqual(AA)
  })
})

describe('contrast — dark', () => {
  const v = cssVars.dark
  const surfaces = [v['--sh-bg'], v['--sh-surface'], v['--sh-surface-muted']]

  it('price, links and selected tabs on every dark surface', () => {
    const t = darkTheme.token!
    for (const bg of surfaces)
      for (const c of [v['--sh-price'], String(t.colorLink), String(darkTheme.components!.Tabs!.itemSelectedColor), v['--sh-primary']])
        expect(contrast(c, bg), `${c} on ${bg}`).toBeGreaterThanOrEqual(AA)
  })

  it('status colours on their soft backgrounds', () => {
    for (const [fg, bg] of [['--sh-up', '--sh-up-soft'], ['--sh-down', '--sh-down-soft'], ['--sh-warning', '--sh-warning-soft'], ['--sh-info', '--sh-info-soft']])
      expect(contrast(v[fg], v[bg]), `${fg} / ${bg}`).toBeGreaterThanOrEqual(AA)
  })

  it('secondary, tertiary and placeholder text', () => {
    const t = darkTheme.token!
    for (const bg of surfaces)
      for (const c of [t.colorTextSecondary, t.colorTextTertiary, t.colorTextDescription, t.colorTextPlaceholder])
        expect(contrast(over(String(c), bg), bg), `${String(c)} on ${bg}`).toBeGreaterThanOrEqual(AA)
  })

  it('selected menu item on its tint', () => {
    const m = darkTheme.components!.Menu!
    expect(contrast(String(m.itemSelectedColor), String(m.itemSelectedBg))).toBeGreaterThanOrEqual(AA)
  })
})

// ---- Documented exception (G-VIS, user decision docs/00 #203 / #208: the buyer site's bright header band) ----
// The 64 px band runs #f53d2d → #ff6633 top to bottom; white reads 3.76:1 at the top and 2.92:1 at the very bottom. Header
// content is centred: the tallest item (the 44 px account button) spans 10–54 px, so text never sits in the bottom 15 %.
// Exception: ≥ 3:1 instead of 4.5:1 there (names in weight 600, secondary lines 500). Same band in light and dark mode.
describe('contrast — header band (exception)', () => {
  for (const mode of ['light', 'dark'] as const) {
    it(`${mode}: white header text on the bright gradient is ≥ 3:1 where text sits`, () => {
      const v = cssVars[mode]
      const [top = '', bottom = ''] = v['--sh-gradient-header'].match(/#[0-9a-fA-F]{6}/g) ?? []
      expect([top, bottom]).toEqual([palette.headerTop, palette.headerBottom])
      for (const t of [0, 10 / 64, 32 / 64, 54 / 64]) expect(contrast(v['--sh-on-header'], mix(top, bottom, t)), `t=${t.toFixed(2)}`).toBeGreaterThanOrEqual(3)
    })
  }

  it('the avatar on the band: brand letter on a white disc', () => {
    expect(contrast(palette.primary, cssVars.light['--sh-surface'])).toBeGreaterThanOrEqual(AA)
    expect(contrast(cssVars.dark['--sh-primary'], cssVars.dark['--sh-surface'])).toBeGreaterThanOrEqual(AA)
  })
})
