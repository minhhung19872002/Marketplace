// The one map from a status to its label and tone. Every list renders statuses through <StatusTag> with one of these.
export type Tone = 'success' | 'processing' | 'warning' | 'error' | 'info' | 'default'

export interface StatusInfo {
  label: string
  tone: Tone
}

export type StatusMap = Record<string, StatusInfo>

/** CSS class per tone (App.css): text / background pairs from the theme tokens that reach WCAG AA in light and dark mode. */
export const TONE_CLASS: Record<Tone, string> = {
  success: 'tone-success',
  processing: 'tone-processing',
  warning: 'tone-warning',
  error: 'tone-error',
  info: 'tone-info',
  default: 'tone-default',
}

export const ORDER_STATUS: StatusMap = {
  PendingPayment: { label: 'Chờ thanh toán', tone: 'warning' },
  PendingConfirmation: { label: 'Chờ xác nhận', tone: 'warning' },
  ReadyToShip: { label: 'Chờ lấy hàng', tone: 'processing' },
  Shipping: { label: 'Đang giao', tone: 'processing' },
  Delivered: { label: 'Đã giao', tone: 'info' },
  Completed: { label: 'Hoàn thành', tone: 'success' },
  Cancelled: { label: 'Đã huỷ', tone: 'default' },
  DeliveryFailed: { label: 'Giao thất bại', tone: 'error' },
  Returning: { label: 'Đang hoàn về', tone: 'warning' },
  Returned: { label: 'Đã hoàn về', tone: 'default' },
}

export const ORDER_PAYMENT_STATUS: StatusMap = {
  Unpaid: { label: 'Chưa trả', tone: 'warning' },
  Paid: { label: 'Đã trả', tone: 'success' },
  Refunded: { label: 'Đã hoàn', tone: 'default' },
}

export const PAYMENT_STATUS: StatusMap = {
  Initiated: { label: 'Khởi tạo', tone: 'processing' },
  Succeeded: { label: 'Thành công', tone: 'success' },
  Failed: { label: 'Thất bại', tone: 'error' },
  Expired: { label: 'Hết hạn', tone: 'default' },
  Refunded: { label: 'Đã hoàn', tone: 'default' },
}

export const REFUND_STATUS: StatusMap = {
  Pending: { label: 'Đang xử lý', tone: 'warning' },
  Succeeded: { label: 'Thành công', tone: 'success' },
  Failed: { label: 'Thất bại', tone: 'error' },
}

export const PAYMENT_METHOD: Record<string, string> = {
  Cod: 'COD',
  Simulated: 'Cổng giả lập',
  Wallet: 'Ví ShopHub',
  VnPay: 'VNPay',
  MoMo: 'MoMo',
  ZaloPay: 'ZaloPay',
}

export const PRODUCT_STATUS: StatusMap = {
  Draft: { label: 'Nháp', tone: 'default' },
  PendingReview: { label: 'Chờ duyệt', tone: 'warning' },
  Active: { label: 'Đang bán', tone: 'success' },
  Hidden: { label: 'Đã ẩn', tone: 'default' },
  Banned: { label: 'Bị khoá', tone: 'error' },
  Deleted: { label: 'Đã xoá', tone: 'default' },
}

export const SHOP_STATUS: StatusMap = {
  PendingReview: { label: 'Chờ duyệt', tone: 'warning' },
  Active: { label: 'Hoạt động', tone: 'success' },
  Vacation: { label: 'Tạm nghỉ', tone: 'info' },
  Locked: { label: 'Bị khoá', tone: 'error' },
  Rejected: { label: 'Bị từ chối', tone: 'default' },
}

export const USER_STATUS: StatusMap = {
  Active: { label: 'Hoạt động', tone: 'success' },
  Locked: { label: 'Đã khoá', tone: 'error' },
  Deleted: { label: 'Đã xoá', tone: 'default' },
}

/** Return requests: only the tone — the API sends the Vietnamese label (statusLabel). */
export const RETURN_STATUS: StatusMap = {
  Requested: { label: 'Chờ shop phản hồi', tone: 'warning' },
  PartialOffered: { label: 'Shop đề nghị hoàn một phần', tone: 'warning' },
  Rejected: { label: 'Shop từ chối', tone: 'error' },
  Disputed: { label: 'Chờ sàn phân xử', tone: 'warning' },
  AwaitingReturn: { label: 'Chờ gửi hàng về', tone: 'processing' },
  Returning: { label: 'Hàng đang về', tone: 'processing' },
  AwaitingShopCheck: { label: 'Chờ shop kiểm hàng', tone: 'processing' },
  Refunded: { label: 'Đã hoàn tiền', tone: 'success' },
  Closed: { label: 'Đã đóng', tone: 'default' },
  Cancelled: { label: 'Đã huỷ', tone: 'default' },
}

export const WITHDRAWAL_STATUS: StatusMap = {
  Pending: { label: 'Chờ duyệt', tone: 'warning' },
  Processing: { label: 'Đang xử lý', tone: 'processing' },
  Done: { label: 'Đã chuyển', tone: 'success' },
  Rejected: { label: 'Từ chối', tone: 'error' },
}

/** Shop registrations for a Flash Sale slot or a campaign. */
export const APPROVAL_STATUS: StatusMap = {
  Pending: { label: 'Chờ duyệt', tone: 'warning' },
  Approved: { label: 'Đã duyệt', tone: 'success' },
  Rejected: { label: 'Từ chối', tone: 'error' },
}

/** Voucher / slot / campaign state as the API words it. */
export const SCHEDULE_STATE: StatusMap = {
  'Đang diễn ra': { label: 'Đang diễn ra', tone: 'success' },
  'Sắp diễn ra': { label: 'Sắp diễn ra', tone: 'info' },
  'Đã kết thúc': { label: 'Đã kết thúc', tone: 'default' },
  'Đã dừng': { label: 'Đã dừng', tone: 'default' },
  'Hết lượt': { label: 'Hết lượt', tone: 'warning' },
}

export const ON_OFF: StatusMap = {
  on: { label: 'Đang bật', tone: 'success' },
  off: { label: 'Đã tắt', tone: 'default' },
}

/** Label + tone of a status; unknown values fall back to the raw value in the neutral tone. */
export const statusInfo = (map: StatusMap, value: string, label?: string | null): StatusInfo => {
  const known = map[value]
  return { label: label ?? known?.label ?? value, tone: known?.tone ?? 'default' }
}
