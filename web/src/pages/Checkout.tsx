import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { checkoutApi, type CheckoutRequest, type PaymentMethod, type PaymentOption } from '../api/commerce';
import VoucherPicker from '../components/VoucherPicker';
import QueryState from '../components/QueryState';
import { useShopVouchers } from '../stores/shopVouchers';
import { accountApi, type Address } from '../api/account';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { CART_KEY } from '../context/CartContext';
import { formatCount, formatPrice } from '../lib/money';
import { formatDate } from '../lib/datetime';
import { goTo } from '../lib/navigation';
import { handleImgError, sizedImage } from '../lib/image';
import { AddressForm } from './account/AddressesPage';
import './account/Account.css';
import './Checkout.css';
import { Banknote, Check, Coins, CreditCard, Gift, MapPin, QrCode, Store, Truck, Wallet, X, type LucideIcon } from 'lucide-react';
import { Badge } from '../components/ui';
import '../components/VoucherPicker.css';

const METHOD_ICONS: Partial<Record<PaymentMethod, LucideIcon>> = { Cod: Banknote, Wallet, Simulated: QrCode, VnPay: QrCode, MoMo: Wallet, ZaloPay: Wallet };

const newKey = () => (typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`);

const Checkout = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { isLoggedIn, isChecking } = useAuth();
  const idempotencyKey = useRef(newKey());

  const [addressId, setAddressId] = useState<string | null>(null);
  // Thêm địa chỉ ngay tại trang (II.7): the new one is picked for this order
  const [adding, setAdding] = useState(false);
  const [picking, setPicking] = useState(false);
  const [carriers, setCarriers] = useState<Record<string, string>>({});
  // Shared with the cart (E6): a shop voucher picked there is the one used here, and the other way round
  const shopVouchers = useShopVouchers((st) => st.codes);
  const setShopVoucher = useShopVouchers((st) => st.set);
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [platformCode, setPlatformCode] = useState<string | null>(null);
  const [freeshipCode, setFreeshipCode] = useState<string | null>(null);
  const [useCoins, setUseCoins] = useState(false);
  const [method, setMethod] = useState<PaymentMethod>('Cod');
  const [option, setOption] = useState<PaymentOption>('Default');
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
      paymentOption: option,
    }),
    [addressId, carriers, shopVouchers, platformCode, freeshipCode, useCoins, method, option],
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
  // First quote still loading, or it failed: spinner or the error with "Thử lại" (F3)
  if (!quote) return <div className="container"><QueryState query={quoteQuery}>{() => null}</QueryState></div>;

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

  const payOptions = quote.paymentMethods.find((m) => m.code === method)?.options;
  const itemCount = quote.shops.reduce((n, s) => n + s.lines.reduce((m, l) => m + l.quantity, 0), 0);
  const savings = quote.shippingDiscount + quote.comboDiscount + quote.shopDiscount + quote.platformDiscount + quote.coinUsed;
  const placeDisabled = !quote.canPlace || placing || quoteQuery.isFetching || (method === 'Wallet' && walletPin.length !== 6);

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

        <div className="checkout-layout">
          <div className="checkout-main">
            {/* Envelope-striped address card (G2-B4); "Thay đổi" opens the address book */}
            <div className="checkout-box checkout-address" data-testid="checkout-address">
              <h2 className="checkout-box-title"><MapPin size={18} aria-hidden /> Địa Chỉ Nhận Hàng</h2>
              {quote.address ? (
                <div className="checkout-address-current">
                  <strong>{quote.address.receiverName} · {quote.address.phone}</strong>
                  <span>{quote.address.fullAddress}</span>
                  {(addresses.data?.length ?? 0) > 1 && (
                    <button type="button" className="checkout-address-change" onClick={() => setPicking(true)} data-testid="address-change">Thay đổi</button>
                  )}
                </div>
              ) : (
                <p>Bạn chưa có địa chỉ nhận hàng.</p>
              )}
              {adding ? (
                <div className="checkout-address-form" data-testid="checkout-address-form">
                  <AddressForm initial={null} onDone={(saved) => { setAdding(false); if (saved) setAddressId(saved.id); }} />
                </div>
              ) : (
                <span className="checkout-address-actions">
                  <button type="button" className="checkout-address-manage" onClick={() => setAdding(true)} data-testid="checkout-address-add">+ Thêm địa chỉ mới</button>
                  <Link to="/tai-khoan/dia-chi" className="checkout-address-manage">Sửa sổ địa chỉ</Link>
                </span>
              )}
            </div>
            {picking && addresses.data && (
              <AddressDialog addresses={addresses.data} current={quote.address?.id ?? null}
                onPick={(id) => { setAddressId(id); setPicking(false); }} onClose={() => setPicking(false)} />
            )}

            {quote.shops.map((shop) => (
              <div key={shop.shopId} className="checkout-box" data-testid="checkout-shop">
                <div className="checkout-shop-name">
                  {shop.isMall && <Badge tone="mall">Mall</Badge>} <Store size={16} aria-hidden /> {shop.shopName}
                </div>
                {shop.lines.map((it) => (
                  <div key={it.skuId} className="checkout-product" data-testid="checkout-item">
                    <div className="cp-col-product">
                      <img src={sizedImage(it.imageUrl, 200)} alt={it.name} onError={handleImgError} />
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
                    <div className="cp-col-product"><div className="checkout-product-name"><Gift size={14} aria-hidden /> Quà tặng: {g.name}{g.variant && ` (${g.variant})`}</div></div>
                    <span className="cp-col-price">{formatPrice(0)}</span>
                    <span className="cp-col-qty">x{g.quantity}</span>
                    <span className="cp-col-total">{formatPrice(0)}</span>
                  </div>
                ))}
                {shop.comboDiscount > 0 && (
                  <div className="checkout-combo" data-testid="checkout-combo">Ưu đãi combo của shop: −{formatPrice(shop.comboDiscount)}</div>
                )}
                {(shop.parcels?.length ?? 0) > 1 && (
                  <div className="checkout-parcels" data-testid="checkout-parcels">
                    Đơn của shop được gửi thành {shop.parcels!.length} kiện từ các kho khác nhau:
                    <ul>
                      {shop.parcels!.map((p) => (
                        <li key={p.no}>
                          Kiện {p.no} — {p.warehouseName}: {shop.lines.filter((l) => p.productIds.includes(l.productId)).map((l) => l.name).join(', ')}
                          {' '}· phí vận chuyển {formatPrice(p.shippingFee)}
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                <div className="checkout-shop-options">
                  <VoucherPicker
                    title="Voucher của shop"
                    options={shop.shopVoucherOptions}
                    value={shopVouchers[shop.shopId] ?? null}
                    onChange={(code) => setShopVoucher(shop.shopId, code)}
                    testId="shop-voucher"
                  />
                  <label className="checkout-note">
                    <span>Lời nhắn:</span>
                    <input
                      value={notes[shop.shopId] ?? ''}
                      maxLength={200}
                      onChange={(e) => setNotes({ ...notes, [shop.shopId]: e.target.value })}
                      placeholder="Lưu ý cho người bán…"
                      aria-label={`Lời nhắn cho ${shop.shopName}`}
                    />
                  </label>
                  <div className="checkout-carriers" role="radiogroup" aria-label={`Đơn vị vận chuyển của ${shop.shopName}`}>
                    <span className="checkout-carriers-title"><Truck size={16} aria-hidden /> Đơn vị vận chuyển</span>
                    {shop.shippingOptions.map((o) => (
                      <label key={o.code} className={`checkout-carrier ${shop.carrierCode === o.code ? 'active' : ''}`} data-testid="carrier-option">
                        <input type="radio" name={`carrier-${shop.shopId}`} checked={shop.carrierCode === o.code}
                          onChange={() => setCarriers({ ...carriers, [shop.shopId]: o.code })} />
                        <span className="checkout-carrier-name"><strong>{o.name}</strong> Nhận dự kiến {formatDate(`${o.expectedDate}T12:00:00+07:00`)}</span>
                        <span className="checkout-carrier-fee">{formatPrice(o.fee)}</span>
                      </label>
                    ))}
                  </div>
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
                <span><Coins size={18} aria-hidden /> ShopHub Xu <small>(có {formatCount(quote.coins.balance)} xu)</small></span>
                <span>
                  {useCoins && quote.coins.used > 0 && <span className="checkout-coins-used">−{formatPrice(quote.coins.used)}</span>}
                  <input type="checkbox" role="switch" checked={useCoins} onChange={(e) => setUseCoins(e.target.checked)} disabled={quote.coins.balance === 0}
                    aria-label="Dùng ShopHub Xu" data-testid="use-coins" />
                </span>
              </label>
            </div>

            <div className="checkout-box">
              <h2 className="checkout-box-title"><CreditCard size={18} aria-hidden /> Phương Thức Thanh Toán</h2>
              <div className="checkout-methods" role="radiogroup" aria-label="Phương thức thanh toán">
                {quote.paymentMethods.map((m) => {
                  const Icon = METHOD_ICONS[m.code] ?? CreditCard;
                  return (
                    <label key={m.code} className={`checkout-method ${method === m.code ? 'active' : ''} ${m.available ? '' : 'disabled'}`} data-testid={`method-${m.code}`}>
                      <input type="radio" name="payment" checked={method === m.code} disabled={!m.available} onChange={() => { setMethod(m.code); setOption('Default'); }} />
                      <Icon size={22} aria-hidden className="checkout-method-icon" />
                      <span className="checkout-method-text">
                        {m.name}
                        {!m.available && m.reason && <small>{m.reason}</small>}
                      </span>
                      {method === m.code && <span className="checkout-method-tick" aria-hidden><Check size={10} strokeWidth={3} /></span>}
                    </label>
                  );
                })}
              </div>
              {payOptions && payOptions.length >= 2 && (
                <div className="checkout-options" data-testid="payment-options">
                  <span className="checkout-options-title">Hình thức tại cổng:</span>
                  {payOptions.map((o) => (
                    <label key={o.code} className={`checkout-method ${option === o.code ? 'active' : ''} ${o.available ? '' : 'disabled'}`} data-testid={`option-${o.code}`}>
                      <input type="radio" name="payment-option" checked={option === o.code} disabled={!o.available} onChange={() => setOption(o.code)} />
                      <span className="checkout-method-text">
                        {o.name}
                        {!o.available && o.reason && <small>{o.reason}</small>}
                      </span>
                    </label>
                  ))}
                  {(option === 'Installment' || option === 'PayLater') && (
                    <small className="checkout-options-note">Kỳ hạn, lãi suất và việc duyệt khoản do cổng thanh toán và đối tác tài chính quyết định; ShopHub không cấp tín dụng.</small>
                  )}
                </div>
              )}
            </div>
          </div>

          {/* Summary on the right on wide screens; on phones it follows the page and "Đặt hàng" sticks to the bottom */}
          <aside className="checkout-box checkout-summary" data-testid="checkout-summary" aria-label="Tóm tắt đơn hàng">
            <h2 className="checkout-box-title">Chi Tiết Thanh Toán</h2>
            <div className="checkout-summary-row"><span>Tổng tiền hàng ({itemCount} sản phẩm)</span><span>{formatPrice(quote.subtotal)}</span></div>
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
            {savings > 0 && <div className="checkout-summary-saving">Tiết kiệm {formatPrice(savings)}</div>}
            {method === 'Wallet' && (
              <label className="checkout-wallet-pin">
                Mật khẩu Ví ShopHub
                <input type="password" inputMode="numeric" autoComplete="off" maxLength={6} value={walletPin}
                  onChange={(e) => setWalletPin(e.target.value.replace(/\D/g, ''))} data-testid="wallet-pin" />
              </label>
            )}
            <p className="checkout-terms">Nhấn "Đặt hàng" đồng nghĩa với việc bạn đồng ý tuân theo <Link to="/trang/dieu-khoan-su-dung">Điều khoản ShopHub</Link>.</p>
            <div className="checkout-place-bar">
              <span className="checkout-place-bar-total">Tổng: <strong>{formatPrice(quote.grandTotal)}</strong></span>
              <button type="button" className="checkout-place" onClick={place} disabled={placeDisabled} data-testid="place-order">
                {placing ? 'Đang đặt hàng…' : 'Đặt Hàng'}
              </button>
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
};

/** The address book as a dialog (G2-B4): one radio card per saved address, the default one marked. */
const AddressDialog = ({ addresses, current, onPick, onClose }: { addresses: Address[]; current: string | null; onPick: (id: string) => void; onClose: () => void }) => {
  const [draft, setDraft] = useState(current);
  const panel = useRef<HTMLDivElement>(null);
  useEffect(() => {
    panel.current?.querySelector<HTMLInputElement>('input:checked, input')?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div className="voucher-dialog" role="presentation">
      <button type="button" className="voucher-dialog-backdrop" aria-label="Đóng" tabIndex={-1} onClick={onClose} />
      <div className="voucher-dialog-panel" role="dialog" aria-modal="true" aria-labelledby="address-dialog-title" ref={panel} data-testid="address-dialog">
        <div className="voucher-dialog-head">
          <h2 id="address-dialog-title">Địa Chỉ Của Tôi</h2>
          <button type="button" className="voucher-dialog-close" onClick={onClose} aria-label="Đóng"><X size={20} aria-hidden /></button>
        </div>
        <div className="voucher-dialog-list" role="radiogroup" aria-label="Địa chỉ nhận hàng">
          {addresses.map((a) => (
            <label key={a.id} className={`checkout-address-option ${draft === a.id ? 'is-checked' : ''}`} data-testid="address-option">
              <input type="radio" name="checkout-address" checked={draft === a.id} onChange={() => setDraft(a.id)} />
              <span>
                <strong>{a.receiverName}</strong> | {a.phone}
                <span className="checkout-address-option-line">{[a.street, a.wardName, a.districtName, a.provinceName].filter(Boolean).join(', ')}</span>
                {a.isDefault && <Badge tone="outline">Mặc định</Badge>}
              </span>
            </label>
          ))}
        </div>
        <div className="voucher-dialog-foot">
          <button type="button" className="voucher-dialog-none" data-confirm="dialog" onClick={onClose}>Huỷ</button>
          <button type="button" className="voucher-dialog-ok" onClick={() => draft && onPick(draft)} disabled={!draft} data-testid="address-confirm">Xác nhận</button>
        </div>
      </div>
    </div>
  );
};

export default Checkout;
