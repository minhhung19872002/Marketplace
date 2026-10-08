// Phase 12 — reports, user detail, penalties, orders, catalog extras, content, providers (spec VI)
import { ApiError, apiCommand, apiRequest, refreshSession } from './http'
import type { PagedResult } from './admin'
import { useAuthStore } from '../stores/auth'

const query = (params: Record<string, string | number | boolean | undefined | null>) => {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
  const s = qs.toString()
  return s ? `?${s}` : ''
}

async function raw(path: string, retried = false): Promise<Response> {
  const token = useAuthStore.getState().accessToken
  const res = await fetch(`/api${path}`, { credentials: 'include', headers: token ? { Authorization: `Bearer ${token}` } : {} })
  if (res.status === 401 && !retried && (await refreshSession())) return raw(path, true)
  return res
}

/** A file endpoint (Excel / PDF); the server's Vietnamese error message when it refuses. */
async function file(path: string): Promise<Blob> {
  const res = await raw(path)
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { message?: string } | null
    throw new ApiError(res.status, body?.message ?? 'Không tải được tệp.')
  }
  return res.blob()
}

// ---------- reports ----------

export type Granularity = 'Day' | 'Week' | 'Month'
export type ReportKind =
  | 'GmvByTime' | 'GmvByCategory' | 'GmvByProvince' | 'TopShops' | 'TopProducts' | 'Vouchers' | 'FlashSales' | 'Cancellations' | 'Returns' | 'Users' | 'Funnel'
export type CellKind = 'Text' | 'Integer' | 'Money' | 'Percent'

export const REPORTS: { kind: ReportKind; label: string }[] = [
  { kind: 'GmvByTime', label: 'GMV theo thời gian' },
  { kind: 'GmvByCategory', label: 'GMV theo ngành hàng' },
  { kind: 'GmvByProvince', label: 'GMV theo tỉnh / thành' },
  { kind: 'TopShops', label: 'Top shop' },
  { kind: 'TopProducts', label: 'Top sản phẩm' },
  { kind: 'Vouchers', label: 'Hiệu quả voucher' },
  { kind: 'FlashSales', label: 'Hiệu quả Flash Sale' },
  { kind: 'Cancellations', label: 'Tỉ lệ huỷ theo shop & lý do' },
  { kind: 'Returns', label: 'Trả hàng / hoàn tiền' },
  { kind: 'Users', label: 'Người dùng mới & quay lại' },
  { kind: 'Funnel', label: 'Phễu chuyển đổi' },
]

export interface ReportTable {
  title: string
  subtitle: string
  columns: { title: string; kind: CellKind }[]
  rows: (string | number)[][]
  totals: (string | number)[] | null
}

export interface ChartPoint { label: string; value: number; value2: number | null }

export interface ReportResult {
  table: ReportTable
  chart: { kind: 'line' | 'bar' | 'funnel'; series: string; series2: string | null; points: ChartPoint[] }
}

export interface Kpis { gmv: number; orders: number; newBuyers: number; newShops: number; cancelRateBp: number; returnRateBp: number; feeRevenue: number }

export interface Overview {
  period: string
  current: Kpis
  previous: Kpis
  gmvSeries: ChartPoint[]
  orderSeries: ChartPoint[]
  pending: { shopsToReview: number; productsToReview: number; openDisputes: number; pendingWithdrawals: number; openProductReports: number; openReviewReports: number }
  // Configuration that needs an admin soon (e.g. no holidays for next year)
  warnings: string[] | null
}

export interface Range { from?: string; to?: string; granularity: Granularity }

export const reportsApi = {
  overview: (r: Range) => apiRequest<Overview>(`/admin/reports/overview${query({ ...r })}`),
  report: (kind: ReportKind, r: Range) => apiRequest<ReportResult>(`/admin/reports/${kind}${query({ ...r })}`),
  export: (kind: ReportKind, r: Range, format: 'Xlsx' | 'Pdf') => file(`/admin/reports/${kind}/export${query({ ...r, format })}`),
}

// ---------- users ----------

export interface UserDetail {
  id: string
  fullName: string
  phone: string | null
  email: string | null
  status: string
  lockReason: string | null
  createdAt: string
  lastLoginAt: string | null
  roles: string[]
  orderCount: number
  spent: number
  reviewCount: number
  reportedReviews: number
  returnCount: number
  recentOrders: { code: string; shopName: string; status: string; grandTotal: number; createdAt: string }[]
  devices: { device: string | null; ip: string | null; signedInAt: string; expiresAt: string; active: boolean }[]
  ownedShops: string[]
}

// ---------- penalties ----------

export type PenaltyLevel = 'None' | 'Restricted' | 'CampaignBan' | 'Locked'

export interface PenaltyStatus { points: number; level: PenaltyLevel; consequence: string; restrictAt: number; campaignBanAt: number; lockAt: number }

