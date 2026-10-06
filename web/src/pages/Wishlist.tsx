import { Link, Navigate } from 'react-router-dom';
import { useInfiniteQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { useAuth } from '../context/AuthContext';
import ProductGrid from '../components/ProductGrid';
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

  const items = query.data?.pages.flatMap((p) => p.items) ?? [];
  const total = query.data?.pages[0]?.totalCount ?? 0;

  return (
    <div className="wishlist-page">
      <div className="container">
        <h1 className="wishlist-title">Sản Phẩm Yêu Thích ({total})</h1>

        {!query.isLoading && items.length === 0 ? (
          <div className="wishlist-empty" data-testid="wishlist-empty">
            <div className="wishlist-empty-icon">♡</div>
            <p>Bạn chưa có sản phẩm yêu thích nào</p>
            <Link to="/" className="wishlist-empty-btn">Khám Phá Ngay</Link>
          </div>
        ) : (
          <ProductGrid title="" products={items} loading={query.isLoading} />
        )}
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
