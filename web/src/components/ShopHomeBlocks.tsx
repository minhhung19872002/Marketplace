import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import ProductGrid from './ProductGrid';
import { handleImgError } from '../lib/image';
import type { ShopHomeBlock } from '../types';

/** A shop's banner block: one image at a time, rotating, with dots to pick one. */
const ShopBanner = ({ block }: { block: ShopHomeBlock }) => {
  const [index, setIndex] = useState(0);
  const count = block.images.length;
  useEffect(() => {
    if (count < 2) return undefined;
    const id = window.setInterval(() => setIndex((i) => (i + 1) % count), 5000);
    return () => window.clearInterval(id);
  }, [count]);
  const image = block.images[Math.min(index, count - 1)];
  if (!image) return null;
  const img = <img src={image.url} alt={block.title ?? 'Banner của shop'} onError={handleImgError} />;
  return (
    <section className="shop-block shop-block-banner" data-testid="shop-block">
      {image.link ? <Link to={image.link}>{img}</Link> : img}
      {count > 1 && (
        <div className="shop-block-dots">
          {block.images.map((_, i) => (
            <button key={i} className={i === index ? 'active' : ''} onClick={() => setIndex(i)} aria-label={`Ảnh ${i + 1}`} />
          ))}
        </div>
      )}
    </section>
  );
};

/** The "Dạo" tab of a shop page: the blocks the seller arranged in Kênh Người Bán → Trang trí shop. */
const ShopHomeBlocks = ({ blocks, onOpenCategory }: { blocks: ShopHomeBlock[]; onOpenCategory: (id: string) => void }) => (
  <div className="shop-home">
    {blocks.map((b, i) => {
      switch (b.type) {
        case 'Banner':
          return <ShopBanner key={i} block={b} />;
        case 'Products':
          return (
            <section key={i} className="shop-block" data-testid="shop-block">
              <ProductGrid title={b.title ?? 'Sản phẩm nổi bật'} products={b.products} />
            </section>
          );
        case 'Category':
          return (
            <section key={i} className="shop-block" data-testid="shop-block">
              <ProductGrid title={b.title ?? ''} products={b.products} />
              {b.shopCategoryId && (
                <button className="shop-block-more" onClick={() => onOpenCategory(b.shopCategoryId!)}>Xem tất cả ›</button>
              )}
            </section>
          );
        case 'Video':
          return b.videoUrl ? (
            <section key={i} className="shop-block shop-block-video" data-testid="shop-block">
              {b.title && <h2 className="shop-block-title">{b.title}</h2>}
              <video src={b.videoUrl} controls preload="metadata" />
            </section>
          ) : null;
        default:
          return (
            <section key={i} className="shop-block shop-block-text" data-testid="shop-block">
              {b.title && <h2 className="shop-block-title">{b.title}</h2>}
              <p>{b.text}</p>
            </section>
          );
      }
    })}
  </div>
);

export default ShopHomeBlocks;
