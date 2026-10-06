import React from 'react';
import { Link } from 'react-router-dom';
import { useWishlist } from '../context/WishlistContext';
import { products } from '../data/products';
import ProductGrid from '../components/ProductGrid';
import './Wishlist.css';

const Wishlist = () => {
  const { ids } = useWishlist();
  const liked = products.filter((p) => ids.includes(p.id));

  return (
    <div className="wishlist-page">
      <div className="container">
        <h1 className="wishlist-title">Sản Phẩm Yêu Thích ({liked.length})</h1>

        {liked.length === 0 ? (
          <div className="wishlist-empty" data-testid="wishlist-empty">
            <div className="wishlist-empty-icon">♡</div>
            <p>Bạn chưa có sản phẩm yêu thích nào</p>
            <Link to="/" className="wishlist-empty-btn">Khám Phá Ngay</Link>
          </div>
        ) : (
          <ProductGrid title="" products={liked} />
        )}
      </div>
    </div>
  );
};

export default Wishlist;
