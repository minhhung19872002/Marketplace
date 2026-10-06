import type { MouseEvent } from 'react';
import type { Product } from '../types';
import { Link } from 'react-router-dom';
import { formatPrice, formatSold, handleImgError } from '../data/products';
import { useWishlist } from '../context/WishlistContext';
import './ProductCard.css';

// Vẽ 5 sao theo rating (sao vàng + phần lẻ)
const Stars = ({ rating }: { rating: number }) => {
  const full = Math.floor(rating);
  return (
    <span className="pc-stars" aria-label={`${rating} sao`}>
      {[0, 1, 2, 3, 4].map((i) => (
        <span key={i} className={i < full ? 'pc-star on' : 'pc-star'}>★</span>
      ))}
    </span>
  );
};

const ProductCard = ({ product }: { product: Product }) => {
  const { has, toggle } = useWishlist();
  const liked = has(product.id);

  const handleHeart = (e: MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    e.stopPropagation();
    toggle(product.id);
  };

  return (
    <Link to={`/san-pham/${product.id}`} className="product-card" data-testid="product-card">
      <div className="product-card-img">
        <img
          src={product.image}
          alt={product.name}
          loading="lazy"
          onError={(e) => handleImgError(e, product.fallbackImage)}
        />
        {product.discount > 0 && (
          <span className="product-card-discount">
            {product.discount}%<br />GIẢM
          </span>
        )}
        {product.isMall && <span className="product-card-mall">Mall</span>}
        <button
          className={`product-card-heart ${liked ? 'liked' : ''}`}
          onClick={handleHeart}
          aria-label="Yêu thích"
          data-testid="card-heart"
        >
          {liked ? '♥' : '♡'}
        </button>
      </div>

      <div className="product-card-body">
        <h3 className="product-card-name">
          {product.isPreferred && !product.isMall && <span className="pc-name-tag">Yêu thích</span>}
          {product.name}
        </h3>

        <div className="product-card-tags">
          {product.hasVoucher && <span className="tag-voucher">Voucher giảm ₫30k</span>}
          {product.freeship && <span className="tag-freeship">Freeship</span>}
        </div>

        <div className="product-card-price-row">
          <span className="product-card-price">{formatPrice(product.price)}</span>
          <span className="product-card-original">{formatPrice(product.originalPrice)}</span>
        </div>

        <div className="product-card-meta">
          <Stars rating={product.rating} />
          <span className="product-card-sold">Đã bán {formatSold(product.sold)}</span>
        </div>
        <div className="product-card-location">📍 {product.location}</div>
      </div>
    </Link>
  );
};

export default ProductCard;
