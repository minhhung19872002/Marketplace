import { useState, useEffect } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useCart } from '../context/CartContext';
import { formatPrice, handleImgError } from '../data/products';
import './CartPage.css';

const CartPage = () => {
  const navigate = useNavigate();
  const { items, updateQuantity, removeFromCart, removeMany } = useCart();

  // chọn item bằng checkbox, chỉ tính tiền item đã chọn
  const [selected, setSelected] = useState<Set<string>>(() => new Set(items.map((it) => it.cartKey)));

  // Giữ selection hợp lệ khi giỏ thay đổi (xóa item khỏi selection nếu không còn)
  useEffect(() => {
    setSelected((prev) => {
      const keys = new Set(items.map((it) => it.cartKey));
      const next = new Set([...prev].filter((k) => keys.has(k)));
      return next;
    });
  }, [items]);

  if (items.length === 0) {
    return (
      <div className="cart-page">
        <div className="container">
          <div className="cart-empty" data-testid="cart-empty">
            <div className="cart-empty-icon">🛒</div>
            <p>Giỏ hàng của bạn còn trống</p>
            <Link to="/" className="cart-empty-btn">Mua Sắm Ngay</Link>
          </div>
        </div>
      </div>
    );
  }

  const allSelected = items.length > 0 && selected.size === items.length;

  const toggleOne = (key: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const toggleAll = () => {
    setSelected(allSelected ? new Set() : new Set(items.map((it) => it.cartKey)));
  };

  const selectedItems = items.filter((it) => selected.has(it.cartKey));
  const selectedCount = selectedItems.reduce((sum, it) => sum + it.quantity, 0);
  const selectedTotal = selectedItems.reduce((sum, it) => sum + it.price * it.quantity, 0);

  const handleCheckout = () => {
    if (selectedItems.length === 0) return;
    // Sang trang thanh toán với các item đã chọn
    navigate('/thanh-toan', { state: { keys: selectedItems.map((it) => it.cartKey) } });
  };

  const handleDeleteSelected = () => {
    removeMany(selectedItems.map((it) => it.cartKey));
  };

  return (
    <div className="cart-page">
      <div className="container">
        <h1 className="cart-title">Giỏ Hàng</h1>

        <div className="cart-header-row">
          <span className="cart-col-check">
            <input
              type="checkbox"
              checked={allSelected}
              onChange={toggleAll}
              aria-label="Chọn tất cả"
              data-testid="select-all"
            />
          </span>
          <span className="cart-col-product">Sản Phẩm</span>
          <span className="cart-col-price">Đơn Giá</span>
          <span className="cart-col-qty">Số Lượng</span>
          <span className="cart-col-total">Số Tiền</span>
          <span className="cart-col-action">Thao Tác</span>
        </div>

        <div className="cart-items">
          {items.map((item) => (
            <div key={item.cartKey} className="cart-item" data-testid="cart-item">
              <span className="cart-col-check">
                <input
                  type="checkbox"
                  checked={selected.has(item.cartKey)}
                  onChange={() => toggleOne(item.cartKey)}
                  aria-label={`Chọn ${item.name}`}
                  data-testid="select-item"
                />
              </span>
              <div className="cart-col-product cart-item-product">
                <img
                  src={item.image}
                  alt={item.name}
                  className="cart-item-img"
                  onError={(e) => handleImgError(e, item.fallbackImage)}
                />
                <div className="cart-item-textblock">
                  <Link to={`/san-pham/${item.id}`} className="cart-item-name">{item.name}</Link>
                  {item.selectedVariant && (
                    <span className="cart-item-variant">Phân loại: {item.selectedVariant}</span>
                  )}
                </div>
              </div>
              <span className="cart-col-price cart-item-price">{formatPrice(item.price)}</span>
              <div className="cart-col-qty cart-item-qty">
                <button onClick={() => updateQuantity(item.cartKey, item.quantity - 1)} aria-label="Giảm">−</button>
                <input
                  type="number"
                  min="1"
                  max={item.stock}
                  value={item.quantity}
                  onChange={(e) => updateQuantity(item.cartKey, Math.max(1, Number(e.target.value) || 1))}
                  aria-label="Số lượng"
                />
                <button
                  onClick={() => updateQuantity(item.cartKey, item.quantity + 1)}
                  disabled={item.stock != null && item.quantity >= item.stock}
                  aria-label="Tăng"
                >
                  +
                </button>
              </div>
              <span className="cart-col-total cart-item-total">{formatPrice(item.price * item.quantity)}</span>
              <div className="cart-col-action">
                <button className="cart-item-remove" onClick={() => removeFromCart(item.cartKey)}>Xóa</button>
              </div>
            </div>
          ))}
        </div>

        <div className="cart-footer">
          <div className="cart-footer-left">
            <input
              type="checkbox"
              checked={allSelected}
              onChange={toggleAll}
              aria-label="Chọn tất cả"
            />
            <button className="cart-select-all-btn" onClick={toggleAll}>
              Chọn Tất Cả ({items.length})
            </button>
            <button className="cart-clear" onClick={handleDeleteSelected}>Xóa</button>
          </div>
          <div className="cart-summary">
            <span className="cart-summary-label">
              Tổng thanh toán ({selectedCount} sản phẩm):
            </span>
            <span className="cart-summary-total" data-testid="cart-total">{formatPrice(selectedTotal)}</span>
            <button
              className="cart-checkout"
              onClick={handleCheckout}
              disabled={selectedItems.length === 0}
              data-testid="checkout"
            >
              Mua Hàng
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default CartPage;
