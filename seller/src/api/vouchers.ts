import { apiCommand, apiRequest } from './http'

export interface VoucherInput {
  code: string
  name: string
  type: 'Amount' | 'Percent'
  discountValue: number
  discountPercentBp: number
  maxDiscount: number | null
  minOrder: number
  audience: 'Everyone' | 'ShopFollowers'
  categoryIds: string[]
  productIds: string[]
  startAt: string
  endAt: string
  totalQuota: number | null
  perUserLimit: number
  isPublic: boolean
  channel: 'All'
}

export interface Voucher extends Omit<VoucherInput, 'type' | 'audience'> {
  id: string
  type: 'Amount' | 'Percent' | 'FreeShipping' | 'CoinCashback'
  audience: string
  usedCount: number
  stats: { claims: number; uses: number; orders: number; sales: number } | null
  isActive: boolean
  state: string
}

const base = (shopId: string) => `/seller/shops/${shopId}/vouchers`

export const voucherApi = {
  list: (shopId: string) => apiRequest<{ items: Voucher[]; totalCount: number }>(`${base(shopId)}?pageSize=100`),
  create: (shopId: string, body: VoucherInput) => apiCommand<Voucher>(base(shopId), { method: 'POST', body }),
  stop: (shopId: string, id: string) => apiCommand<Voucher>(`${base(shopId)}/${id}/stop`, { method: 'POST' }),
}
