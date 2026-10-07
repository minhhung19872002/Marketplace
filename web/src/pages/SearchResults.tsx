import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { ApiError } from '../api/http';
import ProductGrid from '../components/ProductGrid';
import { formatCount } from '../lib/money';
import type { CategoryPage, FacetValue, ProductSort, SearchParams } from '../types';
import { handleImgError } from '../lib/image';
import { BannerLink } from '../components/Banner';
import QueryState from '../components/QueryState';
import './SearchResults.css';
import { usePageTitle } from '../lib/pageTitle';

const SORTS: { key: ProductSort; label: string }[] = [
  { key: 'Relevance', label: 'Liên Quan' },
  { key: 'Newest', label: 'Mới Nhất' },
  { key: 'BestSelling', label: 'Bán Chạy' },
];

const FACET_LIMIT = 8;
const SORT_KEYS = new Set<string>(['Relevance', 'Newest', 'BestSelling', 'PriceAsc', 'PriceDesc']);

const num = (v: string | null): number | undefined => (v && /^\d+$/.test(v) ? Number(v) : undefined);

/** The URL is the single source of truth for filters, so results are shareable and Back works. */
function readParams(sp: URLSearchParams, categoryId?: string): SearchParams {
  const sort = sp.get('sort') ?? 'Relevance';
  return {
    q: sp.get('q') ?? undefined,
    categoryId: categoryId ?? sp.get('categoryId') ?? undefined,
    provinces: sp.getAll('provinces'),
    brands: sp.getAll('brands'),
    minPrice: num(sp.get('minPrice')),
    maxPrice: num(sp.get('maxPrice')),
    minRating: num(sp.get('minRating')),
    mall: sp.get('mall') === 'true',
    preferred: sp.get('preferred') === 'true',
    inStock: sp.get('inStock') === 'true',
    condition: sp.get('condition') ?? undefined,
    attrs: sp.getAll('attrs'),
    carriers: sp.getAll('carriers'),
    freeship: sp.get('freeship') === 'true',
    voucher: sp.get('voucher') === 'true',
    cod: sp.get('cod') === 'true',
    sort: (SORT_KEYS.has(sort) ? sort : 'Relevance') as ProductSort,
    page: num(sp.get('page')) ?? 1,
  };
}

interface FacetListProps {
  title: string;
  values: FacetValue[];
  selected: string[];
  onToggle: (value: string) => void;
  testId: string;
}

const FacetList = ({ title, values, selected, onToggle, testId }: FacetListProps) => {
  const [expanded, setExpanded] = useState(false);
  if (values.length === 0) return null;
  // Selected values always stay visible, even beyond the limit
  const shown = expanded ? values : values.filter((v, i) => i < FACET_LIMIT || selected.includes(v.value));
  return (
    <div className="filter-group" data-testid={`facet-${testId}`}>
      <h4 className="filter-group-title">{title}</h4>
      <div className="filter-cats">
        {shown.map((v) => (
          <label key={v.value} className="filter-cat">
            <input type="checkbox" checked={selected.includes(v.value)} onChange={() => onToggle(v.value)} data-testid="facet-option" />
            <span>
              {v.label} <span className="filter-count">({v.count})</span>
            </span>
          </label>
        ))}
      </div>
      {values.length > FACET_LIMIT && (
        <button className="filter-cats-more" onClick={() => setExpanded((x) => !x)}>
          {expanded ? 'Thu gọn ▴' : 'Thêm ▾'}
        </button>
      )}
    </div>
  );
};

