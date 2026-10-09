import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { storefrontApi } from '../api/storefront';
import { ApiError } from '../api/http';
import ProductGrid from '../components/ProductGrid';
import { formatCount, formatPrice } from '../lib/money';
import type { CategoryPage, FacetValue, ProductSort, SearchFacets, SearchParams } from '../types';
import { handleImgError } from '../lib/image';
import { BannerLink } from '../components/Banner';
import QueryState from '../components/QueryState';
import './SearchResults.css';
import { usePageTitle } from '../lib/pageTitle';
import { Check, ChevronDown, ChevronLeft, ChevronRight, ChevronUp, ListFilter, SearchX, X, Wrench } from 'lucide-react';
import { EmptyState, Pager, Skeleton, Stars } from '../components/ui';

const SORTS: { key: ProductSort; label: string }[] = [
  { key: 'Relevance', label: 'Liên quan' },
  { key: 'Newest', label: 'Mới nhất' },
  { key: 'BestSelling', label: 'Bán chạy' },
];

const FACET_LIMIT = 8;
const SORT_KEYS = new Set<string>(['Relevance', 'Newest', 'BestSelling', 'PriceAsc', 'PriceDesc']);
const FILTER_KEYS = ['categoryId', 'provinces', 'brands', 'minPrice', 'maxPrice', 'minRating', 'mall', 'preferred', 'inStock', 'condition', 'attrs',
  'carriers', 'freeship', 'voucher', 'cod'];

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

/**
 * The filters in force as removable chips ("Đang lọc", G2-B3): each carries the URL key and value it removes. Labels
 * come from the facets of the answer (names, not ids); a value no facet knows any more keeps its raw text.
 */
export function activeFilters(sp: URLSearchParams, facets: SearchFacets | undefined, brandNames: Record<string, string> = {}, withCategory = true) {
  const label = (values: FacetValue[] | null | undefined, v: string) => values?.find((x) => x.value === v)?.label ?? v;
  const chips: { key: string; value?: string; label: string }[] = [];
  const categoryId = sp.get('categoryId');
  if (withCategory && categoryId) chips.push({ key: 'categoryId', label: label(facets?.categories, categoryId) });
  for (const v of sp.getAll('provinces')) chips.push({ key: 'provinces', value: v, label: label(facets?.provinces, v) });
  for (const v of sp.getAll('brands')) chips.push({ key: 'brands', value: v, label: brandNames[v] ?? label(facets?.brands, v) });
  const min = num(sp.get('minPrice'));
  const max = num(sp.get('maxPrice'));
  if (min !== undefined || max !== undefined) {
    chips.push({
      key: 'price',
      label: min !== undefined && max !== undefined ? `${formatPrice(min)} – ${formatPrice(max)}` : min !== undefined ? `Từ ${formatPrice(min)}` : `Đến ${formatPrice(max!)}`,
    });
  }
  const rating = num(sp.get('minRating'));
  if (rating) chips.push({ key: 'minRating', label: rating === 5 ? '5 sao' : `Từ ${rating} sao` });
  if (sp.get('mall') === 'true') chips.push({ key: 'mall', label: 'ShopHub Mall' });
  if (sp.get('preferred') === 'true') chips.push({ key: 'preferred', label: 'Shop yêu thích' });
  if (sp.get('inStock') === 'true') chips.push({ key: 'inStock', label: 'Còn hàng' });
  const condition = sp.get('condition');
  if (condition) chips.push({ key: 'condition', label: label(facets?.conditions, condition) });
  for (const v of sp.getAll('carriers')) chips.push({ key: 'carriers', value: v, label: label(facets?.carriers, v) });
  if (sp.get('freeship') === 'true') chips.push({ key: 'freeship', label: 'Freeship Xtra' });
  if (sp.get('voucher') === 'true') chips.push({ key: 'voucher', label: 'Có voucher' });
  if (sp.get('cod') === 'true') chips.push({ key: 'cod', label: 'COD' });
  for (const v of sp.getAll('attrs')) {
    const name = v.split('=')[0];
    chips.push({ key: 'attrs', value: v, label: `${name}: ${label(facets?.attributes[name], v).replace(`${name}=`, '')}` });
  }
  return chips;
}

