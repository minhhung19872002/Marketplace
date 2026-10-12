import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

interface PlatformVoucherState {
  // The ShopHub Voucher code the buyer picked (null = none)
  code: string | null;
  setCode: (code: string | null) => void;
}

/**
 * "ShopHub Voucher" (G8 C): one choice for the cart's total bar and the checkout. Kept for the browser session only (a
 * convenience: the server re-checks the code on every quote and at placing).
 */
export const usePlatformVoucher = create<PlatformVoucherState>()(
  persist(
    (set) => ({
      code: null,
      setCode: (code) => set({ code }),
    }),
    { name: 'sh-platform-voucher', storage: createJSONStorage(() => sessionStorage) },
  ),
);
