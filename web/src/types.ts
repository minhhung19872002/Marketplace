// Shared types for the buyer site — mirror the API DTOs (ids are Guid strings, money is integer VND).

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ---------- catalogue ----------

export interface CategoryNode {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  iconUrl: string | null;
  level: number;
  sortOrder: number;
  isActive: boolean;
  isLeaf: boolean;
  children: CategoryNode[];
}

export interface CategoryCrumb {
  id: string;
  name: string;
  slug: string;
}

export interface CategoryPage {
  category: CategoryCrumb;
  breadcrumb: CategoryCrumb[];
  children: CategoryCrumb[];
}

export interface ProductCard {
  id: string;
  name: string;
  slug: string;
  imageUrl: string | null;
  minPrice: number;
  maxPrice: number;
  originalPrice: number;
  discountPercent: number;
  ratingAvg: number;
  ratingCount: number;
  soldCount: number;
  inStock: boolean;
  shopId: string;
  shopName: string;
  isMall: boolean;
  isPreferred: boolean;
  provinceName: string | null;
}

// ---------- search ----------

export type ProductSort = 'Relevance' | 'Newest' | 'BestSelling' | 'PriceAsc' | 'PriceDesc';

export interface SearchParams {
  q?: string;
  categoryId?: string;
  shopId?: string;
  provinces?: string[];
  brands?: string[];
  minPrice?: number;
  maxPrice?: number;
  minRating?: number;
  mall?: boolean;
  preferred?: boolean;
  inStock?: boolean;
  condition?: string;
  /** "Attribute name=value" pairs */
  attrs?: string[];
  sort?: ProductSort;
  page?: number;
  pageSize?: number;
}

export interface FacetValue {
  value: string;
  label: string;
  count: number;
}

export interface SearchFacets {
  categories: FacetValue[];
  provinces: FacetValue[];
  brands: FacetValue[];
  ratings: FacetValue[];
  shopTypes: FacetValue[];
  conditions: FacetValue[];
  attributes: Record<string, FacetValue[]>;
}

export interface SearchResult extends PagedResult<ProductCard> {
  facets: SearchFacets;
  engine: 'meilisearch' | 'postgres';
}

export interface Suggestion {
  keywords: string[];
  products: { id: string; name: string; slug: string; imageUrl: string | null }[];
  shops: { id: string; name: string; slug: string; logoUrl: string | null; isMall: boolean }[];
}

// ---------- product page ----------

export interface ShopSummary {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  isMall: boolean;
  isPreferred: boolean;
  followerCount: number;
  productCount: number;
  joinedAt: string;
  provinceName: string | null;
  onVacation: boolean;
  vacationUntil: string | null;
}

export interface PublicOption {
  value: string;
  imageUrl: string | null;
  available: boolean;
}

export interface PublicTier {
  name: string;
  options: PublicOption[];
}

export interface PublicSku {
  id: string;
  option1: string | null;
  option2: string | null;
  price: number;
  originalPrice: number;
  available: number;
}

export interface ProductPage {
  id: string;
  name: string;
  slug: string;
  description: string;
  condition: 'New' | 'Used';
  breadcrumb: CategoryCrumb[];
  brandName: string | null;
  minPrice: number;
  maxPrice: number;
  originalMinPrice: number;
  originalMaxPrice: number;
  discountPercent: number;
  ratingAvg: number;
  ratingCount: number;
  soldCount: number;
  likeCount: number;
  totalAvailable: number;
  isPreorder: boolean;
  preorderDays: number;
  weightG: number;
  media: { type: 'Image' | 'Video'; url: string; optionValue: string | null }[];
  tiers: PublicTier[];
  skus: PublicSku[];
  attributes: { name: string; value: string }[];
  shop: ShopSummary;
  purchasable: boolean;
}

// ---------- home & shop ----------

export interface TopCategoryProduct {
  category: CategoryCrumb;
  product: ProductCard;
}

export interface MallShop {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  coverImageUrl: string | null;
  productCount: number;
}

export interface ShopTab {
  id: string;
  name: string;
  productCount: number;
}

export interface ShopPage {
  shop: ShopSummary;
  description: string;
  coverUrl: string | null;
  isFollowing: boolean;
  categories: ShopTab[];
  hasDecoration: boolean;
}

export type ShopBlockType = 'Banner' | 'Products' | 'Category' | 'Video' | 'Text';

export interface ShopHomeBlock {
  type: ShopBlockType;
  title: string | null;
  images: { url: string; link: string | null }[];
  products: ProductCard[];
  shopCategoryId: string | null;
  videoUrl: string | null;
  text: string | null;
}
