import type { MouseEvent } from 'react';
import type { ProductCard as Card } from '../types';
import { Link } from 'react-router-dom';
import { formatPrice, formatSold } from '../lib/money';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import { useWishlist } from '../context/WishlistContext';
import './ProductCard.css';

// Five stars, filled up to the rounded-down rating
const Stars = ({ rating }: { rating: number }) => {
  const full = Math.floor(rating);
  return (
    <span className="pc-stars" aria-label={`${rating.toFixed(1)} sao`}>
      {[0, 1, 2, 3, 4].map((i) => (
        <span key={i} className={i < full ? 'pc-star on' : 'pc-star'}>★</span>
      ))}
    </span>
  );
};

const ProductCard = ({ product }: { product: Card }) => {
  const { has, toggle } = useWishlist();
  const liked = has(product.id);

  const handleHeart = (e: MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    e.stopPropagation();
    toggle(product.id);
  };

  return (
    <Link to={`/san-pham/${product.id}`} className={`product-card ${product.inStock ? '' : 'sold-out'}`} data-testid="product-card">
      <div className="product-card-img">
        <img src={imageOrPlaceholder(product.imageUrl)} alt={product.name} loading="lazy" onError={handleImgError} />
        {product.discountPercent > 0 && (
          <span className="product-card-discount">
            {product.discountPercent}%<br />GIẢM
          </span>
        )}
        {product.isMall && <span className="product-card-mall">Mall</span>}
        {!product.inStock && <span className="product-card-soldout">Hết hàng</span>}
        <button className={`product-card-heart ${liked ? 'liked' : ''}`} onClick={handleHeart} aria-label="Yêu thích" data-testid="card-heart">
          {liked ? '♥' : '♡'}
        </button>
      </div>

      <div className="product-card-body">
        <h3 className="product-card-name" data-testid="product-card-name">
          {product.isPreferred && !product.isMall && <span className="pc-name-tag">Yêu thích</span>}
          {product.name}
        </h3>

        <div className="product-card-price-row">
          <span className="product-card-price" data-testid="product-card-price">{formatPrice(product.minPrice)}</span>
          {product.originalPrice > product.minPrice && <span className="product-card-original">{formatPrice(product.originalPrice)}</span>}
        </div>

        <div className="product-card-meta">
          {product.ratingCount > 0 ? <Stars rating={product.ratingAvg} /> : <span className="pc-no-rating">Chưa có đánh giá</span>}
          <span className="product-card-sold">Đã bán {formatSold(product.soldCount)}</span>
        </div>
        {product.provinceName && <div className="product-card-location">📍 {product.provinceName}</div>}
      </div>
    </Link>
  );
};

export default ProductCard;
