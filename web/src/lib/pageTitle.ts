import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { contentApi } from '../api/content';

/** "<page> | <platform>" — the platform name comes from SITE.PLATFORM_NAME, not from the bundle. */
export const pageTitle = (title: string | null | undefined, platform: string): string =>
  title && title.trim() ? `${title.trim()} | ${platform}` : platform;

/** The browser tab follows the page (F6): product, shop, category, search, orders… */
export const usePageTitle = (title: string | null | undefined) => {
  const site = useQuery({ queryKey: ['site'], queryFn: contentApi.site, staleTime: 3_600_000 });
  const platform = site.data?.platformName ?? 'ShopHub';
  useEffect(() => {
    document.title = pageTitle(title, platform);
  }, [title, platform]);
};
