// Return disputes (sàn phân xử) and reported reviews.
import { apiCommand, apiRequest } from './http'
import type { PagedResult } from './admin'

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

export interface ReviewReport {
  id: string
  reviewId: string
  reason: string
  status: 'Pending' | 'Upheld' | 'Dismissed'
  createdAt: string
  review: Review
  productName: string
  reviewHidden: boolean
}

export interface DisputeInfo {
  id: string
  code: string
  orderCode: string
  shopName: string
  type: 'RefundOnly' | 'ReturnAndRefund'
  reason: string
  description: string
  status: string
  statusLabel: string
  requestedAmount: number
  offeredAmount: number | null
  refundAmount: number | null
  shopNote: string | null
  createdAt: string
  items: { orderItemId: string; name: string; variant: string | null; quantity: number; refundAmount: number }[]
  evidence: { party: 'Buyer' | 'Shop' | 'Admin'; type: 'Image' | 'Video'; url: string; note: string | null }[]
  history: { label: string; note: string | null; occurredAt: string }[]
  disputeReason: string | null
  disputeDecision: string | null
  disputeDecisionReason: string | null
}

export const aftercareApi = {
  disputes: (open: boolean, page: number) => apiRequest<PagedResult<DisputeInfo>>(`/admin/disputes?open=${open}&page=${page}&pageSize=20`),
  decide: (returnId: string, body: { decision: 'FavorBuyer' | 'FavorShop'; reason: string; refundAmount: number | null; requireReturn: boolean }) =>
    apiCommand<DisputeInfo>(`/admin/disputes/${returnId}/decide`, { method: 'POST', body }),
  reports: (status: ReviewReport['status'], page: number) => apiRequest<PagedResult<ReviewReport>>(`/admin/review-reports?status=${status}&page=${page}&pageSize=20`),
  resolve: (reportId: string, hide: boolean, reason: string | null) =>
    apiCommand(`/admin/review-reports/${reportId}/resolve`, { method: 'POST', body: { hide, reason } }),
}
