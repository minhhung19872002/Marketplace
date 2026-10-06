import { useState } from 'react';
import { useLocation, useNavigate, Link } from 'react-router-dom';
import { useCart } from '../context/CartContext';
import { formatPrice } from '../lib/money';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import './Checkout.css';

const SHIPPING_OPTIONS = [
  { id: 'fast', label: 'Nhanh', desc: 'Nhận vào 2-3 ngày', fee: 30000 },
  { id: 'economy', label: 'Tiết Kiệm', desc: 'Nhận vào 4-6 ngày', fee: 16000 },
  { id: 'express', label: 'Hỏa Tốc', desc: 'Nhận trong hôm nay', fee: 60000 },
];

const PAYMENT_METHODS = [
  { id: 'cod', label: 'Thanh toán khi nhận hàng (COD)' },
  { id: 'bank', label: 'Chuyển khoản ngân hàng' },
  { id: 'wallet', label: 'Ví ShopHub' },
];

// Voucher demo: mã -> số tiền giảm
const VOUCHERS: Record<string, number> = { SHOPHUB50: 50000, FREESHIP: 16000, SALE12: 30000 };

const Checkout = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const { items, removeMany } = useCart();

  const keys: string[] | undefined = (location.state as { keys?: string[] } | null)?.keys;
  const orderItems = keys && keys.length ? items.filter((it) => keys.includes(it.cartKey)) : items;

  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [address, setAddress] = useState('');
  const [shipping, setShipping] = useState('fast');
  const [payment, setPayment] = useState('cod');
  const [voucherCode, setVoucherCode] = useState('');
  const [voucherApplied, setVoucherApplied] = useState(0);
  const [voucherMsg, setVoucherMsg] = useState('');
  const [error, setError] = useState('');

  if (orderItems.length === 0) {
    return (
      <div className="checkout-page">
        <div className="container checkout-empty">
          <p>Không có sản phẩm để thanh toán.</p>
          <Link to="/" className="checkout-empty-btn">Về trang chủ</Link>
        </div>
      </div>
    );
  }

  const subtotal = orderItems.reduce((sum, it) => sum + it.price * it.quantity, 0);
  const shipFee = (SHIPPING_OPTIONS.find((s) => s.id === shipping) ?? SHIPPING_OPTIONS[0]).fee;
  const total = Math.max(0, subtotal + shipFee - voucherApplied);

  const applyVoucher = () => {
    const code = voucherCode.trim().toUpperCase();
    if (VOUCHERS[code]) {
      setVoucherApplied(VOUCHERS[code]);
      setVoucherMsg(`Áp dụng mã ${code}: -${formatPrice(VOUCHERS[code])}`);
    } else {
      setVoucherApplied(0);
      setVoucherMsg('Mã giảm giá không hợp lệ');
    }
  };

  const placeOrder = () => {
    if (!name.trim() || !phone.trim() || !address.trim()) {
      setError('Vui lòng nhập đầy đủ thông tin nhận hàng');
      window.scrollTo(0, 0);
      return;
    }
    const count = orderItems.reduce((s, it) => s + it.quantity, 0);
    // Lưu đơn vừa đặt để trang thành công vẫn hiển thị khi reload
    try {
      sessionStorage.setItem('shophub_last_order', JSON.stringify({ total, count }));
    } catch {
      // storage bị chặn -> chỉ dùng location.state
    }
    removeMany(orderItems.map((it) => it.cartKey));
    navigate('/dat-hang-thanh-cong', { state: { total, count } });
  };

  return (
    <div className="checkout-page">
      <div className="container">
        <h1 className="checkout-title">Thanh Toán</h1>

        {error && <div className="checkout-error" data-testid="checkout-error">{error}</div>}

        {/* Địa chỉ nhận hàng */}
        <div className="checkout-box checkout-address">
          <h2 className="checkout-box-title">📍 Địa Chỉ Nhận Hàng</h2>
          <div className="checkout-address-form">
            <input
              className="checkout-input"
              placeholder="Họ và tên"
              value={name}
              onChange={(e) => setName(e.target.value)}
              aria-label="Họ và tên"
            />
            <input
              className="checkout-input"
              placeholder="Số điện thoại"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              aria-label="Số điện thoại"
            />
            <input
              className="checkout-input checkout-input-full"
              placeholder="Địa chỉ nhận hàng (số nhà, đường, phường/xã, quận/huyện, tỉnh/thành)"
              value={address}
              onChange={(e) => setAddress(e.target.value)}
              aria-label="Địa chỉ"
            />
          </div>
        </div>

        {/* Sản phẩm */}
        <div className="checkout-box">
          <div className="checkout-products-head">
            <span className="cp-col-product">Sản phẩm</span>
            <span className="cp-col-price">Đơn giá</span>
            <span className="cp-col-qty">Số lượng</span>
            <span className="cp-col-total">Thành tiền</span>
          </div>
          {orderItems.map((it) => (
            <div key={it.cartKey} className="checkout-product" data-testid="checkout-item">
              <div className="cp-col-product checkout-product-info">
                <img src={imageOrPlaceholder(it.image)} alt={it.name} onError={handleImgError} />
                <div>
                  <div className="checkout-product-name">{it.name}</div>
                  {it.selectedVariant && (
                    <div className="checkout-product-variant">Phân loại: {it.selectedVariant}</div>
                  )}
                </div>
              </div>
              <span className="cp-col-price">{formatPrice(it.price)}</span>
              <span className="cp-col-qty">x{it.quantity}</span>
              <span className="cp-col-total checkout-product-total">{formatPrice(it.price * it.quantity)}</span>
            </div>
          ))}
        </div>

        {/* Vận chuyển */}
        <div className="checkout-box">
          <h2 className="checkout-box-title">🚚 Phương Thức Vận Chuyển</h2>
          <div className="checkout-options">
            {SHIPPING_OPTIONS.map((s) => (
              <label key={s.id} className={`checkout-option ${shipping === s.id ? 'active' : ''}`}>
                <input
                  type="radio"
                  name="shipping"
                  checked={shipping === s.id}
                  onChange={() => setShipping(s.id)}
                />
                <span className="checkout-option-label">{s.label}</span>
                <span className="checkout-option-desc">{s.desc}</span>
                <span className="checkout-option-fee">{formatPrice(s.fee)}</span>
              </label>
            ))}
          </div>
        </div>

        {/* Voucher */}
        <div className="checkout-box checkout-voucher">
          <h2 className="checkout-box-title">🎟️ ShopHub Voucher</h2>
          <div className="checkout-voucher-row">
            <input
              className="checkout-input"
              placeholder="Nhập mã: SHOPHUB50, FREESHIP, SALE12"
              value={voucherCode}
              onChange={(e) => setVoucherCode(e.target.value)}
              aria-label="Mã giảm giá"
            />
            <button className="checkout-voucher-btn" onClick={applyVoucher} data-testid="apply-voucher">
              Áp Dụng
            </button>
          </div>
          {voucherMsg && (
            <div className={`checkout-voucher-msg ${voucherApplied ? 'ok' : 'err'}`} data-testid="voucher-msg">
              {voucherMsg}
            </div>
          )}
        </div>

        {/* Thanh toán */}
        <div className="checkout-box">
          <h2 className="checkout-box-title">💳 Phương Thức Thanh Toán</h2>
          <div className="checkout-payments">
            {PAYMENT_METHODS.map((p) => (
              <label key={p.id} className={`checkout-payment ${payment === p.id ? 'active' : ''}`}>
                <input
                  type="radio"
                  name="payment"
                  checked={payment === p.id}
                  onChange={() => setPayment(p.id)}
                />
                {p.label}
              </label>
            ))}
          </div>
        </div>

        {/* Tổng kết */}
        <div className="checkout-box checkout-summary">
          <div className="checkout-summary-rows">
            <div className="checkout-summary-row">
              <span>Tổng tiền hàng</span>
              <span>{formatPrice(subtotal)}</span>
            </div>
            <div className="checkout-summary-row">
              <span>Phí vận chuyển</span>
              <span>{formatPrice(shipFee)}</span>
            </div>
            {voucherApplied > 0 && (
              <div className="checkout-summary-row">
                <span>Giảm giá voucher</span>
                <span>-{formatPrice(voucherApplied)}</span>
              </div>
            )}
            <div className="checkout-summary-row checkout-summary-total">
              <span>Tổng thanh toán</span>
              <span data-testid="checkout-total">{formatPrice(total)}</span>
            </div>
          </div>
          <button className="checkout-place-order" onClick={placeOrder} data-testid="place-order">
            Đặt Hàng
          </button>
        </div>
      </div>
    </div>
  );
};

export default Checkout;
