import { apiCommand, apiRequest } from './http'

export type VoucherType = 'Amount' | 'Percent' | 'FreeShipping' | 'CoinCashback'

export interface PlatformVoucherInput {
  code: string
  name: string
  type: VoucherType
  discountValue: number
  discountPercentBp: number
  maxDiscount: number | null
  minOrder: number
  audience: 'Everyone' | 'NewBuyer'
  categoryIds: string[]
  productIds: string[]
  startAt: string
  endAt: string
  totalQuota: number | null
  perUserLimit: number
  isPublic: boolean
  channel: 'All'
}

export interface PlatformVoucher extends Omit<PlatformVoucherInput, 'audience'> {
  id: string
  audience: string
  usedCount: number
  isActive: boolean
  state: string
}

export const promoApi = {
  vouchers: (q: string) => apiRequest<{ items: PlatformVoucher[]; totalCount: number }>(`/admin/vouchers?pageSize=100${q ? `&q=${encodeURIComponent(q)}` : ''}`),
  createVoucher: (body: PlatformVoucherInput) => apiCommand<PlatformVoucher>('/admin/vouchers', { method: 'POST', body }),
  stopVoucher: (id: string) => apiCommand<PlatformVoucher>(`/admin/vouchers/${id}/stop`, { method: 'POST' }),
  grantCoins: (userId: string, delta: number, reason: string) =>
    apiCommand<number>(`/admin/users/${userId}/coins`, { method: 'POST', body: { delta, reason } }),
}
