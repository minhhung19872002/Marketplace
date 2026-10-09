import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useParams, Link, useNavigate, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { ApiError } from '../api/http';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { useWishlist } from '../context/WishlistContext';
import ProductGrid from '../components/ProductGrid';
import ProductCard from '../components/ProductCard';
import ProductReviews from '../components/ProductReviews';
import ProductGallery from '../components/ProductGallery';
import Countdown from '../components/Countdown';
import { viewSource } from '../lib/navigation';
import { productIdOf, productPath } from '../lib/urls';
import ShopVouchers from '../components/ShopVouchers';
import ProductShipping from '../components/ProductShipping';
import ShareProduct from '../components/ShareProduct';
import QueryState from '../components/QueryState';
import ReportProduct from '../components/ReportProduct';
import Carousel from '../components/ui/Carousel';
import { ChatNowButton, ChatStats } from '../components/chat/Chat';
import { clockSkew, marketingApi } from '../api/marketing';
import { formatPrice, formatSold } from '../lib/money';
import { formatDate, formatSince } from '../lib/datetime';
import { handleImgError, imageOrPlaceholder, imageSrcSet } from '../lib/image';
import type { ProductPage, PublicSku } from '../types';
import './ProductDetail.css';
import { usePageTitle } from '../lib/pageTitle';
import { toast } from '../lib/toast';
import { Check, Zap, Store, Heart, ShoppingCart, X } from 'lucide-react';
import { Badge, Section, Stars } from '../components/ui';

const priceRange = (min: number, max: number) => (min === max ? formatPrice(min) : `${formatPrice(min)} - ${formatPrice(max)}`);

/** SKUs still possible with the current selection (null = not chosen yet). */
const matching = (skus: PublicSku[], picked: (string | null)[]) =>
  skus.filter((s) => (picked[0] == null || s.option1 === picked[0]) && (picked[1] == null || s.option2 === picked[1]));

/** The shop block's figures that have data (G2-A5): a count of 0 or a rating nobody gave is left out, not shown as "0". */
export const shopFacts = (shop: ProductPage['shop']) => [
  ...(shop.ratingCount > 0 ? [{ key: 'rating', value: shop.ratingAvg.toFixed(1), label: `Đánh giá (${formatSold(shop.ratingCount)})` }] : []),
  ...(shop.productCount > 0 ? [{ key: 'products', value: formatSold(shop.productCount), label: 'Sản phẩm' }] : []),
  ...(shop.followerCount > 0 ? [{ key: 'followers', value: formatSold(shop.followerCount), label: 'Người theo dõi' }] : []),
  { key: 'joined', value: formatDate(shop.joinedAt), label: 'Tham gia' },
];

/** Long descriptions fold to a fixed height with "Xem thêm" (G2-B2). */
const Description = ({ html }: { html: string }) => {
  const box = useRef<HTMLDivElement>(null);
  const [long, setLong] = useState(false);
  const [open, setOpen] = useState(false);
  useLayoutEffect(() => {
    if (box.current) setLong(box.current.scrollHeight > 460);
  }, [html]);
  return (
    <div className="pd-section">
      <h2 className="pd-section-title">Mô tả sản phẩm</h2>
      {/* Description HTML is sanitised by the server when the seller saves it */}
      <div ref={box} id="pd-description" className={`pd-description ${long && !open ? 'is-folded' : ''}`} dangerouslySetInnerHTML={{ __html: html }} />
      {long && (
        <button type="button" className="pd-description-toggle" onClick={() => setOpen(!open)} aria-expanded={open} aria-controls="pd-description"
          data-testid="description-toggle">
          {open ? 'Thu gọn' : 'Xem thêm'}
        </button>
      )}
    </div>
  );
};

