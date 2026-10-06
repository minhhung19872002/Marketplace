import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import ProductGrid from '../components/ProductGrid';
import { ChatNowButton, ChatStats } from '../components/chat/Chat';
import { formatSold } from '../lib/money';
import { formatDate } from '../lib/datetime';
import { handleImgError } from '../lib/image';
import type { ProductSort } from '../types';
import './ShopPage.css';

const TABS: { key: ProductSort; label: string }[] = [
  { key: 'BestSelling', label: 'Phổ Biến' },
  { key: 'Newest', label: 'Mới Nhất' },
  { key: 'PriceAsc', label: 'Giá Thấp' },
  { key: 'PriceDesc', label: 'Giá Cao' },
];

const ShopPage = () => {
  const { slug = '' } = useParams();
  const navigate = useNavigate();
  const { isLoggedIn } = useAuth();
  const queryClient = useQueryClient();
  const [sort, setSort] = useState<ProductSort>('BestSelling');
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState('');

  const shopQuery = useQuery({ queryKey: ['shop', slug, isLoggedIn], queryFn: () => storefrontApi.shop(slug), retry: false });
  const shopId = shopQuery.data?.shop.id;
  const products = useQuery({
    queryKey: ['search', { shopId, sort, page }],
    queryFn: () => storefrontApi.search({ shopId, sort, page, pageSize: 30 }),
    enabled: !!shopId,
    placeholderData: keepPreviousData,
  });

  const follow = useMutation({
    mutationFn: (on: boolean) => storefrontApi.follow(shopId!, on),
    onSuccess: (r) => {
      setMessage(r.message);
      void queryClient.invalidateQueries({ queryKey: ['shop', slug] });
    },
    onError: (e) => setMessage(e instanceof ApiError ? e.message : 'Không thực hiện được, vui lòng thử lại.'),
  });

  if (shopQuery.isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!shopQuery.data) {
    return (
      <div className="container product-not-found" data-testid="shop-not-found">
        <p>Không tìm thấy shop.</p>
        <Link to="/" className="btn-back-home">Về trang chủ</Link>
      </div>
    );
  }

  const { shop, description, coverUrl, isFollowing } = shopQuery.data;
  const totalPages = products.data ? Math.max(1, Math.ceil(products.data.totalCount / products.data.pageSize)) : 1;

  const onFollow = () => {
    if (!isLoggedIn) {
      navigate('/dang-nhap', { state: { from: `/shop/${slug}` } });
      return;
    }
    follow.mutate(!isFollowing);
  };

  return (
    <div className="shop-page">
      <div className="shop-banner">
        <div className="container shop-banner-inner">
          <div className="shop-avatar">
            {shop.logoUrl ? <img src={shop.logoUrl} alt="" onError={handleImgError} /> : shop.name.charAt(0)}
          </div>
          <div className="shop-info">
            <div className="shop-name" data-testid="shop-name">
              {shop.name} {shop.isMall && <span className="shop-badge">Mall</span>}
              {shop.isPreferred && !shop.isMall && <span className="shop-badge">Yêu thích</span>}
            </div>
            <div className="shop-online">
              {shop.onVacation ? `Đang tạm nghỉ${shop.vacationUntil ? ` đến ${formatDate(shop.vacationUntil)}` : ''}` : shop.provinceName}
            </div>
            <div className="shop-actions">
              <button className={`shop-follow ${isFollowing ? 'following' : ''}`} onClick={onFollow} disabled={follow.isPending} data-testid="shop-follow">
                {isFollowing ? '✓ Đang Theo Dõi' : '+ Theo Dõi'}
              </button>
              <ChatNowButton shopId={shop.id} className="shop-follow" />
            </div>
            {message && <div className="shop-message" role="status">{message}</div>}
          </div>
          <div className="shop-stats">
            <div><strong>{formatSold(shop.productCount)}</strong><span>Sản Phẩm</span></div>
            <div><strong data-testid="shop-followers">{formatSold(shop.followerCount)}</strong><span>Người Theo Dõi</span></div>
            <div><strong>{formatDate(shop.joinedAt)}</strong><span>Tham Gia</span></div>
            <ChatStats shopId={shop.id} />
          </div>
        </div>
        {coverUrl && <img className="shop-cover" src={coverUrl} alt="" onError={handleImgError} />}
      </div>

      <div className="container">
        {description && <div className="shop-description">{description}</div>}
        <div className="shop-section-head">TẤT CẢ SẢN PHẨM</div>
        <div className="shop-tabs">
          {TABS.map((t) => (
            <button
              key={t.key}
              className={`shop-tab ${sort === t.key ? 'active' : ''}`}
              onClick={() => {
                setSort(t.key);
                setPage(1);
              }}
            >
              {t.label}
            </button>
          ))}
        </div>
        <ProductGrid title="" products={products.data?.items ?? []} loading={products.isLoading} emptyText="Shop chưa có sản phẩm nào đang bán." />
        {totalPages > 1 && (
          <nav className="shop-pager" aria-label="Phân trang">
            <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
            <span>Trang {page} / {totalPages}</span>
            <button disabled={page >= totalPages} onClick={() => setPage(page + 1)}>›</button>
          </nav>
        )}
      </div>
    </div>
  );
};

export default ShopPage;
