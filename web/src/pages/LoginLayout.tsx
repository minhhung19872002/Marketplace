import type { ReactNode } from 'react';
import { BadgePercent, Coins, ShieldCheck, Truck } from 'lucide-react';
import BrandMark from '../components/BrandMark';
import './Auth.css';

const BENEFITS = [
  { icon: ShieldCheck, title: 'Mua sắm an tâm', text: 'Người bán được xác minh, tiền chỉ đến tay shop khi bạn đã nhận hàng.' },
  { icon: Truck, title: 'Giao hàng toàn quốc', text: 'Nhiều đơn vị vận chuyển, theo dõi hành trình đơn hàng theo thời gian thực.' },
  { icon: BadgePercent, title: 'Voucher mỗi ngày', text: 'Mã giảm giá, miễn phí vận chuyển và Flash Sale theo khung giờ.' },
  { icon: Coins, title: 'Tích ShopHub Xu', text: 'Nhận xu khi đánh giá, điểm danh và dùng xu để trừ tiền đơn sau.' },
];

/**
 * Shared shell of the sign-in / sign-up / reset pages: on the bright brand band, the brand (mark, name, headline, benefits)
 * on the left and the white form card on the right, as on the reference marketplace (G5-B); only the card on phones.
 */
const LoginLayout = ({ headline, children }: { headline: string; children: ReactNode }) => (
  <div className="auth-page">
    <div className="auth-hero">
      <div className="container auth-hero-inner">
        <section className="auth-benefits" aria-label="Lợi ích khi mua sắm tại ShopHub">
          <div className="auth-brand">
            <BrandMark size={96} className="auth-brand-mark" />
            <span className="auth-brand-name">ShopHub</span>
          </div>
          <h1 className="auth-benefits-title">{headline}</h1>
          <ul className="auth-benefits-list">
            {BENEFITS.map(({ icon: Icon, title, text }) => (
              <li key={title}>
                <span className="auth-benefits-icon"><Icon size={22} aria-hidden /></span>
                <span>
                  <strong>{title}</strong>
                  <span>{text}</span>
                </span>
              </li>
            ))}
          </ul>
        </section>
        <div className="auth-card">{children}</div>
      </div>
    </div>
  </div>
);

export default LoginLayout;