const SearchView = ({ category }: { category?: CategoryPage }) => {
  const [sp, setSp] = useSearchParams();
  const params = readParams(sp, category?.category.id);
  const [minDraft, setMinDraft] = useState(sp.get('minPrice') ?? '');
  const [maxDraft, setMaxDraft] = useState(sp.get('maxPrice') ?? '');
  const [priceError, setPriceError] = useState('');

  useEffect(() => {
    setMinDraft(sp.get('minPrice') ?? '');
    setMaxDraft(sp.get('maxPrice') ?? '');
  }, [sp]);

  usePageTitle(params.q?.trim() ? `Kết quả tìm kiếm "${params.q.trim()}"` : 'Tìm kiếm');
  const search = useQuery({
    queryKey: ['search', params],
    queryFn: () => storefrontApi.search(params),
    placeholderData: keepPreviousData,
  });
  const { data, isLoading, isFetching } = search;
  // "Shop liên quan đến từ khoá" (II.3) above the results
  const keyword = (params.q ?? '').trim();
  const relatedShops = useQuery({
    queryKey: ['related-shops', keyword],
    queryFn: () => storefrontApi.relatedShops(keyword),
    enabled: keyword.length >= 2 && !category,
    staleTime: 60_000,
  });
  const relatedShop = relatedShops.data?.[0];

  /** Change URL params; any filter change goes back to page 1. */
  const update = (change: (next: URLSearchParams) => void, keepPage = false) => {
    const next = new URLSearchParams(sp);
    change(next);
    if (!keepPage) next.delete('page');
    setSp(next);
  };
  const toggleMulti = (key: string, value: string) =>
    update((n) => {
      const all = n.getAll(key);
      n.delete(key);
      (all.includes(value) ? all.filter((x) => x !== value) : [...all, value]).forEach((v) => n.append(key, v));
    });
  const setFlag = (key: string, on: boolean) => update((n) => (on ? n.set(key, 'true') : n.delete(key)));

  const applyPrice = (e: FormEvent) => {
    e.preventDefault();
    const min = minDraft.trim() === '' ? undefined : Number(minDraft);
    const max = maxDraft.trim() === '' ? undefined : Number(maxDraft);
    if ((min !== undefined && (!Number.isInteger(min) || min < 0)) || (max !== undefined && (!Number.isInteger(max) || max < 0))) {
      setPriceError('Vui lòng nhập giá là số nguyên không âm.');
      return;
    }
    if (min !== undefined && max !== undefined && min > max) {
      setPriceError('Khoảng giá không hợp lệ: giá từ phải nhỏ hơn hoặc bằng giá đến.');
      return;
    }
    setPriceError('');
    update((n) => {
      if (min === undefined) n.delete('minPrice');
      else n.set('minPrice', String(min));
      if (max === undefined) n.delete('maxPrice');
      else n.set('maxPrice', String(max));
    });
  };

  const clearFilters = () =>
    update((n) => {
      for (const key of ['categoryId', 'provinces', 'brands', 'minPrice', 'maxPrice', 'minRating', 'mall', 'preferred', 'inStock', 'condition', 'attrs', 'carriers', 'freeship', 'voucher', 'cod'])
        n.delete(key);
    });

  const facets = data?.facets;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const page = params.page ?? 1;
  const priceSort = params.sort === 'PriceAsc' ? 'asc' : params.sort === 'PriceDesc' ? 'desc' : '';
  const heading = params.q
    ? `Kết quả tìm kiếm cho "${params.q}"`
    : category
      ? category.category.name
      : params.mall
        ? 'ShopHub Mall'
        : 'Tất cả sản phẩm';

  return (
    <div className="search-results">
      <div className="container">
        {category && (
          <>
            <div className="breadcrumb" data-testid="category-breadcrumb">
              <Link to="/">Trang chủ</Link>
              {category.breadcrumb.map((c) => (
                <span key={c.id}>
                  <span> › </span>
                  {c.id === category.category.id ? <span className="breadcrumb-current">{c.name}</span> : <Link to={`/danh-muc/${c.slug}`}>{c.name}</Link>}
                </span>
              ))}
            </div>
            {category.children.length > 0 && (
              <div className="category-children" data-testid="category-children">
                {category.children.map((c) => (
                  <Link key={c.id} to={`/danh-muc/${c.slug}`} className="category-child">
                    {c.name}
                  </Link>
                ))}
              </div>
            )}
          </>
        )}
      </div>

      <div className="container search-layout">
        <aside className="search-sidebar" data-testid="filter-sidebar">
          <div className="filter-title">
            <span>☰ BỘ LỌC TÌM KIẾM</span>
          </div>

          {!category && facets && (
            <FacetList
              title="Theo Danh Mục"
              values={facets.categories}
              selected={params.categoryId ? [params.categoryId] : []}
              onToggle={(v) => update((n) => (n.get('categoryId') === v ? n.delete('categoryId') : n.set('categoryId', v)))}
              testId="categories"
            />
          )}
          {facets && (
            <FacetList title="Nơi Bán" values={facets.provinces} selected={params.provinces ?? []} onToggle={(v) => toggleMulti('provinces', v)} testId="provinces" />
          )}
          {facets && (
            <FacetList title="Thương Hiệu" values={facets.brands} selected={params.brands ?? []} onToggle={(v) => toggleMulti('brands', v)} testId="brands" />
          )}

          <div className="filter-group">
            <h4 className="filter-group-title">Khoảng Giá</h4>
            <form className="filter-price" onSubmit={applyPrice}>
              <input type="number" min="0" placeholder="₫ TỪ" value={minDraft} onChange={(e) => setMinDraft(e.target.value)} aria-label="Giá từ" />
              <span className="filter-price-sep">—</span>
              <input type="number" min="0" placeholder="₫ ĐẾN" value={maxDraft} onChange={(e) => setMaxDraft(e.target.value)} aria-label="Giá đến" />
              <button type="submit" className="filter-price-apply" data-testid="price-apply">ÁP DỤNG</button>
            </form>
            {priceError && <div className="filter-error" role="alert" data-testid="price-error">{priceError}</div>}
          </div>

          <div className="filter-group">
            <h4 className="filter-group-title">Loại Shop</h4>
            <label className="filter-cat">
              <input type="checkbox" checked={!!params.mall} onChange={(e) => setFlag('mall', e.target.checked)} data-testid="filter-mall" />
              <span>ShopHub Mall {facets && <span className="filter-count">({facets.shopTypes.find((s) => s.value === 'mall')?.count ?? 0})</span>}</span>
            </label>
            <label className="filter-cat">
              <input type="checkbox" checked={!!params.preferred} onChange={(e) => setFlag('preferred', e.target.checked)} />
              <span>Shop Yêu Thích {facets && <span className="filter-count">({facets.shopTypes.find((s) => s.value === 'preferred')?.count ?? 0})</span>}</span>
            </label>
            <label className="filter-cat">
              <input type="checkbox" checked={!!params.inStock} onChange={(e) => setFlag('inStock', e.target.checked)} data-testid="filter-in-stock" />
              <span>Còn hàng</span>
            </label>
          </div>

          {facets && (facets.carriers?.length ?? 0) > 0 && (
            <FacetList title="Đơn Vị Vận Chuyển" values={facets.carriers!} selected={params.carriers ?? []} onToggle={(v) => toggleMulti('carriers', v)}
              testId="carriers" />
          )}
          {facets && (
            <div className="filter-group" data-testid="facet-services">
              <h4 className="filter-group-title">Dịch Vụ & Khuyến Mãi</h4>
              {([['freeship', 'Freeship Xtra'], ['voucher', 'Có voucher của shop'], ['cod', 'Thanh toán khi nhận hàng']] as const).map(([key, label]) => (
                <label key={key} className="filter-cat">
                  <input type="checkbox" checked={!!params[key]} onChange={(e) => setFlag(key, e.target.checked)} data-testid={`filter-${key}`} />
                  <span>{label} <span className="filter-count">({facets.services?.find((s) => s.value === key)?.count ?? 0})</span></span>
                </label>
              ))}
            </div>
          )}

          {facets && facets.conditions.length > 1 && (
            <FacetList
              title="Tình Trạng"
              values={facets.conditions}
              selected={params.condition ? [params.condition] : []}
              onToggle={(v) => update((n) => (n.get('condition') === v ? n.delete('condition') : n.set('condition', v)))}
              testId="conditions"
            />
          )}

          <div className="filter-group">
            <h4 className="filter-group-title">Đánh Giá</h4>
            <div className="filter-ratings">
              {[5, 4, 3].map((r) => (
                <button
                  key={r}
                  className={`filter-rating ${params.minRating === r ? 'active' : ''}`}
                  onClick={() => update((n) => (params.minRating === r ? n.delete('minRating') : n.set('minRating', String(r))))}
                  data-testid="filter-rating"
                >
                  <span className="filter-rating-stars">{'★'.repeat(r)}{'☆'.repeat(5 - r)}</span>
                  {r < 5 && <span>trở lên</span>}
                </button>
              ))}
            </div>
          </div>

          {facets &&
            Object.entries(facets.attributes).map(([name, values]) => (
              <FacetList
                key={name}
                title={name}
                values={values}
                // Attribute facet values are already "Name=Value"
                selected={(params.attrs ?? []).filter((a) => a.startsWith(`${name}=`))}
                onToggle={(v) => toggleMulti('attrs', v)}
                testId={`attr-${name}`}
              />
            ))}

          <button className="filter-clear" onClick={clearFilters} data-testid="filter-clear">
            XÓA TẤT CẢ
          </button>
        </aside>

        <div className="search-content">
          {category && (category.banners?.length ?? 0) > 0 && (
            <div className="category-banners" data-testid="category-banners">
              {category.banners!.map((b) => (
                <BannerLink key={b.id} to={b.link} className="category-banner"><img src={b.imageUrl} alt={b.title} onError={handleImgError} /></BannerLink>
              ))}
            </div>
          )}
          {category && (category.brands?.length ?? 0) > 0 && (
            <div className="category-brands" data-testid="category-brands">
              <div className="category-brands-title">Thương hiệu nổi bật</div>
              <div className="category-brands-list">
                {category.brands!.map((b) => (
                  <button key={b.id} type="button" className={`category-brand ${params.brands?.includes(b.id) ? 'active' : ''}`}
                    onClick={() => toggleMulti('brands', b.id)} data-testid="category-brand">
                    {b.logoUrl ? <img src={b.logoUrl} alt="" onError={handleImgError} /> : null}
                    <span>{b.name}{b.isVerified && ' ✓'}</span>
                  </button>
                ))}
              </div>
            </div>
          )}
          {relatedShop && (
            <div className="related-shop" data-testid="related-shop">
              <div className="related-shop-title">SHOP LIÊN QUAN ĐẾN “{keyword}”</div>
              <Link to={`/shop/${relatedShop.slug}`} className="related-shop-card">
                <span className="related-shop-logo">
                  {relatedShop.logoUrl ? <img src={relatedShop.logoUrl} alt="" onError={handleImgError} /> : relatedShop.name.charAt(0)}
                </span>
                <span className="related-shop-info">
                  <strong>{relatedShop.name}</strong>
                  {relatedShop.isMall && <span className="related-shop-badge">Mall</span>}
                  <span className="related-shop-sub">
                    {formatCount(relatedShop.followerCount)} người theo dõi · {formatCount(relatedShop.productCount)} sản phẩm
                    {relatedShop.ratingAvg > 0 && ` · ${relatedShop.ratingAvg.toFixed(1)} ★`}
                    {relatedShop.provinceName && ` · ${relatedShop.provinceName}`}
                  </span>
                </span>
                <span className="related-shop-view">Xem Shop ›</span>
              </Link>
            </div>
          )}
          <div className="search-results-head">
            <h1 className="search-results-title" data-testid="search-heading">{heading}</h1>
            <span className="search-results-count" data-testid="search-count">
              {data ? `${formatCount(data.totalCount)} sản phẩm` : ''}
            </span>
          </div>

          <div className="search-sort-bar">
            <span className="search-sort-label">Sắp xếp theo</span>
            {SORTS.map((s) => (
              <button
                key={s.key}
                className={`search-sort-btn ${params.sort === s.key ? 'active' : ''}`}
                onClick={() => update((n) => (s.key === 'Relevance' ? n.delete('sort') : n.set('sort', s.key)))}
              >
                {s.label}
              </button>
            ))}
            <button
              className={`search-sort-btn search-sort-price ${priceSort ? 'active' : ''}`}
              onClick={() => update((n) => n.set('sort', priceSort === 'asc' ? 'PriceDesc' : 'PriceAsc'))}
              data-testid="sort-price"
            >
              Giá {priceSort === 'asc' ? '↑' : priceSort === 'desc' ? '↓' : '⇅'}
            </button>
            {data && totalPages > 1 && (
              <span className="search-mini-pager">
                {page}/{totalPages}
              </span>
            )}
          </div>

          <QueryState query={search} loading={<ProductGrid title="" products={[]} loading />} isEmpty={(d) => d.items.length === 0}
            emptyText={
              <div className="search-empty" data-testid="search-empty">
                <p>Không tìm thấy sản phẩm nào phù hợp.</p>
                <p className="search-empty-hint">Hãy thử từ khoá khác hoặc bỏ bớt bộ lọc.</p>
                <Link to="/" className="search-empty-btn">Về trang chủ</Link>
              </div>
            }>
            {(found) => (
              <div className={isFetching && !isLoading ? 'search-refreshing' : ''}>
                <ProductGrid title="" products={found.items} />
              </div>
            )}
          </QueryState>

          {data && totalPages > 1 && (
            <nav className="search-pager" aria-label="Phân trang" data-testid="search-pager">
              <button disabled={page <= 1} onClick={() => update((n) => n.set('page', String(page - 1)), true)}>‹</button>
              <span>
                Trang {page} / {totalPages}
              </span>
              <button disabled={page >= totalPages} onClick={() => update((n) => n.set('page', String(page + 1)), true)} data-testid="next-page">›</button>
            </nav>
          )}
        </div>
      </div>
    </div>
  );
};

/** /tim-kiem */
export const SearchResults = () => <SearchView />;

/** /danh-muc/:slug — the same search, scoped to a category subtree. */
export const CategoryResults = () => {
  const { slug = '' } = useParams();
  const categoryQuery = useQuery({ queryKey: ['category', slug], queryFn: () => storefrontApi.categoryBySlug(slug) });
  const { data, error } = categoryQuery;
  usePageTitle(data?.category.name ?? (error ? 'Không tìm thấy danh mục' : null));
  // Loading, or a network / server error (not "no such category"): spinner or the error with "Thử lại" (F3)
  if (categoryQuery.isPending || (error && !(error instanceof ApiError && error.status === 404)))
    return <div className="container"><QueryState query={categoryQuery}>{() => null}</QueryState></div>;
  if (error)
    return (
      <div className="container search-empty">
        <p>Danh mục không tồn tại hoặc đã ngừng hoạt động.</p>
        <Link to="/" className="search-empty-btn">Về trang chủ</Link>
      </div>
    );
  if (!data) return <div className="page-loader"><div className="loading-spinner" /></div>;
  return <SearchView key={data.category.id} category={data} />;
};

export default SearchResults;
