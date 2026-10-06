import { create } from 'zustand'

const KEY = 'sh_seller_shop'

const read = (): string | null => {
  try {
    return localStorage.getItem(KEY)
  } catch {
    return null
  }
}

interface ShopState {
  // Remembered per browser; the API re-checks membership on every call
  currentShopId: string | null
  select: (id: string | null) => void
}

export const useShopStore = create<ShopState>((set) => ({
  currentShopId: read(),
  select: (id) => {
    try {
      if (id) localStorage.setItem(KEY, id)
      else localStorage.removeItem(KEY)
    } catch {
      // storage blocked: selection lasts for this tab only
    }
    set({ currentShopId: id })
  },
}))