/** One block of the filter sidebar; the title folds it away (G2-B3). */
const FilterGroup = ({ title, testId, children }: { title: string; testId?: string; children: ReactNode }) => {
  const [open, setOpen] = useState(true);
  return (
    <div className="filter-group" data-testid={testId}>
      <h4 className="filter-group-head">
        <button type="button" className="filter-group-title" onClick={() => setOpen(!open)} aria-expanded={open}>
          {title} {open ? <ChevronUp size={14} aria-hidden /> : <ChevronDown size={14} aria-hidden />}
        </button>
      </h4>
      {open && children}
    </div>
  );
};

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
    <FilterGroup title={title} testId={`facet-${testId}`}>
      <div className="filter-cats">
        {shown.map((v) => (
          <label key={v.value} className="filter-cat">
            <input type="checkbox" checked={selected.includes(v.value)} onChange={() => onToggle(v.value)} data-testid="facet-option" />
            {/* Name and count stay on one line: a long name is cut, the count never drops alone to the next line (G3 C6) */}
            <span className="filter-cat-text" title={v.label}>
              <span className="filter-cat-name">{v.label}</span> <span className="filter-count">({v.count})</span>
            </span>
          </label>
        ))}
      </div>
      {values.length > FACET_LIMIT && (
        <button type="button" className="filter-cats-more" onClick={() => setExpanded((x) => !x)} aria-expanded={expanded}>
          {expanded ? <>Thu gọn <ChevronUp size={14} aria-hidden /></> : <>Thêm <ChevronDown size={14} aria-hidden /></>}
        </button>
      )}
    </FilterGroup>
  );
};

/** "Giá" in the sort bar: a small menu with the two directions (G2-B3). */
const PriceSort = ({ value, onPick }: { value: '' | 'asc' | 'desc'; onPick: (sort: ProductSort) => void }) => {
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return undefined;
    const close = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const pick = (sort: ProductSort) => { setOpen(false); onPick(sort); };
  return (
    <div className="search-price-sort" ref={box} onKeyDown={(e) => { if (e.key === 'Escape') setOpen(false); }}>
      <button type="button" className={`search-price-sort-btn ${value ? 'active' : ''}`} onClick={() => setOpen(!open)} aria-haspopup="menu" aria-expanded={open}
        data-testid="sort-price">
        <span>{value === 'asc' ? 'Giá: thấp đến cao' : value === 'desc' ? 'Giá: cao đến thấp' : 'Giá'}</span>
        <ChevronDown size={16} aria-hidden />
      </button>
      {open && (
        <div className="search-price-sort-menu" role="menu">
          <button type="button" role="menuitemradio" aria-checked={value === 'asc'} onClick={() => pick('PriceAsc')} data-testid="sort-price-asc">
            Giá: thấp đến cao {value === 'asc' && <Check size={16} aria-hidden />}
          </button>
          <button type="button" role="menuitemradio" aria-checked={value === 'desc'} onClick={() => pick('PriceDesc')} data-testid="sort-price-desc">
            Giá: cao đến thấp {value === 'desc' && <Check size={16} aria-hidden />}
          </button>
        </div>
      )}
    </div>
  );
};

/** Products to look at when a search or a category has nothing (the buyer is never left at a dead end). */
const Suggestions = () => {
  const { data } = useQuery({ queryKey: ['home', 'recommendations', 'fallback'], queryFn: () => storefrontApi.recommendations(1, 12), staleTime: 5 * 60_000 });
  if (!data || data.items.length === 0) return null;
  return <ProductGrid title="Có thể bạn cũng thích" products={data.items} />;
};

