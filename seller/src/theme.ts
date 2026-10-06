import type { ThemeConfig } from 'antd'

// Brand colour tokens — change here, never hard-code colours in TSX
export const palette = {
  primary: '#ee4d2d',
} as const

export const theme: ThemeConfig = {
  token: {
    colorPrimary: palette.primary,
    fontFamily: "'Be Vietnam Pro', Inter, system-ui, sans-serif",
  },
}
