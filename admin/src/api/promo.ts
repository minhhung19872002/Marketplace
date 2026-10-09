import { apiCommand, apiRequest } from './http'

export type VoucherType = 'Amount' | 'Percent' | 'FreeShipping' | 'CoinCashback'
export type VoucherAudience = 'Everyone' | 'NewBuyer' | 'MemberGold' | 'MemberDiamond'
export type VoucherChannel = 'All' | 'Web' | 'App'

export interface PlatformVoucherInput {
  code: string
  name: string
  type: VoucherType
  discountValue: number
  discountPercentBp: number
  maxDiscount: number | null
  minOrder: number
  audience: VoucherAudience
  categoryIds: string[]
  productIds: string[]
  startAt: string
  endAt: string
  totalQuota: number | null
  perUserLimit: number
  isPublic: boolean
  channel: VoucherChannel
  /** Only shops in Freeship Xtra (free shipping) / Voucher Xtra (other types) */
  xtraOnly: boolean
}

export interface PlatformVoucher extends Omit<PlatformVoucherInput, 'audience'> {
  id: string
  audience: string
  usedCount: number
  isActive: boolean
  state: string
}

export const promoApi = {
  vouchers: (q: string, page = 1, pageSize = 20) =>
    apiRequest<{ items: PlatformVoucher[]; totalCount: number }>(`/admin/vouchers?page=${page}&pageSize=${pageSize}${q ? `&q=${encodeURIComponent(q)}` : ''}`),
  createVoucher: (body: PlatformVoucherInput) => apiCommand<PlatformVoucher>('/admin/vouchers', { method: 'POST', body }),
  stopVoucher: (id: string) => apiCommand<PlatformVoucher>(`/admin/vouchers/${id}/stop`, { method: 'POST' }),
  grantCoins: (userId: string, delta: number, reason: string) =>
    apiCommand<number>(`/admin/users/${userId}/coins`, { method: 'POST', body: { delta, reason } }),
}
