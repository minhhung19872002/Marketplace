import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import QRCode from 'qrcode';
import { PageBand } from '../components/ui/PageBand';
import { PhoneArt } from '../components/ui/Illustration';
import { usePageTitle } from '../lib/pageTitle';
import './AppDownloadPage.css';

/**
 * "Tải ứng dụng" (II.1, E2). The mobile app is a later phase (spec 6.7, Phase 14): this page says so plainly; the QR
 * opens this site on a phone meanwhile — no store link that does not exist yet. The bright band with a phone drawing
 * carries the large text only; the details and the QR sit on a white card (AA, G5-B).
 */
const AppDownloadPage = () => {
  const [qr, setQr] = useState<string | null>(null);
  const siteUrl = `${window.location.origin}/`;
  usePageTitle('Tải ứng dụng');
  useEffect(() => {
    let alive = true;
    QRCode.toDataURL(siteUrl, { margin: 1, width: 200 })
      .then((url) => { if (alive) setQr(url); })
      .catch(() => { if (alive) setQr(null); });
    return () => { alive = false; };
  }, [siteUrl]);

  return (
    <>
      <PageBand className="app-band">
        <div className="app-band-grid">
          <div>
            <h1>Ứng dụng ShopHub</h1>
            <p className="app-page-soon" data-testid="app-coming-soon">Sắp ra mắt trên iOS và Android.</p>
          </div>
          <PhoneArt />
        </div>
      </PageBand>
      <div className="container app-page">
        <section className="app-page-card">
          <div className="app-page-text">
            <h2>Trong lúc chờ ứng dụng</h2>
            <p>
              Bạn mua sắm đầy đủ trên trình duyệt điện thoại: quét mã để mở ShopHub, đăng nhập bằng tài khoản hiện có — giỏ hàng,
              đơn mua, voucher và ShopHub Xu đều giữ nguyên khi ứng dụng ra mắt.
            </p>
            <p><Link to="/" className="app-page-continue">Tiếp tục mua sắm</Link></p>
          </div>
          <div className="app-page-qr">
            {qr ? <img src={qr} alt={`Mã QR mở ${siteUrl}`} width={200} height={200} data-testid="app-qr" /> : <div className="app-page-qr-empty" />}
            <span>Quét để mở ShopHub trên điện thoại</span>
          </div>
        </section>
      </div>
    </>
  );
};

export default AppDownloadPage;
