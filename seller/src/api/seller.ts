import { apiCommand, apiRequest, ApiError } from './http'
import { useAuthStore, type AuthResult } from '../stores/auth'

export type ShopType = 'Personal' | 'Business' | 'Mall'
export type ShopStatus = 'PendingReview' | 'Active' | 'Vacation' | 'Locked' | 'Rejected'
export type ProductStatus = 'Draft' | 'PendingReview' | 'Active' | 'Hidden' | 'Banned' | 'Deleted'
export type ProductTab = 'All' | 'Active' | 'SoldOut' | 'Pending' | 'Violation' | 'Hidden' | 'Draft' | 'LowStock'
export type AttributeInputType = 'SingleSelect' | 'MultiSelect' | 'Text' | 'Number'
export type MediaPurpose = 'product' | 'kyc' | 'shop' | 'avatar' | 'evidence' | 'chat'

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface MyShop {
  id: string
  name: string
  slug: string
  type: ShopType
  status: ShopStatus
  role: string
  permissions: string[]
  logoUrl: string | null
  rejectReason: string | null
  lockReason: string | null
  createdAt: string
  description: string
  coverUrl: string | null
  lowStockThreshold: number | null
}

export interface CategoryNode {
  id: string
  parentId: string | null
  name: string
  level: number
  isLeaf: boolean
  children: CategoryNode[]
}

export interface CategoryAttribute {
  id: string
  name: string
  inputType: AttributeInputType
  unit: string | null
  isRequired: boolean
  options: string[]
}

export interface Brand {
  id: string
  name: string
}

export interface MediaAsset {
  id: string
  kind: 'Image' | 'Video' | 'Document'
  url: string | null
  thumbnailUrl: string | null
}

export interface ProductRow {
  id: string
  name: string
  imageUrl: string | null
  status: ProductStatus
  minPrice: number
  maxPrice: number
  totalStock: number
  totalAvailable: number
  skuCount: number
  soldCount: number
  reviewNote: string | null
  banReason: string | null
  updatedAt: string | null
  createdAt: string
}

export interface Sku {
  id: string
  option1: string | null
  option2: string | null
  sellerSku: string | null
  price: number
  originalPrice: number
  stock: number
  reserved: number
  available: number
  weightG: number | null
  isActive: boolean
  size: PackageSize | null
}

/** Kích thước đóng gói of a variant (mm); null = the product's */
export interface PackageSize { lengthMm: number; widthMm: number; heightMm: number }

export interface ProductDetail {
  id: string
  categoryId: string
  categoryPath: string[]
  brandId: string | null
  name: string
  description: string
  status: ProductStatus
  condition: 'New' | 'Used'
  weightG: number
  lengthMm: number
  widthMm: number
  heightMm: number
  isPreorder: boolean
  preorderDays: number
  maxPerBuyer: number | null
  warehouseId: string | null
  carrierCodes: string[]
  attributes: { attributeId: string; values: string[] }[]
  media: { type: 'Image' | 'Video'; assetId: string | null; url: string; optionValue: string | null }[]
  tiers: { tierIndex: number; name: string; options: { id: string; value: string; imageUrl: string | null }[] }[]
  skus: Sku[]
  reviewNote: string | null
  banReason: string | null
  flags: string | null
  version: number
}

export interface ProductInput {
  categoryId: string
  brandId: string | null
  name: string
  description: string
  condition: 'New' | 'Used'
  weightG: number
  lengthMm: number
  widthMm: number
  heightMm: number
  isPreorder: boolean
  preorderDays: number
  maxPerBuyer: number | null
  warehouseId: string | null
  carrierCodes: string[]
  attributes: { attributeId: string; values: string[] }[]
  media: { assetId: string; optionValue: string | null }[]
  tiers: { name: string; options: { value: string; imageAssetId: string | null }[] }[]
  skus: { option1: string | null; option2: string | null; sellerSku: string | null; price: number; originalPrice: number; stock: number; weightG: number | null; isActive: boolean;
    size: PackageSize | null }[]
}

export interface Movement {
  id: string
  deltaStock: number
  deltaReserved: number
  reason: string
  note: string | null
  occurredAt: string
}

export interface RegisterShopInput {
  name: string
  type: 'Personal' | 'Business'
  description: string
  warehouse: { contactName: string; phone: string; provinceCode: string; districtCode: string; wardCode: string; street: string }
  personal: { legalName: string; idCardNumber: string; frontAssetId: string; backAssetId: string } | null
  business: { legalName: string; taxCode: string; licenseAssetId: string } | null
  bank: { bankCode: string; accountNo: string; accountName: string }
}

export interface AdminDivision {
  code: string
  name: string
}

const shop = (shopId: string) => `/seller/shops/${shopId}`