const ProductView = ({ product }: { product: ProductPage }) => {
  const navigate = useNavigate();
  const { add: addToCart, select: selectInCart, update: updateInCart } = useCart();
  const { has, toggle } = useWishlist();

  const tiers = product.tiers;
  const [picked, setPicked] = useState<(string | null)[]>([null, null]);
  const [quantity, setQuantity] = useState(1);
  const [variantError, setVariantError] = useState(false);
  const [activeImg, setActiveImg] = useState(0);
  // Mobile: the buy bar opens a sheet to pick the variant and quantity, then adds or buys
  const [sheet, setSheet] = useState<'cart' | 'buy' | null>(null);

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
  // The quantity box stops at the stock and at the per-buyer limit (the cart and checkout also count past orders)
  const maxQty = Math.max(1, Math.min(available, product.maxPerBuyer ?? Number.MAX_SAFE_INTEGER));

  // Programme prices (discount / Flash Sale) and shop offers; the countdown runs on the server's clock
  const deals = useQuery({ queryKey: ['deals', product.id], queryFn: () => marketingApi.deals(product.id), staleTime: 30_000 });
  const [receivedAt, setReceivedAt] = useState(0);
  useEffect(() => {
    if (deals.data) setReceivedAt(Date.now());
  }, [deals.data]);
  const dealOf = (skuId: string) => deals.data?.skus.find((d) => d.skuId === skuId && d.price < d.basePrice);
  const skuDeal = sku ? dealOf(sku.id) : undefined;
  const dealPrices = product.skus.map((s) => dealOf(s.id)?.price ?? s.price);
  const flash = deals.data?.flash;

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
      // The shared toast reports the server's answer (F4)
      await addToCart(sku.id, quantity);
      return true;
    } catch (e) {
      if (!(e instanceof ApiError)) toast.error('Không thêm được vào giỏ, vui lòng thử lại.');
      return false;
    } finally {
      setBusy(false);
    }
  };

  /** An add-on of a "Mua kèm deal sốc" offer (L142): its deal price applies at checkout when this product is in the cart too. */
  const addOffer = async (skuId: string) => {
    try {
      await addToCart(skuId, 1);
    } catch (e) {
      if (!(e instanceof ApiError)) toast.error('Không thêm được vào giỏ, vui lòng thử lại.');
    }
  };

  /** "Mua ngay" (II.4, E4): straight to checkout with only this line ticked — the other cart lines stay, unticked. */
  const buyNow = async () => {
    if (!(await add()) || !sku) return;
    try {
      await selectInCart(false);
      await updateInCart(sku.id, { selected: true });
      navigate('/thanh-toan');
    } catch (e) {
      if (!(e instanceof ApiError)) toast.error('Không mở được trang thanh toán, vui lòng thử lại.');
    }
  };

  const confirmSheet = async () => {
    if (sheet === 'buy') await buyNow();
    else if (await add()) setSheet(null);
  };

  const liked = has(product.id);
  const missingTiers = tiers.filter((_, i) => picked[i] == null).map((t) => t.name);
  const leaf = product.breadcrumb[product.breadcrumb.length - 1];

  const priceBox = (
    <div className="product-detail-price-box">
      {sku && skuDeal ? (
        <>
          <span className="price-original">{formatPrice(Math.max(sku.originalPrice, sku.price))}</span>
          <span className="price-current" data-testid="pd-price">{formatPrice(skuDeal.price)}</span>
          <span className="price-discount">{skuDeal.label}</span>
        </>
      ) : sku ? (
        <>
          {sku.originalPrice > sku.price && <span className="price-original">{formatPrice(sku.originalPrice)}</span>}
          <span className="price-current" data-testid="pd-price">{formatPrice(sku.price)}</span>
          {sku.originalPrice > sku.price && (
            <span className="price-discount">{Math.floor(((sku.originalPrice - sku.price) * 100) / sku.originalPrice)}% GIẢM</span>
          )}
        </>
      ) : dealPrices.some((d, i) => d < product.skus[i].price) ? (
        <>
          {/* A deal on a middle SKU leaves the range unchanged: nothing to strike then */}
          {priceRange(product.minPrice, product.maxPrice) !== priceRange(Math.min(...dealPrices), Math.max(...dealPrices)) && (
            <span className="price-original">{priceRange(product.minPrice, product.maxPrice)}</span>
          )}
          <span className="price-current" data-testid="pd-price">{priceRange(Math.min(...dealPrices), Math.max(...dealPrices))}</span>
          <span className="price-discount">{flash ? 'Flash Sale' : 'Giảm giá'}</span>
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
  );

  // Variant chips: the chosen one carries a tick in its corner, sold-out ones are struck through and cannot be pressed
  const variantRows = (
    <>
      {tiers.map((tier, ti) => (
        <div key={tier.name} className="product-detail-row product-detail-row-top">
          <span className="row-label">{tier.name}</span>
          <div className="pd-variants" role="group" aria-label={tier.name}>
            {tier.options.map((opt) => {
              const enabled = optionAvailable(ti, opt.value);
              const active = picked[ti] === opt.value;
              return (
                <button
                  key={opt.value}
                  type="button"
                  className={`pd-variant ${active ? 'active' : ''} ${enabled ? '' : 'is-soldout'}`}
                  onClick={() => pick(ti, opt.value)}
                  disabled={!enabled}
                  aria-pressed={active}
                  title={enabled ? undefined : 'Hết hàng'}
                  data-testid="variant-option"
                >
                  {opt.imageUrl && <img src={opt.imageUrl} alt="" className="pd-variant-img" onError={handleImgError} />}
                  {opt.value}
                  {active && <span className="pd-variant-tick" aria-hidden><Check size={10} strokeWidth={3} /></span>}
                </button>
              );
            })}
          </div>
        </div>
      ))}
      {variantError && missingTiers.length > 0 && (
        <div className="pd-variant-error" data-testid="variant-error" role="alert">
          Vui lòng chọn {missingTiers.join(', ')}
        </div>
      )}
    </>
  );

  const quantityRow = (
    <div className="product-detail-row pd-quantity-row">
      <span className="row-label">Số lượng</span>
      <div className="quantity-control">
        <button type="button" onClick={() => setQuantity((q) => Math.max(1, q - 1))} aria-label="Giảm">−</button>
        <input
          type="number"
          min="1"
          max={maxQty}
          value={quantity}
          onChange={(e) => setQuantity(Math.min(maxQty, Math.max(1, Number(e.target.value) || 1)))}
          aria-label="Số lượng"
        />
        <button type="button" onClick={() => setQuantity((q) => Math.min(maxQty, q + 1))} aria-label="Tăng">+</button>
      </div>
      <span className="pd-stock" data-testid="pd-stock">
        {available > 0 ? `${available} sản phẩm có sẵn` : 'Hết hàng'}
      </span>
      {product.maxPerBuyer && (
        <span className="pd-limit" data-testid="pd-limit">Mỗi người mua tối đa {product.maxPerBuyer} sản phẩm</span>
      )}
    </div>
  );

  return (
    <div className="product-detail">
      <div className="container">
        <nav className="breadcrumb" aria-label="Đường dẫn" data-testid="product-breadcrumb">
          <Link to="/">ShopHub</Link>
          {product.breadcrumb.map((c) => (
            <span key={c.id} className="breadcrumb-step">
              <span aria-hidden>›</span>
              <Link to={`/danh-muc/${c.slug}`}>{c.name}</Link>
            </span>
          ))}
          <span aria-hidden>›</span>
          <span className="breadcrumb-current" title={product.name} aria-current="page">{product.name}</span>
        </nav>

        <div className="product-detail-main">
          <div className="product-detail-gallery">
            <ProductGallery images={gallery} alt={product.name} active={activeImg} onActive={setActiveImg} videoUrl={video?.url} />
            <div className="pd-gallery-foot">
              <ShareProduct title={product.name} />
              <button type="button" className={`btn-wishlist ${liked ? 'liked' : ''}`} onClick={() => toggle(product.id)} data-testid="detail-heart"
                aria-pressed={liked} aria-label="Yêu thích">
                <Heart size={18} fill={liked ? 'currentColor' : 'none'} aria-hidden /> {liked ? 'Đã thích' : 'Yêu thích'} ({formatSold(product.likeCount)})
              </button>
            </div>
          </div>

          <div className="product-detail-info">
            <h1 className="product-detail-name" data-testid="pd-name">
              {product.shop.isMall && <Badge tone="mall" className="pd-name-badge">Mall</Badge>}
              {product.shop.isPreferred && !product.shop.isMall && <Badge tone="preferred" className="pd-name-badge">Yêu thích</Badge>}
              {product.name}
            </h1>

            {/* Rating · reviews · sold wrap as one line; the dividers sit before each item and are clipped at a line start */}
            <div className="product-detail-stats">
              <div className="pd-stats-clip">
                <ul className="pd-stats-list">
                  {product.ratingCount > 0 ? (
                    <>
                      <li className="stat-rating">
                        {product.ratingAvg.toFixed(1)} <Stars value={product.ratingAvg} size={14} />
                      </li>
                      <li className="stat-count">{formatSold(product.ratingCount)} đánh giá</li>
                    </>
                  ) : (
                    <li className="stat-count">Chưa có đánh giá</li>
                  )}
                  <li className="stat-sold">{formatSold(product.soldCount)} đã bán</li>
                </ul>
              </div>
              <span className="stat-report"><ReportProduct productId={product.id} /></span>
            </div>

            {flash && deals.data && receivedAt > 0 && (
              <div className="pd-flash" data-testid="pd-flash">
                <span className="pd-flash-title"><Zap size={18} fill="currentColor" aria-hidden /> {flash.platform ? 'Flash Sale' : 'Flash Sale của shop'}</span>
                <span>Kết thúc sau <Countdown endAt={flash.endAt} skewMs={clockSkew(deals.data.serverTime, receivedAt)} /></span>
                <span className="pd-flash-sold">Đã bán {flash.sold}/{flash.quota} · tối đa {flash.perUserLimit} sản phẩm/người</span>
              </div>
            )}
            {priceBox}

            {(deals.data?.offers.length ?? 0) > 0 && (
              <div className="product-detail-row" data-testid="pd-offers">
                <span className="row-label">Ưu đãi shop</span>
                <span className="pd-offers">
                  {deals.data!.offers.map((o) => (
                    <span key={o.promotionId} className="pd-offer-block">
                      <span className="pd-offer">{o.text}</span>
                      {/* L142: the items of the offer — add-ons go to the cart at their deal price, combo products open their page */}
                      {(o.items ?? []).map((it) => (
                        <span key={`${o.promotionId}-${it.skuId ?? it.productId}`} className="pd-offer-item" data-testid="pd-offer-item">
                          {it.imageUrl && <img src={it.imageUrl} alt="" onError={handleImgError} />}
                          <Link to={`/san-pham/${it.productId}`} className="pd-offer-name">{it.name}{it.variant ? ` (${it.variant})` : ''}</Link>
                          <span className="pd-offer-price">
                            {o.type === 'Gift' ? 'Quà tặng' : formatPrice(it.price)}
                            {it.basePrice > it.price && <s>{formatPrice(it.basePrice)}</s>}
                          </span>
                          {o.type === 'AddOn' && it.skuId && (
                            <button type="button" className="pd-offer-add" data-testid="pd-offer-add" onClick={() => void addOffer(it.skuId!)}>Thêm vào giỏ</button>
                          )}
                        </span>
                      ))}
                    </span>
                  ))}
                </span>
              </div>
            )}

            <ShopVouchers shopId={product.shop.id} compact rowLabel="Voucher của shop" moreTo={`/shop/${product.shop.slug}`} />

            <ProductShipping productId={product.id} />

            {product.isPreorder && (
              <div className="product-detail-row">
                <span className="row-label">Đặt trước</span>
                <span>Hàng đặt trước — chuẩn bị trong {product.preorderDays} ngày</span>
              </div>
            )}

            {/* While the mobile sheet is open it owns the variant and quantity inputs (one of each on the page) */}
            {!sheet && <div className="pd-desktop-only">
              {variantRows}
              {quantityRow}
            </div>}

            {!product.purchasable && (
              <div className="pd-unavailable" role="status" data-testid="pd-unavailable">
                {product.shop.onVacation
                  ? `Shop đang tạm nghỉ${product.shop.vacationUntil ? ` đến ${formatDate(product.shop.vacationUntil)}` : ''} — chưa thể đặt mua.`
                  : 'Sản phẩm hiện đã hết hàng.'}
              </div>
            )}

            <div className="product-detail-actions">
              <button
                type="button"
                className="btn-add-cart"
                onClick={() => void add()}
                disabled={!product.purchasable || (sku != null && sku.available <= 0)}
                data-testid="add-to-cart"
              >
                <ShoppingCart size={20} aria-hidden /> Thêm vào giỏ hàng
              </button>
              <button type="button" className="btn-buy-now" onClick={() => void buyNow()} disabled={!product.purchasable || busy} data-testid="buy-now">
                Mua ngay
              </button>
            </div>
          </div>
        </div>

        <div className="pd-shop" data-testid="pd-shop">
          <Link to={`/shop/${product.shop.slug}`} className="pd-shop-avatar" aria-label={product.shop.name}>
            {product.shop.logoUrl ? <img src={product.shop.logoUrl} alt="" onError={handleImgError} /> : product.shop.name.charAt(0)}
            {product.shop.isMall && <Badge tone="mall" className="pd-shop-avatar-badge">Mall</Badge>}
          </Link>
          <div className="pd-shop-info">
            <div className="pd-shop-name">{product.shop.name}</div>
            <div className="pd-shop-sub" data-testid="shop-last-active">
              {product.shop.lastActiveAt && `Online ${formatSince(product.shop.lastActiveAt)}`}
              {product.shop.lastActiveAt && product.shop.provinceName && ' · '}
              {product.shop.provinceName ?? ''}
            </div>
            <div className="pd-shop-actions">
              <ChatNowButton shopId={product.shop.id} productId={product.id} className="pd-shop-chat" />
              <Link to={`/shop/${product.shop.slug}`} className="pd-shop-view" data-testid="view-shop"><Store size={16} aria-hidden /> Xem shop</Link>
            </div>
          </div>
          <div className="pd-shop-stats">
            {shopFacts(product.shop).map((f) => (
              <div key={f.key} data-testid={f.key === 'rating' ? 'shop-rating' : undefined}>
                <strong>{f.value}</strong><span>{f.label}</span>
              </div>
            ))}
            <ChatStats shopId={product.shop.id} />
          </div>
        </div>

        <div className="pd-lower">
          <div className="pd-lower-main">
            <div className="pd-section">
              <h2 className="pd-section-title">Chi tiết sản phẩm</h2>
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

            <Description html={product.description} />

            <ProductReviews productId={product.id} />
          </div>
          <ShopBestSellers productId={product.id} />
        </div>
      </div>

      {/* Mobile: a buy bar fixed to the bottom; the actions open a sheet to pick the variant and quantity */}
      <div className="pd-mobile-bar" data-testid="pd-mobile-bar">
        <ChatNowButton shopId={product.shop.id} productId={product.id} className="pd-mobile-chat" testId="mobile-chat" label="Chat" />
        <button type="button" className="pd-mobile-cart" onClick={() => setSheet('cart')} disabled={!product.purchasable} data-testid="mobile-add-to-cart">
          <ShoppingCart size={20} aria-hidden /> Thêm vào giỏ
        </button>
        <button type="button" className="pd-mobile-buy" onClick={() => setSheet('buy')} disabled={!product.purchasable} data-testid="mobile-buy-now">
          Mua ngay
        </button>
      </div>
      {sheet && (
        <div className="pd-sheet" role="dialog" aria-modal="true" aria-label={sheet === 'buy' ? 'Mua ngay' : 'Thêm vào giỏ hàng'} data-testid="pd-sheet"
          onKeyDown={(e) => { if (e.key === 'Escape') setSheet(null); }}>
          <button type="button" className="pd-sheet-backdrop" aria-label="Đóng" onClick={() => setSheet(null)} tabIndex={-1} />
          <div className="pd-sheet-panel">
            <div className="pd-sheet-head">
              <img src={gallery[activeImg] ?? gallery[0]} alt="" onError={handleImgError} />
              <div className="pd-sheet-price">{priceBox}<span className="pd-stock">Kho: {available}</span></div>
              <button type="button" className="pd-sheet-close" onClick={() => setSheet(null)} aria-label="Đóng" autoFocus><X size={22} aria-hidden /></button>
            </div>
            <div className="pd-sheet-body">
              {variantRows}
              {quantityRow}
            </div>
            <button type="button" className="pd-sheet-confirm" onClick={() => void confirmSheet()} disabled={busy || (sku != null && sku.available <= 0)}
              data-testid="pd-sheet-confirm">
              {sheet === 'buy' ? 'Mua ngay' : 'Thêm vào giỏ hàng'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

/** "Top sản phẩm bán chạy của shop" (G2-B2): a sticky sidebar next to the details on wide screens. */
const ShopBestSellers = ({ productId }: { productId: string }) => {
  const shop = useQuery({ queryKey: ['product', productId, 'shop-products'], queryFn: () => storefrontApi.shopProducts(productId) });
  const top = (shop.data ?? []).filter((p) => p.soldCount > 0).slice(0, 5);
  if (top.length === 0) return null;
  return (
    <aside className="pd-aside" aria-labelledby="pd-aside-title" data-testid="shop-best-sellers">
      <h2 id="pd-aside-title" className="pd-aside-title">Top sản phẩm bán chạy</h2>
      {top.map((p) => (
        <Link key={p.id} to={productPath(p.slug, p.shopId, p.id)} className="pd-aside-item">
          <img src={imageOrPlaceholder(p.imageUrl)} srcSet={imageSrcSet(p.imageUrl)} sizes="200px" alt="" loading="lazy" width={160} height={160} onError={handleImgError} />
          <span className="pd-aside-name">{p.name}</span>
          <span className="pd-aside-price">{formatPrice(p.minPrice)}</span>
        </Link>
      ))}
    </aside>
  );
};

const Related = ({ id, shopSlug }: { id: string; shopSlug: string }) => {
  const shop = useQuery({ queryKey: ['product', id, 'shop-products'], queryFn: () => storefrontApi.shopProducts(id) });
  const related = useQuery({ queryKey: ['product', id, 'related'], queryFn: () => storefrontApi.related(id) });
  return (
    <div className="container pd-related">
      {(shop.data?.length ?? 0) > 0 && (
        <Section title="Các sản phẩm khác của shop" more={{ to: `/shop/${shopSlug}` }} testId="shop-other-products">
          <div className="pd-related-row">
            <Carousel label="Các sản phẩm khác của shop">{shop.data!.map((p) => <ProductCard key={p.id} product={p} />)}</Carousel>
          </div>
        </Section>
      )}
      {(related.data?.length ?? 0) > 0 && <ProductGrid title="Có thể bạn cũng thích" products={related.data!} />}
    </div>
  );
};

const ProductDetail = () => {
  const { id: key = '' } = useParams();
  // /san-pham/{id} and /san-pham/{slug}-i.{shopId}.{id} both open the product
  const id = productIdOf(key) ?? key;
  const navigate = useNavigate();
  const location = useLocation();
  const productQuery = useQuery({ queryKey: ['product', id], queryFn: () => storefrontApi.product(id), retry: false });
  const { data, error, isLoading } = productQuery;
  usePageTitle(data?.name ?? (error ? 'Không tìm thấy sản phẩm' : null));

  useEffect(() => { window.scrollTo(0, 0); }, [id]);
  // One view per viewer per 30 minutes — the server de-duplicates. Wait for the session to be restored, or a signed-in
  // buyer opening a product link directly is counted as a guest and the product never shows in "Đã xem" (L059).
  const { isChecking } = useAuth();
  useEffect(() => {
    if (isChecking) return;
    if (/^[0-9a-f-]{36}$/i.test(id)) storefrontApi.recordView(id, viewSource()).catch(() => undefined);
  }, [id, isChecking]);

  // Old or stale URLs settle on the canonical one (the crawler version answers 301 for the same)
  useEffect(() => {
    if (!data) return;
    const canonical = productPath(data.slug, data.shop.id, data.id);
    if (location.pathname !== canonical) navigate(`${canonical}${location.search}`, { replace: true });
  }, [data, location.pathname, location.search, navigate]);

  if (isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
  // A network / server error is not "no such product": the error with "Thử lại" (F3)
  if (error && !(error instanceof ApiError && error.status === 404)) {
    return <div className="container"><QueryState query={productQuery}>{() => null}</QueryState></div>;
  }
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
      <Related id={data.id} shopSlug={data.shop.slug} />
    </>
  );
};

export default ProductDetail;
