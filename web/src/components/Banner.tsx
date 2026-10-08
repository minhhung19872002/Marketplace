import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight, Ticket, Truck } from 'lucide-react';
import { marketingApi } from '../api/marketing';
import { handleImgError, isImageUrl } from '../lib/image';
import './Banner.css';

/** Internal paths stay in the app; https links (only kind the admin may set besides "/...") open in a new tab. */
export const BannerLink = ({ to, className, children, testId, label }: { to: string; className?: string; children: ReactNode; testId?: string; label?: string }) =>
  to.startsWith('/') ? (
    <Link to={to} className={className} data-testid={testId} aria-label={label}>{children}</Link>
  ) : (
    <a href={to} className={className} target="_blank" rel="noopener noreferrer" data-testid={testId} aria-label={label}>{children}</a>
  );

const SIDE_ART = [
  { icon: Ticket, tone: 'voucher', kicker: 'Ưu đãi hôm nay' },
  { icon: Truck, tone: 'freeship', kicker: 'Giao hàng' },
];

/**
 * Home carousel + two side banners, scheduled by the platform (admin → Marketing → Banner).
 * Main slides: artwork at a fixed 2.4:1 ratio (object-fit: cover) with the title (≤ 2 lines, clamp() size) and the CTA
 * on a gradient scrim so white text stays readable; autoplay 5 s, paused on hover / focus; arrows and dots show on hover.
 * Side banners: the uploaded artwork, or a designed voucher / freeship card when none is set.
 */
const Banner = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const main = data?.main ?? [];
  const [active, setActive] = useState(0);
  const [paused, setPaused] = useState(false);

  const next = useCallback(() => setActive((i) => (main.length === 0 ? 0 : (i + 1) % main.length)), [main.length]);
  const prev = () => setActive((i) => (main.length === 0 ? 0 : (i - 1 + main.length) % main.length));

  useEffect(() => {
    if (main.length < 2 || paused) return undefined;
    const timer = setInterval(next, 5000);
    return () => clearInterval(timer);
  }, [next, main.length, paused]);

  // While loading, the same box (fixed ratio) holds the place so nothing below jumps when the slides arrive (CLS)
  if (!data) {
    return (
      <section className="banner" aria-hidden data-testid="home-banners-loading">
        <div className="banner-slider sh-skeleton" />
        <div className="banner-side"><span className="sh-skeleton" /><span className="sh-skeleton" /></div>
      </section>
    );
  }
  if (main.length === 0) return null;

  return (
    <section className="banner" aria-label="Khuyến mãi nổi bật" data-testid="home-banners">
      <div className="banner-slider" onMouseEnter={() => setPaused(true)} onMouseLeave={() => setPaused(false)}
        onFocus={() => setPaused(true)} onBlur={() => setPaused(false)}>
        {main.map((b, i) => (
          <BannerLink key={b.id} to={b.link} className={`banner-slide ${i === active ? 'active' : ''}`}>
            {isImageUrl(b.imageUrl)
              ? <img className="banner-image" src={b.imageUrl} alt="" onError={handleImgError} fetchPriority={i === 0 ? 'high' : 'auto'} />
              : <span className="banner-image banner-image--fallback" aria-hidden />}
            <span className="banner-scrim" aria-hidden />
            <span className="banner-content">
              <span className="banner-title">{b.title}</span>
              <span className="banner-cta">Mua Ngay</span>
            </span>
          </BannerLink>
        ))}

        {main.length > 1 && (
          <>
            <button type="button" className="banner-arrow banner-arrow-left" onClick={prev} aria-label="Banner trước"><ChevronLeft size={22} aria-hidden /></button>
            <button type="button" className="banner-arrow banner-arrow-right" onClick={next} aria-label="Banner sau"><ChevronRight size={22} aria-hidden /></button>
            <div className="banner-dots">
              {main.map((b, i) => (
                <button key={b.id} type="button" className={`banner-dot ${i === active ? 'active' : ''}`} onClick={() => setActive(i)}
                  aria-label={`Chuyển tới banner ${i + 1}`} aria-current={i === active} />
              ))}
            </div>
          </>
        )}
      </div>

      <div className="banner-side">
        {(data?.side ?? []).slice(0, 2).map((b, i) => {
          const art = SIDE_ART[i % SIDE_ART.length];
          const Icon = art.icon;
          return isImageUrl(b.imageUrl) ? (
            <BannerLink key={b.id} to={b.link} className="banner-side-item" label={b.title}>
              <img className="banner-image" src={b.imageUrl} alt="" onError={handleImgError} />
            </BannerLink>
          ) : (
            <BannerLink key={b.id} to={b.link} className={`banner-side-item banner-side-card banner-side-card--${art.tone}`}>
              <span className="banner-side-icon"><Icon size={30} strokeWidth={1.8} aria-hidden /></span>
              <span className="banner-side-text">
                <span className="banner-side-kicker">{art.kicker}</span>
                <span className="banner-side-title">{b.title}</span>
                <span className="banner-side-cta">Xem ngay <ChevronRight size={14} aria-hidden /></span>
              </span>
            </BannerLink>
          );
        })}
      </div>
    </section>
  );
};

export default Banner;
