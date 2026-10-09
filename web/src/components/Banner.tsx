import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight, Ticket, Truck } from 'lucide-react';
import { marketingApi, type PublicBanner } from '../api/marketing';
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

interface BannerLike {
  title: string | null;
  imageUrl: string | null;
  hasTextInImage?: boolean;
}

/**
 * Whether a banner gets its title drawn over the artwork (G-VIS): never over artwork flagged as carrying its own text,
 * always on a banner without artwork; otherwise `byDefault` decides — the hero draws it (its seed art has no headline),
 * the side / strip / Mall banners do not (designed with their headline). The home API sends `hasTextInImage: false` for
 * every banner not flagged yet, so false is the default, not a promise that the art is text-free.
 */
export const bannerShowsTitle = (b: BannerLike, byDefault: boolean): boolean => {
  if (!b.title) return false;
  if (!b.imageUrl || !isImageUrl(b.imageUrl)) return true;
  if (b.hasTextInImage === true) return false;
  return byDefault;
};

/**
 * One banner, rendered the same way everywhere (hero, side, strip, Mall slides, campaign blocks): the artwork filling
 * its frame, and — only when bannerShowsTitle says so — the title (and a CTA) on a scrim so white text stays readable.
 */
export const BannerArt = ({ banner, to, className, titled, cta, eager = false, testId }: {
  banner: BannerLike; to: string; className?: string; titled: boolean; cta?: string; eager?: boolean; testId?: string;
}) => {
  const image = banner.imageUrl && isImageUrl(banner.imageUrl) ? banner.imageUrl : null;
  return (
    <BannerLink to={to} className={['banner-art', className].filter(Boolean).join(' ')} testId={testId}
      label={titled ? undefined : banner.title ?? undefined}>
      {image
        ? <img className="banner-image" src={image} alt="" onError={handleImgError} loading={eager ? 'eager' : 'lazy'}
          // React 18 warns on the camel-case prop: pass the plain HTML attribute (first hero slide only)
          {...(eager ? { fetchpriority: 'high' } : {})} />
        : <span className="banner-image banner-image--fallback" aria-hidden />}
      {titled && (
        <>
          <span className="banner-scrim" aria-hidden />
          <span className="banner-content">
            <span className="banner-title">{banner.title}</span>
            {cta && <span className="banner-cta">{cta}</span>}
          </span>
        </>
      )}
    </BannerLink>
  );
};

/** Carousel dots: small translucent white, the active one orange, centred at the bottom of the artwork. */
export const BannerDots = ({ count, active, onPick, label }: { count: number; active: number; onPick: (i: number) => void; label: string }) => (
  <div className="banner-dots">
    {Array.from({ length: count }, (_, i) => (
      <button key={i} type="button" className={`banner-dot ${i === active ? 'active' : ''}`} onClick={() => onPick(i)}
        aria-label={`${label} ${i + 1}`} aria-current={i === active} />
    ))}
  </div>
);

/**
 * Home hero (G-VIS): main carousel at 797 × 235 and two side banners at 398 × 115, scheduled by the platform (admin →
 * Marketing → Banner). Autoplay 5 s, paused on hover / focus; arrows on hover, dots always. A side banner without artwork
 * is a designed voucher / freeship card.
 */
const Banner = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const main = data?.main ?? [];
  const [active, setActive] = useState(0);
  const [paused, setPaused] = useState(false);
  // Slides whose image may load: the first, then one ahead of the slide shown. All six at once (~70 KB each) fought the
  // first one for bandwidth on a phone — LCP 3.2 s → 5.3 s (G-VIS); the others arrive while the carousel turns
  const [reach, setReach] = useState(0);
  useEffect(() => {
    if (!data) return undefined;
    const timer = window.setTimeout(() => setReach((r) => Math.max(r, 1)), 2500);
    return () => window.clearTimeout(timer);
  }, [data]);
  useEffect(() => setReach((r) => Math.max(r, active + 1)), [active]);

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
          <BannerArt key={b.id} banner={i <= reach ? b : { ...b, imageUrl: '' }} to={b.link}
            className={`banner-slide ${i === active ? 'active' : ''}`} titled={bannerShowsTitle(b, true)} cta="Mua ngay" eager={i === 0} />
        ))}

        {main.length > 1 && (
          <>
            <button type="button" className="banner-arrow banner-arrow-left" onClick={prev} aria-label="Banner trước"><ChevronLeft size={22} aria-hidden /></button>
            <button type="button" className="banner-arrow banner-arrow-right" onClick={next} aria-label="Banner sau"><ChevronRight size={22} aria-hidden /></button>
            <BannerDots count={main.length} active={active} onPick={setActive} label="Chuyển tới banner" />
          </>
        )}
      </div>

      <div className="banner-side">
        {(data?.side ?? []).slice(0, 2).map((b, i) => {
          const art = SIDE_ART[i % SIDE_ART.length];
          const Icon = art.icon;
          return isImageUrl(b.imageUrl) ? (
            <BannerArt key={b.id} banner={b} to={b.link} className="banner-side-item" titled={bannerShowsTitle(b, false)} />
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

/** Strip of three wide banners between Flash Sale and the categories (G-VIS); nothing when the API has none. */
export const HomeStrip = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const strip: PublicBanner[] = (data?.strip ?? []).slice(0, 3);
  if (strip.length === 0) return null;
  return (
    <section className="home-strip" aria-label="Ưu đãi nổi bật" data-testid="home-strip">
      {strip.map((b) => (
        <BannerArt key={b.id} banner={b} to={b.link} className="home-strip-item" titled={bannerShowsTitle(b, false)} />
      ))}
    </section>
  );
};

export default Banner;
