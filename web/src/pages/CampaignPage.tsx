import { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { marketingApi, type CampaignVoucher } from '../api/marketing';
import { walletApi } from '../api/commerce';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import ProductGrid from '../components/ProductGrid';
import { FlashSaleBoard } from '../components/FlashSaleBlock';
import { BannerLink } from '../components/Banner';
import { formatPrice } from '../lib/money';
import { formatDate } from '../lib/datetime';
import { handleImgError, isImageUrl } from '../lib/image';
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

/** /su-kien/:slug — Ngày hội mua sắm built by the platform from blocks (spec II.13). */
const CampaignPage = () => {
  const { slug = '' } = useParams();
  const { data, error, isLoading } = useQuery({ queryKey: ['campaign', slug], queryFn: () => marketingApi.campaign(slug), retry: false });
  if (isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
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
            <BannerLink to={b.link ?? '/'} className="campaign-banner">
              {b.imageUrl && isImageUrl(b.imageUrl) && <img src={b.imageUrl} alt="" onError={handleImgError} />}
              {b.title && <span>{b.title}</span>}
            </BannerLink>
          )}
          {b.type === 'Vouchers' && (
            <>
              {b.title && <h2 className="campaign-block-title">{b.title}</h2>}
              <div className="campaign-vouchers">{(b.vouchers ?? []).map((v) => <VoucherTile key={v.id} v={v} />)}</div>
            </>
          )}
          {b.type === 'FlashSale' && b.flashSale && <FlashSaleBoard board={b.flashSale} compact />}
          {b.type === 'Products' && <ProductGrid title={b.title ?? 'SẢN PHẨM'} products={b.products ?? []} />}
          {b.type === 'Registered' && (b.products?.length ?? 0) > 0 && (
            <div data-testid="campaign-registered"><ProductGrid title={b.title ?? 'SẢN PHẨM THAM GIA'} products={b.products ?? []} /></div>
          )}
        </section>
      ))}
    </div>
  );
};

export default CampaignPage;
