import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Trash2 } from 'lucide-react';
import Banner from '../components/Banner';
import CategoryShortcuts from '../components/CategoryShortcuts';
import CategoryGrid from '../components/CategoryGrid';
import MallBrands from '../components/MallBrands';
import TopCategories from '../components/TopCategories';
import ProductCard from '../components/ProductCard';
import QueryState from '../components/QueryState';
import FlashSaleBlock from '../components/FlashSaleBlock';
import HomePopup from '../components/HomePopup';
import { ConfirmButton } from '../components/ConfirmDialog';
import Carousel from '../components/ui/Carousel';
import { ProductGridSkeleton, Section } from '../components/ui';
import { storefrontApi } from '../api/storefront';
import { useAuth } from '../context/AuthContext';
import './HomePage.css';
import '../components/ProductGrid.css';

const PAGE_SIZE = 24;

/** "Đã xem gần đây" (G2-B1): one sliding row with "Xoá lịch sử"; hidden when there is nothing yet. */
const RecentlyViewed = ({ isLoggedIn }: { isLoggedIn: boolean }) => {
  const queryClient = useQueryClient();
  const viewed = useQuery({ queryKey: ['viewed', isLoggedIn], queryFn: storefrontApi.viewed });
  const clear = useMutation({
    mutationFn: storefrontApi.clearViewed,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['viewed'] }),
  });
  const items = viewed.data ?? [];
  if (items.length === 0) return null;
  return (
    <Section title="Sản phẩm đã xem" testId="viewed-section" className="home-viewed"
      extra={<ConfirmButton className="home-viewed-clear" message="Xoá toàn bộ lịch sử sản phẩm đã xem?" confirmLabel="Xoá"
        onConfirm={() => clear.mutate()} disabled={clear.isPending} testId="viewed-clear">
        <Trash2 size={14} aria-hidden /> Xoá lịch sử
      </ConfirmButton>}>
      <div className="home-row">
        <Carousel label="Sản phẩm đã xem">{items.map((p) => <ProductCard key={p.id} product={p} />)}</Carousel>
      </div>
    </Section>
  );
};

const HomePage = () => {
  const { isLoggedIn } = useAuth();
  const recommendations = useInfiniteQuery({
    queryKey: ['home', 'recommendations', isLoggedIn],
    queryFn: ({ pageParam }) => storefrontApi.recommendations(pageParam, PAGE_SIZE),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined),
  });

  return (
    <div className="home-page">
      <HomePopup />
      <div className="container">
        <Banner />
        <CategoryShortcuts />
        <FlashSaleBlock />
        <CategoryGrid />
        <MallBrands />
        <TopCategories />
        <RecentlyViewed isLoggedIn={isLoggedIn} />

        {/* "Gợi ý hôm nay": the title bar stays under the header while the grid scrolls (G2-B1) */}
        <section className="home-daily" aria-labelledby="home-daily-title">
          <div className="home-daily-bar">
            <h2 id="home-daily-title" className="home-daily-title">Gợi ý hôm nay</h2>
          </div>
          <QueryState query={recommendations} loading={<ProductGridSkeleton count={12} />}>
            {(d) => (
              <div className="product-grid" data-testid="daily-grid">
                {d.pages.flatMap((p) => p.items).map((p) => <ProductCard key={p.id} product={p} />)}
              </div>
            )}
          </QueryState>
          {recommendations.isFetchingNextPage && <ProductGridSkeleton count={6} />}
          {recommendations.hasNextPage && (
            <div className="home-load-more">
              <button
                className="home-load-more-btn"
                onClick={() => void recommendations.fetchNextPage()}
                disabled={recommendations.isFetchingNextPage}
                data-testid="load-more"
              >
                {recommendations.isFetchingNextPage ? 'Đang tải…' : 'Xem thêm'}
              </button>
            </div>
          )}
        </section>
      </div>
    </div>
  );
};

export default HomePage;
