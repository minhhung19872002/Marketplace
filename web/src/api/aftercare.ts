// Reviews, returns / refunds, disputes — and the media upload they use.
import { ApiError, apiCommand, apiRequest, refreshSession } from './http';
import { useAuthStore } from '../stores/auth';
import type { PagedResult } from '../types';

export type MediaPurpose = 'review' | 'evidence' | 'chat' | 'avatar';

/** Multipart upload (photos are re-encoded server-side; MP4 up to 30 s). */
export async function uploadMedia(purpose: MediaPurpose, file: File, retried = false): Promise<{ id: string; url: string | null }> {
  const form = new FormData();
  form.append('file', file);
  const token = useAuthStore.getState().accessToken;
  const res = await fetch(`/api/media/${purpose}`, {
    method: 'POST',
    body: form,
    credentials: 'include',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (res.status === 401 && !retried && (await refreshSession())) return uploadMedia(purpose, file, true);
  const body = (await res.json().catch(() => null)) as { success: boolean; data: { id: string; url: string | null }; message: string } | null;
  if (!res.ok || !body?.success) throw new ApiError(res.status, body?.message ?? 'Tải tệp thất bại.');
  return body.data;
}

// ---------- reviews ----------

export const REVIEW_TAGS = ['Đúng mô tả', 'Chất lượng tốt', 'Giao hàng nhanh', 'Đóng gói cẩn thận', 'Shop phục vụ tốt', 'Đáng đồng tiền'];

export interface Review {
  id: string;
  reviewerName: string;
  rating: number;
  content: string;
  tags: string[];
  variant: string | null;
  media: { type: 'Image' | 'Video'; url: string }[];
  createdAt: string;
  edited: boolean;
  sellerReply: string | null;
  repliedAt: string | null;
}

export interface ProductReviews {
  summary: { average: number; total: number; byStar: Record<string, number>; withMedia: number; withComment: number;
    variants: { variant: string; count: number }[] | null };
  reviews: PagedResult<Review>;
}

export interface ReviewableItem {
  orderItemId: string;
  name: string;
  variant: string | null;
  imageUrl: string | null;
  quantity: number;
  review: Review | null;
  canReview: boolean;
  canEdit: boolean;
  deadline: string | null;
}

export interface ReviewInput {
  rating: number;
  content: string;
  tags: string[];
  anonymous: boolean;
  mediaAssetIds: string[];
}

export const reviewsApi = {
  forProduct: (productId: string, p: { rating?: number; withMedia?: boolean; withComment?: boolean; variant?: string; page: number }) => {
    const qs = new URLSearchParams({ page: String(p.page), pageSize: '10' });
    if (p.rating) qs.set('rating', String(p.rating));
    if (p.withMedia) qs.set('withMedia', 'true');
    if (p.withComment) qs.set('withComment', 'true');
    if (p.variant) qs.set('variant', p.variant);
    return apiRequest<ProductReviews>(`/products/${productId}/reviews?${qs}`, { auth: false });
  },
  reviewable: (code: string) => apiRequest<ReviewableItem[]>(`/orders/${encodeURIComponent(code)}/reviews`),
  write: (code: string, orderItemId: string, input: ReviewInput) =>
    apiCommand<string>(`/orders/${encodeURIComponent(code)}/items/${orderItemId}/review`, { method: 'POST', body: input }),
  edit: (reviewId: string, input: ReviewInput) => apiCommand(`/reviews/${reviewId}`, { method: 'PUT', body: input }),
  report: (reviewId: string, reason: string) => apiCommand(`/reviews/${reviewId}/report`, { method: 'POST', body: { reason } }),
};

// ---------- returns ----------

export type ReturnType = 'RefundOnly' | 'ReturnAndRefund';
export type ReturnReason = 'MissingItem' | 'WrongItem' | 'Damaged' | 'NotAsDescribed' | 'Counterfeit' | 'Other';

export const RETURN_REASONS: { value: ReturnReason; label: string }[] = [
  { value: 'MissingItem', label: 'Thiếu hàng' },
  { value: 'WrongItem', label: 'Người bán gửi sai hàng' },
  { value: 'Damaged', label: 'Hàng bể vỡ / hư hỏng' },
  { value: 'NotAsDescribed', label: 'Hàng khác với mô tả' },
  { value: 'Counterfeit', label: 'Hàng giả, hàng nhái' },
  { value: 'Other', label: 'Lý do khác' },
];

export interface Returnable {
  canReturn: boolean;
  reason: string | null;
  deadline: string | null;
  lines: { orderItemId: string; name: string; variant: string | null; imageUrl: string | null; quantity: number; returnable: number; unitRefundEstimate: number }[];
}

export interface ReturnInfo {
  id: string;
  code: string;
  orderId: string;
  orderCode: string;
  shopId: string;
  shopName: string;
  type: ReturnType;
  reason: ReturnReason;
  description: string;
  status: string;
  statusLabel: string;
  requestedAmount: number;
  requestedCoins: number;
  offeredAmount: number | null;
  refundAmount: number | null;
  refundCoins: number | null;
  shopNote: string | null;
  respondBy: string | null;
  createdAt: string;
  items: { orderItemId: string; name: string; variant: string | null; imageUrl: string | null; quantity: number; refundAmount: number; refundCoins: number }[];
  evidence: { party: 'Buyer' | 'Shop' | 'Admin'; type: 'Image' | 'Video'; url: string; note: string | null }[];
  history: { to: string; label: string; by: string; note: string | null; occurredAt: string }[];
  returnTrackingNo: string | null;
  disputeReason: string | null;
  disputeDecision: string | null;
  disputeDecisionReason: string | null;
  refundDestination: string;
}

export const returnsApi = {
  returnable: (code: string) => apiRequest<Returnable>(`/orders/${encodeURIComponent(code)}/returnable`),
  create: (code: string, body: { type: ReturnType; reason: ReturnReason; description: string; lines: { orderItemId: string; quantity: number }[]; evidenceAssetIds: string[] }) =>
    apiCommand<ReturnInfo>(`/orders/${encodeURIComponent(code)}/returns`, { method: 'POST', body }),
  mine: (page: number) => apiRequest<PagedResult<ReturnInfo>>(`/returns?page=${page}&pageSize=20`),
  get: (code: string) => apiRequest<ReturnInfo>(`/returns/${encodeURIComponent(code)}`),
  act: (code: string, operation: 'cancel' | 'accept-offer' | 'dispute', reason?: string) =>
    apiCommand<ReturnInfo>(`/returns/${encodeURIComponent(code)}/${operation}`, { method: 'POST', body: { reason: reason ?? null } }),
};
