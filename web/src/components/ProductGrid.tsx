import ProductCard from './ProductCard';
import type { Product } from '../types';
import './ProductGrid.css';

interface ProductGridProps {
  title?: string;
  products: Product[];
}

const ProductGrid = ({ title = 'GỢI Ý HÔM NAY', products }: ProductGridProps) => {
  return (
    <section className="product-grid-section">
      {title && (
        <div className="product-grid-header">
          <h2 className="product-grid-title">{title}</h2>
        </div>
      )}
      {products.length === 0 ? (
        <div className="product-grid-empty">Không tìm thấy sản phẩm nào phù hợp.</div>
      ) : (
        <div className="product-grid">
          {products.map((p) => (
            <ProductCard key={p.id} product={p} />
          ))}
        </div>
      )}
    </section>
  );
};

export default ProductGrid;
