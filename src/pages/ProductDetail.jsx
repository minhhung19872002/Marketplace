import React, { useState, useEffect } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { getProductById, products, formatPrice, formatSold, handleImgError } from '../data/products';
import { useCart } from '../context/CartContext';
import { useWishlist } from '../context/WishlistContext';
import ProductGrid from '../components/ProductGrid';
import './ProductDetail.css';

const ProductDetail = () => {
  const { id } = useParams();
  const navigate = useNavigate();
  const { addToCart } = useCart();
  const { has, toggle } = useWishlist();
  const product = getProductById(id);

  const [quantity, setQuantity] = useState(1);
  const [selectedVariant, setSelectedVariant] = useState('');
  const [variantError, setVariantError] = useState(false);
  const [toast, setToast] = useState('');
  const [activeImg, setActiveImg] = useState(0);

  useEffect(() => {
    window.scrollTo(0, 0);
    setQuantity(1);
    setSelectedVariant('');
    setVariantError(false);
    setActiveImg(0);
  }, [id]);

  if (!product) {
    return (
      <div className="container product-not-found">
        <p>Sản phẩm không tồn tại.</p>
        <Link to="/" className="btn-back-home">Về trang chủ</Link>
      </div>
    );
  }

  const related = products.filter((p) => p.id !== product.id).slice(0, 6);

  // Gallery ảnh thật của sản phẩm
  const gallery = product.gallery && product.gallery.length ? product.gallery : [product.image];

  // Nghiệp vụ Shopee: bắt buộc chọn phân loại nếu sản phẩm có biến thể
  const requireVariant = Boolean(product.variant);
  const ensureVariant = () => {
    if (requireVariant && !selectedVariant) {
      setVariantError(true);
      return false;
    }
    return true;
  };

  const showToast = (msg) => {
    setToast(msg);
    setTimeout(() => setToast(''), 2000);
  };

  const handleAddToCart = () => {
    if (!ensureVariant()) return;
    addToCart({ ...product, selectedVariant }, quantity);
    showToast('Đã thêm vào giỏ hàng!');
  };

  const handleBuyNow = () => {
    if (!ensureVariant()) return;
    addToCart({ ...product, selectedVariant }, quantity);
    navigate('/gio-hang');
  };

  // Phân bố sao (giả lập) cho phần đánh giá
  const ratingBuckets = [5, 4, 3, 2, 1].map((star) => ({
    star,
    count: product.reviews.filter((r) => r.rating === star).length,
  }));

  return (
    <div className="product-detail">
      <div className="container">
        <div className="breadcrumb">
          <Link to="/">Trang chủ</Link>
          <span>›</span>
          <Link to={`/tim-kiem?category=${product.categoryId}`}>{product.categoryName}</Link>
          <span>›</span>
          <span className="breadcrumb-current">{product.name}</span>
        </div>

        {toast && <div className="pd-toast" data-testid="pd-toast">{toast}</div>}

        <div className="product-detail-main">
          <div className="product-detail-gallery">
            <div className="product-detail-image">
              <img
                src={gallery[activeImg]}
                alt={product.name}
                onError={(e) => handleImgError(e, product.fallbackImage)}
              />
            </div>
            <div className="product-detail-thumbs">
              {gallery.map((src, i) => (
                <button
                  key={i}
                  className={`pd-thumb ${i === activeImg ? 'active' : ''}`}
                  onMouseEnter={() => setActiveImg(i)}
                  onClick={() => setActiveImg(i)}
                  aria-label={`Ảnh ${i + 1}`}
                >
                  <img src={src} alt="" onError={(e) => handleImgError(e, product.fallbackImage)} />
                </button>
              ))}
            </div>
          </div>

          <div className="product-detail-info">
            <h1 className="product-detail-name">
              {product.isMall && <span className="pd-mall-tag">Mall</span>}
              {product.isPreferred && !product.isMall && <span className="pd-pref-tag">Yêu thích</span>}
              {product.name}
            </h1>

            <div className="product-detail-stats">
              <span className="stat-rating">
                {product.rating} <span className="stat-stars">★★★★★</span>
              </span>
              <span className="stat-divider" />
              <span className="stat-count">{formatSold(product.ratingCount)} Đánh Giá</span>
              <span className="stat-divider" />
              <span className="stat-sold">{formatSold(product.sold)} Đã Bán</span>
            </div>

            <div className="product-detail-price-box">
              <span className="price-original">{formatPrice(product.originalPrice)}</span>
              <span className="price-current">{formatPrice(product.price)}</span>
              <span className="price-discount">{product.discount}% GIẢM</span>
            </div>

            {(product.hasVoucher || product.freeship) && (
              <div className="product-detail-row">
                <span className="row-label">Ưu Đãi</span>
                <div className="pd-benefits">
                  {product.freeship && <span className="pd-benefit">🚚 Miễn phí vận chuyển</span>}
                  {product.hasVoucher && <span className="pd-benefit">🎟️ Voucher giảm ₫30.000</span>}
                </div>
              </div>
            )}

            {requireVariant && (
              <div className="product-detail-row product-detail-row-top">
                <span className="row-label">{product.variant.label}</span>
                <div className="pd-variants">
                  {product.variant.options.map((opt) => (
                    <button
                      key={opt}
                      className={`pd-variant ${selectedVariant === opt ? 'active' : ''}`}
                      onClick={() => {
                        setSelectedVariant(opt);
                        setVariantError(false);
                      }}
                      data-testid="variant-option"
                    >
                      {opt}
                    </button>
                  ))}
                </div>
              </div>
            )}
            {variantError && (
              <div className="pd-variant-error" data-testid="variant-error">
                Vui lòng chọn {product.variant.label}
              </div>
            )}

            <div className="product-detail-row">
              <span className="row-label">Số Lượng</span>
              <div className="quantity-control">
                <button onClick={() => setQuantity((q) => Math.max(1, q - 1))} aria-label="Giảm">−</button>
                <input
                  type="number"
                  min="1"
                  max={product.stock}
                  value={quantity}
                  onChange={(e) =>
                    setQuantity(Math.min(product.stock, Math.max(1, Number(e.target.value) || 1)))
                  }
                  aria-label="Số lượng"
                />
                <button
                  onClick={() => setQuantity((q) => Math.min(product.stock, q + 1))}
                  aria-label="Tăng"
                >
                  +
                </button>
              </div>
              <span className="pd-stock">{product.stock} sản phẩm có sẵn</span>
            </div>

            <div className="product-detail-actions">
              <button className="btn-add-cart" onClick={handleAddToCart} data-testid="add-to-cart">
                🛒 Thêm Vào Giỏ Hàng
              </button>
              <button className="btn-buy-now" onClick={handleBuyNow}>
                Mua Ngay
              </button>
              <button
                className={`btn-wishlist ${has(product.id) ? 'liked' : ''}`}
                onClick={() => toggle(product.id)}
                data-testid="detail-heart"
                aria-label="Yêu thích"
              >
                {has(product.id) ? '♥ Đã Thích' : '♡ Yêu Thích'}
              </button>
            </div>
          </div>
        </div>

        {/* Thông tin Shop */}
        <div className="pd-shop">
          <div className="pd-shop-avatar">{product.shopName.charAt(0)}</div>
          <div className="pd-shop-info">
            <div className="pd-shop-name">{product.shopName}</div>
            <div className="pd-shop-sub">Online 5 phút trước</div>
            <div className="pd-shop-actions">
              <button className="pd-shop-chat">💬 Chat Ngay</button>
              <Link to="/shop" className="pd-shop-view">🏪 Xem Shop</Link>
            </div>
          </div>
          <div className="pd-shop-stats">
            <div><strong>{formatSold(product.ratingCount)}</strong><span>Đánh Giá</span></div>
            <div><strong>{product.rating}★</strong><span>Tỉ Lệ Phản Hồi</span></div>
            <div><strong>Vài Phút</strong><span>Thời Gian Phản Hồi</span></div>
          </div>
        </div>

        {/* Thông số kỹ thuật */}
        <div className="pd-section">
          <h2 className="pd-section-title">CHI TIẾT SẢN PHẨM</h2>
          <table className="pd-specs">
            <tbody>
              {product.specs.map(([k, v]) => (
                <tr key={k}>
                  <td className="pd-spec-key">{k}</td>
                  <td className="pd-spec-val">{v}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Mô tả */}
        <div className="pd-section">
          <h2 className="pd-section-title">MÔ TẢ SẢN PHẨM</h2>
          <p className="pd-description">{product.description}</p>
        </div>

        {/* Đánh giá */}
        <div className="pd-section">
          <h2 className="pd-section-title">ĐÁNH GIÁ SẢN PHẨM</h2>
          <div className="pd-rating-summary">
            <div className="pd-rating-score">
              <span className="pd-rating-big">{product.rating}</span>
              <span className="pd-rating-outof">trên 5</span>
              <span className="pd-rating-stars">★★★★★</span>
            </div>
            <div className="pd-rating-bars">
              {ratingBuckets.map((b) => (
                <div key={b.star} className="pd-rating-bar-row">
                  <span className="pd-rating-bar-label">{b.star} sao</span>
                  <span className="pd-rating-bar-count">({b.count})</span>
                </div>
              ))}
            </div>
          </div>

          <div className="pd-reviews" data-testid="reviews">
            {product.reviews.map((r) => (
              <div key={r.id} className="pd-review">
                <div className="pd-review-avatar">{r.name.charAt(0).toUpperCase()}</div>
                <div className="pd-review-body">
                  <div className="pd-review-name">{r.name}</div>
                  <div className="pd-review-stars">
                    {'★'.repeat(r.rating)}<span className="pd-review-stars-off">{'★'.repeat(5 - r.rating)}</span>
                  </div>
                  <div className="pd-review-date">{r.date}</div>
                  <p className="pd-review-text">{r.text}</p>
                </div>
              </div>
            ))}
          </div>
        </div>

        <ProductGrid title="CÓ THỂ BẠN CŨNG THÍCH" products={related} />
      </div>
    </div>
  );
};

export default ProductDetail;
