import { useState, useRef, useEffect, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { useWishlist } from '../context/WishlistContext';
import { products, formatPrice, removeTones, handleImgError } from '../data/products';
import './Header.css';

const TOP_LINKS = ['Kênh Người Bán', 'Trở thành Người bán', 'Tải ứng dụng', 'Kết nối'];
const HOT_KEYWORDS = ['Áo thun', 'Điện thoại', 'Tai nghe', 'Giày sneaker', 'Nồi chiên không dầu'];

const Header = () => {
  const navigate = useNavigate();
  const { items, totalItems, totalPrice } = useCart();
  const { user, isLoggedIn, logout } = useAuth();
  const { count: wishCount } = useWishlist();

  const [keyword, setKeyword] = useState('');
  const [showSuggest, setShowSuggest] = useState(false);
  const [showUserMenu, setShowUserMenu] = useState(false);
  const searchRef = useRef<HTMLDivElement>(null);
  const userRef = useRef<HTMLDivElement>(null);

  // Đóng dropdown khi click ra ngoài
  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      const target = e.target as Node;
      if (searchRef.current && !searchRef.current.contains(target)) setShowSuggest(false);
      if (userRef.current && !userRef.current.contains(target)) setShowUserMenu(false);
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, []);

  const goSearch = (q: string) => {
    navigate(q && q.trim() ? `/tim-kiem?q=${encodeURIComponent(q.trim())}` : '/tim-kiem');
    setShowSuggest(false);
  };

  const handleSearch = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    goSearch(keyword);
  };

  // Gợi ý sản phẩm khớp từ khóa — so khớp không dấu như trang tìm kiếm
  const needle = removeTones(keyword.trim().toLowerCase());
  const suggestions =
    needle.length >= 1
      ? products
          .filter((p) => removeTones(p.name.toLowerCase()).includes(needle))
          .slice(0, 6)
      : [];

  return (
    <header className="header">
      {/* Thanh trên cùng */}
      <div className="header-top">
        <div className="container header-top-inner">
          <nav className="header-top-links">
            {TOP_LINKS.map((label) => (
              <span key={label} className="header-top-link">{label}</span>
            ))}
          </nav>
          <nav className="header-top-links">
            <Link to="/thong-bao" className="header-top-link">🔔 Thông Báo</Link>
            <span className="header-top-link">❓ Hỗ Trợ</span>
            {isLoggedIn && user ? (
              <div className="header-user" ref={userRef}>
                <button
                  className="header-user-btn"
                  onClick={() => setShowUserMenu((v) => !v)}
                  data-testid="user-menu"
                >
                  <span className="header-user-avatar">{user.fullName.charAt(0).toUpperCase()}</span>
                  {user.fullName}
                </button>
                {showUserMenu && (
                  <div className="header-user-dropdown">
                    <Link to="/tai-khoan/ho-so" onClick={() => setShowUserMenu(false)}>Tài Khoản Của Tôi</Link>
                    <Link to="/yeu-thich" onClick={() => setShowUserMenu(false)}>Sản Phẩm Yêu Thích</Link>
                    <Link to="/gio-hang" onClick={() => setShowUserMenu(false)}>Giỏ Hàng</Link>
                    <button
                      onClick={async () => {
                        setShowUserMenu(false);
                        await logout();
                        navigate('/');
                      }}
                      data-testid="logout"
                    >
                      Đăng Xuất
                    </button>
                  </div>
                )}
              </div>
            ) : (
              <>
                <Link to="/dang-ky" className="header-top-link">Đăng Ký</Link>
                <span className="header-top-sep">|</span>
                <Link to="/dang-nhap" className="header-top-link" data-testid="login-link">Đăng Nhập</Link>
              </>
            )}
          </nav>
        </div>
      </div>

      {/* Thanh chính */}
      <div className="container header-main">
        <Link to="/" className="header-logo">
          <svg width="42" height="42" viewBox="0 0 32 32" aria-hidden="true">
            <path d="M10 11h12l-1 12a2 2 0 0 1-2 1.8H13a2 2 0 0 1-2-1.8L10 11z"
              fill="none" stroke="currentColor" strokeWidth="1.8" />
            <path d="M12.5 11a3.5 3.5 0 0 1 7 0" fill="none" stroke="currentColor" strokeWidth="1.8" />
          </svg>
          <span className="header-logo-text">ShopHub</span>
        </Link>

        <div className="header-search-block" ref={searchRef}>
          <form className="header-search" onSubmit={handleSearch}>
            <input
              type="text"
              className="header-search-input"
              placeholder="Sinh Nhật ShopHub - Sale To Toàn Sàn"
              value={keyword}
              onChange={(e) => {
                setKeyword(e.target.value);
                setShowSuggest(true);
              }}
              onFocus={() => setShowSuggest(true)}
              aria-label="Tìm kiếm sản phẩm"
            />
            <button type="submit" className="header-search-btn" aria-label="Tìm kiếm">
              <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <circle cx="11" cy="11" r="8" />
                <path d="m21 21-4.35-4.35" />
              </svg>
            </button>
          </form>

          {/* Dropdown gợi ý tìm kiếm */}
          {showSuggest && (
            <div className="header-suggest" data-testid="search-suggest">
              {suggestions.length > 0 ? (
                suggestions.map((p) => (
                  <button
                    key={p.id}
                    className="header-suggest-item"
                    onClick={() => {
                      setShowSuggest(false);
                      navigate(`/san-pham/${p.id}`);
                    }}
                  >
                    <svg className="header-suggest-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <circle cx="11" cy="11" r="8" /><path d="m21 21-4.35-4.35" />
                    </svg>
                    <span>{p.name}</span>
                  </button>
                ))
              ) : (
                <div className="header-suggest-trending">
                  <div className="header-suggest-title">Tìm kiếm phổ biến</div>
                  {HOT_KEYWORDS.map((k) => (
                    <button key={k} className="header-suggest-item" onClick={() => goSearch(k)}>
                      🔥 <span>{k}</span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}

          <div className="header-hot-keywords">
            {HOT_KEYWORDS.map((k) => (
              <Link key={k} to={`/tim-kiem?q=${encodeURIComponent(k)}`} className="header-hot-keyword">
                {k}
              </Link>
            ))}
          </div>
        </div>

        <div className="header-actions">
          {/* Yêu thích */}
          <Link to="/yeu-thich" className="header-icon-link" aria-label="Yêu thích" data-testid="wishlist-link">
            <span className="header-heart">♡</span>
            {wishCount > 0 && <span className="header-cart-badge">{wishCount}</span>}
          </Link>

          {/* Giỏ hàng + xem nhanh khi hover */}
          <div className="header-cart-wrapper">
            <Link to="/gio-hang" className="header-cart" aria-label="Giỏ hàng">
              <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6">
                <circle cx="9" cy="21" r="1" />
                <circle cx="20" cy="21" r="1" />
                <path d="M1 1h4l2.68 13.39a2 2 0 0 0 2 1.61h9.72a2 2 0 0 0 2-1.61L23 6H6" />
              </svg>
              {totalItems > 0 && <span className="header-cart-badge">{totalItems}</span>}
            </Link>

            <div className="header-cart-preview" data-testid="cart-preview">
              {items.length === 0 ? (
                <div className="header-cart-preview-empty">
                  <div className="header-cart-preview-empty-icon">🛒</div>
                  Chưa có sản phẩm
                </div>
              ) : (
                <>
                  <div className="header-cart-preview-title">Sản Phẩm Mới Thêm</div>
                  <div className="header-cart-preview-list">
                    {items.slice(0, 5).map((it) => (
                      <Link key={it.cartKey} to={`/san-pham/${it.id}`} className="header-cart-preview-item">
                        <img src={it.image} alt={it.name} onError={(e) => handleImgError(e, it.fallbackImage)} />
                        <span className="header-cart-preview-name">{it.name}</span>
                        <span className="header-cart-preview-price">{formatPrice(it.price)}</span>
                      </Link>
                    ))}
                  </div>
                  <div className="header-cart-preview-footer">
                    <span>{totalItems} sản phẩm · {formatPrice(totalPrice)}</span>
                    <Link to="/gio-hang" className="header-cart-preview-btn">Xem Giỏ Hàng</Link>
                  </div>
                </>
              )}
            </div>
          </div>
        </div>
      </div>
    </header>
  );
};

export default Header;
