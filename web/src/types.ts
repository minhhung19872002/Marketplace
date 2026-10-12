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
  // Products on sale in the subtree
  productCount: number;
  // Shown to buyers (G2-A7): hidden industries stay out of every menu, filter and list
  isVisible: boolean;
}

export interface CategoryCrumb {
  id: string;
  name: string;
  slug: string;
}

export interface CategoryPage {
  category: CategoryCrumb;
  breadcrumb: CategoryCrumb[];
  // Sub-categories with a round picture (G5-B): the admin's icon, else a photo of the best seller below it
  children: (CategoryCrumb & { imageUrl: string | null })[];
  // Banner ngành and thương hiệu nổi bật (II.2)
  banners: { id: string; title: string; imageUrl: string; link: string }[] | null;
  brands: { id: string; name: string; slug: string; logoUrl: string | null; isVerified: boolean; productCount: number }[] | null;
  // Shop chính hãng: the Mall shops selling in this category, apart from the product brands (G-VIS round 3)
  shops?: { id: string; name: string; slug: string; logoUrl: string | null; wordmarkUrl: string | null; productCount: number }[] | null;
  // false: hidden from buyers for now — the page says "Danh mục đang cập nhật" (G2-A7)
  isVisible: boolean;
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
  /** The shown price is a Flash Sale price */
  isFlashSale?: boolean;
  // Tags under the name backed by data (combo / add-on / gift programme, Freeship+)
  labels?: string[] | null;
  // Campaign frame laid over the photo (product taking part in a running campaign) — on the campaign page only;
  // elsewhere a small chip with the campaign's name (G3 B3)
  frameUrl?: string | null;
  campaignName?: string | null;
  // The photo is an ad cover with text on it: no campaign strip over it; photoUrl is the plain photo a grid shows instead
  // once it holds its share of covers (G-VIS round 5, L203)
  imageHasText?: boolean;
  photoUrl?: string | null;
}

// ---------- search ----------

export type ProductSort = 'Relevance' | 'Newest' | 'BestSelling' | 'PriceAsc' | 'PriceDesc';

export interface SearchParams {
  q?: string;
  categoryId?: string;
  shopId?: string;
  provinces?: string[];
  brands?: string[];
  // Shop filter group (G-VIS round 5, L208)
  shops?: string[];
  minPrice?: number;
  maxPrice?: number;
  minRating?: number;
  mall?: boolean;
  preferred?: boolean;
  inStock?: boolean;
  condition?: string;
  /** "Attribute name=value" pairs */
  attrs?: string[];
  carriers?: string[];
  freeship?: boolean;
  voucher?: boolean;
  cod?: boolean;
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
  carriers: FacetValue[] | null;
  // "freeship", "voucher", "cod"
  services: FacetValue[] | null;
  // The shops selling the results — a Mall shop's house brand is here, not under brands (L208)
  shops?: FacetValue[] | null;
}

export interface SearchResult extends PagedResult<ProductCard> {
  facets: SearchFacets;
  engine: 'meilisearch' | 'postgres';
}

export interface Suggestion {
  keywords: string[];
  products: { id: string; name: string; slug: string; imageUrl: string | null }[];
  shops: { id: string; name: string; slug: string; logoUrl: string | null; isMall: boolean; wordmarkUrl?: string | null }[];
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
  // Latest sign-in of the shop's staff or chat answer ("Online … trước")
  lastActiveAt: string | null;
  // Shop rating over its reviews (recomputed on the server)
  ratingAvg: number;
  ratingCount: number;
  // The wide logo (mark + full name on one line), null when the shop has none (G-VIS round 2)
  wordmarkUrl?: string | null;
}

export interface ShippingDestination {
  addressId: string | null;
  provinceCode: string;
  label: string;
}

export interface ShippingEstimate {
  destination: ShippingDestination;
  fromProvinceName: string | null;
  options: { code: string; name: string; fee: number; days: number; expectedDate: string; supportsCod: boolean }[];
  myAddresses: ShippingDestination[];
}

export interface RelatedShop {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  isMall: boolean;
  isPreferred: boolean;
  ratingAvg: number;
  followerCount: number;
  productCount: number;
  provinceName: string | null;
  // The shop's one logo where it has the wide one (G-VIS round 3)
  wordmarkUrl?: string | null;
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
  /** Giới hạn mua mỗi người (all variants together) */
  maxPerBuyer?: number | null;
}

// ---------- home & shop ----------

export interface TopCategoryProduct {
  category: CategoryCrumb;
  product: ProductCard;
  // Units sold in the last 30 days (orders not cancelled)
  monthlySold: number;
}

export interface MallShop {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  coverImageUrl: string | null;
  productCount: number;
  // Deepest real discount among the shop's SKUs on sale (0 = none)
  maxDiscountPercent: number;
  // The shop's real running programme ("Mua 2 giảm 10%", "Freeship+ cho đơn của shop"…), null when none (G-VIS)
  offer?: string | null;
  // The wide logo of the pill (G-VIS round 2); null → the tile writes the name
  wordmarkUrl?: string | null;
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
