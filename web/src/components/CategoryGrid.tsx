import type { CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import CategoryIcon from './CategoryIcon';
import Carousel from './ui/Carousel';
import './CategoryGrid.css';

export const useCategoryTree = () => useQuery({ queryKey: ['categories'], queryFn: storefrontApi.categories, staleTime: 5 * 60_000 });

/** "DANH MỤC" (G2-B1): two rows that scroll sideways with arrows when they overflow; every icon tile the same size. */
const CategoryGrid = () => {
  const { data: tree = [], isPending } = useCategoryTree();
  // Holds its height while loading (CLS, G3); the real grid is two rows of fixed-size tiles
  if (isPending) return <section className="category-grid-section category-grid-section--loading" aria-hidden />;
  // Industries hidden from buyers or with nothing on sale stay out of the grid (an empty page is a dead end)
  const top = tree.filter((c) => c.isActive && c.isVisible && c.productCount > 0);
  if (top.length === 0) return null;

  return (
    <section className="category-grid-section">
      <div className="category-grid-head">
        <h2 className="sh-section-title">Danh mục</h2>
      </div>
      {/* Two rows: as many columns as needed up to 10 per screen, so a short list still fills the width */}
      <Carousel label="Danh mục" rows={2} className="category-grid"
        style={{ '--category-cols': Math.min(10, Math.ceil(top.length / 2)) } as CSSProperties}>
        {top.map((cat) => (
          <Link key={cat.id} to={`/danh-muc/${cat.slug}`} className="category-item" data-testid="category-item">
            <span className="category-icon"><CategoryIcon icon={cat.iconUrl} size={30} /></span>
            <span className="category-name">{cat.name}</span>
          </Link>
        ))}
      </Carousel>
    </section>
  );
};

export default CategoryGrid;
