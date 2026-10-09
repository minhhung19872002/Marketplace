import type { MouseEvent } from 'react';
import type { ProductCard as Card } from '../types';
import { Link } from 'react-router-dom';
import { Heart, MapPin, Zap } from 'lucide-react';
import { formatPrice, formatSold, priceParts } from '../lib/money';
import { handleImgError, imageOrPlaceholder, imageSrcSet } from '../lib/image';
import { useWishlist } from '../context/WishlistContext';
import { Stars } from './ui';
import './ProductCard.css';
import { productPath } from '../lib/urls';

/**
 * Short label of the campaign date box on the photo (G4-A2): the sale date when the name has one ("Siêu Sale 10.10" →
 * "10.10"), else the name itself up to two words — the chip never cuts its text, the full name is its tooltip.
 */
export const campaignChip = (name: string): string => {
  const date = name.match(/\b\d{1,2}\.\d{1,2}\b/);
  if (date) return date[0];
  const words = name.trim().split(/\s+/);
  return words.length > 2 ? words.slice(0, 2).join(' ') : name.trim();
};

/**
 * Campaign strip along the bottom of a photo (G-VIS), ~14 % of its height: the sale date in a red box, then the
 * programmes of the sale. Drawn in CSS so the photo stays visible; the campaign page draws the frame artwork instead.
 */
export const CampaignStrip = ({ name }: { name: string }) => (
  <span className="pc-campaign" data-testid="product-card-campaign" title={name}>
    <span className="pc-campaign-date">{campaignChip(name)}</span>
    <span className="pc-campaign-tag pc-campaign-tag--voucher">Voucher Plus</span>
    <span className="pc-campaign-tag pc-campaign-tag--freeship">Freeship+</span>
  </span>
);

/** Mall / Yêu thích badge, inline before the product name. */
export const ShopBadge = ({ isMall, isPreferred, className = '' }: { isMall: boolean; isPreferred: boolean; className?: string }) =>
  isMall ? <span className={`pc-flag pc-flag--mall ${className}`}>Mall</span>
    : isPreferred ? <span className={`pc-flag pc-flag--preferred ${className}`}>Yêu thích</span> : null;

/**
 * Product tile of every grid (G-VIS, reference-marketplace density). Square photo filling the frame with the "-xx%" tag
 * top-right and the campaign strip at its bottom; under it the Mall / Yêu thích badge inline before a 2-line 14 px name,
 * orange-outline tags, then the price (digits, small ₫ after) left and "x đã bán" right on one row.
 * `variant="detailed"` (search / category results) adds the struck list price, the stars and the ship-from province.
 * Hover: orange border, 1 px lift, "Tìm sản phẩm tương tự" below (a sibling link — links cannot nest).
 */
const ProductCard = ({ product, showFrame = false, variant = 'compact' }: { product: Card; showFrame?: boolean; variant?: 'compact' | 'detailed' }) => {
  const { has, toggle } = useWishlist();
  const liked = has(product.id);
  const href = productPath(product.slug, product.shopId, product.id);
  const detailed = variant === 'detailed';
  const showOriginal = detailed && product.originalPrice > product.minPrice;
  const price = priceParts(product.minPrice);
  const strip = !showFrame && !!product.campaignName;

  const handleHeart = (e: MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    e.stopPropagation();
    toggle(product.id);
  };

  return (
    <div className={`product-card product-card--${variant} ${product.inStock ? '' : 'sold-out'}`}>
      <Link to={href} className="product-card-link" data-testid="product-card">
        <div className={`product-card-img ${strip ? 'has-campaign' : ''}`}>
          <img src={imageOrPlaceholder(product.imageUrl)} srcSet={imageSrcSet(product.imageUrl)} sizes="(max-width: 640px) 50vw, 200px" alt={product.name} loading="lazy" decoding="async" width={240} height={240} onError={handleImgError} />
          {/* Campaign frame artwork on the campaign's own page only; elsewhere the CSS strip (G-VIS) */}
          {showFrame && product.frameUrl && <img className="product-card-frame" src={product.frameUrl} alt="" aria-hidden loading="lazy" width={240} height={240} />}
          {strip && <CampaignStrip name={product.campaignName!} />}
          {product.discountPercent > 0 && (
            <span className="product-card-discount" aria-label={`Giảm ${product.discountPercent}%`}>-{product.discountPercent}%</span>
          )}
          {product.isFlashSale && (
            <span className="product-card-flash" data-testid="product-card-flash"><Zap size={12} fill="currentColor" aria-hidden /> Flash Sale</span>
          )}
          {!product.inStock && <span className="product-card-soldout">Hết hàng</span>}
          <button type="button" className={`product-card-heart ${liked ? 'liked' : ''}`} onClick={handleHeart}
            aria-label={liked ? 'Bỏ yêu thích' : 'Yêu thích'} aria-pressed={liked} data-testid="card-heart">
            <Heart size={16} fill={liked ? 'currentColor' : 'none'} aria-hidden />
          </button>
        </div>

        <div className="product-card-body">
          <div className="product-card-title">
            <ShopBadge isMall={product.isMall} isPreferred={product.isPreferred} />
            <h3 className="product-card-name" data-testid="product-card-name">{product.name}</h3>
          </div>

          <div className="product-card-labels">
            {(product.labels ?? []).map((l) => <span key={l} className="product-card-label" data-testid="product-card-label">{l}</span>)}
          </div>

          <div className="product-card-price-row">
            <span className="product-card-price price" data-testid="product-card-price" aria-label={formatPrice(product.minPrice)}>
              <span aria-hidden>{price.amount}</span>
              <span className="product-card-currency" aria-hidden>{price.currency}</span>
            </span>
            {showOriginal && <span className="product-card-original price">{formatPrice(product.originalPrice)}</span>}
            {!detailed && <span className="product-card-sold">{formatSold(product.soldCount)} đã bán</span>}
          </div>

          {detailed && (
            <>
              <div className="product-card-meta">
                {/* No rating yet: only the sold count — "Chưa có đánh giá" was cut to "Chưa có …" on narrow tiles (G4-A2) */}
                {product.ratingCount > 0 && <Stars value={product.ratingAvg} size={11} />}
                <span className="product-card-sold">Đã bán {formatSold(product.soldCount)}</span>
              </div>
              <div className="product-card-location">
                {product.provinceName && <><MapPin size={11} aria-hidden /> {product.provinceName}</>}
              </div>
            </>
          )}
        </div>
      </Link>
      <Link to={`${href}#san-pham-tuong-tu`} className="product-card-similar" tabIndex={-1}>Tìm sản phẩm tương tự</Link>
    </div>
  );
};

export default ProductCard;
