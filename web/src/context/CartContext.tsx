import { useCallback, useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { cartApi, type Cart, type CartLine } from '../api/commerce';
import { useAuthStore } from '../stores/auth';

export const CART_KEY = ['cart'] as const;

const EMPTY: Cart = { shops: [], lineCount: 0, totalQuantity: 0, selectedQuantity: 0, selectedSubtotal: 0 };

interface CartValue {
  cart: Cart;
  lines: CartLine[];
  totalItems: number;
  isLoading: boolean;
  /** Adds to the server cart (guests get a cart cookie, merged into the account at sign-in). */
  add: (skuId: string, quantity: number) => Promise<string>;
  update: (skuId: string, change: { quantity?: number; selected?: boolean; skuId?: string }) => Promise<void>;
  remove: (skuIds: string[]) => Promise<void>;
  select: (selected: boolean, shopId?: string) => Promise<void>;
}

/** The cart lives on the server (validated stock / price per line); this hook keeps one cached copy for the whole app. */
export const useCart = (): CartValue => {
  const queryClient = useQueryClient();
  const status = useAuthStore((s) => s.status);
  const userId = useAuthStore((s) => s.user?.id ?? 'guest');
  // Wait for the session restore on page load: asking too early returns the (empty) guest cart
  const key = useMemo(() => [...CART_KEY, userId] as const, [userId]);
  const { data, isLoading } = useQuery({ queryKey: key, queryFn: cartApi.get, staleTime: 10_000, enabled: status !== 'checking' });
  const cart = data ?? EMPTY;
  const set = useCallback((next: Cart): void => {
    queryClient.setQueryData(key, next);
  }, [queryClient, key]);

  const addMutation = useMutation({ mutationFn: ({ skuId, quantity }: { skuId: string; quantity: number }) => cartApi.add(skuId, quantity) });

  const add = useCallback(
    async (skuId: string, quantity: number) => {
      const result = await addMutation.mutateAsync({ skuId, quantity });
      set(result.data);
      return result.message;
    },
    [addMutation, set],
  );
  /** Tick boxes react at once; the server's answer (totals, checks) replaces the guess, a failure restores it. */
  const optimistic = useCallback(
    async (patch: (line: CartLine, shopId: string) => CartLine, call: () => Promise<Cart>) => {
      const before = queryClient.getQueryData<Cart>(key);
      if (before) set({ ...before, shops: before.shops.map((s) => ({ ...s, lines: s.lines.map((l) => patch(l, s.shopId)) })) });
      try {
        set(await call());
      } catch (e) {
        if (before) set(before);
        throw e;
      }
    },
    [queryClient, key, set],
  );

  const update = useCallback(
    async (skuId: string, change: { quantity?: number; selected?: boolean; skuId?: string }) => {
      if (change.selected !== undefined && change.quantity === undefined && change.skuId === undefined)
        return optimistic((l) => (l.skuId === skuId ? { ...l, isSelected: change.selected! } : l), () => cartApi.update(skuId, change));
      set(await cartApi.update(skuId, change));
    },
    [optimistic, set],
  );
  const remove = useCallback(async (skuIds: string[]) => set((await cartApi.remove(skuIds)).data), [set]);
  const select = useCallback(
    (selected: boolean, shopId?: string) =>
      optimistic((l, s) => (shopId === undefined || s === shopId ? { ...l, isSelected: selected } : l), () => cartApi.select(selected, shopId)),
    [optimistic],
  );

  return {
    cart,
    lines: cart.shops.flatMap((s) => s.lines),
    totalItems: cart.totalQuantity,
    isLoading: isLoading || status === 'checking',
    add,
    update,
    remove,
    select,
  };
};
