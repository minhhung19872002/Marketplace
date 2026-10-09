import { useState, useRef, useEffect, type FormEvent, type KeyboardEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { useQuery } from '@tanstack/react-query';
import { useWishlist } from '../context/WishlistContext';
import { storefrontApi } from '../api/storefront';
import { useUnreadNotifications } from '../context/NotificationsContext';
import { formatPrice } from '../lib/money';
import { handleImgError, sizedImage } from '../lib/image';
import { UserAvatar } from './AvatarEditor';
import { cartBadge, recentLines } from '../lib/cart';
import { badgeCount, highlightParts } from '../lib/text';
import { clearSearchHistory, pushSearchHistory, readSearchHistory } from '../lib/searchHistory';
import './Header.css';
import { Bell, CircleHelp, History, Search, Store, TrendingUp, UserRound, Heart, ShoppingCart } from 'lucide-react';

// The seller centre is its own app under /seller (full page load, not a client route)
const SELLER_URL = '/seller/';

/** The value once it has stopped changing for `ms` (avoids one request per keystroke). */
function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = window.setTimeout(() => setDebounced(value), ms);
    return () => window.clearTimeout(t);
  }, [value, ms]);
  return debounced;
}

/** The text with the typed part (accent-insensitive) in bold. */
const Highlight = ({ text, query }: { text: string; query: string }) => (
  <>{highlightParts(text, query).map((part, i) => (part.match ? <b key={i}>{part.text}</b> : part.text))}</>
);

