import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

interface CoinChoiceState {
  useCoins: boolean;
  setUseCoins: (on: boolean) => void;
}

/**
 * "Dùng ShopHub Xu" (G-VIS round 5, #221): one switch for the cart's total bar and the checkout. Kept for the browser
 * session only (a convenience: the checkout's quote settles how many coins apply).
 */
export const useCoinChoice = create<CoinChoiceState>()(
  persist(
    (set) => ({
      useCoins: false,
      setUseCoins: (on) => set({ useCoins: on }),
    }),
    { name: 'sh-use-coins', storage: createJSONStorage(() => sessionStorage) },
  ),
);
