import type { NavigateFunction } from 'react-router-dom';

/** Gateway redirects: VNPay / MoMo pages are absolute URLs (leave the app), the simulated gateway is a route. */
export const goTo = (navigate: NavigateFunction, url: string) => {
  if (/^https?:\/\//i.test(url)) window.location.assign(url);
  else navigate(url);
};
