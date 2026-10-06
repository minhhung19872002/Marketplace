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
  skus: { skuId: string; productName: string; variant: string | null; price: number; basePrice: number }[]
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
  skus: { skuId: string; price: number }[]
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

export const marketingApi = {
  skus: (shopId: string, q: string) => apiRequest<PickSku[]>(`${base(shopId)}/skus?q=${encodeURIComponent(q)}`),
  promotions: (shopId: string) => apiRequest<Promotion[]>(`${base(shopId)}/promotions`),
  createPromotion: (shopId: string, body: PromotionInput) => apiCommand<string>(`${base(shopId)}/promotions`, { method: 'POST', body }),
  stopPromotion: (shopId: string, id: string) => apiCommand(`${base(shopId)}/promotions/${id}/stop`, { method: 'POST' }),
  flashSales: (shopId: string) => apiRequest<FlashSlot[]>(`${base(shopId)}/flash-sales`),
  createFlashSale: (shopId: string, body: { startAt: string; endAt: string; items: FlashItemInput[] }) =>
    apiCommand<string>(`${base(shopId)}/flash-sales`, { method: 'POST', body }),
  platformSlots: (shopId: string) => apiRequest<FlashSlot[]>(`${base(shopId)}/platform-slots`),
  register: (shopId: string, slotId: string, items: FlashItemInput[]) =>
    apiCommand<number>(`${base(shopId)}/platform-slots/${slotId}/items`, { method: 'POST', body: { items } }),
}
