import { Link, Navigate } from 'react-router-dom';
import { useInfiniteQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { useAuth } from '../context/AuthContext';
import ProductGrid from '../components/ProductGrid';
import QueryState from '../components/QueryState';
import './Wishlist.css';

const Wishlist = () => {
  const { isLoggedIn, isChecking } = useAuth();
  const query = useInfiniteQuery({
    queryKey: ['wishlist', 'page'],
    queryFn: ({ pageParam }) => storefrontApi.wishlist(pageParam),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined),
    enabled: isLoggedIn,
  });

  if (isChecking) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!isLoggedIn) return <Navigate to="/dang-nhap" replace state={{ from: '/yeu-thich' }} />;

  const total = query.data?.pages[0]?.totalCount ?? 0;

  return (
    <div className="wishlist-page">
      <div className="container">
        <h1 className="wishlist-title">Sản Phẩm Yêu Thích ({total})</h1>

        <QueryState query={query} loading={<ProductGrid title="" products={[]} loading />}
          isEmpty={(d) => d.pages.every((p) => p.items.length === 0)}
          emptyText={
            <div className="wishlist-empty" data-testid="wishlist-empty">
              <div className="wishlist-empty-icon">♡</div>
              <p>Bạn chưa có sản phẩm yêu thích nào</p>
              <Link to="/" className="wishlist-empty-btn">Khám Phá Ngay</Link>
            </div>
          }>
          {(d) => <ProductGrid title="" products={d.pages.flatMap((p) => p.items)} />}
        </QueryState>
        {query.hasNextPage && (
          <div className="home-load-more">
            <button className="home-load-more-btn" onClick={() => void query.fetchNextPage()}>Xem Thêm</button>
          </div>
        )}
      </div>
    </div>
  );
};

export default Wishlist;
