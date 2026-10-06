import { useMemo, useRef, useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { checkoutApi, type CheckoutRequest, type PaymentMethod, type VoucherOption } from '../api/commerce';
import { accountApi } from '../api/account';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { CART_KEY } from '../context/CartContext';
import { formatCount, formatPrice } from '../lib/money';
import { formatDate } from '../lib/datetime';
import { goTo } from '../lib/navigation';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import './Checkout.css';

const newKey = () => (typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`);

const voucherLabel = (v: VoucherOption) =>
  `${v.code} — ${v.name}${v.usable && v.discount > 0 ? ` (−${formatPrice(v.discount)})` : ''}`;

/** Pick from the list (unusable ones stay visible with the reason) or type a code. */
const VoucherPicker = ({
  title,
  options,
  value,
  onChange,
  testId,
}: {
  title: string;
  options: VoucherOption[];
  value: string | null;
  onChange: (code: string | null) => void;
  testId: string;
}) => {
  const [typed, setTyped] = useState('');
  return (
    <div className="checkout-voucher" data-testid={testId}>
      <span className="checkout-voucher-title">{title}</span>
      <select value={value ?? ''} onChange={(e) => onChange(e.target.value || null)} aria-label={title} data-testid={`${testId}-select`}>
        <option value="">— Không dùng —</option>
        {options.map((v) => (
          <option key={v.id} value={v.code} disabled={!v.usable && v.code !== value}>
            {voucherLabel(v)}{v.problem ? ` · ${v.problem}` : ''}
          </option>
        ))}
      </select>
      <input value={typed} onChange={(e) => setTyped(e.target.value.toUpperCase())} placeholder="Nhập mã" aria-label={`Nhập mã ${title}`} data-testid={`${testId}-input`} />
      <button
        type="button"
        onClick={() => {
          if (typed.trim()) onChange(typed.trim());
          setTyped('');
        }}
        data-testid={`${testId}-apply`}
      >
        Áp dụng
      </button>
      {options.filter((v) => !v.usable && v.problem).length > 0 && (
        <ul className="checkout-voucher-unusable">
          {options.filter((v) => !v.usable && v.problem).slice(0, 3).map((v) => (
            <li key={v.id}>
              <strong>{v.code}</strong>: {v.problem}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

const Checkout = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { isLoggedIn, isChecking } = useAuth();
  const idempotencyKey = useRef(newKey());

  const [addressId, setAddressId] = useState<string | null>(null);
  const [carriers, setCarriers] = useState<Record<string, string>>({});
  const [shopVouchers, setShopVouchers] = useState<Record<string, string | null>>({});
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [platformCode, setPlatformCode] = useState<string | null>(null);
  const [freeshipCode, setFreeshipCode] = useState<string | null>(null);
  const [useCoins, setUseCoins] = useState(false);
  const [method, setMethod] = useState<PaymentMethod>('Cod');
  const [walletPin, setWalletPin] = useState('');
  const [placing, setPlacing] = useState(false);
  const [error, setError] = useState('');

  const addresses = useQuery({ queryKey: ['addresses'], queryFn: accountApi.addresses, enabled: isLoggedIn });

  const request: CheckoutRequest = useMemo(
    () => ({
      addressId,
      shops: Array.from(new Set([...Object.keys(carriers), ...Object.keys(shopVouchers)])).map((shopId) => ({
        shopId,
        carrierCode: carriers[shopId] ?? null,
        voucherCode: shopVouchers[shopId] ?? null,
      })),
      platformVoucherCode: platformCode,
      freeshipVoucherCode: freeshipCode,
      useCoins,
      paymentMethod: method,
    }),
    [addressId, carriers, shopVouchers, platformCode, freeshipCode, useCoins, method],
  );

  const quoteQuery = useQuery({
    queryKey: ['checkout-quote', request],
    queryFn: () => checkoutApi.quote(request),
    enabled: isLoggedIn,
    placeholderData: keepPreviousData,
    staleTime: 0,
  });

  if (isChecking) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!isLoggedIn) return <Navigate to="/dang-nhap" replace state={{ from: '/thanh-toan' }} />;
  const quote = quoteQuery.data;
  if (quoteQuery.isLoading || !quote) return <div className="page-loader"><div className="loading-spinner" /></div>;

  if (quote.shops.length === 0) {
    return (
      <div className="checkout-page">
        <div className="container checkout-empty">
          <p>{quote.problems[0] ?? 'Không có sản phẩm để thanh toán.'}</p>
          <Link to="/gio-hang" className="checkout-empty-btn">Về giỏ hàng</Link>
        </div>
      </div>
    );
  }

  const place = async () => {
    setError('');
    setPlacing(true);
    try {
      const body: CheckoutRequest = {
        ...request,
        addressId: request.addressId ?? quote.address?.id ?? null,
        shops: quote.shops.map((s) => ({
          shopId: s.shopId,
          carrierCode: s.carrierCode,
          voucherCode: shopVouchers[s.shopId] ?? null,
          note: notes[s.shopId]?.trim() || null,
        })),
      };
      const result = await checkoutApi.place(idempotencyKey.current, body, quote.grandTotal, method === 'Wallet' ? walletPin : undefined);
      void queryClient.invalidateQueries({ queryKey: CART_KEY });
      if (result.payment?.redirectUrl) goTo(navigate, result.payment.redirectUrl);
      else navigate(`/dat-hang-thanh-cong?checkout=${result.checkoutId}`);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        // Prices / vouchers moved (or a wrong wallet PIN): show the new numbers and let the buyer confirm again
        setWalletPin('');
        await quoteQuery.refetch();
        idempotencyKey.current = newKey();
      }
      setError(e instanceof ApiError ? e.message : 'Không đặt được hàng, vui lòng thử lại.');
      window.scrollTo(0, 0);
    } finally {
      setPlacing(false);
    }
  };

  return (
    <div className="checkout-page">
      <div className="container">
        <h1 className="checkout-title">Thanh Toán</h1>

        {error && <div className="checkout-error" role="alert" data-testid="checkout-error">{error}</div>}
        {quote.problems.length > 0 && (
          <ul className="checkout-problems" data-testid="checkout-problems">
            {quote.problems.map((p) => <li key={p}>{p}</li>)}
          </ul>
        )}

        <div className="checkout-box checkout-address" data-testid="checkout-address">
          <h2 className="checkout-box-title">📍 Địa Chỉ Nhận Hàng</h2>
          {quote.address ? (
            <div className="checkout-address-current">
              <strong>{quote.address.receiverName} · {quote.address.phone}</strong>
              <span>{quote.address.fullAddress}</span>
            </div>
          ) : (
            <p>Bạn chưa có địa chỉ nhận hàng.</p>
          )}
          {(addresses.data?.length ?? 0) > 1 && (
            <select value={quote.address?.id ?? ''} onChange={(e) => setAddressId(e.target.value)} aria-label="Chọn địa chỉ" data-testid="address-select">
              {addresses.data!.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.receiverName} — {a.street}, {a.wardName}, {a.districtName}, {a.provinceName}
                </option>
              ))}
            </select>
          )}
          <Link to="/tai-khoan/dia-chi" className="checkout-address-manage">+ Thêm / sửa địa chỉ</Link>
        </div>

        {quote.shops.map((shop) => (
          <div key={shop.shopId} className="checkout-box" data-testid="checkout-shop">
            <div className="checkout-shop-name">
              {shop.isMall && <span className="checkout-mall">Mall</span>} {shop.shopName}
            </div>
            {shop.lines.map((it) => (
              <div key={it.skuId} className="checkout-product" data-testid="checkout-item">
                <div className="cp-col-product">
                  <img src={imageOrPlaceholder(it.imageUrl)} alt={it.name} onError={handleImgError} />
                  <div>
                    <div className="checkout-product-name">{it.name}</div>
                    {it.variant && <div className="checkout-product-variant">Phân loại: {it.variant}</div>}
                    {it.priceLabel && <span className="checkout-price-label">{it.priceLabel}</span>}
                  </div>
                </div>
                <span className="cp-col-price">{formatPrice(it.unitPrice)}</span>
                <span className="cp-col-qty">x{it.quantity}</span>
                <span className="cp-col-total checkout-product-total">{formatPrice(it.lineTotal)}</span>
              </div>
            ))}

            {(shop.gifts ?? []).map((g) => (
              <div key={g.skuId} className="checkout-product checkout-gift" data-testid="checkout-gift">
                <div className="cp-col-product"><div className="checkout-product-name">🎁 Quà tặng: {g.name}{g.variant && ` (${g.variant})`}</div></div>
                <span className="cp-col-price">{formatPrice(0)}</span>
                <span className="cp-col-qty">x{g.quantity}</span>
                <span className="cp-col-total">{formatPrice(0)}</span>
              </div>
            ))}
            {shop.comboDiscount > 0 && (
              <div className="checkout-combo" data-testid="checkout-combo">Ưu đãi combo của shop: −{formatPrice(shop.comboDiscount)}</div>
            )}

            <div className="checkout-shop-options">
              <label className="checkout-note">
                Lời nhắn:
                <input
                  value={notes[shop.shopId] ?? ''}
                  maxLength={200}
                  onChange={(e) => setNotes({ ...notes, [shop.shopId]: e.target.value })}
                  placeholder="Lưu ý cho người bán…"
                  aria-label={`Lời nhắn cho ${shop.shopName}`}
                />
              </label>
              <div className="checkout-carriers" role="radiogroup" aria-label="Đơn vị vận chuyển">
                <span className="checkout-voucher-title">Đơn vị vận chuyển:</span>
                {shop.shippingOptions.map((o) => (
                  <label key={o.code} className={`checkout-carrier ${shop.carrierCode === o.code ? 'active' : ''}`} data-testid="carrier-option">
                    <input type="radio" name={`carrier-${shop.shopId}`} checked={shop.carrierCode === o.code}
                      onChange={() => setCarriers({ ...carriers, [shop.shopId]: o.code })} />
                    <span>
                      <strong>{o.name}</strong> {formatPrice(o.fee)} · Nhận dự kiến {formatDate(`${o.expectedDate}T12:00:00+07:00`)}
                    </span>
                  </label>
                ))}
              </div>
              <VoucherPicker
                title="Voucher của shop"
                options={shop.shopVoucherOptions}
                value={shopVouchers[shop.shopId] ?? null}
                onChange={(code) => setShopVouchers({ ...shopVouchers, [shop.shopId]: code })}
                testId="shop-voucher"
              />
            </div>
            <div className="checkout-shop-total">
              Tổng số tiền ({shop.lines.reduce((s, l) => s + l.quantity, 0)} sản phẩm): <strong>{formatPrice(shop.total)}</strong>
            </div>
          </div>
        ))}

        <div className="checkout-box">
          <VoucherPicker title="ShopHub Voucher" options={quote.platformVouchers} value={platformCode} onChange={setPlatformCode} testId="platform-voucher" />
          <VoucherPicker title="Mã miễn phí vận chuyển" options={quote.freeshipVouchers} value={freeshipCode} onChange={setFreeshipCode} testId="freeship-voucher" />
          <label className="checkout-coins">
            <input type="checkbox" checked={useCoins} onChange={(e) => setUseCoins(e.target.checked)} disabled={quote.coins.balance === 0} data-testid="use-coins" />
            Dùng ShopHub Xu (có {formatCount(quote.coins.balance)} xu)
            {useCoins && quote.coins.used > 0 && <span> — trừ {formatPrice(quote.coins.used)}</span>}
          </label>
        </div>

        <div className="checkout-box">
          <h2 className="checkout-box-title">💳 Phương Thức Thanh Toán</h2>
          <div className="checkout-methods">
            {quote.paymentMethods.map((m) => (
              <label key={m.code} className={`checkout-method ${method === m.code ? 'active' : ''} ${m.available ? '' : 'disabled'}`} data-testid={`method-${m.code}`}>
                <input type="radio" name="payment" checked={method === m.code} disabled={!m.available} onChange={() => setMethod(m.code)} />
                {m.name}
                {!m.available && m.reason && <small> — {m.reason}</small>}
              </label>
            ))}
          </div>
        </div>

        <div className="checkout-box checkout-summary" data-testid="checkout-summary">
          <div className="checkout-summary-row"><span>Tổng tiền hàng</span><span>{formatPrice(quote.subtotal)}</span></div>
          <div className="checkout-summary-row"><span>Tổng tiền phí vận chuyển</span><span>{formatPrice(quote.shippingFee)}</span></div>
          {quote.shippingDiscount > 0 && <div className="checkout-summary-row"><span>Giảm giá phí vận chuyển</span><span>−{formatPrice(quote.shippingDiscount)}</span></div>}
          {quote.comboDiscount > 0 && <div className="checkout-summary-row"><span>Ưu đãi combo</span><span>−{formatPrice(quote.comboDiscount)}</span></div>}
          {quote.shopDiscount > 0 && <div className="checkout-summary-row"><span>Voucher của shop</span><span>−{formatPrice(quote.shopDiscount)}</span></div>}
          {quote.platformDiscount > 0 && <div className="checkout-summary-row"><span>ShopHub Voucher</span><span>−{formatPrice(quote.platformDiscount)}</span></div>}
          {quote.coinUsed > 0 && <div className="checkout-summary-row"><span>ShopHub Xu</span><span>−{formatPrice(quote.coinUsed)}</span></div>}
          {quote.coinCashback > 0 && <div className="checkout-summary-row"><span>Hoàn xu sau khi nhận hàng</span><span>+{formatCount(quote.coinCashback)} xu</span></div>}
          <div className="checkout-summary-row checkout-summary-total">
            <span>Tổng thanh toán</span>
            <span data-testid="checkout-total">{formatPrice(quote.grandTotal)}</span>
          </div>
          {method === 'Wallet' && (
            <label className="checkout-wallet-pin">
              Mật khẩu Ví ShopHub
              <input type="password" inputMode="numeric" autoComplete="off" maxLength={6} value={walletPin}
                onChange={(e) => setWalletPin(e.target.value.replace(/\D/g, ''))} data-testid="wallet-pin" />
            </label>
          )}
          <button className="checkout-place" onClick={place}
            disabled={!quote.canPlace || placing || quoteQuery.isFetching || (method === 'Wallet' && walletPin.length !== 6)} data-testid="place-order">
            {placing ? 'Đang đặt hàng…' : 'Đặt Hàng'}
          </button>
        </div>
      </div>
    </div>
  );
};

export default Checkout;
