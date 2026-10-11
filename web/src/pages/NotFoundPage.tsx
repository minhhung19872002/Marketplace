import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import ProductGrid from '../components/ProductGrid';
import QueryState from '../components/QueryState';
import { Section } from '../components/ui';
import { NotFoundArt } from '../components/ui/Illustration';
import { usePageTitle } from '../lib/pageTitle';
import './NotFoundPage.css';

/**
 * "Không tìm thấy trang" (F5): every address the site does not have, with a way back — search, the home page, and the
 * day's suggestions under it so the visitor has something to open (G5-B).
 */
const NotFoundPage = () => {
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  usePageTitle('Không tìm thấy trang');
  const suggestions = useQuery({ queryKey: ['not-found-suggestions'], queryFn: () => storefrontApi.recommendations(1, 12), staleTime: 60_000 });
  const search = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (q.trim()) navigate(`/tim-kiem?q=${encodeURIComponent(q.trim())}`);
  };
  return (
    <div className="container not-found-page">
      <div className="not-found" data-testid="not-found">
        <NotFoundArt />
        <span className="not-found-code">Lỗi 404</span>
        <h1>Không tìm thấy trang</h1>
        <p>Đường dẫn có thể đã bị gõ sai hoặc trang đã được gỡ. Bạn thử tìm sản phẩm cần mua nhé.</p>
        <form className="not-found-search" role="search" onSubmit={search}>
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Tìm sản phẩm, thương hiệu, shop" aria-label="Tìm kiếm" data-testid="not-found-search" />
          <button type="submit">Tìm kiếm</button>
        </form>
        <Link to="/" className="not-found-home">Về trang chủ</Link>
      </div>
      <QueryState query={suggestions} loading={null} isEmpty={(d) => d.items.length === 0} emptyText={null}>
        {(d) => (
          <Section title="Gợi ý hôm nay" className="sh-section--caps not-found-suggest" testId="not-found-suggestions" more={{ to: '/' }}>
            <div className="not-found-suggest-body"><ProductGrid title="" products={d.items} /></div>
          </Section>
        )}
      </QueryState>
    </div>
  );
};

export default NotFoundPage;
