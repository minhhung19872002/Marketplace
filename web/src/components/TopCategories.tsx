import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { formatSold } from '../lib/money';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import './TopCategories.css';

// "Tìm kiếm hàng đầu": the best seller of each busiest category
const TopCategories = () => {
  const { data = [] } = useQuery({ queryKey: ['home', 'top-categories'], queryFn: storefrontApi.topCategories, staleTime: 5 * 60_000 });
  if (data.length === 0) return null;

  return (
    <section className="top-categories">
      <div className="top-categories-header">
        <h2 className="top-categories-title">TÌM KIẾM HÀNG ĐẦU</h2>
      </div>
      <div className="top-categories-list">
        {data.map(({ category, product }) => (
          <Link key={category.id} to={`/danh-muc/${category.slug}`} className="top-category" data-testid="top-category">
            <span className="top-category-badge">TOP</span>
            <img src={imageOrPlaceholder(product.imageUrl)} alt={category.name} loading="lazy" onError={handleImgError} />
            <span className="top-category-sold">Bán {formatSold(product.soldCount)}+ / tháng</span>
            <span className="top-category-name">{category.name}</span>
          </Link>
        ))}
      </div>
    </section>
  );
};

export default TopCategories;
