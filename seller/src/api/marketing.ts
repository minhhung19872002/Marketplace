// Shop marketing: discount / combo / add-on / gift programmes, shop Flash Sale, platform Flash Sale registration.
import { apiCommand, apiRequest } from './http'

export type PromotionType = 'Discount' | 'Combo' | 'AddOn' | 'Gift'

export interface PickSku {
  skuId: string
  productId: string
  productName: string
  variant: string | null
  price: number
  available: number
  rating: number
}

export interface Promotion {
  id: string
  type: PromotionType
  typeLabel: string
  name: string
  startAt: string
  endAt: string
  status: 'Active' | 'Stopped'
  state: string
  productIds: string[]
  productNames: string[]
  skus: { skuId: string; productName: string; variant: string | null; price: number; basePrice: number; perUserLimit: number | null; quota: number | null; sold: number }[]
  minQuantity: number
  discountBp: number
  discountAmount: number
  maxAddOnQuantity: number
  minSpend: number
  giftSkuId: string | null
  giftName: string | null
  giftQuantity: number
}

export interface PromotionInput {
  type: PromotionType
  name: string
  startAt: string
  endAt: string
  productIds: string[]
  /** perUserLimit / quota: discount programmes only (null = no limit) */
  skus: { skuId: string; price: number; perUserLimit?: number | null; quota?: number | null }[]
  minQuantity: number
  discountBp: number
  discountAmount: number
  maxAddOnQuantity: number
  minSpend: number
  giftSkuId: string | null
  giftQuantity: number
}

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
  owner: 'Platform' | 'Shop'
  startAt: string
  endAt: string
  minDiscountBp: number
  minRating: number
  // Ngành hàng của khung (danh mục ở bất kỳ cấp nào); rỗng = mọi ngành
  categoryIds: string[]
  state: string
  items: FlashItem[]
}

export interface FlashItemInput {
  skuId: string
  flashPrice: number
  quota: number
  perUserLimit: number
}

const base = (shopId: string) => `/seller/shops/${shopId}/marketing`

export type XtraProgram = 'FreeshipXtra' | 'VoucherXtra'

export interface XtraProgramState {
  program: XtraProgram
  joined: boolean
  since: string | null
  rateBp: number
}

export const xtraApi = {
  list: (shopId: string) => apiRequest<XtraProgramState[]>(`/seller/shops/${shopId}/xtra`),
  set: (shopId: string, program: XtraProgram, join: boolean) =>
    apiCommand(`/seller/shops/${shopId}/xtra/${program}`, { method: 'PUT', body: { join } }),
}

export interface OpenCampaign { id: string; name: string; slug: string; startAt: string; endAt: string; pending: number; approved: number; rejected: number }

export interface CampaignRegistration {
  id: string
  productId: string
  productName: string
  imageUrl: string | null
  minPrice: number
  status: 'Pending' | 'Approved' | 'Rejected'
  rejectReason: string | null
  createdAt: string
}

export const marketingApi = {
  campaigns: (shopId: string) => apiRequest<OpenCampaign[]>(`${base(shopId)}/campaigns`),
  campaignRegistrations: (shopId: string, campaignId: string) =>
    apiRequest<CampaignRegistration[]>(`${base(shopId)}/campaigns/${campaignId}/registrations`),
  registerCampaign: (shopId: string, campaignId: string, productIds: string[]) =>
    apiCommand<number>(`${base(shopId)}/campaigns/${campaignId}/registrations`, { method: 'POST', body: { productIds } }),
  withdrawCampaign: (shopId: string, registrationId: string) =>
    apiCommand(`${base(shopId)}/campaign-registrations/${registrationId}`, { method: 'DELETE' }),
  skus: (shopId: string, q: string) => apiRequest<PickSku[]>(`${base(shopId)}/skus?q=${encodeURIComponent(q)}`),
  promotions: (shopId: string) => apiRequest<Promotion[]>(`${base(shopId)}/promotions`),
  createPromotion: (shopId: string, body: PromotionInput) => apiCommand<string>(`${base(shopId)}/promotions`, { method: 'POST', body }),
  updatePromotion: (shopId: string, id: string, body: PromotionInput) =>
    apiCommand(`${base(shopId)}/promotions/${id}`, { method: 'PUT', body }),
  stopPromotion: (shopId: string, id: string) => apiCommand(`${base(shopId)}/promotions/${id}/stop`, { method: 'POST' }),
  flashSales: (shopId: string) => apiRequest<FlashSlot[]>(`${base(shopId)}/flash-sales`),
  createFlashSale: (shopId: string, body: { startAt: string; endAt: string; items: FlashItemInput[] }) =>
    apiCommand<string>(`${base(shopId)}/flash-sales`, { method: 'POST', body }),
  updateFlashSale: (shopId: string, id: string, body: { startAt: string; endAt: string; items: FlashItemInput[] }) =>
    apiCommand(`${base(shopId)}/flash-sales/${id}`, { method: 'PUT', body }),
  platformSlots: (shopId: string) => apiRequest<FlashSlot[]>(`${base(shopId)}/platform-slots`),
  register: (shopId: string, slotId: string, items: FlashItemInput[]) =>
    apiCommand<number>(`${base(shopId)}/platform-slots/${slotId}/items`, { method: 'POST', body: { items } }),
}
