import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { storefrontApi } from '../api/storefront';
import { ApiError } from '../api/http';
import { checkoutApi, type CartLine, type CheckoutRequest } from '../api/commerce';
import { formatPrice } from '../lib/money';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import { productPath } from '../lib/urls';
import ProductGrid from '../components/ProductGrid';
import VoucherPicker from '../components/VoucherPicker';
import QueryState from '../components/QueryState';
import { useShopVouchers } from '../stores/shopVouchers';
import { ConfirmButton } from '../components/ConfirmDialog';
import './CartPage.css';
import { ShoppingCart, ChevronDown } from 'lucide-react';

/** "Phân loại: …" with a picker of the product's other SKUs (change variant without leaving the cart). */
const VariantPicker = ({ line, onPick }: { line: CartLine; onPick: (skuId: string) => void }) => {
  const [open, setOpen] = useState(false);
  const { data } = useQuery({ queryKey: ['product', line.productId], queryFn: () => storefrontApi.product(line.productId), enabled: open });
  if (!line.variant) return null;
  return (
    <span className="cart-variant">
      <button type="button" className="cart-item-variant" onClick={() => setOpen((v) => !v)} data-testid="cart-variant">
        Phân loại: {line.variant} <ChevronDown size={14} aria-hidden />
      </button>
      {open && data && (
        <span className="cart-variant-menu">
          {data.skus.map((s) => {
            const label = [s.option1, s.option2].filter(Boolean).join(', ');
            return (
              <button
                key={s.id}
                type="button"
                disabled={s.available <= 0 || s.id === line.skuId}
                className={s.id === line.skuId ? 'active' : ''}
                onClick={() => {
                  setOpen(false);
                  onPick(s.id);
                }}
              >
                {label} {s.available <= 0 ? '(hết hàng)' : ''}
              </button>
            );
          })}
        </span>
      )}
    </span>
  );
};

/** "Sản phẩm tương tự" for a sold-out line (spec 3.4): opens a row of products from the same category. */
const SimilarProducts = ({ productId }: { productId: string }) => {
  const [open, setOpen] = useState(false);
  const similar = useQuery({ queryKey: ['related', productId], queryFn: () => storefrontApi.related(productId), enabled: open });
  return (
    <div className="cart-similar">
      <button type="button" className="cart-similar-toggle" onClick={() => setOpen((v) => !v)} aria-expanded={open} data-testid="cart-similar">
        {open ? 'Ẩn sản phẩm tương tự' : 'Xem sản phẩm tương tự ›'}
      </button>
      {open && (
        <div className="cart-similar-row" data-testid="cart-similar-row">
          <QueryState query={similar} loading={<span className="cart-similar-empty">Đang tải…</span>} isEmpty={(d) => d.length === 0}
            emptyText={<span className="cart-similar-empty">Chưa có sản phẩm tương tự.</span>}>
          {(list) => list.slice(0, 6).map((p) => (
            <Link key={p.id} to={productPath(p.slug, p.shopId, p.id)} className="cart-similar-item" data-testid="cart-similar-item">
              <img src={imageOrPlaceholder(p.imageUrl)} alt="" onError={handleImgError} />
              <span className="cart-similar-name">{p.name}</span>
              <span className="cart-similar-price">{formatPrice(p.minPrice)}</span>
            </Link>
          ))}
          </QueryState>
        </div>
      )}
    </div>
  );
};

