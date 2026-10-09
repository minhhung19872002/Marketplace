import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { BadgeCheck, Facebook, Globe, Instagram, Linkedin, MessageCircle, Music2, Smartphone, Youtube, type LucideIcon } from 'lucide-react';
import { contentApi } from '../api/content';
import { currentYear } from '../lib/datetime';
import './Footer.css';

type FooterLink = { label: string; to?: string; href?: string };

const COLUMNS: { title: string; links: FooterLink[] }[] = [
  {
    title: 'Chăm Sóc Khách Hàng',
    links: [
      { label: 'Trung Tâm Trợ Giúp', to: '/tro-giup' },
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
      { label: 'Điều Khoản Sử Dụng', to: '/trang/dieu-khoan-su-dung' },
      { label: 'Quy Chế Hoạt Động', to: '/trang/quy-che-hoat-dong' },
      { label: 'Chính Sách Bảo Mật', to: '/trang/chinh-sach-bao-mat' },
      { label: 'ShopHub Mall', to: '/tim-kiem?mall=true' },
      { label: 'Flash Sale', to: '/flash-sale' },
      { label: 'Kênh Người Bán', href: '/seller/' },
    ],
  },
];

// Accepted payment methods (spec IV) — drawn as neutral chips, not copies of the networks' logos
const PAYMENTS = ['VISA', 'Mastercard', 'JCB', 'COD', 'Ví ShopHub', 'VNPay', 'MoMo', 'ZaloPay'];

const SOCIAL_ICONS: Record<string, LucideIcon> = {
  Facebook, Instagram, LinkedIn: Linkedin, YouTube: Youtube, TikTok: Music2, Zalo: MessageCircle,
};

/** QR code of the app download page (rendered in the browser, no external service). */
const useQr = (url: string) => {
  const [src, setSrc] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    // Loaded on demand: the QR library stays out of the entry chunk (mobile LCP, G3)
    import('qrcode').then(({ default: QRCode }) => QRCode.toDataURL(url, { margin: 1, width: 168 })).then((d) => alive && setSrc(d)).catch(() => alive && setSrc(null));
    return () => { alive = false; };
  }, [url]);
  return src;
};

const Footer = () => {
  // Legal entity, social links, store links and the Bộ Công Thương badge come from the platform parameters (SITE.*)
  const site = useQuery({ queryKey: ['site'], queryFn: contentApi.site, staleTime: 3_600_000 });
  const s = site.data;
  const qr = useQr(`${window.location.origin}/tai-ung-dung`);
  const platform = s?.platformName ?? 'ShopHub';

  return (
    <footer className="footer">
      <div className="container footer-columns">
        {COLUMNS.map((col) => (
          <nav key={col.title} className="footer-col" aria-label={col.title}>
            <h2 className="footer-col-title">{col.title}</h2>
            <ul>
              {col.links.map((link) => (
                <li key={link.label}>
                  {link.to ? <Link to={link.to}>{link.label}</Link> : <a href={link.href}>{link.label}</a>}
                </li>
              ))}
            </ul>
          </nav>
        ))}

        <div className="footer-col">
          <h2 className="footer-col-title">Thanh Toán</h2>
          <ul className="footer-chips" aria-label="Phương thức thanh toán">
            {PAYMENTS.map((p) => <li key={p} className="footer-chip">{p}</li>)}
          </ul>
          {s && s.carriers.length > 0 && (
            <>
              <h2 className="footer-col-title footer-col-title--spaced">Đơn Vị Vận Chuyển</h2>
              <ul className="footer-chips" aria-label="Đơn vị vận chuyển" data-testid="footer-carriers">
                {s.carriers.map((c) => <li key={c} className="footer-chip">{c}</li>)}
              </ul>
            </>
          )}
        </div>

        <div className="footer-col">
          {/* Social links come from SITE.SOCIAL_* parameters; none set → no block (E8) */}
          {s && s.social.length > 0 && (
            <div data-testid="footer-social">
              <h2 className="footer-col-title">Theo Dõi Chúng Tôi</h2>
              <ul className="footer-social">
                {s.social.map((n) => {
                  const Icon = SOCIAL_ICONS[n.name] ?? Globe;
                  return (
                    <li key={n.name}>
                      <a href={n.url} target="_blank" rel="noopener noreferrer"><Icon size={16} aria-hidden /> {n.name}</a>
                    </li>
                  );
                })}
              </ul>
            </div>
          )}
          <h2 className={`footer-col-title ${s && s.social.length > 0 ? 'footer-col-title--spaced' : ''}`}>Tải Ứng Dụng {platform}</h2>
          <div className="footer-app">
            <Link to="/tai-ung-dung" className="footer-qr" aria-label="Tải ứng dụng">
              {qr ? <img src={qr} alt="Mã QR tải ứng dụng" width={84} height={84} /> : <Smartphone size={40} aria-hidden />}
            </Link>
            <div className="footer-stores">
              {s?.appStoreUrl && <a className="footer-store" href={s.appStoreUrl} target="_blank" rel="noopener noreferrer">App Store</a>}
              {s?.googlePlayUrl && <a className="footer-store" href={s.googlePlayUrl} target="_blank" rel="noopener noreferrer">Google Play</a>}
              {!s?.appStoreUrl && !s?.googlePlayUrl && <Link className="footer-store" to="/tai-ung-dung">Xem cách cài đặt</Link>}
            </div>
          </div>
        </div>
      </div>

      <div className="footer-bottom">
        <div className="container footer-bottom-inner">
          <p className="footer-copy">© {currentYear()} {platform}. Tất cả các quyền được bảo lưu.</p>
          {s && (
            <div className="footer-legal" data-testid="footer-legal">
              <div className="footer-legal-name">{s.legalName}</div>
              <div>Địa chỉ: {s.legalAddress}</div>
              <div>Mã số doanh nghiệp: {s.taxCode} · {s.businessLicense}</div>
              <div>Hotline: {s.hotline} · Email: {s.supportEmail}</div>
            </div>
          )}
          {/* Only with the real online.gov.vn registration link (SITE.MOIT_URL) — never a badge without it */}
          {s?.moitUrl && (
            <a className="footer-moit" href={s.moitUrl} target="_blank" rel="noopener noreferrer" data-testid="footer-moit">
              <BadgeCheck size={18} aria-hidden /> Đã thông báo Bộ Công Thương
            </a>
          )}
        </div>
      </div>
    </footer>
  );
};

export default Footer;
