import React from 'react';
import { Link } from 'react-router-dom';
import { featureShortcuts } from '../data/products';
import './CategoryShortcuts.css';

// Dãy tính năng nhanh (circular icons) - như trang chủ Shopee
const CategoryShortcuts = () => {
  return (
    <section className="feature-shortcuts">
      <div className="feature-shortcuts-grid">
        {featureShortcuts.map((f) => (
          <Link
            key={f.id}
            to="/tim-kiem"
            className="feature-shortcut"
            data-testid="feature-shortcut"
          >
            <span className="feature-shortcut-icon" style={{ background: f.color }}>
              {f.icon}
            </span>
            <span className="feature-shortcut-label">{f.label}</span>
          </Link>
        ))}
      </div>
    </section>
  );
};

export default CategoryShortcuts;
