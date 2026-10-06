import { useState } from 'react';
import Banner from '../components/Banner';
import CategoryShortcuts from '../components/CategoryShortcuts';
import CategoryGrid from '../components/CategoryGrid';
import FlashSale from '../components/FlashSale';
import MallBrands from '../components/MallBrands';
import ProductGrid from '../components/ProductGrid';
import { products } from '../data/products';
import './HomePage.css';

const PAGE_SIZE = 24;

const HomePage = () => {
  const [visible, setVisible] = useState(PAGE_SIZE);
  const shown = products.slice(0, visible);
  const hasMore = visible < products.length;

  return (
    <div className="home-page">
      <div className="container">
        <Banner />
        <CategoryShortcuts />
        <CategoryGrid />
        <FlashSale />
        <MallBrands />
        <ProductGrid title="GỢI Ý HÔM NAY" products={shown} />
        {hasMore && (
          <div className="home-load-more">
            <button
              className="home-load-more-btn"
              onClick={() => setVisible((v) => v + PAGE_SIZE)}
              data-testid="load-more"
            >
              Xem Thêm
            </button>
          </div>
        )}
      </div>
    </div>
  );
};

export default HomePage;
