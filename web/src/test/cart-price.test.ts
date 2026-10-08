import { describe, expect, it } from 'vitest';
import { struckPrice } from '../lib/cart';

// P0-2: a struck-through price is never below the selling price
describe('struckPrice', () => {
  it('shows the list price when it is above the selling price', () => {
    expect(struckPrice({ price: 137_000, originalPrice: 196_000, previousPrice: null })).toBe(196_000);
  });

  it('hides a list price that is below the selling price (variant priced above the product list price)', () => {
    expect(struckPrice({ price: 196_000, originalPrice: 137_000, previousPrice: null })).toBeNull();
  });

  it('shows the old price after a drop, never after a rise', () => {
    expect(struckPrice({ price: 150_000, originalPrice: 150_000, previousPrice: 180_000 })).toBe(180_000);
    expect(struckPrice({ price: 180_000, originalPrice: 180_000, previousPrice: 150_000 })).toBeNull();
  });
});
