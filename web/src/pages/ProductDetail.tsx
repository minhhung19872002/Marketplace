import { useEffect, useMemo, useState } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { ApiError } from '../api/http';
import { useCart } from '../context/CartContext';
import { useWishlist } from '../context/WishlistContext';
import ProductGrid from '../components/ProductGrid';
import { formatPrice, formatSold } from '../lib/money';
import { formatDate } from '../lib/datetime';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import type { ProductPage, PublicSku } from '../types';
import './ProductDetail.css';

const priceRange = (min: number, max: number) => (min === max ? formatPrice(min) : `${formatPrice(min)} - ${formatPrice(max)}`);

/** SKUs still possible with the current selection (null = not chosen yet). */
const matching = (skus: PublicSku[], picked: (string | null)[]) =>
  skus.filter((s) => (picked[0] == null || s.option1 === picked[0]) && (picked[1] == null || s.option2 === picked[1]));

const ProductView = ({ product }: { product: ProductPage }) => {
  const navigate = useNavigate();
  const { add: addToCart } = useCart();
  const { has, toggle } = useWishlist();

  const tiers = product.tiers;
  const [picked, setPicked] = useState<(string | null)[]>([null, null]);
  const [quantity, setQuantity] = useState(1);
  const [variantError, setVariantError] = useState(false);
  const [toast, setToast] = useState('');
  const [activeImg, setActiveImg] = useState(0);

  const images = product.media.filter((m) => m.type === 'Image');
  const video = product.media.find((m) => m.type === 'Video');
  const gallery = images.length ? images.map((m) => m.url) : [imageOrPlaceholder(null)];

  // A single SKU once every tier has a value (or the product has no tiers)
  const sku = useMemo(() => {
    if (tiers.length === 0) return product.skus[0] ?? null;
    if (picked.slice(0, tiers.length).some((p) => p == null)) return null;
    return matching(product.skus, picked)[0] ?? null;
  }, [tiers.length, picked, product.skus]);
  const available = sku ? sku.available : product.totalAvailable;

  /** An option is selectable when some SKU with it (and the other tier's choice) still has stock. */
  const optionAvailable = (tierIndex: number, value: string) => {
    const probe = [...picked];
    probe[tierIndex] = value;
    return matching(product.skus, probe).some((s) => s.available > 0);
  };

  const pick = (tierIndex: number, value: string) => {
    const next = [...picked];
    next[tierIndex] = next[tierIndex] === value ? null : value;
    setPicked(next);
    setVariantError(false);
    setQuantity(1);
    // Show the option's own photo when it has one
    const optionImage = tiers[tierIndex].options.find((o) => o.value === value)?.imageUrl
      ?? product.media.find((m) => m.optionValue === value)?.url;
    if (optionImage) {
      const index = gallery.indexOf(optionImage);
      setActiveImg(index >= 0 ? index : 0);
    }
  };

  const showToast = (msg: string) => {
    setToast(msg);
    window.setTimeout(() => setToast(''), 2000);
  };

  const [busy, setBusy] = useState(false);

  /** Adds the chosen SKU to the server cart; false when a variant is missing or the server refused. */
  const add = async (): Promise<boolean> => {
    if (!product.purchasable) return false;
    if (!sku) {
      setVariantError(true);
      return false;
    }
    if (sku.available <= 0 || busy) return false;
    setBusy(true);
    try {
      showToast(await addToCart(sku.id, quantity));
      return true;
    } catch (e) {
      showToast(e instanceof ApiError ? e.message : 'Không thêm được vào giỏ, vui lòng thử lại.');
      return false;
    } finally {
      setBusy(false);
    }
  };

  const liked = has(product.id);
  const missingTiers = tiers.filter((_, i) => picked[i] == null).map((t) => t.name);
  const leaf = product.breadcrumb[product.breadcrumb.length - 1];

  return (
    <div className="product-detail">
      <div className="container">
        <div className="breadcrumb" data-testid="product-breadcrumb">
          <Link to="/">Trang chủ</Link>
          {product.breadcrumb.map((c) => (
            <span key={c.id}>
              <span>› </span>
              <Link to={`/danh-muc/${c.slug}`}>{c.name}</Link>
            </span>
          ))}
          <span>›</span>
          <span className="breadcrumb-current">{product.name}</span>
        </div>

        {toast && <div className="pd-toast" data-testid="pd-toast">{toast}</div>}

        <div className="product-detail-main">
          <div className="product-detail-gallery">
            <div className="product-detail-image">
              <img src={gallery[activeImg] ?? gallery[0]} alt={product.name} onError={handleImgError} data-testid="pd-main-image" />
            </div>
            <div className="product-detail-thumbs">
              {gallery.map((src, i) => (
                <button
                  key={src}
                  className={`pd-thumb ${i === activeImg ? 'active' : ''}`}
                  onMouseEnter={() => setActiveImg(i)}
                  onClick={() => setActiveImg(i)}
                  aria-label={`Ảnh ${i + 1}`}
                >
                  <img src={src} alt="" onError={handleImgError} />
                </button>
              ))}
            </div>
            {video && (
              <video className="pd-video" src={video.url} controls preload="metadata">
                Trình duyệt không hỗ trợ video.
              </video>
            )}
          </div>

          <div className="product-detail-info">
            <h1 className="product-detail-name" data-testid="pd-name">
              {product.shop.isMall && <span className="pd-mall-tag">Mall</span>}
              {product.shop.isPreferred && !product.shop.isMall && <span className="pd-pref-tag">Yêu thích</span>}
              {product.name}
            </h1>

            <div className="product-detail-stats">
              {product.ratingCount > 0 ? (
                <>
                  <span className="stat-rating">
                    {product.ratingAvg.toFixed(1)} <span className="stat-stars">★★★★★</span>
                  </span>
                  <span className="stat-divider" />
                  <span className="stat-count">{formatSold(product.ratingCount)} Đánh Giá</span>
                </>
              ) : (
                <span className="stat-count">Chưa có đánh giá</span>
              )}
              <span className="stat-divider" />
              <span className="stat-sold">{formatSold(product.soldCount)} Đã Bán</span>
            </div>

            <div className="product-detail-price-box">
              {sku ? (
                <>
                  {sku.originalPrice > sku.price && <span className="price-original">{formatPrice(sku.originalPrice)}</span>}
                  <span className="price-current" data-testid="pd-price">{formatPrice(sku.price)}</span>
                  {sku.originalPrice > sku.price && (
                    <span className="price-discount">{Math.floor(((sku.originalPrice - sku.price) * 100) / sku.originalPrice)}% GIẢM</span>
                  )}
                </>
              ) : (
                <>
                  {product.originalMaxPrice > product.minPrice && (
                    <span className="price-original">{priceRange(product.originalMinPrice, product.originalMaxPrice)}</span>
                  )}
                  <span className="price-current" data-testid="pd-price">{priceRange(product.minPrice, product.maxPrice)}</span>
                  {product.discountPercent > 0 && <span className="price-discount">{product.discountPercent}% GIẢM</span>}
                </>
              )}
            </div>

            {product.isPreorder && (
              <div className="product-detail-row">
                <span className="row-label">Đặt Trước</span>
                <span>Hàng đặt trước — chuẩn bị trong {product.preorderDays} ngày</span>
              </div>
            )}

            {tiers.map((tier, ti) => (
              <div key={tier.name} className="product-detail-row product-detail-row-top">
                <span className="row-label">{tier.name}</span>
                <div className="pd-variants">
                  {tier.options.map((opt) => {
                    const enabled = optionAvailable(ti, opt.value);
                    return (
                      <button
                        key={opt.value}
                        className={`pd-variant ${picked[ti] === opt.value ? 'active' : ''}`}
                        onClick={() => pick(ti, opt.value)}
                        disabled={!enabled}
                        title={enabled ? undefined : 'Hết hàng'}
                        data-testid="variant-option"
                      >
                        {opt.imageUrl && <img src={opt.imageUrl} alt="" className="pd-variant-img" onError={handleImgError} />}
                        {opt.value}
                      </button>
                    );
                  })}
                </div>
              </div>
            ))}
            {variantError && missingTiers.length > 0 && (
              <div className="pd-variant-error" data-testid="variant-error">
                Vui lòng chọn {missingTiers.join(', ')}
              </div>
            )}

            <div className="product-detail-row">
              <span className="row-label">Số Lượng</span>
              <div className="quantity-control">
                <button onClick={() => setQuantity((q) => Math.max(1, q - 1))} aria-label="Giảm">−</button>
                <input
                  type="number"
                  min="1"
                  max={Math.max(1, available)}
                  value={quantity}
                  onChange={(e) => setQuantity(Math.min(Math.max(1, available), Math.max(1, Number(e.target.value) || 1)))}
                  aria-label="Số lượng"
                />
                <button onClick={() => setQuantity((q) => Math.min(Math.max(1, available), q + 1))} aria-label="Tăng">+</button>
              </div>
              <span className="pd-stock" data-testid="pd-stock">
                {available > 0 ? `${available} sản phẩm có sẵn` : 'Hết hàng'}
              </span>
            </div>

            {!product.purchasable && (
              <div className="pd-unavailable" role="status" data-testid="pd-unavailable">
                {product.shop.onVacation
                  ? `Shop đang tạm nghỉ${product.shop.vacationUntil ? ` đến ${formatDate(product.shop.vacationUntil)}` : ''} — chưa thể đặt mua.`
                  : 'Sản phẩm hiện đã hết hàng.'}
              </div>
            )}

            <div className="product-detail-actions">
              <button
                className="btn-add-cart"
                onClick={() => void add()}
                disabled={!product.purchasable || (sku != null && sku.available <= 0)}
                data-testid="add-to-cart"
              >
                🛒 Thêm Vào Giỏ Hàng
              </button>
              <button className="btn-buy-now" onClick={async () => (await add()) && navigate('/gio-hang')} disabled={!product.purchasable || busy}>
                Mua Ngay
              </button>
              <button className={`btn-wishlist ${liked ? 'liked' : ''}`} onClick={() => toggle(product.id)} data-testid="detail-heart" aria-label="Yêu thích">
                {liked ? '♥ Đã Thích' : '♡ Yêu Thích'} ({formatSold(product.likeCount)})
              </button>
            </div>
          </div>
        </div>

        <div className="pd-shop" data-testid="pd-shop">
          <div className="pd-shop-avatar">
            {product.shop.logoUrl ? <img src={product.shop.logoUrl} alt="" onError={handleImgError} /> : product.shop.name.charAt(0)}
          </div>
          <div className="pd-shop-info">
            <div className="pd-shop-name">{product.shop.name}</div>
            <div className="pd-shop-sub">{product.shop.provinceName ?? ''}</div>
            <div className="pd-shop-actions">
              <Link to={`/shop/${product.shop.slug}`} className="pd-shop-view" data-testid="view-shop">🏪 Xem Shop</Link>
            </div>
          </div>
          <div className="pd-shop-stats">
            <div><strong>{formatSold(product.shop.productCount)}</strong><span>Sản Phẩm</span></div>
            <div><strong>{formatSold(product.shop.followerCount)}</strong><span>Người Theo Dõi</span></div>
            <div><strong>{formatDate(product.shop.joinedAt)}</strong><span>Tham Gia</span></div>
          </div>
        </div>

        <div className="pd-section">
          <h2 className="pd-section-title">CHI TIẾT SẢN PHẨM</h2>
          <table className="pd-specs" data-testid="pd-specs">
            <tbody>
              {leaf && (
                <tr>
                  <td className="pd-spec-key">Danh mục</td>
                  <td className="pd-spec-val"><Link to={`/danh-muc/${leaf.slug}`}>{leaf.name}</Link></td>
                </tr>
              )}
              {product.brandName && (
                <tr>
                  <td className="pd-spec-key">Thương hiệu</td>
                  <td className="pd-spec-val">{product.brandName}</td>
                </tr>
              )}
              {product.attributes.map((a) => (
                <tr key={a.name}>
                  <td className="pd-spec-key">{a.name}</td>
                  <td className="pd-spec-val">{a.value}</td>
                </tr>
              ))}
              <tr>
                <td className="pd-spec-key">Tình trạng</td>
                <td className="pd-spec-val">{product.condition === 'New' ? 'Mới' : 'Đã sử dụng'}</td>
              </tr>
              {product.shop.provinceName && (
                <tr>
                  <td className="pd-spec-key">Gửi từ</td>
                  <td className="pd-spec-val">{product.shop.provinceName}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="pd-section">
          <h2 className="pd-section-title">MÔ TẢ SẢN PHẨM</h2>
          {/* Description HTML is sanitised by the server when the seller saves it */}
          <div className="pd-description" dangerouslySetInnerHTML={{ __html: product.description }} />
        </div>

        <div className="pd-section">
          <h2 className="pd-section-title">ĐÁNH GIÁ SẢN PHẨM</h2>
          <div className="pd-rating-summary" data-testid="reviews">
            <div className="pd-rating-score">
              <span className="pd-rating-big">{product.ratingCount > 0 ? product.ratingAvg.toFixed(1) : '0'}</span>
              <span className="pd-rating-outof">trên 5</span>
            </div>
            <p className="pd-review-empty">
              {product.ratingCount > 0 ? `${formatSold(product.ratingCount)} đánh giá` : 'Chưa có đánh giá nào cho sản phẩm này.'}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};

const Related = ({ id }: { id: string }) => {
  const shop = useQuery({ queryKey: ['product', id, 'shop-products'], queryFn: () => storefrontApi.shopProducts(id) });
  const related = useQuery({ queryKey: ['product', id, 'related'], queryFn: () => storefrontApi.related(id) });
  return (
    <div className="container">
      {(shop.data?.length ?? 0) > 0 && <ProductGrid title="CÁC SẢN PHẨM KHÁC CỦA SHOP" products={shop.data!.slice(0, 6)} />}
      {(related.data?.length ?? 0) > 0 && <ProductGrid title="SẢN PHẨM TƯƠNG TỰ" products={related.data!} />}
    </div>
  );
};

const ProductDetail = () => {
  const { id = '' } = useParams();
  const { data, error, isLoading } = useQuery({ queryKey: ['product', id], queryFn: () => storefrontApi.product(id), retry: false });

  useEffect(() => {
    window.scrollTo(0, 0);
    // One view per viewer per 30 minutes — the server de-duplicates
    if (/^[0-9a-f-]{36}$/i.test(id)) storefrontApi.recordView(id).catch(() => undefined);
  }, [id]);

  if (isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (error || !data) {
    return (
      <div className="container product-not-found" data-testid="product-not-found">
        <p>{error instanceof ApiError && error.status === 404 ? 'Sản phẩm không tồn tại hoặc đã ngừng bán.' : 'Không tải được sản phẩm, vui lòng thử lại.'}</p>
        <Link to="/" className="btn-back-home">Về trang chủ</Link>
      </div>
    );
  }

  return (
    <>
      <ProductView key={data.id} product={data} />
      <Related id={data.id} />
    </>
  );
};

export default ProductDetail;