const SearchView = ({ category }: { category?: CategoryPage }) => {
  const [sp, setSp] = useSearchParams();
  const params = readParams(sp, category?.category.id);
  const [minDraft, setMinDraft] = useState(sp.get('minPrice') ?? '');
  const [maxDraft, setMaxDraft] = useState(sp.get('maxPrice') ?? '');
  const [priceError, setPriceError] = useState('');
  // Phones: the sidebar opens as a sheet from the "Lọc" button
  const [filtersOpen, setFiltersOpen] = useState(false);

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
  const goPage = (p: number) => {
    update((n) => (p <= 1 ? n.delete('page') : n.set('page', String(p))), true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

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

  const clearFilters = () => update((n) => { for (const key of FILTER_KEYS) n.delete(key); });

  const facets = data?.facets;
  const brandNames = Object.fromEntries((category?.brands ?? []).map((b) => [b.id, b.name]));
  const chips = activeFilters(sp, facets, brandNames, !category);
  const removeChip = (c: { key: string; value?: string }) =>
    update((n) => {
      if (c.key === 'price') { n.delete('minPrice'); n.delete('maxPrice'); return; }
      if (c.value === undefined) { n.delete(c.key); return; }
      const rest = n.getAll(c.key).filter((x) => x !== c.value);
      n.delete(c.key);
      rest.forEach((v) => n.append(c.key, v));
    });

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
            <nav className="breadcrumb" aria-label="Đường dẫn" data-testid="category-breadcrumb">
              <Link to="/">ShopHub</Link>
              {category.breadcrumb.map((c) => (
                <span key={c.id} className="breadcrumb-step">
                  <span aria-hidden>›</span>
                  {c.id === category.category.id ? <span className="breadcrumb-current" aria-current="page">{c.name}</span> : <Link to={`/danh-muc/${c.slug}`}>{c.name}</Link>}
                </span>
              ))}
            </nav>
            {(category.banners?.length ?? 0) > 0 && (
              <div className="category-banners" data-testid="category-banners">
                {category.banners!.map((b) => (
                  <BannerLink key={b.id} to={b.link} className="category-banner"><img src={b.imageUrl} alt={b.title} onError={handleImgError} /></BannerLink>
                ))}
              </div>
            )}
            {category.children.length > 0 && (
              <div className="category-children" data-testid="category-children" role="list" aria-label="Danh mục con">
                {category.children.map((c) => (
                  <Link key={c.id} to={`/danh-muc/${c.slug}`} className="category-child" role="listitem">
                    {c.name}
                  </Link>
                ))}
              </div>
            )}
          </>
        )}
      </div>

      <div className="container search-layout">
        {filtersOpen && <button type="button" className="search-sidebar-backdrop" aria-label="Đóng bộ lọc" onClick={() => setFiltersOpen(false)} tabIndex={-1} />}
        <aside className={`search-sidebar ${filtersOpen ? 'is-open' : ''}`} data-testid="filter-sidebar" aria-label="Bộ lọc tìm kiếm">
          <div className="filter-title">
            <span><ListFilter size={16} aria-hidden /> Bộ lọc tìm kiếm</span>
            <button type="button" className="filter-sheet-close" onClick={() => setFiltersOpen(false)} aria-label="Đóng bộ lọc"><X size={20} aria-hidden /></button>
          </div>

          {/* Facet groups hold their place until the first answer (CLS) */}
          {!facets && [0, 1, 2].map((i) => (
            <div key={i} className="filter-group filter-group--loading" aria-hidden>
              <Skeleton width="60%" height={16} />
              {[0, 1, 2, 3, 4].map((j) => <Skeleton key={j} width="85%" height={14} />)}
            </div>
          ))}
          {!category && facets && (
            <FacetList
              title="Theo danh mục"
              values={facets.categories}
              selected={params.categoryId ? [params.categoryId] : []}
              onToggle={(v) => update((n) => (n.get('categoryId') === v ? n.delete('categoryId') : n.set('categoryId', v)))}
              testId="categories"
            />
          )}
          {facets && (
            <FacetList title="Nơi bán" values={facets.provinces} selected={params.provinces ?? []} onToggle={(v) => toggleMulti('provinces', v)} testId="provinces" />
          )}
          {facets && (
            <FacetList title="Thương hiệu" values={facets.brands} selected={params.brands ?? []} onToggle={(v) => toggleMulti('brands', v)} testId="brands" />
          )}

          <FilterGroup title="Khoảng giá">
            <form className="filter-price" onSubmit={applyPrice} noValidate>
              <span className="filter-price-field">
                <span className="filter-price-currency" aria-hidden>₫</span>
                <input type="number" min="0" step="1000" inputMode="numeric" placeholder="Từ" value={minDraft} onChange={(e) => setMinDraft(e.target.value)}
                  aria-label="Giá từ" aria-invalid={!!priceError} />
              </span>
              <span className="filter-price-sep" aria-hidden>—</span>
              <span className="filter-price-field">
                <span className="filter-price-currency" aria-hidden>₫</span>
                <input type="number" min="0" step="1000" inputMode="numeric" placeholder="Đến" value={maxDraft} onChange={(e) => setMaxDraft(e.target.value)}
                  aria-label="Giá đến" aria-invalid={!!priceError} />
              </span>
              <button type="submit" className="filter-price-apply" data-testid="price-apply">Áp dụng</button>
            </form>
            {priceError && <div className="filter-error" role="alert" data-testid="price-error">{priceError}</div>}
          </FilterGroup>

          <FilterGroup title="Loại shop">
            <div className="filter-cats">
              <label className="filter-cat">
                <input type="checkbox" checked={!!params.mall} onChange={(e) => setFlag('mall', e.target.checked)} data-testid="filter-mall" />
                <span>ShopHub Mall {facets && <span className="filter-count">({facets.shopTypes.find((s) => s.value === 'mall')?.count ?? 0})</span>}</span>
              </label>
              <label className="filter-cat">
                <input type="checkbox" checked={!!params.preferred} onChange={(e) => setFlag('preferred', e.target.checked)} />
                <span>Shop yêu thích {facets && <span className="filter-count">({facets.shopTypes.find((s) => s.value === 'preferred')?.count ?? 0})</span>}</span>
              </label>
              <label className="filter-cat">
                <input type="checkbox" checked={!!params.inStock} onChange={(e) => setFlag('inStock', e.target.checked)} data-testid="filter-in-stock" />
                <span>Còn hàng</span>
              </label>
            </div>
          </FilterGroup>

          {facets && (facets.carriers?.length ?? 0) > 0 && (
            <FacetList title="Đơn vị vận chuyển" values={facets.carriers!} selected={params.carriers ?? []} onToggle={(v) => toggleMulti('carriers', v)}
              testId="carriers" />
          )}
          {facets && (
            <FilterGroup title="Dịch vụ & khuyến mãi" testId="facet-services">
              <div className="filter-cats">
                {([['freeship', 'Freeship Xtra'], ['voucher', 'Có voucher của shop'], ['cod', 'Thanh toán khi nhận hàng']] as const).map(([key, label]) => (
                  <label key={key} className="filter-cat">
                    <input type="checkbox" checked={!!params[key]} onChange={(e) => setFlag(key, e.target.checked)} data-testid={`filter-${key}`} />
                    <span>{label} <span className="filter-count">({facets.services?.find((s) => s.value === key)?.count ?? 0})</span></span>
                  </label>
                ))}
              </div>
            </FilterGroup>
          )}

          {facets && facets.conditions.length > 1 && (
            <FacetList
              title="Tình trạng"
              values={facets.conditions}
              selected={params.condition ? [params.condition] : []}
              onToggle={(v) => update((n) => (n.get('condition') === v ? n.delete('condition') : n.set('condition', v)))}
              testId="conditions"
            />
          )}

          <FilterGroup title="Đánh giá">
            <div className="filter-ratings">
              {[5, 4, 3].map((r) => (
                <button
                  key={r}
                  type="button"
                  className={`filter-rating ${params.minRating === r ? 'active' : ''}`}
                  onClick={() => update((n) => (params.minRating === r ? n.delete('minRating') : n.set('minRating', String(r))))}
                  aria-pressed={params.minRating === r}
                  aria-label={r === 5 ? '5 sao' : `Từ ${r} sao trở lên`}
                  data-testid="filter-rating"
                >
                  <Stars value={r} size={14} />
                  {r < 5 && <span>trở lên</span>}
                </button>
              ))}
            </div>
          </FilterGroup>

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

          {/* Only drops filters from the URL (nothing saved is lost): no confirmation */}
          <button type="button" className="filter-clear" onClick={clearFilters} data-testid="filter-clear" data-confirm="local">
            Xoá tất cả
          </button>
          <button type="button" className="filter-sheet-apply" onClick={() => setFiltersOpen(false)}>
            Xem {data ? formatCount(data.totalCount) : ''} sản phẩm
          </button>
        </aside>

        <div className="search-content">
          {category && (category.brands?.length ?? 0) > 0 && (
            <div className="category-brands" data-testid="category-brands">
              <div className="category-brands-title">Thương hiệu nổi bật</div>
              <div className="category-brands-list">
                {category.brands!.map((b) => {
                  const on = params.brands?.includes(b.id) ?? false;
                  return (
                    <button key={b.id} type="button" className={`category-brand ${on ? 'active' : ''}`} aria-pressed={on}
                      onClick={() => toggleMulti('brands', b.id)} data-testid="category-brand" title={b.isVerified ? `${b.name} — thương hiệu đã xác minh` : b.name}>
                      {b.logoUrl ? <img src={b.logoUrl} alt="" onError={handleImgError} /> : null}
                      <span>{b.name}</span>
                      {/* The tick only marks a brand that is filtering the results */}
                      {on && <span className="category-brand-tick" aria-hidden><Check size={10} strokeWidth={3} /></span>}
                    </button>
                  );
                })}
              </div>
            </div>
          )}
          {relatedShops.isLoading && <div className="related-shop related-shop--loading" aria-hidden />}
          {relatedShop && (
            <div className="related-shop" data-testid="related-shop">
              <div className="related-shop-title">Shop liên quan đến “{keyword}”</div>
              <Link to={`/shop/${relatedShop.slug}`} className="related-shop-card">
                <span className="related-shop-logo">
                  {relatedShop.logoUrl ? <img src={relatedShop.logoUrl} alt="" onError={handleImgError} /> : relatedShop.name.charAt(0)}
                </span>
                <span className="related-shop-info">
                  <strong>{relatedShop.name}</strong>
                  {relatedShop.isMall && <span className="related-shop-badge">Mall</span>}
                  <span className="related-shop-sub">
                    {relatedShop.followerCount > 0 && `${formatCount(relatedShop.followerCount)} người theo dõi · `}
                    {formatCount(relatedShop.productCount)} sản phẩm
                    {relatedShop.ratingAvg > 0 && ` · ${relatedShop.ratingAvg.toFixed(1)}/5`}
                    {relatedShop.provinceName && ` · ${relatedShop.provinceName}`}
                  </span>
                </span>
                <span className="related-shop-view">Xem shop ›</span>
              </Link>
            </div>
          )}
          <div className="search-results-head">
            <h1 className="search-results-title" data-testid="search-heading">{heading}</h1>
            <span className="search-results-count" data-testid="search-count">
              {data ? `${formatCount(data.totalCount)} sản phẩm` : ''}
            </span>
          </div>

          {chips.length > 0 && (
            <div className="search-chips" data-testid="active-filters" aria-label="Đang lọc">
              <span className="search-chips-label">Đang lọc:</span>
              {chips.map((c) => (
                <button key={`${c.key}-${c.value ?? ''}`} type="button" className="search-chip" onClick={() => removeChip(c)} aria-label={`Bỏ lọc ${c.label}`}
                  data-testid="active-filter">
                  {c.label} <X size={14} aria-hidden />
                </button>
              ))}
              <button type="button" className="search-chips-clear" data-confirm="local" onClick={clearFilters} data-testid="chips-clear">Xoá tất cả</button>
            </div>
          )}

          <div className="search-sort-bar">
            <button type="button" className="search-filter-open" onClick={() => setFiltersOpen(true)} data-testid="open-filters">
              <ListFilter size={16} aria-hidden /> Bộ lọc{chips.length > 0 && <span className="search-filter-count">{chips.length}</span>}
            </button>
            <span className="search-sort-label">Sắp xếp theo</span>
            {SORTS.map((s) => (
              <button
                key={s.key}
                type="button"
                className={`search-sort-btn ${params.sort === s.key ? 'active' : ''}`}
                aria-pressed={params.sort === s.key}
                onClick={() => update((n) => (s.key === 'Relevance' ? n.delete('sort') : n.set('sort', s.key)))}
              >
                {s.label}
              </button>
            ))}
            <PriceSort value={priceSort} onPick={(sort) => update((n) => n.set('sort', sort))} />
            {data && totalPages > 1 && (
              <span className="search-mini-pager" data-testid="mini-pager">
                <span><strong>{page}</strong>/{totalPages}</span>
                <button type="button" disabled={page <= 1} onClick={() => goPage(page - 1)} aria-label="Trang trước"><ChevronLeft size={16} aria-hidden /></button>
                <button type="button" disabled={page >= totalPages} onClick={() => goPage(page + 1)} aria-label="Trang sau"><ChevronRight size={16} aria-hidden /></button>
              </span>
            )}
          </div>

          <QueryState query={search} loading={<ProductGrid title="" products={[]} loading />} isEmpty={(d) => d.items.length === 0}
            emptyText={
              <div className="search-empty" data-testid="search-empty">
                <EmptyState icon={SearchX} title="Không tìm thấy sản phẩm nào phù hợp"
                  text={chips.length > 0 ? 'Hãy bỏ bớt bộ lọc hoặc thử từ khoá khác.' : 'Hãy thử từ khoá khác, viết ngắn hơn hoặc không dấu.'}
                  action={chips.length > 0
                    ? <button type="button" className="search-empty-btn" data-confirm="local" onClick={clearFilters}>Xoá bộ lọc</button>
                    : <Link to="/" className="search-empty-btn">Về trang chủ</Link>} />
              </div>
            }>
            {(found) => (
              <div className={isFetching && !isLoading ? 'search-refreshing' : ''}>
                <h2 className="sh-visually-hidden">Danh sách sản phẩm</h2>
                <ProductGrid title="" products={found.items} />
              </div>
            )}
          </QueryState>
          {data && data.items.length === 0 && <Suggestions />}

          {data && <Pager page={page} total={totalPages} onPage={goPage} testId="search-pager" />}
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
        <EmptyState icon={SearchX} title="Danh mục không tồn tại hoặc đã ngừng hoạt động." action={<Link to="/" className="search-empty-btn">Về trang chủ</Link>} />
      </div>
    );
  if (!data) return <div className="page-loader"><div className="loading-spinner" /></div>;
  // An industry hidden from buyers for now (G2-A7): the link still opens, with a notice and things to look at instead
  if (!data.isVisible)
    return (
      <div className="search-results container" data-testid="category-hidden">
        <div className="search-empty">
          <EmptyState icon={Wrench} title="Danh mục đang cập nhật" text={`Ngành hàng “${data.category.name}” đang được sắp xếp lại, mời bạn xem các sản phẩm khác.`}
            action={<Link to="/" className="search-empty-btn">Về trang chủ</Link>} />
        </div>
        <Suggestions />
      </div>
    );
  return <SearchView key={data.category.id} category={data} />;
};

export default SearchResults;
