// Platform marketing: Flash Sale slots and registrations, banners / shortcuts / popup, campaign pages.
import { apiCommand, apiRequest } from './http'

export interface FlashItem {
  id: string
  skuId: string
  productName: string
  variant: string | null
  flashPrice: number
  basePrice: number
  quota: number
  sold: number
  perUserLimit: number
  status: 'Pending' | 'Approved' | 'Rejected'
  rejectReason: string | null
}

export interface FlashSlot {
  id: string
  startAt: string
  endAt: string
  minDiscountBp: number
  minRating: number
  categoryIds: string[]
  state: string
  items: FlashItem[]
}

export type BannerPosition = 'HomeMain' | 'HomeSide' | 'Shortcut' | 'Category' | 'Popup'

export interface Banner {
  id: string
  position: BannerPosition
  title: string
  imageUrl: string
  link: string
  categoryId: string | null
  startAt: string
  endAt: string
  sortOrder: number
  isActive: boolean
}

export interface CampaignBlock {
  type: 'Banner' | 'Vouchers' | 'FlashSale' | 'Products' | 'Registered'
  title: string | null
  imageUrl: string | null
  link: string | null
  voucherCodes: string[] | null
  keyword: string | null
  categoryId: string | null
  maxPrice: number | null
  limit: number | null
}

export interface Campaign {
  id: string
  name: string
  slug: string
  startAt: string
  endAt: string
  blocks: CampaignBlock[]
  isActive: boolean
}

export const POSITION_LABEL: Record<BannerPosition, string> = {
  HomeMain: 'Banner chính trang chủ',
  HomeSide: 'Banner phụ trang chủ',
  Shortcut: 'Lối tắt',
  Category: 'Đầu trang danh mục',
  Popup: 'Popup',
}

export type RegistrationStatus = 'Pending' | 'Approved' | 'Rejected'

export interface CampaignRegistration {
  id: string
  shopName: string
  productId: string
  productName: string
  imageUrl: string | null
  minPrice: number
  status: RegistrationStatus
  rejectReason: string | null
  createdAt: string
}

export const marketingApi = {
  slots: () => apiRequest<FlashSlot[]>('/admin/marketing/flash-slots'),
  createSlot: (body: { date: string; hour: number; minDiscountBp: number; minRating: number; categoryIds: string[] }) =>
    apiCommand<string>('/admin/marketing/flash-slots', { method: 'POST', body }),
  approve: (itemId: string) => apiCommand(`/admin/marketing/flash-items/${itemId}/approve`, { method: 'POST' }),
  reject: (itemId: string, reason: string) => apiCommand(`/admin/marketing/flash-items/${itemId}/reject`, { method: 'POST', body: { reason } }),
  banners: () => apiRequest<Banner[]>('/admin/marketing/banners'),
  saveBanner: (body: Omit<Banner, 'id'> & { id: string | null }) => apiCommand<string>('/admin/marketing/banners', { method: 'POST', body }),
  campaigns: () => apiRequest<Campaign[]>('/admin/marketing/campaigns'),
  saveCampaign: (body: Omit<Campaign, 'id'> & { id: string | null }) => apiCommand<string>('/admin/marketing/campaigns', { method: 'POST', body }),
  registrations: (campaignId: string, status?: RegistrationStatus) =>
    apiRequest<{ items: CampaignRegistration[]; totalCount: number }>(`/admin/marketing/campaigns/${campaignId}/registrations?pageSize=100${status ? `&status=${status}` : ''}`),
  decideRegistrations: (campaignId: string, body: { registrationIds: string[]; approve: boolean; reason: string | null }) =>
    apiCommand<number>(`/admin/marketing/campaigns/${campaignId}/registrations/decisions`, { method: 'POST', body }),
  broadcasts: () => apiRequest<Broadcast[]>('/admin/marketing/broadcasts'),
  sendBroadcast: (body: { title: string; body: string; link: string | null; segment: BroadcastSegment }) =>
    apiCommand<Broadcast>('/admin/marketing/broadcasts', { method: 'POST', body }),
}

export type BroadcastSegment = 'Everyone' | 'MemberGold' | 'MemberDiamond' | 'NoOrderYet'

export const SEGMENT_LABEL: Record<BroadcastSegment, string> = {
  Everyone: 'Tất cả người mua',
  MemberGold: 'Thành viên hạng Vàng (đúng hạng, không gồm Kim cương)',
  MemberDiamond: 'Thành viên hạng Kim cương',
  NoOrderYet: 'Chưa từng đặt hàng',
}

export interface Broadcast {
  id: string
  title: string
  body: string
  link: string | null
  segment: BroadcastSegment
  recipients: number
  skippedToday: number
  createdAt: string
}
