import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import ProductGrid from '../components/ProductGrid';
import QueryState from '../components/QueryState';
import ShopHomeBlocks from '../components/ShopHomeBlocks';
import { ChatNowButton, ChatStats } from '../components/chat/Chat';
import { formatSold } from '../lib/money';
import { formatDate } from '../lib/datetime';
import { handleImgError } from '../lib/image';
import type { ProductSort } from '../types';
import ShopVouchers from '../components/ShopVouchers';
import { marketingApi } from '../api/marketing';
import './ShopPage.css';
import { usePageTitle } from '../lib/pageTitle';
import { Check, Plus } from 'lucide-react';

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
  const [params, setParams] = useSearchParams();
  // Tìm trong shop (II.3): the keyword lives in the URL so the result can be shared
  const q = params.get('q') ?? '';
  const [draft, setDraft] = useState(q);

  const shopQuery = useQuery({ queryKey: ['shop', slug, isLoggedIn], queryFn: () => storefrontApi.shop(slug), retry: false });
  const shopId = shopQuery.data?.shop.id;
  // Tabs (II.5): "Dạo" when the shop is decorated, all products, one per shop category, the shop profile
  const tab = params.get('tab') ?? (shopQuery.data?.hasDecoration ? 'dao' : 'tat-ca');
  const categoryId = tab.startsWith('dm-') ? tab.slice(3) : undefined;
  const openTab = (key: string) => {
    setPage(1);
    setParams({ tab: key }, { replace: true });
  };
  const searchInShop = () => {
    setPage(1);
    const keyword = draft.trim();
    setParams(keyword ? { tab: 'tat-ca', q: keyword } : { tab: 'tat-ca' }, { replace: true });
  };
  const products = useQuery({
    queryKey: ['search', { shopId, sort, page, q }],
    queryFn: () => storefrontApi.search({ shopId, sort, page, pageSize: 30, q: q || undefined }),
    enabled: !!shopId && tab === 'tat-ca',
    placeholderData: keepPreviousData,
  });
  const home = useQuery({
    queryKey: ['shop-home', shopId],
    queryFn: () => storefrontApi.shopHome(shopId!),
    enabled: !!shopId && tab === 'dao',
  });
  const offers = useQuery({ queryKey: ['shop-offers', shopId], queryFn: () => marketingApi.shopOffers(shopId!), enabled: !!shopId, staleTime: 60_000 });
  const categoryProducts = useQuery({
    queryKey: ['shop-category', shopId, categoryId, page],
    queryFn: () => storefrontApi.shopCategoryProducts(shopId!, categoryId!, page),
    enabled: !!shopId && !!categoryId,
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

  usePageTitle(shopQuery.data?.shop.name ?? (shopQuery.isError ? 'Không tìm thấy shop' : null));
  if (shopQuery.isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
  // A network / server error is not "no such shop": say so and offer "Thử lại" (F3)
  if (shopQuery.isError && !(shopQuery.error instanceof ApiError && shopQuery.error.status === 404)) {
    return <div className="container"><QueryState query={shopQuery}>{() => null}</QueryState></div>;
  }
  if (!shopQuery.data) {
    return (
      <div className="container product-not-found" data-testid="shop-not-found">
        <p>Không tìm thấy shop.</p>
        <Link to="/" className="btn-back-home">Về trang chủ</Link>
      </div>
    );
  }

  const { shop, description, coverUrl, isFollowing, categories, hasDecoration } = shopQuery.data;
  const listing = categoryId ? categoryProducts.data : products.data;
  const totalPages = listing ? Math.max(1, Math.ceil(listing.totalCount / listing.pageSize)) : 1;
  const tabs = [
    ...(hasDecoration ? [{ key: 'dao', label: 'Dạo' }] : []),
    { key: 'tat-ca', label: 'Tất Cả Sản Phẩm' },
    ...categories.map((c) => ({ key: `dm-${c.id}`, label: c.name })),
    { key: 'ho-so', label: 'Hồ Sơ Shop' },
  ];
  const category = categories.find((c) => c.id === categoryId);

  const onFollow = () => {
    if (!isLoggedIn) {
      navigate('/dang-nhap', { state: { from: `/shop/${slug}` } });
      return;
    }
    follow.mutate(!isFollowing);
  };

  return (
    <div className="shop-page">
      {/* Tạm nghỉ (II.5, E5): said loudly at the top — orders are blocked until the shop is back */}
      {shop.onVacation && (
        <div className="shop-vacation" role="status" data-testid="shop-vacation">
          <strong>Shop đang tạm nghỉ{shop.vacationUntil ? ` đến ${formatDate(shop.vacationUntil)}` : ''}.</strong>
          <span> Bạn vẫn xem được sản phẩm nhưng chưa thể đặt mua trong thời gian này.</span>
        </div>
      )}
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
                {isFollowing ? <><Check size={16} aria-hidden /> Đang Theo Dõi</> : <><Plus size={16} aria-hidden /> Theo Dõi</>}
              </button>
              <ChatNowButton shopId={shop.id} className="shop-follow" />
            </div>
            {message && <div className="shop-message" role="status">{message}</div>}
          </div>
          <div className="shop-stats">
            <div data-testid="shop-rating">
              <strong>{shop.ratingCount > 0 ? `${shop.ratingAvg.toFixed(1)}/5` : '—'}</strong>
              <span>Đánh Giá ({formatSold(shop.ratingCount)})</span>
            </div>
            <div><strong>{formatSold(shop.productCount)}</strong><span>Sản Phẩm</span></div>
            <div><strong data-testid="shop-followers">{formatSold(shop.followerCount)}</strong><span>Người Theo Dõi</span></div>
            <div><strong>{formatDate(shop.joinedAt)}</strong><span>Tham Gia</span></div>
            <ChatStats shopId={shop.id} />
          </div>
        </div>
        {coverUrl && <img className="shop-cover" src={coverUrl} alt="" onError={handleImgError} />}
      </div>

      <div className="container">
        <nav className="shop-nav" aria-label="Mục của shop">
          {tabs.map((t) => (
            <button key={t.key} className={`shop-nav-item ${tab === t.key ? 'active' : ''}`} onClick={() => openTab(t.key)}
              aria-current={tab === t.key ? 'page' : undefined} data-testid="shop-nav">
              {t.label}
            </button>
          ))}
        </nav>

        <form className="shop-search" role="search" onSubmit={(e) => { e.preventDefault(); searchInShop(); }}>
          <input value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="Tìm trong shop này" aria-label="Tìm trong shop" data-testid="shop-search" />
          <button type="submit">Tìm</button>
        </form>
        <ShopVouchers shopId={shop.id} />
        {(offers.data?.length ?? 0) > 0 && (
          <div className="shop-offers" data-testid="shop-offers">
            <div className="shop-section-head">CHƯƠNG TRÌNH ĐANG CHẠY</div>
            <ul>
              {offers.data!.map((o) => (
                <li key={o.id}><strong>{o.name}</strong> — {o.text} · đến {formatDate(o.endAt)}</li>
              ))}
            </ul>
          </div>
        )}

        {tab === 'dao' && (
          <QueryState query={home}>
            {(blocks) => <ShopHomeBlocks blocks={blocks} onOpenCategory={(id) => openTab(`dm-${id}`)} />}
          </QueryState>
        )}

        {tab === 'ho-so' && (
          <div className="shop-profile" data-testid="shop-profile">
            <div className="shop-section-head">HỒ SƠ SHOP</div>
            <div className="shop-description">{description || 'Shop chưa có giới thiệu.'}</div>
            <dl className="shop-profile-facts">
              <div><dt>Nơi gửi hàng</dt><dd>{shop.provinceName ?? '—'}</dd></div>
              <div><dt>Sản phẩm đang bán</dt><dd>{formatSold(shop.productCount)}</dd></div>
              <div><dt>Người theo dõi</dt><dd>{formatSold(shop.followerCount)}</dd></div>
              <div><dt>Tham gia</dt><dd>{formatDate(shop.joinedAt)}</dd></div>
            </dl>
          </div>
        )}

        {tab === 'tat-ca' && (
          <>
            {!hasDecoration && description && <div className="shop-description">{description}</div>}
            <div className="shop-section-head">{q ? `KẾT QUẢ CHO “${q.toUpperCase()}” TRONG SHOP` : 'TẤT CẢ SẢN PHẨM'}</div>
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
            <QueryState query={products} loading={<ProductGrid title="" products={[]} loading />}>
              {(found) => <ProductGrid title="" products={found.items} emptyText={q ? 'Không tìm thấy sản phẩm phù hợp trong shop.' : 'Shop chưa có sản phẩm nào đang bán.'} />}
            </QueryState>
          </>
        )}

        {categoryId && (
          category ? (
            <>
              <div className="shop-section-head">{category.name.toUpperCase()}</div>
              <QueryState query={categoryProducts} loading={<ProductGrid title="" products={[]} loading />}>
                {(found) => <ProductGrid title="" products={found.items} emptyText="Danh mục chưa có sản phẩm nào đang bán." />}
              </QueryState>
            </>
          ) : <div className="shop-description">Danh mục này không còn hiển thị.</div>
        )}

        {(tab === 'tat-ca' || categoryId) && totalPages > 1 && (
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
