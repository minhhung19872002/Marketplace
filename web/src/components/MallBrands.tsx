import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import ProductCard from './ProductCard';
import './MallBrands.css';

// Official stores (Mall shops) with their newest product photo as cover, then the best-selling Mall products (II.1, E3)
const MallBrands = () => {
  const { data: shops = [] } = useQuery({ queryKey: ['home', 'mall'], queryFn: storefrontApi.mall, staleTime: 5 * 60_000 });
  const products = useQuery({
    queryKey: ['home', 'mall-products'],
    queryFn: () => storefrontApi.search({ mall: true, sort: 'BestSelling', page: 1, pageSize: 12 }),
    staleTime: 5 * 60_000,
  });
  if (shops.length === 0) return null;

  return (
    <section className="mall-brands">
      <div className="mall-brands-header">
        <h2 className="mall-brands-title">
          <span className="mall-brands-badge">Mall</span> THƯƠNG HIỆU CHÍNH HÃNG
        </h2>
        <Link to="/tim-kiem?mall=true" className="mall-brands-more">Xem tất cả ›</Link>
      </div>
      <div className="mall-brands-grid">
        {shops.map((s) => (
          <Link key={s.id} to={`/shop/${s.slug}`} className="mall-brand" data-testid="mall-brand">
            <img src={imageOrPlaceholder(s.logoUrl ?? s.coverImageUrl)} alt={s.name} loading="lazy" onError={handleImgError} />
            <span className="mall-brand-name">{s.name}</span>
          </Link>
        ))}
      </div>
      {(products.data?.items.length ?? 0) > 0 && (
        <div className="mall-products" data-testid="mall-products">
          {products.data!.items.map((p) => <ProductCard key={p.id} product={p} />)}
        </div>
      )}
    </section>
  );
};

export default MallBrands;
