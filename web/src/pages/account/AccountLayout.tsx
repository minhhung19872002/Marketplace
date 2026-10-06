import { NavLink, Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import './Account.css';

const LINKS = [
  { to: '/tai-khoan/don-mua', label: 'Đơn Mua' },
  { to: '/tai-khoan/tra-hang', label: 'Trả Hàng / Hoàn Tiền' },
  { to: '/tai-khoan/voucher', label: 'Ví Voucher' },
  { to: '/tai-khoan/vi', label: 'Ví ShopHub' },
  { to: '/tai-khoan/xu', label: 'ShopHub Xu' },
  { to: '/tai-khoan/ho-so', label: 'Hồ Sơ' },
  { to: '/tai-khoan/dia-chi', label: 'Địa Chỉ' },
  { to: '/tai-khoan/mat-khau', label: 'Đổi Mật Khẩu' },
  { to: '/tai-khoan/thiet-bi', label: 'Thiết Bị Đăng Nhập' },
];

/** "Tài khoản của tôi" shell — signed-in users only. */
const AccountLayout = () => {
  const { user, isLoggedIn, isChecking } = useAuth();
  const location = useLocation();

  if (isChecking) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!isLoggedIn || !user) return <Navigate to="/dang-nhap" replace state={{ from: location.pathname }} />;

  return (
    <div className="account-page">
      <div className="container account-layout">
        <aside className="account-sidebar">
          <div className="account-user">
            <span className="account-avatar">{user.fullName.charAt(0).toUpperCase()}</span>
            <span className="account-user-name">{user.fullName}</span>
          </div>
          <div className="account-nav-title">Tài Khoản Của Tôi</div>
          <nav className="account-nav">
            {LINKS.map((l) => (
              <NavLink key={l.to} to={l.to} className={({ isActive }) => `account-nav-link ${isActive ? 'active' : ''}`}>
                {l.label}
              </NavLink>
            ))}
          </nav>
        </aside>
        <section className="account-content">
          <Outlet />
        </section>
      </div>
    </div>
  );
};

export default AccountLayout;
