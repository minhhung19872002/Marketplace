import React from 'react';
import './Footer.css';

const COLUMNS = [
  {
    title: 'Chăm Sóc Khách Hàng',
    links: ['Trung Tâm Trợ Giúp', 'ShopHub Blog', 'ShopHub Mall', 'Hướng Dẫn Mua Hàng', 'Hướng Dẫn Bán Hàng', 'Thanh Toán', 'ShopHub Xu'],
  },
  {
    title: 'Về ShopHub',
    links: ['Giới Thiệu', 'Tuyển Dụng', 'Điều Khoản', 'Chính Sách Bảo Mật', 'Chính Hãng', 'Kênh Người Bán', 'Flash Sale'],
  },
  {
    title: 'Thanh Toán',
    links: ['Visa', 'Mastercard', 'JCB', 'COD', 'Trả Góp'],
  },
  {
    title: 'Theo Dõi Chúng Tôi',
    links: ['Facebook', 'Instagram', 'LinkedIn', 'TikTok', 'YouTube'],
  },
];

const Footer = () => {
  return (
    <footer className="footer">
      <div className="container footer-columns">
        {COLUMNS.map((col) => (
          <div key={col.title} className="footer-col">
            <h4 className="footer-col-title">{col.title}</h4>
            <ul>
              {col.links.map((link) => (
                <li key={link}>{link}</li>
              ))}
            </ul>
          </div>
        ))}
      </div>
      <div className="footer-bottom">
        <div className="container">
          <p>© 2024 ShopHub. Tất cả các quyền được bảo lưu.</p>
          <p className="footer-country">
            Quốc gia &amp; Khu vực: Singapore | Indonesia | Thái Lan | Malaysia | Việt Nam | Philippines | Brazil
          </p>
        </div>
      </div>
    </footer>
  );
};

export default Footer;
