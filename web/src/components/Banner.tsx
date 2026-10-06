import { useState, useEffect, useCallback } from 'react';
import { banners } from '../data/products';
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
          <div
            key={b.id}
            className={`banner-slide ${i === active ? 'active' : ''}`}
            style={{ background: b.bg }}
          >
            <div className="banner-content">
              <h2 className="banner-title">{b.title}</h2>
              <p className="banner-subtitle">{b.subtitle}</p>
              <button className="banner-cta">Mua Ngay</button>
            </div>
          </div>
        ))}

        <button className="banner-arrow banner-arrow-left" onClick={prev} aria-label="Trước">‹</button>
        <button className="banner-arrow banner-arrow-right" onClick={next} aria-label="Sau">›</button>

        <div className="banner-dots">
          {banners.map((b, i) => (
            <button
              key={b.id}
              className={`banner-dot ${i === active ? 'active' : ''}`}
              onClick={() => setActive(i)}
              aria-label={`Chuyển tới banner ${i + 1}`}
            />
          ))}
        </div>
      </div>

      <div className="banner-side">
        <div className="banner-side-item" style={{ background: 'linear-gradient(135deg,#7b4397,#dc2430)' }}>
          <span>Mã Giảm 50%</span>
        </div>
        <div className="banner-side-item" style={{ background: 'linear-gradient(135deg,#11998e,#38ef7d)' }}>
          <span>Freeship Xtra</span>
        </div>
      </div>
    </section>
  );
};

export default Banner;
