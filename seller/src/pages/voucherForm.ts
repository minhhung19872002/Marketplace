import dayjs, { type Dayjs } from 'dayjs'
import type { Voucher, VoucherInput } from '../api/vouchers'
import { vnWallTime, vnWallTimeIso } from '../lib/datetime'

export interface VoucherFormValues {
  code: string
  name: string
  type: 'Amount' | 'Percent'
  discountValue?: number
  percent?: number
  maxDiscount?: number
  minOrder: number
  // Picked as Vietnam wall-clock times
  period: [Dayjs, Dayjs]
  totalQuota?: number
  perUserLimit: number
  isPublic: boolean
  followersOnly: boolean
  // Empty = the whole shop
  productIds: string[]
  categoryIds: string[]
}

const WALL = 'YYYY-MM-DDTHH:mm'

/** Form → API body (D6: products and categories are what the seller picked, not []). */
export const toVoucherInput = (v: VoucherFormValues): VoucherInput => ({
  code: v.code.trim().toUpperCase(),
  name: v.name.trim(),
  type: v.type,
  discountValue: v.type === 'Amount' ? v.discountValue ?? 0 : 0,
  discountPercentBp: v.type === 'Percent' ? Math.round((v.percent ?? 0) * 100) : 0,
  maxDiscount: v.type === 'Percent' ? v.maxDiscount ?? null : null,
  minOrder: v.minOrder ?? 0,
  audience: v.followersOnly ? 'ShopFollowers' : 'Everyone',
  categoryIds: v.categoryIds ?? [],
  productIds: v.productIds ?? [],
  startAt: vnWallTimeIso(v.period[0].format(WALL)),
  endAt: vnWallTimeIso(v.period[1].format(WALL)),
  totalQuota: v.totalQuota ?? null,
  perUserLimit: v.perUserLimit,
  isPublic: v.isPublic,
  channel: 'All',
})

/** An existing voucher back into the form, for "Sửa". */
export const toFormValues = (v: Voucher): VoucherFormValues => ({
  code: v.code,
  name: v.name,
  type: v.type === 'Percent' ? 'Percent' : 'Amount',
  discountValue: v.type === 'Amount' ? v.discountValue : undefined,
  percent: v.type === 'Percent' ? v.discountPercentBp / 100 : undefined,
  maxDiscount: v.type === 'Percent' ? v.maxDiscount ?? undefined : undefined,
  minOrder: v.minOrder,
  period: [dayjs(vnWallTime(v.startAt)), dayjs(vnWallTime(v.endAt))],
  totalQuota: v.totalQuota ?? undefined,
  perUserLimit: v.perUserLimit,
  isPublic: v.isPublic,
  followersOnly: v.audience === 'ShopFollowers',
  productIds: v.productIds,
  categoryIds: v.categoryIds,
})
