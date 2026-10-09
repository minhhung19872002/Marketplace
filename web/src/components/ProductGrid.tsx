import ProductCard from './ProductCard';
import { EmptyState, ProductGridSkeleton } from './ui';
import type { ProductCard as Card } from '../types';
import './ProductGrid.css';

interface ProductGridProps {
  title?: string;
  products: Card[];
  loading?: boolean;
  emptyText?: string;
  // The campaign's own page draws the campaign frame over the photos (G3 B3)
  showFrames?: boolean;
  // Search / category results keep the stars, struck price and province on the card (G-VIS)
  variant?: 'compact' | 'detailed';
}

const ProductGrid = ({ title = 'Gợi ý hôm nay', products, loading = false, emptyText = 'Không tìm thấy sản phẩm nào phù hợp.', showFrames = false, variant = 'compact' }: ProductGridProps) => (
  <section className="product-grid-section">
    {title && (
      <div className="product-grid-header">
        <h2 className="product-grid-title">{title}</h2>
      </div>
    )}
    {loading && products.length === 0 ? (
      <ProductGridSkeleton count={12} />
    ) : products.length === 0 ? (
      <div className="product-grid-empty"><EmptyState title={emptyText} /></div>
    ) : (
      <div className="product-grid">
        {products.map((p) => (
          <ProductCard key={p.id} product={p} showFrame={showFrames} variant={variant} />
        ))}
      </div>
    )}
  </section>
);

export default ProductGrid;
