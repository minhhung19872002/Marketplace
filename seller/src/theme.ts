import type { ThemeConfig } from 'antd'

// Brand colour tokens — change here, never hard-code colours in TSX
export const palette = {
  // WCAG AA: 5:1 with white text / on white
  primary: '#c93d19',
  // Second chart series and positive / negative change
  secondary: '#1f5fb8',
  up: '#13776b',
  down: '#d0011b',
} as const

export const theme: ThemeConfig = {
  token: {
    colorPrimary: palette.primary,
    // Secondary / hint text at 4.5:1 or more on white (antd defaults are ~3:1)
    colorTextSecondary: 'rgba(0, 0, 0, 0.62)',
    colorTextTertiary: 'rgba(0, 0, 0, 0.6)',
    colorTextDescription: 'rgba(0, 0, 0, 0.6)',
    colorTextPlaceholder: 'rgba(0, 0, 0, 0.55)',
    fontFamily: "'Be Vietnam Pro', Inter, system-ui, sans-serif",
  },
}
