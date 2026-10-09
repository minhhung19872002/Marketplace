import { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { marketingApi, type CampaignBlock, type CampaignVoucher } from '../api/marketing';
import { walletApi } from '../api/commerce';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import ProductGrid from '../components/ProductGrid';
import { FlashSaleBoard } from '../components/FlashSaleBlock';
import { BannerArt, bannerShowsTitle } from '../components/Banner';
import { formatPrice } from '../lib/money';
import { formatDate } from '../lib/datetime';
import QueryState from '../components/QueryState';
import './CampaignPage.css';

const describe = (v: CampaignVoucher) =>
  v.type === 'FreeShipping' ? `Miễn phí vận chuyển tối đa ${formatPrice(v.maxDiscount ?? 0)}`
    : v.type === 'Amount' ? `Giảm ${formatPrice(v.discountValue)}`
      : `Giảm ${v.discountPercentBp / 100}%${v.maxDiscount ? ` tối đa ${formatPrice(v.maxDiscount)}` : ''}`;

const VoucherTile = ({ v }: { v: CampaignVoucher }) => {
  const { isLoggedIn } = useAuth();
  const [state, setState] = useState('');
  const claim = async () => {
    try {
      setState((await walletApi.claim(v.id)).message);
    } catch (e) {
      setState(e instanceof ApiError ? e.message : 'Không lưu được mã.');
    }
  };
  return (
    <div className="campaign-voucher" data-testid="campaign-voucher">
      <strong>{v.code}</strong>
      <span>{describe(v)}</span>
      <small>Đơn từ {formatPrice(v.minOrder)} · HSD {formatDate(v.endAt)}</small>
      {isLoggedIn ? <button onClick={claim} disabled={!!state}>{state || 'Lưu'}</button> : <Link to="/dang-nhap">Đăng nhập để lưu</Link>}
    </div>
  );
};

/**
 * Whether a banner block draws its title over the picture: never over artwork that has its own headline (the 10.10 banner
 * says "SIÊU SALE 10.10" and has a button — a second title on top of it was noise, G4-A3). Without the backend flag an
 * image banner is taken to carry text; the title then stays as the image's alt text.
 */
// The campaign API does not send the flag yet: only an explicit "no text in the artwork" (false) draws the title on an image
export const bannerOverlay = (b: CampaignBlock): boolean => bannerShowsTitle(b, b.hasTextInImage === false);

/** /su-kien/:slug — Ngày hội mua sắm built by the platform from blocks (spec II.13). */
const CampaignPage = () => {
  const { slug = '' } = useParams();
  const campaign = useQuery({ queryKey: ['campaign', slug], queryFn: () => marketingApi.campaign(slug), retry: false });
  const { data, error } = campaign;
  // Loading, or a network / server error (not "no such campaign"): spinner or the error with "Thử lại" (F3)
  if (campaign.isPending || (error && !(error instanceof ApiError && error.status === 404))) {
    return <div className="container"><QueryState query={campaign}>{() => null}</QueryState></div>;
  }
  if (error || !data) {
    return (
      <div className="container product-not-found" data-testid="campaign-not-found">
        <p>{error instanceof ApiError && error.status === 404 ? 'Chương trình không tồn tại hoặc đã kết thúc.' : 'Không tải được chương trình.'}</p>
        <Link to="/" className="btn-back-home">Về trang chủ</Link>
      </div>
    );
  }
  return (
    <div className="container campaign-page" data-testid="campaign-page">
      <h1 className="campaign-title">{data.name}</h1>
      <p className="campaign-period">Từ {formatDate(data.startAt)} đến {formatDate(data.endAt)}</p>
      {data.blocks.map((b, i) => (
        <section key={i} className="campaign-block">
          {b.type === 'Banner' && (
            // Same rendering as the home banners (G-VIS): artwork filling the frame, a title only when it has none
            <BannerArt banner={b} to={b.link ?? '/'} className="campaign-banner" titled={bannerOverlay(b)} />
          )}
          {b.type === 'Vouchers' && (
            <>
              {b.title && <h2 className="campaign-block-title">{b.title}</h2>}
              <div className="campaign-vouchers">{(b.vouchers ?? []).map((v) => <VoucherTile key={v.id} v={v} />)}</div>
            </>
          )}
          {b.type === 'FlashSale' && b.flashSale && <FlashSaleBoard board={b.flashSale} compact />}
          {b.type === 'Products' && <ProductGrid title={b.title ?? 'Sản phẩm'} products={b.products ?? []} showFrames />}
          {b.type === 'Registered' && (b.products?.length ?? 0) > 0 && (
            <div data-testid="campaign-registered"><ProductGrid title={b.title ?? 'Sản phẩm tham gia'} products={b.products ?? []} showFrames /></div>
          )}
        </section>
      ))}
    </div>
  );
};

export default CampaignPage;