/** Multipart upload — the server decides the type from the bytes. */
export async function uploadMedia(purpose: MediaPurpose, file: File): Promise<MediaAsset> {
  const form = new FormData()
  form.append('file', file)
  const token = useAuthStore.getState().accessToken
  const res = await fetch(`/api/media/${purpose}`, {
    method: 'POST',
    body: form,
    credentials: 'include',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })
  const body = (await res.json().catch(() => null)) as { success: boolean; data: MediaAsset; message: string; errors: { field: string; message: string }[] } | null
  if (!res.ok || !body?.success) throw new ApiError(res.status, body?.errors?.[0]?.message ?? body?.message ?? 'Tải tệp thất bại.', body?.errors ?? [])
  return body.data
}

export const sellerApi = {
  login: (identifier: string, password: string) =>
    apiRequest<AuthResult>('/auth/login', { method: 'POST', body: { identifier, password }, auth: false }),
  logout: () => apiCommand('/auth/logout', { method: 'POST', body: {}, auth: false }),

  myShops: () => apiRequest<MyShop[]>('/seller/shops'),
  registerShop: (body: RegisterShopInput) => apiCommand<string>('/seller/shops', { method: 'POST', body }),
  setLowStock: (shopId: string, units: number | null) => apiCommand(`${shop(shopId)}/low-stock-threshold`, { method: 'PUT', body: { units } }),
  updateShopProfile: (shopId: string, body: { description: string; logoAssetId: string | null; coverAssetId: string | null }) =>
    apiCommand(`${shop(shopId)}/profile`, { method: 'PUT', body }),
  setVacation: (shopId: string, until: string | null) => apiCommand(`${shop(shopId)}/vacation`, { method: 'PUT', body: { until } }),

  categories: () => apiRequest<CategoryNode[]>('/categories', { auth: false }),
  attributes: (categoryId: string) => apiRequest<CategoryAttribute[]>(`/categories/${categoryId}/attributes`, { auth: false }),
  brands: (q: string) => apiRequest<Brand[]>(`/brands?q=${encodeURIComponent(q)}`, { auth: false }),
  suggestCategories: (name: string) => apiRequest<{ id: string; path: string[] }[]>(`/seller/category-suggestions?name=${encodeURIComponent(name)}`),
  divisions: (parent?: string) => apiRequest<AdminDivision[]>(parent ? `/admin-divisions?parent=${parent}` : '/admin-divisions', { auth: false }),

  products: (shopId: string, p: { tab: ProductTab; q: string; page: number; pageSize: number; minStock?: number | null; maxStock?: number | null;
    minPrice?: number | null; maxPrice?: number | null }) => {
    const qs = new URLSearchParams({ tab: p.tab, q: p.q, page: String(p.page), pageSize: String(p.pageSize) })
    for (const k of ['minStock', 'maxStock', 'minPrice', 'maxPrice'] as const) if (p[k] != null) qs.set(k, String(p[k]))
    return apiRequest<PagedResult<ProductRow>>(`${shop(shopId)}/products?${qs}`)
  },
  bulkProducts: (shopId: string, productIds: string[], action: 'Submit' | 'Hide' | 'Show' | 'Delete') =>
    apiCommand<{ productId: string; ok: boolean; status: string | null; error: string | null }[]>(`${shop(shopId)}/products/bulk-actions`,
      { method: 'POST', body: { productIds, action } }),
  copyProduct: (shopId: string, id: string) => apiCommand<string>(`${shop(shopId)}/products/${id}/copy`, { method: 'POST' }),
  product: (shopId: string, id: string) => apiRequest<ProductDetail>(`${shop(shopId)}/products/${id}`),
  createProduct: (shopId: string, body: ProductInput) => apiCommand<string>(`${shop(shopId)}/products`, { method: 'POST', body }),
  updateProduct: (shopId: string, id: string, input: ProductInput, version: number) =>
    apiCommand(`${shop(shopId)}/products/${id}`, { method: 'PUT', body: { input, version } }),
  productAction: (shopId: string, id: string, op: 'submit' | 'hide' | 'show' | 'delete') =>
    apiCommand<string>(`${shop(shopId)}/products/${id}/actions/${op}`, { method: 'POST' }),

  quickSku: (shopId: string, skuId: string, body: { price?: number; originalPrice?: number; stock?: number }) =>
    apiCommand<Sku>(`${shop(shopId)}/skus/${skuId}`, { method: 'PUT', body }),
  adjustStock: (shopId: string, skuId: string, delta: number, note: string) =>
    apiCommand<number>(`${shop(shopId)}/skus/${skuId}/stock-adjustments`, { method: 'POST', body: { delta, note } }),
  movements: (shopId: string, skuId: string) => apiRequest<PagedResult<Movement>>(`${shop(shopId)}/skus/${skuId}/movements?pageSize=50`),
}
