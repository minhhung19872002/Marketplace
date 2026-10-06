import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { banners } from '../data/home';
import './Banner.css';

const Banner = () => {
  const [active, setActive] = useState(0);

  const next = useCallback(() => setActive((i) => (i + 1) % banners.length), []);
  const prev = () => setActive((i) => (i - 1 + banners.length) % banners.length);

  useEffect(() => {
    const timer = setInterval(next, 4000);
    return () => clearInterval(timer);
  }, [next]);

  return (
    <section className="banner" aria-label="Khuyến mãi nổi bật">
      <div className="banner-slider">
        {banners.map((b, i) => (
          <div key={b.id} className={`banner-slide ${b.tone} ${i === active ? 'active' : ''}`}>
            <div className="banner-content">
              <h2 className="banner-title">{b.title}</h2>
              <p className="banner-subtitle">{b.subtitle}</p>
              <Link to={b.to} className="banner-cta">Mua Ngay</Link>
            </div>
          </div>
        ))}

        <button className="banner-arrow banner-arrow-left" onClick={prev} aria-label="Trước">‹</button>
        <button className="banner-arrow banner-arrow-right" onClick={next} aria-label="Sau">›</button>

        <div className="banner-dots">
          {banners.map((b, i) => (
            <button key={b.id} className={`banner-dot ${i === active ? 'active' : ''}`} onClick={() => setActive(i)} aria-label={`Chuyển tới banner ${i + 1}`} />
          ))}
        </div>
      </div>

      <div className="banner-side">
        <Link to="/tim-kiem?mall=true" className="banner-side-item banner-side-item--voucher">
          <span>ShopHub Mall</span>
        </Link>
        <Link to="/tim-kiem?sort=BestSelling" className="banner-side-item banner-side-item--freeship">
          <span>Bán Chạy Nhất</span>
        </Link>
      </div>
    </section>
  );
};

export default Banner;
