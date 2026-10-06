import ProductCard from './ProductCard';
import type { ProductCard as Card } from '../types';
import './ProductGrid.css';

interface ProductGridProps {
  title?: string;
  products: Card[];
  loading?: boolean;
  emptyText?: string;
}

const ProductGrid = ({ title = 'GỢI Ý HÔM NAY', products, loading = false, emptyText = 'Không tìm thấy sản phẩm nào phù hợp.' }: ProductGridProps) => (
  <section className="product-grid-section">
    {title && (
      <div className="product-grid-header">
        <h2 className="product-grid-title">{title}</h2>
      </div>
    )}
    {loading && products.length === 0 ? (
      <div className="product-grid">
        {Array.from({ length: 12 }, (_, i) => (
          <div key={i} className="product-card product-card-skeleton" aria-hidden="true" />
        ))}
      </div>
    ) : products.length === 0 ? (
      <div className="product-grid-empty">{emptyText}</div>
    ) : (
      <div className="product-grid">
        {products.map((p) => (
          <ProductCard key={p.id} product={p} />
        ))}
      </div>
    )}
  </section>
);

export default ProductGrid;
