import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import CategoryIcon from './CategoryIcon';
import './CategoryGrid.css';

export const useCategoryTree = () => useQuery({ queryKey: ['categories'], queryFn: storefrontApi.categories, staleTime: 5 * 60_000 });

const CategoryGrid = () => {
  const { data: tree = [] } = useCategoryTree();
  // Industries with nothing on sale stay out of the home grid (an empty page is a dead end)
  const top = tree.filter((c) => c.isActive && c.productCount > 0);
  if (top.length === 0) return null;

  return (
    <section className="category-grid-section">
      <h2 className="section-title">DANH MỤC</h2>
      <div className="category-grid">
        {top.map((cat) => (
          <Link key={cat.id} to={`/danh-muc/${cat.slug}`} className="category-item" data-testid="category-item">
            <span className="category-icon"><CategoryIcon icon={cat.iconUrl} /></span>
            <span className="category-name">{cat.name}</span>
          </Link>
        ))}
      </div>
    </section>
  );
};

export default CategoryGrid;