export interface Penalty {
  id: string
  points: number
  reason: string
  orderCode: string | null
  createdAt: string
  expiresAt: string | null
  givenBy: string | null
  revokedAt: string | null
  revokeReason: string | null
  counts: boolean
}

// ---------- orders ----------

export interface AdminOrderRow {
  id: string
  code: string
  shopName: string
  buyerName: string
  status: string
  paymentStatus: string
  paymentMethod: string
  grandTotal: number
  createdAt: string
}

export interface AdminOrderDetail {
  order: AdminOrderRow
  subtotal: number
  shopDiscount: number
  platformDiscount: number
  shippingFee: number
  shippingDiscount: number
  coinUsed: number
  cancelReason: string | null
  // paid: after every allocated discount; refundable: units not refunded and not in an open return
  lines: { id: string; name: string; variant: string | null; unitPrice: number; quantity: number; lineTotal: number; paid: number; refundable: number }[]
  history: { from: string | null; to: string; actor: string; actorName: string | null; reason: string | null; at: string }[]
  payments: { id: string; method: string; status: string; amount: number; providerTxnId: string | null; createdAt: string; paidAt: string | null; failureReason: string | null }[]
  refunds: { id: string; amount: number; destination: string; status: string; reason: string; providerRef: string | null; createdAt: string }[]
  shipments: { trackingNo: string; carrierCode: string; direction: string; status: string; events: { status: string; description: string; location: string | null; at: string }[] }[]
  returns: string[]
}

// ---------- catalog ----------

export interface Brand { id: string; name: string; slug: string; logoUrl: string | null; isVerified: boolean; productCount: number }

export type ProductReportReason = 'Counterfeit' | 'Prohibited' | 'WrongInfo' | 'Offensive' | 'IntellectualProperty' | 'Other'

export const REPORT_REASON_LABEL: Record<ProductReportReason, string> = {
  Counterfeit: 'Hàng giả, hàng nhái',
  Prohibited: 'Hàng cấm',
  WrongInfo: 'Thông tin sai lệch',
  Offensive: 'Nội dung phản cảm',
  IntellectualProperty: 'Vi phạm sở hữu trí tuệ',
  Other: 'Khác',
}

export interface ProductReport {
  id: string
  productId: string
  productName: string
  shopName: string
  reason: ProductReportReason
  details: string | null
  reporterName: string
  status: 'Open' | 'Banned' | 'Dismissed'
  resolution: string | null
  createdAt: string
  openReportsOnProduct: number
}

// ---------- content ----------

export type CmsKind = 'Page' | 'Help'

export interface CmsPage { id: string; kind: CmsKind; slug: string; title: string; content: string; topic: string | null; sortOrder: number; isPublished: boolean; updatedAt: string }

export interface MessageTemplate { id: string; key: string; channel: 'Sms' | 'Email' | 'InApp'; name: string; subject: string | null; body: string; placeholders: string; updatedAt: string }

// ---------- providers ----------

export interface CarrierRow {
  id: string
  code: string
  name: string
  provider: string
  providerConfigured: boolean
  isActive: boolean
  supportsCod: boolean
  sameProvinceOnly: boolean
  daysSameProvince: number
  daysSameRegion: number
  daysCrossRegion: number
}

export interface GatewayRow { method: string; name: string; provider: string; enabled: boolean }

export type ChatReportStatus = 'Open' | 'Dismissed' | 'Penalized'

export interface ChatReportRow {
  id: string
  conversationId: string
  shopId: string
  shopName: string
  reporterName: string
  reason: string
  status: ChatReportStatus
  resolution: string | null
  createdAt: string
  resolvedAt: string | null
  reportsOnConversation: number
}

export interface ChatReviewMessage {
  id: string
  senderRole: 'Buyer' | 'Shop' | 'System'
  type: 'Text' | 'Image' | 'Product' | 'Order' | 'Voucher'
  body: string
  flagged: boolean
  createdAt: string
}

export interface ChatReportDetail {
  report: ChatReportRow
  messages: ChatReviewMessage[]
  totalMessages: number
}

