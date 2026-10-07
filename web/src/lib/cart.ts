import type { Cart, CartLine } from '../api/commerce';

/** The number on the cart icon: lines in the cart, not units (E2). */
export const cartBadge = (cart: Cart): number => cart.lineCount;

/** The lines added last across every shop (the cart itself comes grouped by shop). */
export const recentLines = (cart: Cart, n: number): CartLine[] =>
  cart.shops
    .flatMap((s) => s.lines)
    .sort((a, b) => Date.parse(b.addedAt) - Date.parse(a.addedAt) || a.skuId.localeCompare(b.skuId))
    .slice(0, n);
