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
 * Short label of the campaign chip on the photo (G4-A2): the sale date when the name has one ("Siêu Sale 10.10" →
 * "10.10"), else the name itself up to two words — the chip never cuts its text, the full name is its tooltip.
 */
export const campaignChip = (name: string): string => {
  const date = name.match(/\b\d{1,2}\.\d{1,2}\b/);
  if (date) return date[0];
  const words = name.trim().split(/\s+/);
  return words.length > 2 ? words.slice(0, 2).join(' ') : name.trim();
};

/**
 * Product tile of every grid. Fixed layout so all tiles of a row have the same height: square image with the Mall /
 * Yêu thích / % badges on its corners (never inside the name), 2-line name, price line that wraps instead of cutting,
 * rating + sold, ship-from province. Hover lifts the tile and shows "Tìm sản phẩm tương tự" (a sibling link — links
 * cannot nest).
 */
const ProductCard = ({ product, showFrame = false }: { product: Card; showFrame?: boolean }) => {
  const { has, toggle } = useWishlist();
  const liked = has(product.id);
  const href = productPath(product.slug, product.shopId, product.id);
  const showOriginal = product.originalPrice > product.minPrice;

  const handleHeart = (e: MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    e.stopPropagation();
    toggle(product.id);
  };

  return (
    <div className={`product-card ${product.inStock ? '' : 'sold-out'}`}>
      <Link to={href} className="product-card-link" data-testid="product-card">
        <div className="product-card-img">
          <img src={imageOrPlaceholder(product.imageUrl)} srcSet={imageSrcSet(product.imageUrl)} sizes="(max-width: 640px) 50vw, 200px" alt={product.name} loading="lazy" decoding="async" width={240} height={240} onError={handleImgError} />
          {/* Campaign frame (e.g. 10.10): only on the campaign's own page — elsewhere it hid the photo (G3 B3), a chip says it */}
          {showFrame && product.frameUrl && <img className="product-card-frame" src={product.frameUrl} alt="" aria-hidden loading="lazy" width={240} height={240} />}
          {!showFrame && product.campaignName && <span className="product-card-campaign" data-testid="product-card-campaign" title={product.campaignName}>{campaignChip(product.campaignName)}</span>}
          <div className="product-card-flags">
            {product.isMall && <span className="pc-flag pc-flag--mall">Mall</span>}
            {product.isPreferred && !product.isMall && <span className="pc-flag pc-flag--preferred">Yêu thích</span>}
          </div>
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
          <h3 className="product-card-name" data-testid="product-card-name">{product.name}</h3>

          <div className="product-card-labels">
            {(product.labels ?? []).map((l) => <span key={l} className="product-card-label" data-testid="product-card-label">{l}</span>)}
          </div>

          <div className="product-card-price-row">
            <span className="product-card-price price" data-testid="product-card-price" aria-label={formatPrice(product.minPrice)}>
              <span className="product-card-currency" aria-hidden>{priceParts(product.minPrice).currency}</span>
              <span aria-hidden>{priceParts(product.minPrice).amount}</span>
            </span>
            {showOriginal && <span className="product-card-original price">{formatPrice(product.originalPrice)}</span>}
          </div>

          <div className="product-card-meta">
            {/* No rating yet: only the sold count — "Chưa có đánh giá" was cut to "Chưa có …" on narrow tiles (G4-A2) */}
            {product.ratingCount > 0 && <Stars value={product.ratingAvg} size={11} />}
            <span className="product-card-sold">Đã bán {formatSold(product.soldCount)}</span>
          </div>
          <div className="product-card-location">
            {product.provinceName && <><MapPin size={11} aria-hidden /> {product.provinceName}</>}
          </div>
        </div>
      </Link>
      <Link to={`${href}#san-pham-tuong-tu`} className="product-card-similar" tabIndex={-1}>Tìm sản phẩm tương tự</Link>
    </div>
  );
};

export default ProductCard;
