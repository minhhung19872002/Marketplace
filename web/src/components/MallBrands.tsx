import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { BadgeCheck, ChevronRight, RotateCcw, Truck } from 'lucide-react';
import { storefrontApi } from '../api/storefront';
import { marketingApi } from '../api/marketing';
import { handleImgError, imageOrPlaceholder, imageSrcSet, isImageUrl } from '../lib/image';
import { BannerLink } from './Banner';
import './MallBrands.css';

/** Offer line of a Mall cell: the shop's real deepest discount, else the Mall promise. */
export const mallOffer = (maxDiscountPercent: number): string =>
  maxDiscountPercent >= 5 ? `Giảm đến ${maxDiscountPercent}%` : 'Chính hãng 100%';

/**
 * "ShopHub Mall" (G2-B1): portrait slides at the left (admin → Banner, position Mall), a 2 × 4 grid of official stores at
 * the right — each with its best seller's photo, its logo and an offer line taken from real prices.
 */
/** Loading state with the section's own grid (slide + 8 square cells), so the page below does not jump (CLS, G3). */
const MallBrandsSkeleton = () => (
  <section className="mall-brands" aria-hidden data-testid="mall-brands-loading">
    <div className="mall-brands-header">
      <h2 className="mall-brands-title"><span className="mall-brands-badge">Mall</span> ShopHub Mall</h2>
    </div>
    <div className="mall-brands-body">
      <div className="mall-slides sh-skeleton" />
      <div className="mall-brands-grid">
        {Array.from({ length: 8 }, (_, i) => (
          <span key={i} className="mall-brand">
            <span className="mall-brand-photo sh-skeleton" />
            <span className="mall-brand-name">&nbsp;</span>
            <span className="mall-brand-offer">&nbsp;</span>
          </span>
        ))}
      </div>
    </div>
  </section>
);

const MallBrands = () => {
  const { data: shops = [], isPending } = useQuery({ queryKey: ['home', 'mall'], queryFn: storefrontApi.mall, staleTime: 5 * 60_000 });
  const { data: banners } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const slides = (banners?.mall ?? []).filter((b) => isImageUrl(b.imageUrl));
  const [active, setActive] = useState(0);
  useEffect(() => {
    if (slides.length < 2) return undefined;
    const t = setInterval(() => setActive((i) => (i + 1) % slides.length), 5000);
    return () => clearInterval(t);
  }, [slides.length]);
  if (isPending || !banners) return <MallBrandsSkeleton />;
  if (shops.length === 0) return null;

  return (
    <section className="mall-brands" data-testid="mall-brands">
      <div className="mall-brands-header">
        <h2 className="mall-brands-title">
          <span className="mall-brands-badge">Mall</span> ShopHub Mall
        </h2>
        <ul className="mall-brands-promises" aria-label="Cam kết của ShopHub Mall">
          <li><BadgeCheck size={16} aria-hidden /> Chính hãng 100%</li>
          <li><RotateCcw size={16} aria-hidden /> Trả hàng 15 ngày</li>
          <li><Truck size={16} aria-hidden /> Voucher Freeship mỗi ngày</li>
        </ul>
        <Link to="/tim-kiem?mall=true" className="mall-brands-more">Xem tất cả <ChevronRight size={16} aria-hidden /></Link>
      </div>
      <div className="mall-brands-body">
        {slides.length > 0 && (
          <div className="mall-slides" aria-label="Banner ShopHub Mall">
            {slides.map((b, i) => (
              <BannerLink key={b.id} to={b.link} className={`mall-slide ${i === active ? 'active' : ''}`} label={b.title}>
                <img src={b.imageUrl} alt="" loading="lazy" width={480} height={660} onError={handleImgError} />
              </BannerLink>
            ))}
            {slides.length > 1 && (
              <div className="mall-slide-dots">
                {slides.map((b, i) => (
                  <button key={b.id} type="button" className={i === active ? 'active' : ''} onClick={() => setActive(i)}
                    aria-label={`Banner Mall ${i + 1}`} aria-current={i === active} />
                ))}
              </div>
            )}
          </div>
        )}
        <div className="mall-brands-grid">
          {shops.slice(0, 8).map((s) => (
            <Link key={s.id} to={`/shop/${s.slug}`} className="mall-brand" data-testid="mall-brand">
              <span className="mall-brand-photo">
                <img src={imageOrPlaceholder(s.coverImageUrl)} srcSet={imageSrcSet(s.coverImageUrl)} sizes="200px" alt="" loading="lazy" width={200} height={200} onError={handleImgError} />
                {s.logoUrl && <img className="mall-brand-logo" src={s.logoUrl} alt="" loading="lazy" width={48} height={48} onError={handleImgError} />}
              </span>
              <span className="mall-brand-name">{s.name.replace(/^Mall /, '')}</span>
              <span className="mall-brand-offer">{mallOffer(s.maxDiscountPercent)}</span>
            </Link>
          ))}
          {/* The grid always ends full: the last cell leads to every official store */}
          {Math.min(shops.length, 8) % 4 !== 0 && (
            <Link to="/tim-kiem?mall=true" className="mall-brand mall-brand--all" data-testid="mall-brand-all">
              <span className="mall-brand-all-icon"><ChevronRight size={28} aria-hidden /></span>
              <span className="mall-brand-name">Xem tất cả thương hiệu</span>
              <span className="mall-brand-offer">{shops.length} shop chính hãng</span>
            </Link>
          )}
        </div>
      </div>
    </section>
  );
};

export default MallBrands;
