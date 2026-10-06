import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { flashSaleProducts, formatPrice, handleImgError } from '../data/products';
import './FlashSale.css';

// Đếm ngược tới cuối khung giờ (mốc tiếp theo theo 2 tiếng)
const getSecondsToNextSlot = () => {
  const now = new Date();
  const next = new Date(now);
  const nextHour = Math.ceil((now.getHours() + 1) / 2) * 2;
  next.setHours(nextHour, 0, 0, 0);
  return Math.max(1, Math.floor((next - now) / 1000));
};

const pad = (n) => String(n).padStart(2, '0');

const FlashSale = () => {
  const [seconds, setSeconds] = useState(getSecondsToNextSlot);

  useEffect(() => {
    const timer = setInterval(() => {
      setSeconds((s) => (s <= 1 ? getSecondsToNextSlot() : s - 1));
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;

  return (
    <section className="flash-sale">
      <div className="flash-sale-header">
        <div className="flash-sale-title">
          <span className="flash-sale-logo">⚡ FLASH SALE</span>
          <div className="flash-sale-countdown" aria-label="Thời gian còn lại">
            <span className="countdown-box">{pad(h)}</span>
            <span className="countdown-sep">:</span>
            <span className="countdown-box">{pad(m)}</span>
            <span className="countdown-sep">:</span>
            <span className="countdown-box">{pad(s)}</span>
          </div>
        </div>
        <Link to="/tim-kiem?q=flash-sale" className="flash-sale-more">Xem tất cả ›</Link>
      </div>

      <div className="flash-sale-list">
        {flashSaleProducts.map((p) => {
          const soldPercent = Math.min(
            100,
            Math.round((p.flashSold / (p.flashSold + p.flashStock)) * 100)
          );
          return (
            <Link key={p.id} to={`/san-pham/${p.id}`} className="flash-item" data-testid="flash-item">
              <div className="flash-item-img">
                <img
                  src={p.image}
                  alt={p.name}
                  loading="lazy"
                  onError={(e) => handleImgError(e, p.fallbackImage)}
                />
                <span className="flash-item-discount">-{p.discount}%</span>
              </div>
              <div className="flash-item-price">{formatPrice(p.price)}</div>
              <div className="flash-item-bar">
                <div className="flash-item-bar-fill" style={{ width: `${soldPercent}%` }} />
                <span className="flash-item-bar-text">
                  {p.flashSold > 60 ? 'Đang bán chạy' : `Đã bán ${p.flashSold}`}
                </span>
              </div>
            </Link>
          );
        })}
      </div>
    </section>
  );
};

export default FlashSale;
