import type { ThemeConfig } from 'antd'

// Brand colour tokens — change here, never hard-code colours in TSX (same tokens as seller/src/theme.ts and web `--sh-*`)
export const palette = {
  // WCAG AA: 5:1 with white text / on white
  primary: '#c93d19',
  primaryDark: '#a8300f',
  // Bright brand orange for decoration only (white text on it is 3.6:1)
  primaryBright: '#ee4d2d',
  // Second chart series and positive / negative change
  secondary: '#1f5fb8',
  up: '#13776b',
  down: '#d0011b',
} as const

export const radius = { sm: 4, md: 8, lg: 12 } as const

export type ThemeMode = 'light' | 'dark'

/** CSS variables per mode, written on <html> (App.css reads them) — the one source for colours used outside Ant Design. */
export const cssVars: Record<ThemeMode, Record<string, string>> = {
  light: {
    '--sh-primary': palette.primary,
    '--sh-primary-dark': palette.primaryDark,
    '--sh-primary-bright': palette.primaryBright,
    '--sh-primary-soft': '#fff6f3',
    '--sh-on-primary': '#fff',
    '--sh-gradient-brand': `linear-gradient(135deg, #d8381a, ${palette.primary} 55%, ${palette.primaryDark})`,
    '--sh-bg': '#f5f5f5',
    '--sh-surface': '#fff',
    '--sh-surface-muted': '#fafafa',
    '--sh-border': '#f0f0f0',
    '--sh-border-strong': '#d9d9d9',
    '--sh-text': 'rgba(0, 0, 0, 0.88)',
    '--sh-text-muted': '#666',
    '--sh-up': palette.up,
    '--sh-up-soft': '#e8f6ee',
    '--sh-down': palette.down,
    '--sh-down-soft': '#fdecec',
    '--sh-info': palette.secondary,
    '--sh-info-soft': '#eaf2fd',
    '--sh-warning': '#9a5b00',
    '--sh-warning-soft': '#fff7e6',
    '--sh-chat-own': '#ffe8de',
    '--sh-chat-buyer': '#fff',
    '--sh-chat-system': '#eef3ff',
    '--sh-shadow-sm': '0 1px 2px rgba(0, 0, 0, 0.06), 0 1px 1px rgba(0, 0, 0, 0.04)',
    '--sh-shadow-md': '0 4px 16px rgba(0, 0, 0, 0.1)',
    '--sh-radius-sm': `${radius.sm}px`,
    '--sh-radius-md': `${radius.md}px`,
    '--sh-radius-lg': `${radius.lg}px`,
  },
  dark: {
    '--sh-primary': '#f07a55',
    '--sh-primary-dark': palette.primary,
    '--sh-primary-bright': palette.primaryBright,
    '--sh-primary-soft': '#3a1d14',
    '--sh-on-primary': '#fff',
    '--sh-gradient-brand': `linear-gradient(135deg, #b8341a, ${palette.primaryDark} 60%, #6e1f0a)`,
    '--sh-bg': '#000',
    '--sh-surface': '#141414',
    '--sh-surface-muted': '#1f1f1f',
    '--sh-border': '#303030',
    '--sh-border-strong': '#424242',
    '--sh-text': 'rgba(255, 255, 255, 0.85)',
    '--sh-text-muted': 'rgba(255, 255, 255, 0.65)',
    '--sh-up': '#4fc59a',
    '--sh-up-soft': '#12301f',
    '--sh-down': '#ff7875',
    '--sh-down-soft': '#3a1517',
    '--sh-info': '#69a7ff',
    '--sh-info-soft': '#13243d',
    '--sh-warning': '#f0b13a',
    '--sh-warning-soft': '#33260c',
    '--sh-chat-own': '#4a2418',
    '--sh-chat-buyer': '#1f1f1f',
    '--sh-chat-system': '#1b2438',
    '--sh-shadow-sm': '0 1px 2px rgba(0, 0, 0, 0.5)',
    '--sh-shadow-md': '0 4px 16px rgba(0, 0, 0, 0.6)',
    '--sh-radius-sm': `${radius.sm}px`,
    '--sh-radius-md': `${radius.md}px`,
    '--sh-radius-lg': `${radius.lg}px`,
  },
}

export const theme: ThemeConfig = {
  token: {
    colorPrimary: palette.primary,
    colorLink: palette.primary,
    colorSuccess: palette.up,
    colorError: palette.down,
    // Success alerts / tags tinted green, not the grey the teal brand green derives
    colorSuccessBg: '#e8f6ee',
    colorSuccessBorder: '#a8dcc0',
    borderRadius: radius.md,
    borderRadiusLG: radius.lg,
    borderRadiusSM: radius.sm,
    // Secondary / hint text at 4.5:1 or more on white (antd defaults are ~3:1)
    colorTextSecondary: 'rgba(0, 0, 0, 0.62)',
    colorTextTertiary: 'rgba(0, 0, 0, 0.6)',
    colorTextDescription: 'rgba(0, 0, 0, 0.6)',
    colorTextPlaceholder: 'rgba(0, 0, 0, 0.55)',
    fontFamily: "'Be Vietnam Pro', Inter, system-ui, sans-serif",
  },
  components: {
    Layout: { headerBg: '#fff', siderBg: '#fff', bodyBg: '#f5f5f5' },
    Menu: { itemSelectedBg: '#fff6f3', itemSelectedColor: palette.primary, itemBorderRadius: radius.md },
    Card: { borderRadiusLG: radius.lg },
  },
}

/** Dark tokens; the dark algorithm itself is added where the ConfigProvider is mounted (main.tsx). */
export const darkTheme: ThemeConfig = {
  token: {
    // Buttons keep the AA brand colour (white text on it); links / selected text use the lighter tint (5.6:1 on #141414)
    colorPrimary: palette.primary,
    colorLink: '#f07a55',
    colorSuccess: '#4fc59a',
    colorError: '#ff7875',
    borderRadius: radius.md,
    borderRadiusLG: radius.lg,
    borderRadiusSM: radius.sm,
    // Secondary / hint / empty-state text at 4.5:1 or more on #000 and #141414 (G4-A: axe found 2.2–4.4:1 in dark mode)
    colorTextSecondary: 'rgba(255, 255, 255, 0.68)',
    colorTextTertiary: 'rgba(255, 255, 255, 0.62)',
    colorTextDescription: 'rgba(255, 255, 255, 0.62)',
    colorTextPlaceholder: 'rgba(255, 255, 255, 0.55)',
    colorTextQuaternary: 'rgba(255, 255, 255, 0.55)',
    colorTextDisabled: 'rgba(255, 255, 255, 0.5)',
    fontFamily: "'Be Vietnam Pro', Inter, system-ui, sans-serif",
  },
  components: {
    // Selected tab text in the light tint: the AA brand colour is only 2.9:1 on the dark surface
    Tabs: { itemSelectedColor: '#f07a55', itemActiveColor: '#f07a55', itemHoverColor: '#ff9c7a', inkBarColor: '#f07a55' },
    Layout: { headerBg: '#141414', siderBg: '#141414', bodyBg: '#000' },
    Menu: { itemSelectedBg: '#3a1d14', itemSelectedColor: '#f07a55', itemBorderRadius: radius.md, darkItemBg: '#141414' },
    Card: { borderRadiusLG: radius.lg },
  },
}
