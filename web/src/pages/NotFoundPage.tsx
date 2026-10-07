import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { usePageTitle } from '../lib/pageTitle';
import './NotFoundPage.css';

/** "Không tìm thấy trang" (F5): every address the site does not have, with a way back — search or the home page. */
const NotFoundPage = () => {
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  usePageTitle('Không tìm thấy trang');
  const search = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (q.trim()) navigate(`/tim-kiem?q=${encodeURIComponent(q.trim())}`);
  };
  return (
    <div className="container not-found" data-testid="not-found">
      <div className="not-found-code" aria-hidden="true">404</div>
      <h1>Không tìm thấy trang</h1>
      <p>Đường dẫn có thể đã bị gõ sai hoặc trang đã được gỡ. Bạn thử tìm sản phẩm cần mua nhé.</p>
      <form className="not-found-search" role="search" onSubmit={search}>
        <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Tìm sản phẩm, thương hiệu, shop" aria-label="Tìm kiếm" data-testid="not-found-search" />
        <button type="submit">Tìm kiếm</button>
      </form>
      <Link to="/" className="not-found-home">Về trang chủ</Link>
    </div>
  );
};

export default NotFoundPage;
