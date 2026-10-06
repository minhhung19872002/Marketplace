// Danh mục của shop & trang trí shop (III.9).
import { apiCommand, apiRequest } from './http'

export interface ShopCategory {
  id: string
  name: string
  sortOrder: number
  isVisible: boolean
  productCount: number
}

export type BlockType = 'Banner' | 'Products' | 'Category' | 'Video' | 'Text'

export interface DecorationImage {
  url: string
  link: string | null
}

/** A block as stored on the published layout. */
export interface DecorationBlock {
  type: BlockType
  title: string | null
  images: DecorationImage[] | null
  productIds: string[] | null
  shopCategoryId: string | null
  videoUrl: string | null
  text: string | null
}

export interface DecorationProduct {
  id: string
  name: string
  imageUrl: string | null
  status: string
}

export interface Decoration {
  blocks: DecorationBlock[]
  publishedAt: string | null
  products: DecorationProduct[]
  maxBlocks: number
  maxBannerImages: number
  maxProducts: number
}

/** What the editor sends: new uploads by asset id, images already on the layout by URL. */
export interface DecorationImageInput {
  assetId?: string
  url?: string
  link: string | null
}

export interface DecorationBlockInput {
  type: BlockType
  title: string | null
  images?: DecorationImageInput[]
  productIds?: string[]
  shopCategoryId?: string | null
  videoAssetId?: string
  videoUrl?: string | null
  text?: string | null
}

const base = (shopId: string) => `/seller/shops/${shopId}`

export const designApi = {
  categories: (shopId: string) => apiRequest<ShopCategory[]>(`${base(shopId)}/shop-categories`),
  createCategory: (shopId: string, body: { name: string; sortOrder: number; isVisible: boolean }) =>
    apiCommand<string>(`${base(shopId)}/shop-categories`, { method: 'POST', body }),
  updateCategory: (shopId: string, id: string, body: { name: string; sortOrder: number; isVisible: boolean }) =>
    apiCommand<string>(`${base(shopId)}/shop-categories/${id}`, { method: 'PUT', body }),
  deleteCategory: (shopId: string, id: string) => apiCommand(`${base(shopId)}/shop-categories/${id}`, { method: 'DELETE' }),
  members: (shopId: string, id: string) => apiRequest<DecorationProduct[]>(`${base(shopId)}/shop-categories/${id}/products`),
  setMembers: (shopId: string, id: string, productIds: string[]) =>
    apiCommand<number>(`${base(shopId)}/shop-categories/${id}/products`, { method: 'PUT', body: { productIds } }),
  decoration: (shopId: string) => apiRequest<Decoration>(`${base(shopId)}/decoration`),
  saveDecoration: (shopId: string, blocks: DecorationBlockInput[]) =>
    apiCommand(`${base(shopId)}/decoration`, { method: 'PUT', body: { blocks } }),
}
