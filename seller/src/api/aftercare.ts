// Reviews (reply) and return / refund requests of the current shop.
import { apiCommand, apiRequest } from './http'
import type { PagedResult } from './seller'

export interface Review {
  id: string
  reviewerName: string
  rating: number
  content: string
  tags: string[]
  variant: string | null
  media: { type: 'Image' | 'Video'; url: string }[]
  createdAt: string
  edited: boolean
  sellerReply: string | null
  repliedAt: string | null
}

export interface ShopReview {
  review: Review
  productId: string
  productName: string
  orderCode: string
}

export type ReturnStatus =
  | 'Requested' | 'PartialOffered' | 'Rejected' | 'Disputed' | 'AwaitingReturn' | 'Returning' | 'AwaitingShopCheck' | 'Refunded' | 'Closed' | 'Cancelled'

export interface ReturnInfo {
  id: string
  code: string
  orderId: string
  orderCode: string
  shopName: string
  type: 'RefundOnly' | 'ReturnAndRefund'
  reason: string
  description: string
  status: ReturnStatus
  statusLabel: string
  requestedAmount: number
  requestedCoins: number
  offeredAmount: number | null
  refundAmount: number | null
  refundCoins: number | null
  shopNote: string | null
  respondBy: string | null
  createdAt: string
  items: { orderItemId: string; name: string; variant: string | null; imageUrl: string | null; quantity: number; refundAmount: number; refundCoins: number }[]
  evidence: { party: 'Buyer' | 'Shop' | 'Admin'; type: 'Image' | 'Video'; url: string; note: string | null }[]
  history: { to: string; label: string; by: string; note: string | null; occurredAt: string }[]
  returnTrackingNo: string | null
  disputeReason: string | null
  disputeDecision: string | null
  disputeDecisionReason: string | null
  refundDestination: string
}

export type ShopReturnAction = 'Approve' | 'Reject' | 'OfferPartial' | 'ConfirmReceived'

export const RETURN_REASON_LABELS: Record<string, string> = {
  MissingItem: 'Thiếu hàng',
  WrongItem: 'Gửi sai hàng',
  Damaged: 'Hàng bể vỡ / hư hỏng',
  NotAsDescribed: 'Khác với mô tả',
  Counterfeit: 'Hàng giả, hàng nhái',
  Other: 'Lý do khác',
}

const base = (shopId: string) => `/seller/shops/${shopId}`

export const aftercareApi = {
  reviews: (shopId: string, p: { rating?: number; replied?: boolean; page: number }) => {
    const qs = new URLSearchParams({ page: String(p.page), pageSize: '20' })
    if (p.rating) qs.set('rating', String(p.rating))
    if (p.replied !== undefined) qs.set('replied', String(p.replied))
    return apiRequest<PagedResult<ShopReview>>(`${base(shopId)}/reviews?${qs}`)
  },
  reply: (shopId: string, reviewId: string, text: string) => apiCommand(`${base(shopId)}/reviews/${reviewId}/reply`, { method: 'POST', body: { text } }),
  returns: (shopId: string, status: ReturnStatus | undefined, page: number) =>
    apiRequest<PagedResult<ReturnInfo>>(`${base(shopId)}/returns?page=${page}&pageSize=20${status ? `&status=${status}` : ''}`),
  act: (shopId: string, returnId: string, body: { action: ShopReturnAction; note?: string; amount?: number; restock?: boolean; evidenceAssetIds?: string[] }) =>
    apiCommand<ReturnInfo>(`${base(shopId)}/returns/${returnId}/actions`, { method: 'POST', body }),
}
