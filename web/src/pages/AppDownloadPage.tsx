import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import QRCode from 'qrcode';
import './AppDownloadPage.css';

/**
 * "Tải ứng dụng" (II.1, E2). The mobile app is a later phase (spec 6.7, Phase 14): this page says so plainly; the QR
 * opens this site on a phone meanwhile — no store link that does not exist yet.
 */
const AppDownloadPage = () => {
  const [qr, setQr] = useState<string | null>(null);
  const siteUrl = `${window.location.origin}/`;
  useEffect(() => {
    let alive = true;
    QRCode.toDataURL(siteUrl, { margin: 1, width: 200 })
      .then((url) => { if (alive) setQr(url); })
      .catch(() => { if (alive) setQr(null); });
    return () => { alive = false; };
  }, [siteUrl]);

  return (
    <div className="container app-page">
      <section className="app-page-card">
        <div className="app-page-text">
          <h1>Ứng dụng ShopHub</h1>
          <p className="app-page-soon" data-testid="app-coming-soon">Sắp ra mắt trên iOS và Android.</p>
          <p>
            Trong lúc chờ, bạn mua sắm đầy đủ trên trình duyệt điện thoại: quét mã để mở ShopHub, đăng nhập bằng tài khoản hiện có — giỏ hàng,
            đơn mua, voucher và ShopHub Xu đều giữ nguyên khi ứng dụng ra mắt.
          </p>
          <p><Link to="/">Tiếp tục mua sắm</Link></p>
        </div>
        <div className="app-page-qr">
          {qr ? <img src={qr} alt={`Mã QR mở ${siteUrl}`} width={200} height={200} data-testid="app-qr" /> : <div className="app-page-qr-empty" />}
          <span>Quét để mở ShopHub trên điện thoại</span>
        </div>
      </section>
    </div>
  );
};

export default AppDownloadPage;
