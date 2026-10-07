import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

interface ShopVoucherState {
  // shopId → the shop voucher code the buyer picked (null = none)
  codes: Record<string, string | null>;
  set: (shopId: string, code: string | null) => void;
}

/**
 * The shop voucher picked per shop block (II.6, E6) — one choice for the cart and the checkout. Kept for the browser
 * session only (a convenience: the server re-checks the code on every quote and at placing).
 */
export const useShopVouchers = create<ShopVoucherState>()(
  persist(
    (set) => ({
      codes: {},
      set: (shopId, code) => set((s) => ({ codes: { ...s.codes, [shopId]: code } })),
    }),
    { name: 'sh-shop-vouchers', storage: createJSONStorage(() => sessionStorage) },
  ),
);
