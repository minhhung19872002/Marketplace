import type { Cart, CartLine } from '../api/commerce';

/** The number on the cart icon: lines in the cart, not units (E2). */
export const cartBadge = (cart: Cart): number => cart.lineCount;

/** The lines added last across every shop (the cart itself comes grouped by shop). */
export const recentLines = (cart: Cart, n: number): CartLine[] =>
  cart.shops
    .flatMap((s) => s.lines)
    .sort((a, b) => Date.parse(b.addedAt) - Date.parse(a.addedAt) || a.skuId.localeCompare(b.skuId))
    .slice(0, n);

/**
 * The struck-through price of a cart line: the price before a drop since it was added, else the list price — only when
 * it is above the current price (a struck price below the selling price reads as an error, P0-2).
 */
export const struckPrice = (line: Pick<CartLine, 'price' | 'originalPrice' | 'previousPrice'>): number | null => {
  if (line.previousPrice !== null && line.previousPrice > line.price) return line.previousPrice;
  return line.originalPrice > line.price ? line.originalPrice : null;
};
