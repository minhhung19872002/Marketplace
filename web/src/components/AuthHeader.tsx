import { Link, useLocation } from 'react-router-dom';
import Header from './Header';
import BrandMark from './BrandMark';
import DemoNotice from './DemoNotice';
import { usePageTitle } from '../lib/pageTitle';
import './AuthHeader.css';

// The sign-in pages and the name each shows beside the logo (and in the browser tab)
const TITLES: Record<string, string> = { '/dang-nhap': 'Đăng nhập', '/dang-ky': 'Đăng ký', '/quen-mat-khau': 'Đặt lại mật khẩu' };

/** Slim header of the sign-in pages (G5-B), as on the reference marketplace: the logo, the page name, help. */
const AuthHeader = ({ title }: { title: string }) => {
  usePageTitle(title);
  return (
    <header className="auth-header">
      <div className="container auth-header-inner">
        <Link to="/" className="auth-header-logo" aria-label="Về trang chủ ShopHub">
          <BrandMark size={44} />
          <span className="auth-header-brand">ShopHub</span>
        </Link>
        <span className="auth-header-title">{title}</span>
        <DemoNotice variant="badge" />
        <Link to="/tro-giup" className="auth-header-help">Bạn cần giúp đỡ?</Link>
      </div>
    </header>
  );
};

/** The site header, or the slim one on the sign-in pages. */
const SiteHeader = () => {
  const { pathname } = useLocation();
  const title = TITLES[pathname];
  return title ? <AuthHeader title={title} /> : <Header />;
};

export default SiteHeader;
