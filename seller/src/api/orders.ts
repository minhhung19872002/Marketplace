import { ApiError, apiCommand, apiRequest, refreshSession } from './http'
import { useAuthStore } from '../stores/auth'

export type ShopOrderTab = 'All' | 'Unpaid' | 'ToConfirm' | 'ToShip' | 'Shipping' | 'Delivered' | 'Cancelled' | 'CancelRequests' | 'Failed'
export type OrderStatus =
  | 'PendingPayment' | 'PendingConfirmation' | 'ReadyToShip' | 'Shipping' | 'Delivered' | 'Completed' | 'Cancelled' | 'DeliveryFailed' | 'Returning' | 'Returned'

export interface ShopOrderRow {
  id: string
  code: string
  createdAt: string
  status: OrderStatus
  statusLabel: string
  paymentMethod: OrderPaymentMethod
  paymentStatus: 'Unpaid' | 'Paid' | 'Refunded'
  buyerName: string
  itemCount: number
  firstItemName: string | null
  firstItemImage: string | null
  grandTotal: number
  carrierCode: string
  trackingNo: string | null
  shipmentStatus: string | null
  hasCancelRequest: boolean
  labelPrinted: boolean
  parcelCount: number
}

export interface ShipmentEvent { status: string; label: string; location: string | null; description: string; occurredAt: string }

export interface OrderDetail {
  id: string
  code: string
  status: OrderStatus
  statusLabel: string
  paymentMethod: 'Cod' | 'Simulated' | 'Wallet' | 'VnPay' | 'MoMo' | 'ZaloPay'
  paymentStatus: string
  carrierCode: string
  carrierName: string | null
  address: { receiverName: string; phone: string; fullAddress: string }
  buyerNote: string | null
  items: { id: string; name: string; variant: string | null; imageUrl: string | null; unitPrice: number; quantity: number; lineTotal: number; shopDiscount: number }[]
  subtotal: number
  shopDiscount: number
  platformDiscount: number
  shippingFee: number
  shippingDiscount: number
  coinUsed: number
  grandTotal: number
  cancelReason: string | null
  createdAt: string
  history: { toLabel: string; actor: string; reason: string | null; occurredAt: string }[]
  shipment: { trackingNo: string; carrierName: string | null; statusLabel: string; pickupMethod: string; pickupSlot: string | null; codAmount: number; events: ShipmentEvent[] } | null
  cancelRequest: { reason: string; status: string; dueAt: string; rejectReason: string | null } | null
  // Đa kho: one parcel per ship-from warehouse (null = a single parcel)
  parcels: { no: number; warehouseName: string; provinceName: string; itemIds: string[]; shippingFee: number;
    shipment: { trackingNo: string; carrierName: string | null; statusLabel: string; codAmount: number } | null }[] | null
}

export interface ShopOrderDetail {
  order: OrderDetail
  buyerName: string
  sellerNote: string | null
  shipDeadline: string | null
}

export interface PrepareResult { orderId: string; code: string; ok: boolean; trackingNo: string | null; error: string | null }

export interface Dashboard {
  toConfirm: number
  toShip: number
  shipping: number
  cancelRequests: number
  deliveryProblems: number
  bannedProducts: number
  // Products in the "Sắp hết hàng" tab (one SKU at or under the threshold is enough)
  lowStockProducts: number
  penaltyPoints: number
  today: SalesFigure
  last7Days: SalesFigure
  last30Days: SalesFigure
  returnsPending: number
  processedToday: number
  lowStockThreshold: number
  announcements: { title: string; body: string; link: string | null; createdAt: string }[] | null
}

export interface SalesFigure { revenue: number; orders: number; views: number; visitors: number; conversionBp: number }

const base = (shopId: string) => `/seller/shops/${shopId}`

const query = (p: Record<string, string | number | undefined | null>) => {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(p)) if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
  return qs.toString()
}

export type OrderPaymentMethod = 'Cod' | 'Simulated' | 'Wallet' | 'VnPay' | 'MoMo' | 'ZaloPay'

