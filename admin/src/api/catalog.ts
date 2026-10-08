import { apiCommand, apiRequest } from './http'
import type { PagedResult } from './admin'

export type AttributeInputType = 'SingleSelect' | 'MultiSelect' | 'Text' | 'Number'
export type ProductStatus = 'Draft' | 'PendingReview' | 'Active' | 'Hidden' | 'Banned' | 'Deleted'
export type ShopStatus = 'PendingReview' | 'Active' | 'Vacation' | 'Locked' | 'Rejected'

export interface CategoryNode {
  id: string
  parentId: string | null
  name: string
  iconUrl: string | null
  level: number
  sortOrder: number
  isActive: boolean
  // Shown to buyers (false also when a category above is hidden); sellers still list in it
  isVisible: boolean
  commissionRateBp: number
  isLeaf: boolean
  children: CategoryNode[]
}

export interface CategoryAttribute {
  id: string
  categoryId: string
  name: string
  inputType: AttributeInputType
  unit: string | null
  isRequired: boolean
  isFilterable: boolean
  options: string[]
  sortOrder: number
}

export interface Brand {
  id: string
  name: string
  slug: string
  logoUrl: string | null
  isVerified: boolean
}

export interface ReviewRow {
  id: string
  name: string
  imageUrl: string | null
  shopId: string
  shopName: string
  categoryPath: string[]
  minPrice: number
  maxPrice: number
  status: ProductStatus
  flags: string | null
  submittedAt: string | null
}

export interface ProductDetail {
  id: string
  name: string
  description: string
  status: ProductStatus
  categoryPath: string[]
  media: { type: 'Image' | 'Video'; url: string }[]
  tiers: { name: string; options: { value: string }[] }[]
  skus: { id: string; option1: string | null; option2: string | null; price: number; originalPrice: number; stock: number; isActive: boolean }[]
  flags: string | null
  banReason: string | null
  reviewNote: string | null
}

export interface ShopRow {
  id: string
  name: string
  type: 'Personal' | 'Business' | 'Mall'
  status: ShopStatus
  isPreferred: boolean
  ownerName: string
  ownerPhone: string | null
  productCount: number
  createdAt: string
  approvedAt: string | null
}

export interface ShopDetail {
  id: string
  name: string
  description: string
  type: 'Personal' | 'Business' | 'Mall'
  status: ShopStatus
  isPreferred: boolean
  rejectReason: string | null
  lockReason: string | null
  ownerName: string
  ownerPhone: string | null
  kyc: {
    legalName: string
    taxCode: string | null
    idCardNumberMasked: string | null
    status: string
    idCardFrontUrl: string | null
    idCardBackUrl: string | null
    businessLicenseUrl: string | null
  } | null
  warehouse: { contactName: string; phone: string; address: string } | null
  bankName: string | null
  bankAccountLast4: string | null
  createdAt: string
}

const q = (params: Record<string, string | number | boolean | undefined>) =>
  new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => [k, String(v)])).toString()

export const catalogApi = {
  categories: () => apiRequest<CategoryNode[]>('/admin/categories'),
  saveCategory: (body: { id?: string; parentId: string | null; name: string; iconUrl?: string | null; sortOrder: number; commissionRateBp: number; isActive: boolean; isVisible: boolean }) =>
    apiCommand<string>('/admin/categories', { method: 'POST', body }),
  attributes: (categoryId: string) => apiRequest<CategoryAttribute[]>(`/categories/${categoryId}/attributes`),
  saveAttribute: (body: Omit<CategoryAttribute, 'id'> & { id?: string }) => apiCommand<string>('/admin/categories/attributes', { method: 'POST', body }),
  deleteAttribute: (id: string) => apiCommand(`/admin/categories/attributes/${id}`, { method: 'DELETE' }),

  brands: (search: string) => apiRequest<Brand[]>(`/brands?q=${encodeURIComponent(search)}`),
  saveBrand: (body: { id?: string; name: string; logoUrl: string | null; isVerified: boolean }) =>
    apiCommand<string>('/admin/brands', { method: 'POST', body }),

  reviewQueue: (p: { status: ProductStatus; q?: string; flaggedOnly?: boolean; page: number; pageSize: number }) =>
    apiRequest<PagedResult<ReviewRow>>(`/admin/products?${q(p)}`),
  product: (id: string) => apiRequest<ProductDetail>(`/admin/products/${id}`),
  moderate: (id: string, action: 'approve' | 'reject' | 'ban' | 'unban', reason?: string) =>
    apiCommand<string>(`/admin/products/${id}/${action}`, { method: 'POST', body: reason === undefined ? undefined : { reason } }),

  shops: (p: { status?: ShopStatus; q?: string; page: number; pageSize: number }) => apiRequest<PagedResult<ShopRow>>(`/admin/shops?${q(p)}`),
  shop: (id: string) => apiRequest<ShopDetail>(`/admin/shops/${id}`),
  moderateShop: (id: string, action: 'approve' | 'reject' | 'lock' | 'unlock', reason?: string) =>
    apiCommand<string>(`/admin/shops/${id}/${action}`, { method: 'POST', body: reason === undefined ? undefined : { reason } }),
  setLabels: (id: string, isMall: boolean, isPreferred: boolean) =>
    apiCommand(`/admin/shops/${id}/labels`, { method: 'PUT', body: { isMall, isPreferred } }),
}
