import type { NavigateFunction } from 'react-router-dom';

/** Gateway redirects: VNPay / MoMo pages are absolute URLs (leave the app), the simulated gateway is a route. */
export const goTo = (navigate: NavigateFunction, url: string) => {
  if (/^https?:\/\//i.test(url)) window.location.assign(url);
  else navigate(url);
};

export type ViewSource = 'Direct' | 'Home' | 'Search' | 'Category' | 'Shop' | 'Campaign' | 'External' | 'Other';

let previousPath: string | null = null;
let currentPath: string | null = null;

/** Called on every route change (ScrollToTop) — remembers where the buyer came from inside the app. */
export const notePath = (path: string) => {
  if (path === currentPath) return;
  previousPath = currentPath;
  currentPath = path;
};

/** Where a product view came from (seller analytics: nguồn truy cập). */
export const viewSource = (): ViewSource => {
  const p = previousPath;
  if (p === null) {
    // First page of the visit: another site, or typed / bookmarked
    if (!document.referrer) return 'Direct';
    try {
      return new URL(document.referrer).host === window.location.host ? 'Other' : 'External';
    } catch {
      return 'Direct';
    }
  }
  if (p === '/') return 'Home';
  if (p.startsWith('/tim-kiem')) return 'Search';
  if (p.startsWith('/danh-muc')) return 'Category';
  if (p.startsWith('/shop/')) return 'Shop';
  if (p.startsWith('/su-kien')) return 'Campaign';
  return 'Other';
};
