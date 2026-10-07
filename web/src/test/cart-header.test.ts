import { describe, expect, it } from 'vitest';
import { cartBadge, recentLines } from '../lib/cart';
import type { Cart, CartLine } from '../api/commerce';

// E2: the cart icon counts lines (not units) and the preview shows the 5 lines added last across every shop
describe('header cart', () => {
  const line = (skuId: string, addedAt: string, quantity = 1): CartLine => ({
    skuId, productId: `p-${skuId}`, name: skuId, imageUrl: null, variant: null, price: 1000, originalPrice: 1000, previousPrice: null, quantity,
    available: 10, isSelected: true, canBuy: true, problem: null, priceLabel: null, addedAt,
  });
  const cart: Cart = {
    shops: [
      // Shops come grouped (newest shop first), lines inside each shop newest first
      { shopId: 'a', shopName: 'A', shopSlug: 'a', isMall: false, onVacation: false, lines: [line('a1', '2026-10-07T10:00:00Z', 5), line('a2', '2026-10-01T10:00:00Z')] },
      { shopId: 'b', shopName: 'B', shopSlug: 'b', isMall: false, onVacation: false,
        lines: [line('b1', '2026-10-06T10:00:00Z'), line('b2', '2026-10-05T10:00:00Z'), line('b3', '2026-10-04T10:00:00Z'), line('b4', '2026-10-03T10:00:00Z')] },
    ],
    lineCount: 6, totalQuantity: 10, selectedQuantity: 10, selectedSubtotal: 10_000,
  };

  it('the badge is the number of lines', () => {
    expect(cartBadge(cart)).toBe(6);
  });

  it('the preview is the 5 lines added last, whatever their shop', () => {
    expect(recentLines(cart, 5).map((l) => l.skuId)).toEqual(['a1', 'b1', 'b2', 'b3', 'b4']);
  });
});
