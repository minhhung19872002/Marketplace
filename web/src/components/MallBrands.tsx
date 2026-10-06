import { Link } from 'react-router-dom';
import { mallBrands, handleImgError } from '../data/products';
import { BRAND_IMAGES } from '../data/images';
import './MallBrands.css';

const brandFallback = 'data:image/svg+xml;utf8,' +
  encodeURIComponent(
    "<svg xmlns='http://www.w3.org/2000/svg' width='120' height='120'><rect width='120' height='120' fill='#fbe9e7'/></svg>"
  );

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
              src={BRAND_IMAGES[i] || brandFallback}
              alt={b.name}
              loading="lazy"
              onError={(e) => handleImgError(e, brandFallback)}
            />
            <span className="mall-brand-name">{b.name}</span>
          </Link>
        ))}
      </div>
    </section>
  );
};

export default MallBrands;
