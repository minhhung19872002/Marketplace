import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { formatSold } from '../lib/money';
import { handleImgError, imageOrPlaceholder, imageSrcSet } from '../lib/image';
import Carousel from './ui/Carousel';
import { Section } from './ui';
import './TopCategories.css';

/** The sales line of a top tile: units of the last 30 days when there are any ("Bán 23+ / tháng"), else the total. */
export const topSoldLine = (monthlySold: number, soldCount: number): string =>
  monthlySold > 0 ? `Bán ${formatSold(monthlySold)}+ / tháng` : `Đã bán ${formatSold(soldCount)}`;

// "Tìm kiếm hàng đầu" (G2-B1): one sliding row, 6 tiles per view — the best seller of each industry
const TopCategories = () => {
  const { data = [], isPending } = useQuery({ queryKey: ['home', 'top-categories'], queryFn: storefrontApi.topCategories, staleTime: 5 * 60_000 });
  // While loading, the same row of square tiles holds the place (CLS, G3)
  if (isPending) {
    return (
      <Section title="Tìm kiếm hàng đầu" className="top-categories" testId="top-categories-loading">
        <div className="top-categories-row" aria-hidden>
          <Carousel label="Tìm kiếm hàng đầu">
            {Array.from({ length: 6 }, (_, i) => (
              <span key={i} className="top-category">
                <span className="top-category-image sh-skeleton" />
                <span className="top-category-name">&nbsp;</span>
              </span>
            ))}
          </Carousel>
        </div>
      </Section>
    );
  }
  if (data.length === 0) return null;

  return (
    <Section title="Tìm kiếm hàng đầu" more={{ to: '/tim-kiem?sort=BestSelling' }} className="top-categories">
      <div className="top-categories-row">
        <Carousel label="Tìm kiếm hàng đầu">
          {data.map(({ category, product, monthlySold }) => (
            <Link key={category.id} to={`/danh-muc/${category.slug}`} className="top-category" data-testid="top-category">
              <span className="top-category-image">
                <span className="top-category-badge">TOP</span>
                <img src={imageOrPlaceholder(product.imageUrl)} srcSet={imageSrcSet(product.imageUrl)} sizes="200px" alt={category.name} loading="lazy" width={200} height={200} onError={handleImgError} />
                <span className="top-category-sold">{topSoldLine(monthlySold, product.soldCount)}</span>
              </span>
              <span className="top-category-name">{category.name}</span>
            </Link>
          ))}
        </Carousel>
      </div>
    </Section>
  );
};

export default TopCategories;
