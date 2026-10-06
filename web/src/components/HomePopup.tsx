import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { marketingApi } from '../api/marketing';
import { handleImgError, isImageUrl } from '../lib/image';
import { BannerLink } from './Banner';

const SEEN_KEY = 'sh_popup_seen';

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
    if (seen && Date.now() - seen.at < data.popupFrequencyHours * 3_600_000) return;
    setOpen(true);
    try {
      localStorage.setItem(SEEN_KEY, JSON.stringify({ at: Date.now() }));
    } catch {
      // private mode: the popup just shows again next time
    }
  }, [popup, data]);

  if (!open || !popup) return null;
  return (
    <div className="home-popup" role="dialog" aria-label={popup.title} data-testid="home-popup">
      <div className="home-popup-box">
        <button className="home-popup-close" onClick={() => setOpen(false)} aria-label="Đóng">×</button>
        <BannerLink to={popup.link} className="home-popup-link">
          {isImageUrl(popup.imageUrl) && <img src={popup.imageUrl} alt="" onError={handleImgError} />}
          <strong>{popup.title}</strong>
        </BannerLink>
      </div>
    </div>
  );
};

export default HomePopup;