const CartPage = () => {
  const navigate = useNavigate();
  const { isLoggedIn } = useAuth();
  const { cart, lines, isLoading, update, remove, select } = useCart();
  const [error, setError] = useState('');
  // Voucher của shop theo từng khối (II.6, E6): the same choice the checkout uses; the server prices it on the ticked lines
  const codes = useShopVouchers((st) => st.codes);
  const setCode = useShopVouchers((st) => st.set);
  const ticked = cart.shops.flatMap((s) => s.lines.filter((l) => l.isSelected).map((l) => `${l.skuId}:${l.quantity}`)).join(',');
  const quoteRequest: CheckoutRequest = {
    addressId: null,
    shops: cart.shops.map((s) => ({ shopId: s.shopId, carrierCode: null, voucherCode: codes[s.shopId] ?? null })),
    platformVoucherCode: null,
    freeshipVoucherCode: null,
    useCoins: false,
    paymentMethod: 'Cod',
    paymentOption: 'Default',
  };
  const quote = useQuery({
    queryKey: ['cart-quote', ticked, codes],
    queryFn: () => checkoutApi.quote(quoteRequest),
    enabled: isLoggedIn && ticked.length > 0,
    placeholderData: keepPreviousData,
  });

  const run = async (action: () => Promise<void>) => {
    setError('');
    try {
      await action();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Không cập nhật được giỏ hàng, vui lòng thử lại.');
    }
  };

  if (isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (lines.length === 0) {
    return (
      <div className="cart-page">
        <div className="container">
          <div className="cart-empty" data-testid="cart-empty">
            <div className="cart-empty-icon"><ShoppingCart size={56} strokeWidth={1.25} aria-hidden /></div>
            <p>Giỏ hàng của bạn còn trống</p>
            <Link to="/" className="cart-empty-btn">Mua Sắm Ngay</Link>
          </div>
        </div>
      </div>
    );
  }

  const allSelected = lines.every((l) => l.isSelected);
  const selected = lines.filter((l) => l.isSelected);
  const blocked = selected.filter((l) => !l.canBuy);

  const checkout = () => {
    if (selected.length === 0) return;
    if (blocked.length > 0) {
      setError(`Vui lòng bỏ chọn hoặc sửa ${blocked.length} sản phẩm chưa thể mua trước khi thanh toán.`);
      return;
    }
    navigate(isLoggedIn ? '/thanh-toan' : '/dang-nhap', isLoggedIn ? undefined : { state: { from: '/thanh-toan' } });
  };

  return (
    <div className="cart-page">
      <div className="container">
        <h1 className="cart-title">Giỏ Hàng</h1>
        {error && <div className="cart-error" role="alert" data-testid="cart-error">{error}</div>}

        <div className="cart-header-row">
          <span className="cart-col-check">
            <input type="checkbox" checked={allSelected} onChange={() => run(() => select(!allSelected))} aria-label="Chọn tất cả" data-testid="select-all" />
          </span>
          <span className="cart-col-product">Sản Phẩm</span>
          <span className="cart-col-price">Đơn Giá</span>
          <span className="cart-col-qty">Số Lượng</span>
          <span className="cart-col-total">Số Tiền</span>
          <span className="cart-col-action">Thao Tác</span>
        </div>

        {cart.shops.map((shop) => {
          const shopSelected = shop.lines.every((l) => l.isSelected);
          return (
            <div key={shop.shopId} className="cart-shop" data-testid="cart-shop">
              <div className="cart-shop-head">
                <input
                  type="checkbox"
                  checked={shopSelected}
                  onChange={() => run(() => select(!shopSelected, shop.shopId))}
                  aria-label={`Chọn tất cả sản phẩm của ${shop.shopName}`}
                  data-testid="select-shop"
                />
                {shop.isMall && <span className="cart-shop-mall">Mall</span>}
                <Link to={`/shop/${shop.shopSlug}`} className="cart-shop-name">{shop.shopName}</Link>
                {shop.onVacation && <span className="cart-shop-vacation">Shop đang tạm nghỉ</span>}
              </div>
              {shop.lines.map((item) => (
                <div key={item.skuId} className={`cart-item ${item.canBuy ? '' : 'cart-item-blocked'}`} data-testid="cart-item">
                  <span className="cart-col-check">
                    <input
                      type="checkbox"
                      checked={item.isSelected}
                      onChange={() => run(() => update(item.skuId, { selected: !item.isSelected }))}
                      aria-label={`Chọn ${item.name}`}
                      data-testid="select-item"
                    />
                  </span>
                  <div className="cart-col-product cart-item-product">
                    <img src={imageOrPlaceholder(item.imageUrl)} alt={item.name} className="cart-item-img" onError={handleImgError} />
                    <div className="cart-item-textblock">
                      <Link to={`/san-pham/${item.productId}`} className="cart-item-name">{item.name}</Link>
                      <VariantPicker line={item} onPick={(skuId) => run(() => update(item.skuId, { skuId }))} />
                      {item.problem && (
                        <span className="cart-item-problem" data-testid="cart-item-problem">
                          {item.problem}{' '}
                        </span>
                      )}
                      {item.available === 0 && <SimilarProducts productId={item.productId} />}
                    </div>
                  </div>
                  <span className="cart-col-price cart-item-price">
                    {item.previousPrice !== null && <span className="cart-item-old-price" data-testid="cart-old-price">{formatPrice(item.previousPrice)}</span>}
                    {formatPrice(item.price)}
                    {item.priceLabel && <span className="cart-price-label" data-testid="cart-price-label">{item.priceLabel}</span>}
                  </span>
                  <div className="cart-col-qty cart-item-qty">
                    <button onClick={() => run(() => update(item.skuId, { quantity: item.quantity - 1 }))} disabled={item.quantity <= 1} aria-label="Giảm">−</button>
                    <input
                      type="number"
                      min="1"
                      max={Math.max(1, item.available)}
                      defaultValue={item.quantity}
                      key={item.quantity}
                      onBlur={(e) => {
                        const q = Math.max(1, Number(e.target.value) || 1);
                        if (q !== item.quantity) void run(() => update(item.skuId, { quantity: q }));
                      }}
                      aria-label="Số lượng"
                    />
                    <button onClick={() => run(() => update(item.skuId, { quantity: item.quantity + 1 }))} disabled={item.quantity >= item.available} aria-label="Tăng">+</button>
                  </div>
                  <span className="cart-col-total cart-item-total">{formatPrice(item.price * item.quantity)}</span>
                  <div className="cart-col-action">
                    <ConfirmButton className="cart-item-remove" message={`Xoá "${item.name}" khỏi giỏ hàng?`} confirmLabel="Xoá"
                      onConfirm={() => run(() => remove([item.skuId]))} testId="cart-item-remove">Xóa</ConfirmButton>
                  </div>
                </div>
              ))}
              <ShopVoucherBlock
                loggedIn={isLoggedIn}
                ticked={shop.lines.some((l) => l.isSelected)}
                quoteShop={quote.data?.shops.find((q) => q.shopId === shop.shopId)}
                code={codes[shop.shopId] ?? null}
                onChange={(code) => setCode(shop.shopId, code)}
              />
            </div>
          );
        })}

        <div className="cart-footer">
          <div className="cart-footer-left">
            <input type="checkbox" checked={allSelected} onChange={() => run(() => select(!allSelected))} aria-label="Chọn tất cả" />
            <button className="cart-select-all-btn" onClick={() => run(() => select(!allSelected))}>Chọn Tất Cả ({lines.length})</button>
            <ConfirmButton className="cart-clear" message={`Xoá ${selected.length} sản phẩm đã chọn khỏi giỏ hàng?`} confirmLabel="Xoá"
              onConfirm={() => run(() => remove(selected.map((l) => l.skuId)))} disabled={selected.length === 0} testId="cart-clear">Xóa</ConfirmButton>
          </div>
          <div className="cart-summary">
            <span className="cart-summary-label">Tổng thanh toán ({cart.selectedQuantity} sản phẩm):</span>
            <span className="cart-summary-total" data-testid="cart-total">{formatPrice(cart.selectedSubtotal)}</span>
            <button className="cart-checkout" onClick={checkout} disabled={selected.length === 0} data-testid="checkout">Mua Hàng</button>
          </div>
        </div>

        <YouMayLike />
      </div>
    </div>
  );
};

/** One shop block's voucher (E6): pick / type a code, see what the shop takes off the ticked lines (priced by the server). */
const ShopVoucherBlock = ({ loggedIn, ticked, quoteShop, code, onChange }: {
  loggedIn: boolean;
  ticked: boolean;
  quoteShop: { shopVoucherOptions: Parameters<typeof VoucherPicker>[0]['options']; shopDiscount: number } | undefined;
  code: string | null;
  onChange: (code: string | null) => void;
}) => {
  if (!loggedIn) {
    return (
      <div className="cart-shop-voucher" data-testid="cart-shop-voucher">
        <Link to="/dang-nhap" state={{ from: '/gio-hang' }}>Đăng nhập</Link> để chọn voucher của shop.
      </div>
    );
  }
  if (!ticked) return <div className="cart-shop-voucher" data-testid="cart-shop-voucher">Chọn sản phẩm của shop để dùng voucher.</div>;
  if (!quoteShop) return null;
  return (
    <div className="cart-shop-voucher" data-testid="cart-shop-voucher">
      <VoucherPicker title="Voucher của shop" options={quoteShop.shopVoucherOptions} value={code} onChange={onChange} testId="cart-voucher" />
      {quoteShop.shopDiscount > 0 && <span className="cart-shop-voucher-saving" data-testid="cart-voucher-saving">Shop giảm {formatPrice(quoteShop.shopDiscount)}</span>}
    </div>
  );
};

/** "Bạn có thể thích" under the cart (II.6): the same personalised suggestions as the home page. */
const YouMayLike = () => {
  const suggestions = useQuery({ queryKey: ['cart-suggestions'], queryFn: () => storefrontApi.recommendations(1, 12), staleTime: 60_000 });
  if (!suggestions.data?.items.length) return null;
  return (
    <div className="cart-suggestions" data-testid="cart-suggestions">
      <ProductGrid title="BẠN CÓ THỂ THÍCH" products={suggestions.data.items} />
    </div>
  );
};

export default CartPage;
