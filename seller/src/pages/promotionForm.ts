import dayjs, { type Dayjs } from 'dayjs'
import type { FlashItemInput, FlashSlot, Promotion, PromotionInput, PromotionType } from '../api/marketing'
import { vnWallTime, vnWallTimeIso } from '../lib/datetime'

const WALL = 'YYYY-MM-DDTHH:mm'

// Pickers show Vietnam wall-clock time
const toPeriod = (startAt: string, endAt: string): [Dayjs, Dayjs] => [dayjs(vnWallTime(startAt)), dayjs(vnWallTime(endAt))]
const fromPeriod = (p: [Dayjs, Dayjs]) => ({ startAt: vnWallTimeIso(p[0].format(WALL)), endAt: vnWallTimeIso(p[1].format(WALL)) })

export interface PromotionForm {
  type: PromotionType
  name: string
  period: [Dayjs, Dayjs]
  productIds?: string[]
  skus?: { skuId: string; price: number; perUserLimit?: number | null; quota?: number | null }[]
  minQuantity?: number
  discountPercent?: number
  maxAddOnQuantity?: number
  minSpend?: number
  giftSkuId?: string
  giftQuantity?: number
}

export const promotionInput = (v: PromotionForm): PromotionInput => ({
  type: v.type,
  name: v.name.trim(),
  ...fromPeriod(v.period),
  productIds: v.productIds ?? [],
  // Limit and quota only mean something for a discount programme (L139)
  skus: (v.skus ?? []).filter((s) => s?.skuId).map((s) => v.type === 'Discount'
    ? { skuId: s.skuId, price: s.price, perUserLimit: s.perUserLimit ?? null, quota: s.quota ?? null }
    : { skuId: s.skuId, price: s.price }),
  minQuantity: v.minQuantity ?? 0,
  discountBp: Math.round((v.discountPercent ?? 0) * 100),
  discountAmount: 0,
  maxAddOnQuantity: v.maxAddOnQuantity ?? 0,
  minSpend: v.minSpend ?? 0,
  giftSkuId: v.giftSkuId ?? null,
  giftQuantity: v.giftQuantity ?? 0,
})

/** A programme back into the form, for "Sửa" (D6). */
export const promotionFormValues = (p: Promotion): PromotionForm => ({
  type: p.type,
  name: p.name,
  period: toPeriod(p.startAt, p.endAt),
  productIds: p.productIds,
  skus: p.skus.map((s) => ({ skuId: s.skuId, price: s.price, perUserLimit: s.perUserLimit, quota: s.quota })),
  minQuantity: p.minQuantity,
  discountPercent: p.discountBp / 100,
  maxAddOnQuantity: p.maxAddOnQuantity,
  minSpend: p.minSpend,
  giftSkuId: p.giftSkuId ?? undefined,
  giftQuantity: p.giftQuantity,
})

export interface FlashForm {
  period: [Dayjs, Dayjs]
  items: FlashItemInput[]
}

export const flashInput = (v: FlashForm): { startAt: string; endAt: string; items: FlashItemInput[] } => ({ ...fromPeriod(v.period), items: v.items ?? [] })

export const flashFormValues = (s: FlashSlot): FlashForm => ({
  period: toPeriod(s.startAt, s.endAt),
  items: s.items.map((i) => ({ skuId: i.skuId, flashPrice: i.flashPrice, quota: i.quota, perUserLimit: i.perUserLimit })),
})
