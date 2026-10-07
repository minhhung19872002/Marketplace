import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { contentApi } from '../api/content';
import './Footer.css';

type FooterLink = { label: string; to?: string; href?: string };

const COLUMNS: { title: string; links: FooterLink[] }[] = [
  {
    title: 'Chăm Sóc Khách Hàng',
    links: [
      { label: 'Trung Tâm Trợ Giúp', to: '/tro-giup' },
      { label: 'ShopHub Mall', to: '/tim-kiem?mall=true' },
      { label: 'Hướng Dẫn Mua Hàng', to: '/trang/huong-dan-mua-hang' },
      { label: 'Hướng Dẫn Bán Hàng', to: '/tro-giup/tro-giup-dang-ky-ban-hang' },
      { label: 'Trả Hàng & Hoàn Tiền', to: '/trang/chinh-sach-tra-hang' },
      { label: 'Tra Cứu Vận Đơn', to: '/tra-cuu-van-don' },
      { label: 'ShopHub Xu', to: '/tai-khoan/xu' },
    ],
  },
  {
    title: 'Về ShopHub',
    links: [
      { label: 'Điều Khoản', to: '/trang/dieu-khoan-su-dung' },
      { label: 'Quy Chế Hoạt Động', to: '/trang/quy-che-hoat-dong' },
      { label: 'Chính Sách Bảo Mật', to: '/trang/chinh-sach-bao-mat' },
      { label: 'Kênh Người Bán', href: '/seller/' },
      { label: 'Flash Sale', to: '/flash-sale' },
    ],
  },
  {
    title: 'Thanh Toán',
    links: [{ label: 'Visa' }, { label: 'Mastercard' }, { label: 'JCB' }, { label: 'COD' }, { label: 'Ví ShopHub' }],
  },
];

const Footer = () => {
  // Legal entity comes from the platform parameters (SITE.*), never from the bundle
  const site = useQuery({ queryKey: ['site'], queryFn: contentApi.site, staleTime: 3_600_000 });
  const s = site.data;
  return (
    <footer className="footer">
      <div className="container footer-columns">
        {COLUMNS.map((col) => (
          <div key={col.title} className="footer-col">
            <h4 className="footer-col-title">{col.title}</h4>
            <ul>
              {col.links.map((link) => (
                <li key={link.label}>
                  {link.to ? <Link to={link.to}>{link.label}</Link> : link.href ? <a href={link.href}>{link.label}</a> : link.label}
                </li>
              ))}
            </ul>
          </div>
        ))}
        {/* Social links come from SITE.SOCIAL_* parameters; none set → no column (E8) */}
        {s && s.social.length > 0 && (
          <div className="footer-col" data-testid="footer-social">
            <h4 className="footer-col-title">Theo Dõi Chúng Tôi</h4>
            <ul>
              {s.social.map((n) => (
                <li key={n.name}><a href={n.url} target="_blank" rel="noopener noreferrer">{n.name}</a></li>
              ))}
            </ul>
          </div>
        )}
      </div>
      <div className="footer-bottom">
        <div className="container">
          <p>© {s?.platformName ?? 'ShopHub'}. Tất cả các quyền được bảo lưu.</p>
          {s && (
            <div className="footer-legal" data-testid="footer-legal">
              <div>{s.legalName}</div>
              <div>Địa chỉ: {s.legalAddress}</div>
              <div>Mã số doanh nghiệp: {s.taxCode} · {s.businessLicense}</div>
              <div>Hotline: {s.hotline} · Email: {s.supportEmail}</div>
            </div>
          )}
        </div>
      </div>
    </footer>
  );
};

export default Footer;
