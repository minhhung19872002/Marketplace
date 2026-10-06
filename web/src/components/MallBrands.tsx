import { Link } from 'react-router-dom';
import { mallBrands, brandPlaceholder, handleImgError } from '../data/products';
import { BRAND_IMAGES } from '../data/images';
import './MallBrands.css';

// Khu thương hiệu chính hãng - như "ShopHub Mall"
const MallBrands = () => {
  return (
    <section className="mall-brands">
      <div className="mall-brands-header">
        <h2 className="mall-brands-title">
          <span className="mall-brands-badge">Mall</span> THƯƠNG HIỆU CHÍNH HÃNG
        </h2>
        <Link to="/tim-kiem" className="mall-brands-more">Xem tất cả ›</Link>
      </div>
      <div className="mall-brands-grid">
        {mallBrands.map((b, i) => (
          <Link
            key={b.id}
            to="/tim-kiem"
            className="mall-brand"
            data-testid="mall-brand"
          >
            <img
              src={BRAND_IMAGES[i] || brandPlaceholder}
              alt={b.name}
              loading="lazy"
              onError={(e) => handleImgError(e, brandPlaceholder)}
            />
            <span className="mall-brand-name">{b.name}</span>
          </Link>
        ))}
      </div>
    </section>
  );
};

export default MallBrands;
