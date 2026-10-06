import { describe, expect, it } from 'vitest'
import { palette, theme } from '../theme'

const luminance = (hex: string) => {
  const h = hex.replace('#', '')
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}
const contrast = (a: string, b: string) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}
// rgba(0, 0, 0, a) text drawn on white is the grey (1 - a) × 255
const onWhite = (rgba: string) => {
  const a = Number(/rgba\(0, 0, 0, ([0-9.]+)\)/.exec(rgba)?.[1])
  const v = Math.round((1 - a) * 255).toString(16).padStart(2, '0')
  return `#${v}${v}${v}`
}

// Spec 6.5: the theme's text / background pairs reach WCAG AA (4.5:1)
describe('contrast', () => {
  it('brand colour on white and white on the brand colour (buttons)', () => {
    expect(contrast(palette.primary, '#ffffff')).toBeGreaterThanOrEqual(4.5)
  })

  it('chart / change colours used as text', () => {
    for (const c of [palette.secondary, palette.up, palette.down]) expect(contrast(c, '#ffffff'), c).toBeGreaterThanOrEqual(4.5)
  })

  it('secondary, tertiary and placeholder text', () => {
    const t = theme.token!
    for (const v of [t.colorTextSecondary, t.colorTextTertiary, t.colorTextDescription])
      expect(contrast(onWhite(String(v)), '#ffffff'), String(v)).toBeGreaterThanOrEqual(4.5)
  })
})
