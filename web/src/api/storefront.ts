// Buyer-site catalogue: search, product page, home sections, shop page, wishlist and follows.
import { apiCommand, apiRequest } from './http';
import type {
  CategoryNode,
  CategoryPage,
  MallShop,
  PagedResult,
  ProductCard,
  ProductPage,
  SearchParams,
  SearchResult,
  ShopPage,
  ShopSummary,
  Suggestion,
  TopCategoryProduct,
} from '../types';

/** Query string for /search/products — arrays repeat the key (provinces=01&provinces=79). */
export function searchQueryString(p: SearchParams): string {
  const qs = new URLSearchParams();
  const set = (key: string, value: string | number | boolean | undefined | null) => {
    if (value !== undefined && value !== null && value !== '' && value !== false) qs.set(key, String(value));
  };
  set('q', p.q?.trim());
  set('categoryId', p.categoryId);
  set('shopId', p.shopId);
  p.provinces?.forEach((v) => qs.append('provinces', v));
  p.brands?.forEach((v) => qs.append('brands', v));
  set('minPrice', p.minPrice);
  set('maxPrice', p.maxPrice);
  set('minRating', p.minRating);
  set('mall', p.mall);
  set('preferred', p.preferred);
  set('inStock', p.inStock);
  set('condition', p.condition);
  p.attrs?.forEach((v) => qs.append('attrs', v));
  set('sort', p.sort && p.sort !== 'Relevance' ? p.sort : undefined);
  set('page', p.page && p.page > 1 ? p.page : undefined);
  set('pageSize', p.pageSize);
  return qs.toString();
}

export const storefrontApi = {
  categories: () => apiRequest<CategoryNode[]>('/categories', { auth: false }),
  categoryBySlug: (slug: string) => apiRequest<CategoryPage>(`/categories/by-slug/${encodeURIComponent(slug)}`, { auth: false }),

  search: (p: SearchParams) => apiRequest<SearchResult>(`/search/products?${searchQueryString(p)}`),
  suggest: (q: string) => apiRequest<Suggestion>(`/search/suggest?q=${encodeURIComponent(q)}`, { auth: false }),
  hotKeywords: () => apiRequest<string[]>('/search/hot-keywords', { auth: false }),

  product: (id: string) => apiRequest<ProductPage>(`/products/${id}`, { auth: false }),
  recordView: (id: string, source: string) => apiRequest<null>(`/products/${id}/views?source=${source}`, { method: 'POST' }),
  related: (id: string) => apiRequest<ProductCard[]>(`/products/${id}/related`, { auth: false }),
  shopProducts: (id: string) => apiRequest<ProductCard[]>(`/products/${id}/shop-products`, { auth: false }),

  recommendations: (page: number, pageSize = 24) =>
    apiRequest<PagedResult<ProductCard>>(`/home/recommendations?page=${page}&pageSize=${pageSize}`),
  topCategories: () => apiRequest<TopCategoryProduct[]>('/home/top-categories', { auth: false }),
  mall: () => apiRequest<MallShop[]>('/home/mall', { auth: false }),
  viewed: () => apiRequest<ProductCard[]>('/viewed'),

  shop: (slug: string) => apiRequest<ShopPage>(`/shops/${encodeURIComponent(slug)}`),
  follow: (shopId: string, on: boolean) =>
    apiCommand<number>(`/shops/${shopId}/follow`, { method: on ? 'POST' : 'DELETE' }),
  followedShops: () => apiRequest<ShopSummary[]>('/account/followed-shops'),

  wishlist: (page: number, pageSize = 30) => apiRequest<PagedResult<ProductCard>>(`/account/wishlist?page=${page}&pageSize=${pageSize}`),
  wishlistIds: () => apiRequest<string[]>('/account/wishlist/ids'),
  like: (productId: string, on: boolean) =>
    apiCommand<number>(`/account/wishlist/${productId}`, { method: on ? 'POST' : 'DELETE' }),
};