const Header = () => {
  const navigate = useNavigate();
  const { cart } = useCart();
  const items = recentLines(cart, 5);
  const badge = cartBadge(cart);
  const totalPrice = cart.selectedSubtotal;
  const { user, isLoggedIn, logout } = useAuth();
  const { count: wishCount } = useWishlist();
  const { data: unread } = useUnreadNotifications(isLoggedIn);

  const location = useLocation();
  const [keyword, setKeyword] = useState('');
  // On the results page the box shows the keyword being searched (P0-5), not the placeholder
  useEffect(() => {
    if (location.pathname === '/tim-kiem') setKeyword(new URLSearchParams(location.search).get('q') ?? '');
  }, [location.pathname, location.search]);

  // Past the first screen the header collapses (no top bar, no hot keywords) so it covers less of the page
  const [compact, setCompact] = useState(false);
  useEffect(() => {
    const onScroll = () => setCompact((was) => (was ? window.scrollY > 40 : window.scrollY > 120));
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);
  const [showSuggest, setShowSuggest] = useState(false);
  const [showUserMenu, setShowUserMenu] = useState(false);
  const searchRef = useRef<HTMLDivElement>(null);
  // Sticky bars below the header (e.g. "Gợi ý hôm nay") sit right under it through --sh-header-h
  const headerRef = useRef<HTMLElement>(null);
  useEffect(() => {
    const el = headerRef.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const publish = () => document.documentElement.style.setProperty('--sh-header-h', `${el.offsetHeight}px`);
    publish();
    const observer = new ResizeObserver(publish);
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
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

  const [history, setHistory] = useState<string[]>(readSearchHistory);
  const suggestRef = useRef<HTMLDivElement>(null);
  const goSearch = (q: string) => {
    if (q && q.trim()) setHistory(pushSearchHistory(q));
    navigate(q && q.trim() ? `/tim-kiem?q=${encodeURIComponent(q.trim())}` : '/tim-kiem');
    setShowSuggest(false);
  };

  /** ↑ / ↓ move through the suggestions, Esc closes them (the input hands over with ↓). */
  const onSuggestKey = (e: KeyboardEvent) => {
    const items = Array.from(suggestRef.current?.querySelectorAll<HTMLButtonElement>('.header-suggest-item') ?? []);
    const at = items.indexOf(document.activeElement as HTMLButtonElement);
    if (e.key === 'ArrowDown') { e.preventDefault(); items[Math.min(items.length - 1, at + 1)]?.focus(); }
    if (e.key === 'ArrowUp') { e.preventDefault(); if (at <= 0) searchRef.current?.querySelector('input')?.focus(); else items[at - 1]?.focus(); }
    if (e.key === 'Escape') { setShowSuggest(false); searchRef.current?.querySelector('input')?.focus(); }
  };

  const handleSearch = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    goSearch(keyword);
  };

  // Suggestions (accent-insensitive on the server) and the week's most searched keywords
  const needle = useDebounced(keyword.trim(), 250);
  const { data: suggestion } = useQuery({
    queryKey: ['suggest', needle],
    queryFn: () => storefrontApi.suggest(needle),
    enabled: needle.length >= 1,
    staleTime: 60_000,
  });
  const { data: hotKeywords = [] } = useQuery({ queryKey: ['hot-keywords'], queryFn: storefrontApi.hotKeywords, staleTime: 10 * 60_000 });
  const typed = keyword.trim().length > 0;
  const suggestedKeywords = typed ? suggestion?.keywords ?? [] : [];
  const suggestedProducts = typed ? suggestion?.products ?? [] : [];
  const suggestedShops = typed ? suggestion?.shops ?? [] : [];

  return (
    <header ref={headerRef} className={`header ${compact ? 'header--compact' : ''}`} data-compact={compact}>
      {/* Thanh trên cùng */}
      <div className="header-top">
        <div className="container header-top-inner">
          <nav className="header-top-links">
            <a href={SELLER_URL} className="header-top-link">Kênh Người Bán</a>
            <a href={`${SELLER_URL}dang-ky-ban-hang`} className="header-top-link">Trở thành người bán</a>
            <Link to="/tai-ung-dung" className="header-top-link" data-testid="app-link">Tải ứng dụng</Link>
          </nav>
          <nav className="header-top-links">
            <Link to="/thong-bao" className="header-top-link" data-testid="notifications-link">
              <Bell size={14} aria-hidden /> Thông báo{(unread?.total ?? 0) > 0 && <span className="header-noti-badge" data-testid="notifications-badge">{badgeCount(unread!.total)}</span>}
            </Link>
            <Link to="/tro-giup" className="header-top-link" data-testid="help-link"><CircleHelp size={14} aria-hidden /> Hỗ trợ</Link>
            {isLoggedIn && user ? (
              <div className="header-user" ref={userRef}>
                <button
                  className="header-user-btn"
                  onClick={() => setShowUserMenu((v) => !v)}
                  data-testid="user-menu"
                >
                  <UserAvatar className="header-user-avatar" />
                  {user.fullName}
                </button>
                {showUserMenu && (
                  <div className="header-user-dropdown">
                    <Link to="/tai-khoan/ho-so" onClick={() => setShowUserMenu(false)}>Tài khoản của tôi</Link>
                    <Link to="/yeu-thich" onClick={() => setShowUserMenu(false)}>Sản phẩm yêu thích</Link>
                    <Link to="/gio-hang" onClick={() => setShowUserMenu(false)}>Giỏ hàng</Link>
                    <button
                      onClick={async () => {
                        setShowUserMenu(false);
                        await logout();
                        navigate('/');
                      }}
                      data-testid="logout"
                    >
                      Đăng xuất
                    </button>
                  </div>
                )}
              </div>
            ) : (
              <>
                <Link to="/dang-ky" className="header-top-link">Đăng ký</Link>
                <span className="header-top-sep">|</span>
                <Link to="/dang-nhap" className="header-top-link" data-testid="login-link">Đăng nhập</Link>
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
              placeholder="Tìm sản phẩm, thương hiệu và shop"
              value={keyword}
              onChange={(e) => {
                setKeyword(e.target.value);
                setShowSuggest(true);
              }}
              onFocus={() => setShowSuggest(true)}
              onKeyDown={(e) => {
                if (e.key === 'ArrowDown') { e.preventDefault(); setShowSuggest(true); suggestRef.current?.querySelector<HTMLButtonElement>('.header-suggest-item')?.focus(); }
                if (e.key === 'Escape') setShowSuggest(false);
              }}
              aria-label="Tìm kiếm sản phẩm"
            />
            <button type="submit" className="header-search-btn" aria-label="Tìm kiếm">
              <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <circle cx="11" cy="11" r="8" />
                <path d="m21 21-4.35-4.35" />
              </svg>
            </button>
          </form>

          {/* Search suggestions (G2-B3): shop card, keywords and products with the typed part in bold; history when empty */}
          {showSuggest && (typed || history.length > 0 || hotKeywords.length > 0) && (
            <div className="header-suggest" data-testid="search-suggest" onKeyDown={onSuggestKey} ref={suggestRef}>
              {typed ? (
                <>
                  {suggestedShops.slice(0, 1).map((shop) => (
                    <button
                      key={shop.id}
                      type="button"
                      className="header-suggest-item header-suggest-shop"
                      onClick={() => {
                        setShowSuggest(false);
                        navigate(`/shop/${shop.slug}`);
                      }}
                      data-testid="suggest-shop"
                    >
                      <span className="header-suggest-shop-logo">
                        {shop.logoUrl ? <img src={shop.logoUrl} alt="" onError={handleImgError} /> : <Store size={18} aria-hidden />}
                      </span>
                      <span className="header-suggest-shop-text">
                        <strong><Highlight text={shop.name} query={keyword} /></strong>
                        <small>Tìm shop “{keyword.trim()}”</small>
                      </span>
                      {shop.isMall && <span className="header-suggest-mall">Mall</span>}
                    </button>
                  ))}
                  {suggestedKeywords.map((k) => (
                    <button key={k} type="button" className="header-suggest-item" onClick={() => goSearch(k)}>
                      <Search size={14} className="header-suggest-icon" aria-hidden />
                      <span><Highlight text={k} query={keyword} /></span>
                    </button>
                  ))}
                  {suggestedProducts.map((p) => (
                    <button
                      key={p.id}
                      type="button"
                      className="header-suggest-item"
                      onClick={() => {
                        setShowSuggest(false);
                        navigate(`/san-pham/${p.id}`);
                      }}
                      data-testid="suggest-product"
                    >
                      <img className="header-suggest-thumb" src={sizedImage(p.imageUrl, 200)} alt="" onError={handleImgError} />
                      <span><Highlight text={p.name} query={keyword} /></span>
                    </button>
                  ))}
                  <button type="button" className="header-suggest-item header-suggest-all" onClick={() => goSearch(keyword)}>
                    Tìm “{keyword.trim()}”
                  </button>
                </>
              ) : (
                <>
                  {history.length > 0 && (
                    <div className="header-suggest-group" data-testid="search-history">
                      <div className="header-suggest-title">
                        Lịch sử tìm kiếm
                        <button type="button" className="header-suggest-clear" data-confirm="local" onClick={() => { clearSearchHistory(); setHistory([]); }}>Xoá</button>
                      </div>
                      {history.map((k) => (
                        <button key={k} type="button" className="header-suggest-item" onClick={() => goSearch(k)}>
                          <History size={14} className="header-suggest-icon" aria-hidden /> <span>{k}</span>
                        </button>
                      ))}
                    </div>
                  )}
                  {hotKeywords.length > 0 && (
                    <div className="header-suggest-group">
                      <div className="header-suggest-title">Tìm kiếm phổ biến</div>
                      {hotKeywords.map((k) => (
                        <button key={k} type="button" className="header-suggest-item" onClick={() => goSearch(k)}>
                          <TrendingUp size={14} className="header-suggest-icon" aria-hidden /> <span>{k}</span>
                        </button>
                      ))}
                    </div>
                  )}
                </>
              )}
            </div>
          )}

          <div className="header-hot-keywords">
            {hotKeywords.slice(0, 6).map((k) => (
              <Link key={k} to={`/tim-kiem?q=${encodeURIComponent(k)}`} className="header-hot-keyword">
                {k}
              </Link>
            ))}
          </div>
        </div>

        <div className="header-actions">
          {/* Phones: the top bar is hidden, so notifications and the account sit here */}
          {isLoggedIn && (
            <Link to="/thong-bao" className="header-icon-link header-mobile-only" aria-label="Thông báo" data-testid="mobile-notifications">
              <span className="header-heart"><Bell size={22} aria-hidden /></span>
              {(unread?.total ?? 0) > 0 && <span className="header-cart-badge">{badgeCount(unread!.total)}</span>}
            </Link>
          )}
          <Link to={isLoggedIn ? '/tai-khoan' : '/dang-nhap'} className="header-icon-link header-mobile-only"
            aria-label={isLoggedIn ? 'Tài khoản của tôi' : 'Đăng nhập'} data-testid="mobile-account">
            <span className="header-heart">{isLoggedIn && user ? user.fullName.charAt(0).toUpperCase() : <UserRound size={22} aria-hidden />}</span>
          </Link>
          {/* Yêu thích */}
          <Link to="/yeu-thich" className="header-icon-link" aria-label="Yêu thích" data-testid="wishlist-link">
            <span className="header-heart"><Heart size={22} aria-hidden /></span>
            {wishCount > 0 && <span className="header-cart-badge">{badgeCount(wishCount)}</span>}
          </Link>

          {/* Giỏ hàng + xem nhanh khi hover */}
          <div className="header-cart-wrapper">
            <Link to="/gio-hang" className="header-cart" aria-label="Giỏ hàng">
              <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6">
                <circle cx="9" cy="21" r="1" />
                <circle cx="20" cy="21" r="1" />
                <path d="M1 1h4l2.68 13.39a2 2 0 0 0 2 1.61h9.72a2 2 0 0 0 2-1.61L23 6H6" />
              </svg>
              {badge > 0 && <span className="header-cart-badge" data-testid="cart-badge">{badgeCount(badge)}</span>}
            </Link>

            <div className="header-cart-preview" data-testid="cart-preview">
              {items.length === 0 ? (
                <div className="header-cart-preview-empty">
                  <div className="header-cart-preview-empty-icon"><ShoppingCart size={48} strokeWidth={1.25} aria-hidden /></div>
                  Chưa có sản phẩm
                </div>
              ) : (
                <>
                  <div className="header-cart-preview-title">Sản phẩm mới thêm</div>
                  <div className="header-cart-preview-list">
                    {items.map((it) => (
                      <Link key={it.skuId} to={`/san-pham/${it.productId}`} className="header-cart-preview-item">
                        <img src={sizedImage(it.imageUrl, 200)} alt={it.name} onError={handleImgError} />
                        <span className="header-cart-preview-name">{it.name}</span>
                        <span className="header-cart-preview-price">{formatPrice(it.price)}</span>
                      </Link>
                    ))}
                  </div>
                  <div className="header-cart-preview-footer">
                    <span>{badge} sản phẩm · {formatPrice(totalPrice)}</span>
                    <Link to="/gio-hang" className="header-cart-preview-btn">Xem giỏ hàng</Link>
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
