import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { marketingApi } from '../api/marketing';
import { handleImgError, isImageUrl } from '../lib/image';
import './Banner.css';

/** Internal paths stay in the app; https links (only kind the admin may set besides "/...") open in a new tab. */
export const BannerLink = ({ to, className, children, testId }: { to: string; className?: string; children: ReactNode; testId?: string }) =>
  to.startsWith('/') ? (
    <Link to={to} className={className} data-testid={testId}>{children}</Link>
  ) : (
    <a href={to} className={className} target="_blank" rel="noopener noreferrer" data-testid={testId}>{children}</a>
  );

/** Home carousel + two side banners, scheduled by the platform (admin → Marketing → Banner). */
const Banner = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const main = data?.main ?? [];
  const [active, setActive] = useState(0);

  const next = useCallback(() => setActive((i) => (main.length === 0 ? 0 : (i + 1) % main.length)), [main.length]);
  const prev = () => setActive((i) => (main.length === 0 ? 0 : (i - 1 + main.length) % main.length));

  useEffect(() => {
    if (main.length < 2) return undefined;
    const timer = setInterval(next, 4000);
    return () => clearInterval(timer);
  }, [next, main.length]);

  if (main.length === 0) return null;

  return (
    <section className="banner" aria-label="Khuyến mãi nổi bật" data-testid="home-banners">
      <div className="banner-slider">
        {main.map((b, i) => (
          <BannerLink key={b.id} to={b.link} className={`banner-slide tone-primary ${i === active ? 'active' : ''}`}>
            {isImageUrl(b.imageUrl) && <img className="banner-image" src={b.imageUrl} alt="" onError={handleImgError} />}
            <div className="banner-content">
              <h2 className="banner-title">{b.title}</h2>
              <span className="banner-cta">Mua Ngay</span>
            </div>
          </BannerLink>
        ))}

        {main.length > 1 && (
          <>
            <button className="banner-arrow banner-arrow-left" onClick={prev} aria-label="Trước">‹</button>
            <button className="banner-arrow banner-arrow-right" onClick={next} aria-label="Sau">›</button>
            <div className="banner-dots">
              {main.map((b, i) => (
                <button key={b.id} className={`banner-dot ${i === active ? 'active' : ''}`} onClick={() => setActive(i)} aria-label={`Chuyển tới banner ${i + 1}`} />
              ))}
            </div>
          </>
        )}
      </div>

      <div className="banner-side">
        {(data?.side ?? []).map((b, i) => (
          <BannerLink key={b.id} to={b.link} className={`banner-side-item ${i === 0 ? 'banner-side-item--voucher' : 'banner-side-item--freeship'}`}>
            {isImageUrl(b.imageUrl) && <img className="banner-image" src={b.imageUrl} alt="" onError={handleImgError} />}
            <span>{b.title}</span>
          </BannerLink>
        ))}
      </div>
    </section>
  );
};

export default Banner;
