import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { contentApi, type HelpItem } from '../api/content';
import { formatDate } from '../lib/datetime';
import './ContentPages.css';

/** /trang/:slug and /tro-giup/:slug — a static page or help article edited by the platform (HTML sanitised server-side). */
export const CmsPageView = () => {
  const { slug = '' } = useParams();
  const page = useQuery({ queryKey: ['cms', slug], queryFn: () => contentApi.page(slug), retry: false });
  if (page.isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!page.data) {
    return (
      <div className="container content-page">
        <p>Không tìm thấy trang.</p>
        <Link to="/tro-giup">Về Trung tâm trợ giúp</Link>
      </div>
    );
  }
  return (
    <div className="container content-page">
      <nav className="content-crumbs">
        <Link to="/">Trang chủ</Link> › {page.data.kind === 'Help' ? <><Link to="/tro-giup">Trung tâm trợ giúp</Link> › </> : null}{page.data.title}
      </nav>
      <article className="content-article" data-testid="cms-page">
        <h1>{page.data.title}</h1>
        <div className="content-updated">Cập nhật {formatDate(page.data.updatedAt)}</div>
        <div dangerouslySetInnerHTML={{ __html: page.data.content }} />
      </article>
    </div>
  );
};

/** /tro-giup — articles by topic, search without tones, links to the legal pages. */
export const HelpCenter = () => {
  const [q, setQ] = useState('');
  const [term, setTerm] = useState('');
  const items = useQuery({ queryKey: ['help', term], queryFn: () => contentApi.help(term) });
  const help = (items.data ?? []).filter((i) => i.kind === 'Help');
  const pages = (items.data ?? []).filter((i) => i.kind === 'Page');
  const topics = help.reduce<Record<string, HelpItem[]>>((acc, i) => {
    (acc[i.topic ?? 'Khác'] ??= []).push(i);
    return acc;
  }, {});
  return (
    <div className="container content-page">
      <div className="help-hero">
        <h1>Xin chào, ShopHub có thể giúp gì cho bạn?</h1>
        <form onSubmit={(e) => { e.preventDefault(); setTerm(q.trim()); }}>
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Nhập từ khoá, ví dụ: huỷ đơn, hoàn tiền" aria-label="Tìm câu hỏi" data-testid="help-search" />
          <button type="submit">Tìm</button>
        </form>
      </div>
      {items.data && help.length === 0 && pages.length === 0 && <p className="help-empty">Không tìm thấy câu hỏi phù hợp.</p>}
      <div className="help-topics">
        {Object.entries(topics).map(([topic, list]) => (
          <section key={topic} className="help-topic">
            <h2>{topic}</h2>
            <ul>{list.map((i) => <li key={i.slug}><Link to={`/tro-giup/${i.slug}`}>{i.title}</Link></li>)}</ul>
          </section>
        ))}
      </div>
      {pages.length > 0 && (
        <section className="help-topic">
          <h2>Chính sách & quy định</h2>
          <ul>{pages.map((i) => <li key={i.slug}><Link to={`/trang/${i.slug}`}>{i.title}</Link></li>)}</ul>
        </section>
      )}
    </div>
  );
};
