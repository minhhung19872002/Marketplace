import { products } from '../data/products';
import ProductGrid from '../components/ProductGrid';
import './ShopPage.css';

const ShopPage = () => {
  // Shop demo: lấy các sản phẩm của "ShopHub Official Store"
  const shopProducts = products.filter((p) => p.shopName === 'ShopHub Official Store');
  const list = shopProducts.length ? shopProducts : products.slice(0, 12);

  return (
    <div className="shop-page">
      <div className="shop-banner">
        <div className="container shop-banner-inner">
          <div className="shop-avatar">S</div>
          <div className="shop-info">
            <div className="shop-name">
              ShopHub Official Store <span className="shop-badge">Mall</span>
            </div>
            <div className="shop-online">Online 5 phút trước</div>
            <div className="shop-actions">
              <button className="shop-follow">+ Theo Dõi</button>
              <button className="shop-chat">💬 Chat</button>
            </div>
          </div>
          <div className="shop-stats">
            <div><strong>{list.length}</strong><span>Sản Phẩm</span></div>
            <div><strong>4.9★</strong><span>Đánh Giá</span></div>
            <div><strong>98%</strong><span>Phản Hồi Chat</span></div>
            <div><strong>15k</strong><span>Người Theo Dõi</span></div>
          </div>
        </div>
      </div>

      <div className="container">
        <div className="shop-section-head">TẤT CẢ SẢN PHẨM</div>
        <ProductGrid title="" products={list} />
      </div>
    </div>
  );
};

export default ShopPage;
