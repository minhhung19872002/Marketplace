import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { storefrontApi } from '../../api/storefront';
import { ApiError } from '../../api/http';
import ProductGrid from '../../components/ProductGrid';
import { formatSold } from '../../lib/money';
import { handleImgError } from '../../lib/image';

/** /tai-khoan/da-xem — "Đã xem gần đây" (II.9), newest first. */
export const ViewedPage = () => {
  const viewed = useQuery({ queryKey: ['viewed'], queryFn: () => storefrontApi.viewed() });
  return (
    <div className="account-card" data-testid="viewed-page">
      <h2 className="account-title">Sản Phẩm Đã Xem</h2>
      <ProductGrid title="" products={viewed.data ?? []} loading={viewed.isLoading} emptyText="Bạn chưa xem sản phẩm nào." />
    </div>
  );
};

/** /tai-khoan/shop-theo-doi — shops the buyer follows (II.9), with "Bỏ theo dõi". */
export const FollowedShopsPage = () => {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState('');
  const shops = useQuery({ queryKey: ['followed-shops'], queryFn: () => storefrontApi.followedShops() });

  const unfollow = async (id: string) => {
    try {
      setMessage((await storefrontApi.follow(id, false)).message);
      void queryClient.invalidateQueries({ queryKey: ['followed-shops'] });
      void queryClient.invalidateQueries({ queryKey: ['shop'] });
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không thực hiện được, vui lòng thử lại.');
    }
  };

  return (
    <div className="account-card" data-testid="followed-shops-page">
      <h2 className="account-title">Shop Đang Theo Dõi</h2>
      {message && <div className="account-message" role="status">{message}</div>}
      {shops.isLoading && <div className="loading-spinner" />}
      {shops.data?.length === 0 && <p className="account-empty">Bạn chưa theo dõi shop nào.</p>}
      <div className="followed-shops">
        {(shops.data ?? []).map((s) => (
          <div key={s.id} className="followed-shop" data-testid="followed-shop">
            <Link to={`/shop/${s.slug}`} className="followed-shop-link">
              <span className="followed-shop-logo">{s.logoUrl ? <img src={s.logoUrl} alt="" onError={handleImgError} /> : s.name.charAt(0)}</span>
              <span className="followed-shop-info">
                <strong>{s.name}</strong>
                {s.isMall && <span className="followed-shop-badge">Mall</span>}
                <span>{formatSold(s.followerCount)} người theo dõi · {formatSold(s.productCount)} sản phẩm{s.provinceName ? ` · ${s.provinceName}` : ''}</span>
              </span>
            </Link>
            <button type="button" className="account-btn account-btn-outline" onClick={() => void unfollow(s.id)} data-testid="unfollow-shop">Bỏ theo dõi</button>
          </div>
        ))}
      </div>
    </div>
  );
};
