import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import './MallBrands.css';

// Official stores (Mall shops) with their newest product photo as cover
const MallBrands = () => {
  const { data: shops = [] } = useQuery({ queryKey: ['home', 'mall'], queryFn: storefrontApi.mall, staleTime: 5 * 60_000 });
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
    </section>
  );
};

export default MallBrands;