/** The order list's filters — the list and "Xuất Excel" send the same ones (D6). */
export interface OrderFilter {
  tab: ShopOrderTab
  q?: string | null
  from?: string | null
  to?: string | null
  carrier?: string | null
  paymentMethod?: OrderPaymentMethod | null
}

const setFilters = (f: OrderFilter): Record<string, string> => {
  const out: Record<string, string> = { tab: f.tab }
  for (const k of ['q', 'from', 'to', 'carrier', 'paymentMethod'] as const) {
    const v = f[k]
    if (v !== undefined && v !== null && v !== '') out[k] = v
  }
  return out
}

export const orderListQuery = (f: OrderFilter, page: number, pageSize: number): string => query({ ...setFilters(f), page, pageSize })

export const orderExportBody = (f: OrderFilter): Record<string, string> => setFilters(f)

/** Files (PDF / Excel) come back raw, not in the JSON envelope. */
export async function download(path: string, retried = false): Promise<Blob> {
  const token = useAuthStore.getState().accessToken
  const res = await fetch(`/api${path}`, { credentials: 'include', headers: token ? { Authorization: `Bearer ${token}` } : {} })
  if (res.status === 401 && !retried && (await refreshSession())) return download(path, true)
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { message?: string } | null
    throw new ApiError(res.status, body?.message ?? 'Không tải được tệp.')
  }
  return res.blob()
}

export const ordersApi = {
  dashboard: (shopId: string) => apiRequest<Dashboard>(`${base(shopId)}/dashboard`),
  list: (shopId: string, f: OrderFilter, page: number, pageSize: number) =>
    apiRequest<{ items: ShopOrderRow[]; totalCount: number }>(`${base(shopId)}/orders?${orderListQuery(f, page, pageSize)}`),
  get: (shopId: string, id: string) => apiRequest<ShopOrderDetail>(`${base(shopId)}/orders/${id}`),
  pickupSlots: () => apiRequest<string[]>('/seller/pickup-slots'),
  prepare: (shopId: string, orderIds: string[], pickupMethod: 'Pickup' | 'DropOff', pickupSlot: string | null) =>
    apiCommand<PrepareResult[]>(`${base(shopId)}/orders/prepare`, { method: 'POST', body: { orderIds, pickupMethod, pickupSlot } }),
  cancel: (shopId: string, id: string, reason: string) => apiCommand(`${base(shopId)}/orders/${id}/cancel`, { method: 'POST', body: { reason } }),
  decide: (shopId: string, id: string, approve: boolean, rejectReason: string | null) =>
    apiCommand(`${base(shopId)}/orders/${id}/cancel-request`, { method: 'POST', body: { approve, rejectReason } }),
  note: (shopId: string, id: string, note: string) => apiCommand(`${base(shopId)}/orders/${id}/note`, { method: 'PUT', body: { note } }),
  labels: (shopId: string, ids: string[], size: 'A6' | 'A5' = 'A6') =>
    download(`${base(shopId)}/orders/labels?${ids.map((i) => `ids=${i}`).join('&')}&size=${size}`),
  // The carrier's own label (GHTK); 404 when the carrier uses ShopHub's label
  carrierLabel: (shopId: string, id: string, packageNo?: number) =>
    download(`${base(shopId)}/orders/${id}/carrier-label${packageNo ? `?package=${packageNo}` : ''}`),
  pickingList: (shopId: string, ids: string[]) => download(`${base(shopId)}/orders/picking-list?${ids.map((i) => `ids=${i}`).join('&')}`),
  // Xuất Excel runs in the background (6.4): start, follow the task, then download its file
  startExport: (shopId: string, f: OrderFilter) =>
    apiCommand<{ id: string; status: string; message: string | null }>(`${base(shopId)}/orders/export-tasks`, { method: 'POST', body: orderExportBody(f) }),
  exportTask: (shopId: string, taskId: string) =>
    apiRequest<{ id: string; status: string; message: string | null }>(`${base(shopId)}/bulk/tasks/${taskId}`),
  exportFile: (shopId: string, taskId: string) => download(`${base(shopId)}/tasks/${taskId}/file`),
}
