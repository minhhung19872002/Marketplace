// Banners, Flash Sale (server time for countdowns), product deals, campaign pages, membership and daily check-in.
import { apiRequest } from './http';
import type { ProductCard } from '../types';

export interface PublicBanner {
  id: string;
  title: string;
  imageUrl: string;
  link: string;
}

export interface HomeBanners {
  main: PublicBanner[];
  side: PublicBanner[];
  shortcuts: PublicBanner[];
  popup: PublicBanner | null;
  popupFrequencyHours: number;
  pinnedKeywords: string[];
}

export interface FlashBoardItem {
  itemId: string;
  productId: string;
  skuId: string;
  name: string;
  imageUrl: string | null;
  flashPrice: number;
  basePrice: number;
  discountPercent: number;
  quota: number;
  sold: number;
  soldPercent: number;
  perUserLimit: number;
}

export interface FlashBoardSlot {
  id: string;
  startAt: string;
  endAt: string;
  running: boolean;
}

export interface FlashBoard {
  serverTime: string;
  slot: FlashBoardSlot | null;
  upcoming: FlashBoardSlot[];
  items: FlashBoardItem[];
}

export interface ProductDeals {
  serverTime: string;
  skus: { skuId: string; price: number; basePrice: number; label: string | null; endsAt: string | null }[];
  flash: { itemId: string; startAt: string; endAt: string; quota: number; sold: number; perUserLimit: number; platform: boolean } | null;
  offers: { promotionId: string; type: 'Combo' | 'AddOn' | 'Gift'; name: string; text: string }[];
}

export interface CampaignVoucher {
  id: string;
  code: string;
  name: string;
  type: string;
  discountValue: number;
  discountPercentBp: number;
  maxDiscount: number | null;
  minOrder: number;
  endAt: string;
}

export interface CampaignBlock {
  type: 'Banner' | 'Vouchers' | 'FlashSale' | 'Products' | 'Registered';
  title: string | null;
  imageUrl: string | null;
  link: string | null;
  vouchers: CampaignVoucher[] | null;
  flashSale: FlashBoard | null;
  products: ProductCard[] | null;
}

export interface CampaignPage {
  name: string;
  slug: string;
  startAt: string;
  endAt: string;
  serverTime: string;
  blocks: CampaignBlock[];
}

export interface Membership {
  tier: 'Silver' | 'Gold' | 'Diamond';
  tierLabel: string;
  spend: number;
  windowDays: number;
  nextTier: 'Gold' | 'Diamond' | null;
  nextTierSpend: number | null;
}

export interface CheckInStatus {
  doneToday: boolean;
  streak: number;
  days: { day: number; coins: number; done: boolean; today: boolean }[];
}

export interface ShopOffer {
  id: string;
  type: string;
  name: string;
  text: string;
  endAt: string;
  productCount: number;
}

export const marketingApi = {
  shopOffers: (shopId: string) => apiRequest<ShopOffer[]>(`/shops/${shopId}/offers`, { auth: false }),
  banners: () => apiRequest<HomeBanners>('/home/banners', { auth: false }),
  flashSale: (slotId?: string) => apiRequest<FlashBoard>(`/flash-sale${slotId ? `?slotId=${slotId}` : ''}`, { auth: false }),
  deals: (productId: string) => apiRequest<ProductDeals>(`/products/${productId}/deals`, { auth: false }),
  campaign: (slug: string) => apiRequest<CampaignPage>(`/campaigns/${encodeURIComponent(slug)}`, { auth: false }),
  membership: () => apiRequest<Membership>('/account/membership'),
  checkIn: () => apiRequest<CheckInStatus>('/account/check-in'),
  doCheckIn: () => apiRequest<CheckInStatus>('/account/check-in', { method: 'POST' }),
};

/** Milliseconds the browser clock is ahead of the server (countdowns use server time, spec 3.10). */
export const clockSkew = (serverTime: string, receivedAt: number): number => receivedAt - Date.parse(serverTime);
