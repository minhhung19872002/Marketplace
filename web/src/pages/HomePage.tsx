import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import Banner from '../components/Banner';
import CategoryShortcuts from '../components/CategoryShortcuts';
import CategoryGrid from '../components/CategoryGrid';
import MallBrands from '../components/MallBrands';
import TopCategories from '../components/TopCategories';
import ProductGrid from '../components/ProductGrid';
import { storefrontApi } from '../api/storefront';
import { useAuth } from '../context/AuthContext';
import './HomePage.css';

const PAGE_SIZE = 24;

// Flash Sale returns in Phase 9 (marketing) with real campaigns
const HomePage = () => {
  const { isLoggedIn } = useAuth();
  const recommendations = useInfiniteQuery({
    queryKey: ['home', 'recommendations', isLoggedIn],
    queryFn: ({ pageParam }) => storefrontApi.recommendations(pageParam, PAGE_SIZE),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined),
  });
  const viewed = useQuery({ queryKey: ['viewed', isLoggedIn], queryFn: storefrontApi.viewed });

  const shown = recommendations.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="home-page">
      <div className="container">
        <Banner />
        <CategoryShortcuts />
        <CategoryGrid />
        <MallBrands />
        <TopCategories />
        {(viewed.data?.length ?? 0) > 0 && <ProductGrid title="SẢN PHẨM ĐÃ XEM" products={viewed.data!.slice(0, 6)} />}
        <ProductGrid title="GỢI Ý HÔM NAY" products={shown} loading={recommendations.isLoading} />
        {recommendations.hasNextPage && (
          <div className="home-load-more">
            <button
              className="home-load-more-btn"
              onClick={() => void recommendations.fetchNextPage()}
              disabled={recommendations.isFetchingNextPage}
              data-testid="load-more"
            >
              {recommendations.isFetchingNextPage ? 'Đang tải…' : 'Xem Thêm'}
            </button>
          </div>
        )}
      </div>
    </div>
  );
};

export default HomePage;
