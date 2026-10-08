// Kho hàng & địa chỉ trả hàng, đa kho, đơn vị vận chuyển của shop (III.9).
import { apiCommand, apiRequest } from './http'

export interface WarehouseAddress {
  contactName: string
  phone: string
  provinceCode: string
  wardCode: string
  street: string
}

export interface Warehouse extends WarehouseAddress {
  // Only on warehouses saved before the two-level reform (2025-07-01)
  districtCode: string | null
  id: string
  name: string
  fullAddress: string
  isPickupDefault: boolean
  isReturnDefault: boolean
  productCount: number
}

export interface ShippingChannel {
  carrierCode: string
  name: string
  description: string | null
  carrierActive: boolean
  carrierSupportsCod: boolean
  isEnabled: boolean
  codEnabled: boolean
}

export interface ShopLogistics {
  multiWarehouse: boolean
  warehouses: Warehouse[]
  channels: ShippingChannel[]
  maxWarehouses: number
}

export interface WarehouseBody {
  name: string
  address: WarehouseAddress
  isPickupDefault: boolean
  isReturnDefault: boolean
}

const base = (shopId: string) => `/seller/shops/${shopId}`

export const logisticsApi = {
  get: (shopId: string) => apiRequest<ShopLogistics>(`${base(shopId)}/logistics`),
  addWarehouse: (shopId: string, body: WarehouseBody) => apiCommand<string>(`${base(shopId)}/warehouses`, { method: 'POST', body }),
  updateWarehouse: (shopId: string, id: string, body: WarehouseBody) =>
    apiCommand<string>(`${base(shopId)}/warehouses/${id}`, { method: 'PUT', body }),
  removeWarehouse: (shopId: string, id: string) => apiCommand(`${base(shopId)}/warehouses/${id}`, { method: 'DELETE' }),
  setMultiWarehouse: (shopId: string, enabled: boolean) => apiCommand(`${base(shopId)}/multi-warehouse`, { method: 'PUT', body: { enabled } }),
  setChannel: (shopId: string, code: string, body: { enabled: boolean; codEnabled: boolean }) =>
    apiCommand(`${base(shopId)}/shipping-channels/${encodeURIComponent(code)}`, { method: 'PUT', body }),
}
