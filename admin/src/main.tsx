import { StrictMode, useLayoutEffect } from 'react'
import ReactDOM from 'react-dom/client'
import { ConfigProvider, theme as antdTheme } from 'antd'
import viVN from 'antd/locale/vi_VN'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { cssVars, darkTheme, theme } from './theme'
import { useThemeMode } from './stores/themeMode'
// Be Vietnam Pro bundled with the app (full Vietnamese glyphs, no request to a font CDN) — spec 6.5, F1
import '@fontsource/be-vietnam-pro/400.css'
import '@fontsource/be-vietnam-pro/500.css'
import '@fontsource/be-vietnam-pro/600.css'
import '@fontsource/be-vietnam-pro/700.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: false } },
})

// Light / dark theme: Ant Design tokens + the CSS variables App.css reads, both from theme.ts
const Themed = () => {
  const mode = useThemeMode((s) => s.mode)
  useLayoutEffect(() => {
    const el = document.documentElement
    for (const [name, value] of Object.entries(cssVars[mode])) el.style.setProperty(name, value)
    el.dataset.theme = mode
    el.style.colorScheme = mode
  }, [mode])
  return (
    <ConfigProvider locale={viVN}
      theme={mode === 'dark' ? { ...darkTheme, algorithm: antdTheme.darkAlgorithm } : theme}>
      <App />
    </ConfigProvider>
  )
}

const root = document.getElementById('root')
if (!root) throw new Error('Missing #root element')

ReactDOM.createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <Themed />
    </QueryClientProvider>
  </StrictMode>
)
