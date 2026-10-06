import React from 'react';
import { Link } from 'react-router-dom';
import { categories } from '../data/products';
import './CategoryGrid.css';

const CategoryGrid = () => {
  return (
    <section className="category-grid-section">
      <h2 className="section-title">DANH MỤC</h2>
      <div className="category-grid">
        {categories.map((cat) => (
          <Link
            key={cat.id}
            to={`/tim-kiem?category=${cat.id}`}
            className="category-item"
            data-testid="category-item"
          >
            <span className="category-icon">{cat.icon}</span>
            <span className="category-name">{cat.name}</span>
          </Link>
        ))}
      </div>
    </section>
  );
};

export default CategoryGrid;
