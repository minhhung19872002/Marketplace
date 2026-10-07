// Tài khoản phụ (III.9): staff of the shop, their role and grants.
import { apiCommand, apiRequest } from './http'

export type StaffRole = 'Owner' | 'Manager' | 'CustomerService' | 'Warehouse'

export interface ShopStaff {
  id: string
  userId: string
  fullName: string
  phoneMasked: string | null
  emailMasked: string | null
  role: StaffRole
  permissions: string[]
  createdAt: string
}

export interface StaffInvitation {
  id: string
  fullName: string
  phoneMasked: string | null
  emailMasked: string | null
  role: StaffRole
  permissions: string[]
  createdAt: string
  expiresAt: string
}

/** An invitation waiting for the signed-in user. */
export interface MyInvitation {
  id: string
  shopId: string
  shopName: string
  shopLogoUrl: string | null
  role: StaffRole
  permissions: string[]
  expiresAt: string
}

export interface StaffBoard {
  staff: ShopStaff[]
  // Sent, not answered yet
  invitations: StaffInvitation[]
  allPermissions: string[]
  roleDefaults: Record<Exclude<StaffRole, 'Owner'>, string[]>
  maxStaff: number
}

export const ROLE_LABELS: Record<StaffRole, string> = {
  Owner: 'Chủ shop',
  Manager: 'Quản lý',
  CustomerService: 'Chăm sóc khách hàng',
  Warehouse: 'Kho',
}

export const PERMISSION_LABELS: Record<string, string> = {
  'PRODUCT.VIEW': 'Xem sản phẩm',
  'PRODUCT.MANAGE': 'Đăng / sửa sản phẩm',
  'INVENTORY.MANAGE': 'Cập nhật tồn kho',
  'SETTINGS.MANAGE': 'Thiết lập shop',
  'STAFF.MANAGE': 'Quản lý tài khoản phụ',
  'MARKETING.MANAGE': 'Kênh Marketing',
  'ORDER.VIEW': 'Xem đơn hàng',
  'ORDER.MANAGE': 'Xử lý đơn hàng',
  'REVIEW.MANAGE': 'Trả lời đánh giá',
  'FINANCE.VIEW': 'Xem tài chính',
  'FINANCE.WITHDRAW': 'Rút tiền',
  'CHAT.MANAGE': 'Chat với người mua',
}

const base = (shopId: string) => `/seller/shops/${shopId}/staff-accounts`

export const staffApi = {
  board: (shopId: string) => apiRequest<StaffBoard>(base(shopId)),
  add: (shopId: string, body: { login: string; role: StaffRole; permissions: string[] }) =>
    apiCommand<string>(base(shopId), { method: 'POST', body }),
  update: (shopId: string, staffId: string, body: { role: StaffRole; permissions: string[] }) =>
    apiCommand(`${base(shopId)}/${staffId}`, { method: 'PUT', body }),
  remove: (shopId: string, staffId: string) => apiCommand(`${base(shopId)}/${staffId}`, { method: 'DELETE' }),
  revokeInvitation: (shopId: string, id: string) => apiCommand(`/seller/shops/${shopId}/staff-invitations/${id}`, { method: 'DELETE' }),
  myInvitations: () => apiRequest<MyInvitation[]>('/seller/staff-invitations'),
  accept: (id: string) => apiCommand<string>(`/seller/staff-invitations/${id}/accept`, { method: 'POST' }),
  decline: (id: string) => apiCommand(`/seller/staff-invitations/${id}/decline`, { method: 'POST' }),
}
