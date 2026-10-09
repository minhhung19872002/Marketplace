import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { marketingApi } from '../api/marketing';
import { handleImgError, isImageUrl, sizedImage } from '../lib/image';
import { BannerLink } from './Banner';

const SEEN_KEY = 'sh_popup_seen';
// It never covers the page on arrival (G3 A5): it waits for the visitor to scroll a little, or for 8 seconds
export const POPUP_DELAY_MS = 8_000;
const POPUP_SCROLL_PX = 400;

/** Promotional popup, shown again only after POPUP.FREQUENCY_HOURS (remembered in this browser; harmless if storage fails). */
const HomePopup = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const [open, setOpen] = useState(false);
  const popup = data?.popup;

  useEffect(() => {
    if (!popup || !data) return;
    // Any popup seen within the frequency window counts: the visitor is not bothered again, whatever the campaign
    let seen: { at: number } | null = null;
    try {
      seen = JSON.parse(localStorage.getItem(SEEN_KEY) ?? 'null');
    } catch {
      seen = null;
    }
    if (seen && Date.now() - seen.at < data.popupFrequencyHours * 3_600_000) return undefined;
    let done = false;
    const show = () => {
      if (done) return;
      done = true;
      cleanup();
      setOpen(true);
      try {
        localStorage.setItem(SEEN_KEY, JSON.stringify({ at: Date.now() }));
      } catch {
        // private mode: the popup just shows again next time
      }
    };
    const onScroll = () => {
      if (window.scrollY >= POPUP_SCROLL_PX) show();
    };
    const timer = window.setTimeout(show, POPUP_DELAY_MS);
    window.addEventListener('scroll', onScroll, { passive: true });
    const cleanup = () => {
      window.clearTimeout(timer);
      window.removeEventListener('scroll', onScroll);
    };
    return cleanup;
  }, [popup, data]);

  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  if (!open || !popup) return null;
  return (
    <div className="home-popup" role="dialog" aria-modal="true" aria-label={popup.title} data-testid="home-popup"
      onClick={(e) => e.target === e.currentTarget && setOpen(false)}>
      <div className="home-popup-box">
        <button className="home-popup-close" onClick={() => setOpen(false)} aria-label="Đóng">×</button>
        <BannerLink to={popup.link} className="home-popup-link">
          {isImageUrl(popup.imageUrl) && <img src={sizedImage(popup.imageUrl, 600)} alt="" onError={handleImgError} />}
          <strong>{popup.title}</strong>
        </BannerLink>
      </div>
    </div>
  );
};

export default HomePopup;
