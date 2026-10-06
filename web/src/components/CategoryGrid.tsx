import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { isImageUrl } from '../lib/image';
import './CategoryGrid.css';

export const useCategoryTree = () => useQuery({ queryKey: ['categories'], queryFn: storefrontApi.categories, staleTime: 5 * 60_000 });

const CategoryGrid = () => {
  const { data: tree = [] } = useCategoryTree();
  const top = tree.filter((c) => c.isActive);
  if (top.length === 0) return null;

  return (
    <section className="category-grid-section">
      <h2 className="section-title">DANH MỤC</h2>
      <div className="category-grid">
        {top.map((cat) => (
          <Link key={cat.id} to={`/danh-muc/${cat.slug}`} className="category-item" data-testid="category-item">
            <span className="category-icon">{isImageUrl(cat.iconUrl) ? <img src={cat.iconUrl} alt="" /> : cat.iconUrl ?? '🛍️'}</span>
            <span className="category-name">{cat.name}</span>
          </Link>
        ))}
      </div>
    </section>
  );
};

export default CategoryGrid;
