import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ChevronDown, ListTree } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { contentApi, type HelpItem } from '../api/content';
import { formatDate } from '../lib/datetime';
import { ApiError } from '../api/http';
import { removeTones } from '../lib/text';
import QueryState from '../components/QueryState';
import './ContentPages.css';

/** "3. Giao dịch" → "3-giao-dich": anchor id of a heading. */
export const anchorId = (text: string): string =>
  removeTones(text).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'muc';

export interface TocEntry { id: string; text: string; level: 2 | 3 }

/**
 * Table of contents of a static page (G4-B): every h2 / h3 of the (server-sanitised) HTML gets a unique id, and the
 * list of them feeds the side menu. Only ids are added — the markup is otherwise unchanged.
 */
export const withToc = (html: string): { html: string; toc: TocEntry[] } => {
  if (typeof DOMParser === 'undefined') return { html, toc: [] };
  const doc = new DOMParser().parseFromString(`<div>${html}</div>`, 'text/html');
  const used = new Set<string>();
  const toc: TocEntry[] = [];
  doc.querySelectorAll('h2, h3').forEach((h) => {
    const text = (h.textContent ?? '').trim();
    if (!text) return;
    let id = anchorId(text);
    for (let n = 2; used.has(id); n++) id = `${anchorId(text)}-${n}`;
    used.add(id);
    h.id = id;
    toc.push({ id, text, level: h.tagName === 'H3' ? 3 : 2 });
  });
  return { html: doc.body.firstElementChild?.innerHTML ?? html, toc };
};

/** Side menu of a reading page: sticky on desktop, a collapsible "Mục lục" on phones. */
const Toc = ({ entries, title = 'Mục lục' }: { entries: { id: string; text: string; level?: 2 | 3 }[]; title?: string }) => {
  // Open on desktop (where it cannot be folded), folded on phones so the text comes first
  const [open] = useState(() => typeof window === 'undefined' || window.matchMedia('(min-width: 768px)').matches);
  return (
  <aside className="content-toc" data-testid="content-toc">
    <details className="content-toc-box" open={open}>
      <summary><ListTree size={16} aria-hidden /> {title}<ChevronDown className="content-toc-chevron" size={16} aria-hidden /></summary>
      <nav aria-label={title}>
        <ol>
          {entries.map((e) => (
            <li key={e.id} className={e.level === 3 ? 'is-sub' : undefined}><a href={`#${e.id}`}>{e.text}</a></li>
          ))}
        </ol>
      </nav>
    </details>
  </aside>
  );
};

/** /trang/:slug and /tro-giup/:slug — a static page or help article edited by the platform (HTML sanitised server-side). */
export const CmsPageView = () => {
  const { slug = '' } = useParams();
  const page = useQuery({ queryKey: ['cms', slug], queryFn: () => contentApi.page(slug), retry: false });
  const body = useMemo(() => withToc(page.data?.content ?? ''), [page.data?.content]);
  // Loading, or a network / server error (not "no such page"): spinner or the error with "Thử lại" (F3)
  if (page.isPending || (page.isError && !(page.error instanceof ApiError && page.error.status === 404))) {
    return <div className="container content-page"><QueryState query={page}>{() => null}</QueryState></div>;
  }
  if (!page.data) {
    return (
      <div className="container content-page">
        <p>Không tìm thấy trang.</p>
        <Link to="/tro-giup">Về trung tâm trợ giúp</Link>
      </div>
    );
  }
  // Easy reading (G4-B): a ~72ch text column, the table of contents at its left when the page has 2+ headings
  const hasToc = body.toc.length >= 2;
  return (
    <div className="container content-page">
      <nav className="content-crumbs" aria-label="Đường dẫn">
        <Link to="/">Trang chủ</Link> › {page.data.kind === 'Help' ? <><Link to="/tro-giup">Trung tâm trợ giúp</Link> › </> : null}{page.data.title}
      </nav>
      <div className={`content-layout ${hasToc ? 'has-toc' : ''}`}>
        {hasToc && <Toc entries={body.toc} />}
        <article className="content-article" data-testid="cms-page">
          <h1>{page.data.title}</h1>
          <div className="content-updated">Cập nhật {formatDate(page.data.updatedAt)}</div>
          <div className="content-body" dangerouslySetInnerHTML={{ __html: body.html }} />
        </article>
      </div>
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
      <QueryState query={items} isEmpty={(d) => d.length === 0} emptyText={<p className="help-empty">Không tìm thấy câu hỏi phù hợp.</p>}>
        {() => (
          // Topics in a side menu with anchors, the questions in one reading column (G4-B)
          <div className="content-layout has-toc">
            <Toc title="Chủ đề" entries={[
              ...Object.keys(topics).map((t) => ({ id: `chu-de-${anchorId(t)}`, text: t })),
              ...(pages.length > 0 ? [{ id: 'chinh-sach', text: 'Chính sách & quy định' }] : []),
            ]} />
            <div className="help-list">
              {Object.entries(topics).map(([topic, list]) => (
                <section key={topic} id={`chu-de-${anchorId(topic)}`} className="help-topic">
                  <h2>{topic}</h2>
                  <ul>{list.map((i) => <li key={i.slug}><Link to={`/tro-giup/${i.slug}`}>{i.title}</Link></li>)}</ul>
                </section>
              ))}
              {pages.length > 0 && (
                <section id="chinh-sach" className="help-topic">
                  <h2>Chính sách & quy định</h2>
                  <ul>{pages.map((i) => <li key={i.slug}><Link to={`/trang/${i.slug}`}>{i.title}</Link></li>)}</ul>
                </section>
              )}
            </div>
          </div>
        )}
      </QueryState>
    </div>
  );
};
