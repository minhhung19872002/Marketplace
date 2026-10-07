import { StrictMode } from 'react'
import ReactDOM from 'react-dom/client'
import { ConfigProvider } from 'antd'
import viVN from 'antd/locale/vi_VN'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { theme } from './theme'
// Be Vietnam Pro bundled with the app (full Vietnamese glyphs, no request to a font CDN) — spec 6.5, F1
import '@fontsource/be-vietnam-pro/400.css'
import '@fontsource/be-vietnam-pro/500.css'
import '@fontsource/be-vietnam-pro/600.css'
import '@fontsource/be-vietnam-pro/700.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: false } },
})

const root = document.getElementById('root')
if (!root) throw new Error('Missing #root element')

ReactDOM.createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ConfigProvider locale={viVN} theme={theme}>
        <App />
      </ConfigProvider>
    </QueryClientProvider>
  </StrictMode>
)