export const platformApi = {
  user: (id: string) => apiRequest<UserDetail>(`/admin/users/${id}`),
  divisions: (parent?: string) => apiRequest<{ code: string; name: string; level: 'Province' | 'District' | 'Ward'; parentCode: string | null; childCount: number; addressCount: number; isActive: boolean }[]>(`/admin/divisions${parent ? `?parent=${encodeURIComponent(parent)}` : ''}`),
  addDivision: (body: { code: string; name: string; parentCode: string | null }) => apiCommand('/admin/divisions', { method: 'POST', body }),
  renameDivision: (code: string, name: string) => apiCommand(`/admin/divisions/${encodeURIComponent(code)}`, { method: 'PUT', body: { name } }),
  resetPassword: (id: string) => apiCommand<{ temporaryPassword: string }>(`/admin/users/${id}/reset-password`, { method: 'POST' }),

  penalties: (shopId: string) => apiRequest<{ status: PenaltyStatus; items: Penalty[] }>(`/admin/shops/${shopId}/penalties`),
  addPenalty: (shopId: string, body: { points: number; reason: string; expiresInDays: number | null }) =>
    apiCommand<PenaltyStatus>(`/admin/shops/${shopId}/penalties`, { method: 'POST', body }),
  revokePenalty: (id: string, reason: string) => apiCommand<PenaltyStatus>(`/admin/shop-penalties/${id}/revoke`, { method: 'POST', body: { reason } }),

  orders: (p: { q?: string; status?: string; page: number }) => apiRequest<PagedResult<AdminOrderRow>>(`/admin/orders${query({ ...p, pageSize: 20 })}`),
  order: (code: string) => apiRequest<AdminOrderDetail>(`/admin/orders/${encodeURIComponent(code)}`),
  cancelOrder: (code: string, reason: string) => apiCommand(`/admin/orders/${encodeURIComponent(code)}/cancel`, { method: 'POST', body: { reason } }),
  manualRefund: (code: string, body: { lines: { orderItemId: string; quantity: number }[]; amount: number; platformBorne: boolean; reason: string }) =>
    apiCommand(`/admin/orders/${encodeURIComponent(code)}/manual-refund`, { method: 'POST', body }),
  resolveRefund: (id: string, toWallet: boolean, reason: string) =>
    apiCommand<string>(`/admin/refunds/${id}/resolve`, { method: 'POST', body: { toWallet, reason } }),

  brands: (q: string, page: number) => apiRequest<PagedResult<Brand>>(`/admin/brands${query({ q, page, pageSize: 50 })}`),
  updateBrand: (id: string, body: { name: string; logoUrl: string | null; isVerified: boolean }) => apiCommand(`/admin/brands/${id}`, { method: 'PUT', body }),
  moveCategory: (id: string, parentId: string | null, sortOrder: number) =>
    apiCommand(`/admin/categories/${id}/move`, { method: 'PUT', body: { parentId, sortOrder } }),
  bulkBan: (productIds: string[], reason: string) => apiCommand<number>('/admin/products/bulk-ban', { method: 'POST', body: { productIds, reason } }),
  productReports: (status: string | undefined, page: number) =>
    apiRequest<PagedResult<ProductReport>>(`/admin/product-reports${query({ status, page, pageSize: 20 })}`),
  resolveReport: (id: string, ban: boolean, resolution: string | null) =>
    apiCommand(`/admin/product-reports/${id}/resolve`, { method: 'POST', body: { ban, resolution } }),

  chatReports: (status: ChatReportStatus | undefined, page: number) =>
    apiRequest<PagedResult<ChatReportRow>>(`/admin/chat-reports${query({ status, page, pageSize: 20 })}`),
  chatReport: (id: string) => apiRequest<ChatReportDetail>(`/admin/chat-reports/${id}`),
  resolveChatReport: (id: string, resolution: string, penaltyPoints: number | null) =>
    apiCommand(`/admin/chat-reports/${id}/resolve`, { method: 'POST', body: { resolution, penaltyPoints } }),

  cms: () => apiRequest<CmsPage[]>('/admin/cms'),
  saveCms: (body: Omit<CmsPage, 'id' | 'updatedAt'> & { id: string | null }) => apiCommand<string>('/admin/cms', { method: 'POST', body }),
  templates: () => apiRequest<MessageTemplate[]>('/admin/message-templates'),
  saveTemplate: (id: string, subject: string | null, body: string) =>
    apiCommand(`/admin/message-templates/${id}`, { method: 'PUT', body: { subject, body } }),

  // Xuất nhật ký runs in the background (6.4): start, follow the task, then download its file
  startAuditExport: (p: { userId?: string; action?: string; entity?: string; entityId?: string; from?: string; to?: string }) =>
    apiCommand<{ id: string; status: string; message: string | null }>('/admin/audit-logs/export-tasks', { method: 'POST', body: p }),
  myTask: (id: string) => apiRequest<{ id: string; status: string; message: string | null }>(`/admin/my-tasks/${id}`),
  myTaskFile: (id: string) => file(`/admin/my-tasks/${id}/file`),

  providers: () => apiRequest<{ carriers: CarrierRow[]; gateways: GatewayRow[] }>('/admin/providers'),
  updateCarrier: (c: CarrierRow) => apiCommand(`/admin/carriers/${c.id}`, {
    method: 'PUT',
    body: { id: c.id, name: c.name, description: null, isActive: c.isActive, supportsCod: c.supportsCod, daysSameProvince: c.daysSameProvince,
      daysSameRegion: c.daysSameRegion, daysCrossRegion: c.daysCrossRegion },
  }),
  setGateway: (method: string, enabled: boolean) => apiCommand(`/admin/gateways/${method}`, { method: 'PUT', body: { enabled } }),
}

/** Save a downloaded file under its name. */
export const saveBlob = (blob: Blob, name: string) => {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = name
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 10_000)
}
