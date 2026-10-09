import { StrictMode } from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { marketingApi, type HomeBanners } from './api/marketing'
import './index.css'
// Be Vietnam Pro bundled with the app (full Vietnamese glyphs, no request to a font CDN) — spec 6.5, F1
import '@fontsource/be-vietnam-pro/400.css'
import '@fontsource/be-vietnam-pro/500.css'
import '@fontsource/be-vietnam-pro/600.css'
import '@fontsource/be-vietnam-pro/700.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false },
  },
})

// Home page: ask for the banners while the page's code is still loading, and start the first hero image as soon as its
// address is known — it is the largest paint of the page (G-VIS: LCP 3.2 s → 5.3 s when it waited for the home chunk)
if (window.location.pathname === '/') {
  void queryClient.fetchQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 })
    .then((banners: HomeBanners) => {
      const hero = banners.main[0]?.imageUrl
      if (!hero || !/^(https?:)?\/\/|^\//.test(hero)) return
      const link = document.createElement('link')
      link.rel = 'preload'
      link.as = 'image'
      link.href = hero
      link.setAttribute('fetchpriority', 'high')
      document.head.append(link)
    })
    .catch(() => undefined)
}

const root = document.getElementById('root')
if (!root) throw new Error('Missing #root element')

ReactDOM.createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>
)
