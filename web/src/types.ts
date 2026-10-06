// Shared domain types for the buyer site (mirrors the mock data shape until the API lands)

export interface Category {
  id: string;
  name: string;
  icon: string;
}

export interface FeatureShortcut {
  id: string;
  label: string;
  icon: string;
  color: string;
}

export interface MallBrand {
  id: string;
  name: string;
  keyword: string;
}

export interface BannerSlide {
  id: number;
  title: string;
  subtitle: string;
  bg: string;
}

export interface VariantSet {
  label: string;
  options: string[];
}

export interface Review {
  id: string;
  name: string;
  rating: number;
  text: string;
  date: string;
}

export interface Product {
  id: number;
  name: string;
  image: string;
  gallery: string[];
  fallbackImage: string;
  keyword: string;
  price: number;
  originalPrice: number;
  discount: number;
  sold: number;
  rating: number;
  ratingCount: number;
  liked: number;
  stock: number;
  location: string;
  categoryId: string;
  categoryName: string;
  shopName: string;
  isMall: boolean;
  isPreferred: boolean;
  freeship: boolean;
  hasVoucher: boolean;
  variant: VariantSet | null;
  reviews: Review[];
  specs: [string, string][];
  description: string;
}

export interface FlashSaleProduct extends Product {
  flashStock: number;
  flashSold: number;
}

export interface CartLine extends Product {
  cartKey: string;
  selectedVariant: string;
  quantity: number;
}

export interface User {
  name: string;
  joinedAt: string;
}
