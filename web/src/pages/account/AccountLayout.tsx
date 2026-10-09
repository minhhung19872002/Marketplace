import { NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { UserAvatar } from '../../components/AvatarEditor';
import './Account.css';

const LINKS = [
  { to: '/tai-khoan/don-mua', label: 'Đơn mua' },
  { to: '/tai-khoan/tra-hang', label: 'Trả hàng / hoàn tiền' },
  { to: '/tai-khoan/voucher', label: 'Ví voucher' },
  { to: '/tai-khoan/da-xem', label: 'Đã xem gần đây' },
  { to: '/tai-khoan/shop-theo-doi', label: 'Shop theo dõi' },
  { to: '/tai-khoan/vi', label: 'Ví ShopHub' },
  { to: '/tai-khoan/xu', label: 'ShopHub Xu' },
  { to: '/tai-khoan/ho-so', label: 'Hồ sơ' },
  { to: '/tai-khoan/dia-chi', label: 'Địa chỉ' },
  { to: '/tai-khoan/mat-khau', label: 'Đổi mật khẩu' },
  { to: '/tai-khoan/thiet-bi', label: 'Thiết bị đăng nhập' },
  { to: '/tai-khoan/thong-bao', label: 'Cài đặt thông báo' },
  { to: '/tai-khoan/quyen-rieng-tu', label: 'Quyền riêng tư' },
];

/** "Tài khoản của tôi" shell — signed-in users only. */
const AccountLayout = () => {
  const { user, isLoggedIn, isChecking, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  if (isChecking) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!isLoggedIn || !user) return <Navigate to="/dang-nhap" replace state={{ from: location.pathname }} />;

  return (
    <div className="account-page">
      <div className="container account-layout">
        <aside className="account-sidebar">
          <div className="account-user">
            <UserAvatar className="account-avatar" />
            <span className="account-user-name">{user.fullName}</span>
          </div>
          <div className="account-nav-title">Tài khoản của tôi</div>
          <nav className="account-nav">
            {LINKS.map((l) => (
              <NavLink key={l.to} to={l.to} className={({ isActive }) => `account-nav-link ${isActive ? 'active' : ''}`}>
                {l.label}
              </NavLink>
            ))}
            <button type="button" className="account-nav-link account-logout" onClick={async () => { await logout(); navigate('/'); }}
              data-testid="account-logout">Đăng xuất</button>
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
