import { describe, expect, it } from 'vitest';
import { formatPrice, formatSold, priceParts, struckPrice } from '../lib/money';

describe('formatSold', () => {
  it('writes sold counts the Vietnamese way', () => {
    expect(formatSold(999)).toBe('999');
    expect(formatSold(1000)).toBe('1k');
    expect(formatSold(1250)).toBe('1,2k');
    expect(formatSold(12_340)).toBe('12k+');
  });

  it('writes the ₫ sign after the number', () => {
    expect(formatPrice(79_000)).toBe('79.000₫');
    expect(formatPrice(0)).toBe('0₫');
  });

  it('splits a price into sign and digits', () => {
    expect(priceParts(1_250_000)).toEqual({ currency: '₫', amount: '1.250.000' });
  });
});

describe('struckPrice (L181)', () => {
  it('strikes nothing when the original equals the selling price, single or range', () => {
    expect(struckPrice(79_000, 79_000, 79_000, 79_000)).toBeNull();
    expect(struckPrice(459_000, 479_000, 459_000, 479_000)).toBeNull();
  });

  it('strikes nothing when the original is below the selling price', () => {
    expect(struckPrice(100_000, 100_000, 90_000, 90_000)).toBeNull();
    expect(struckPrice(100_000, 200_000, 150_000, 150_000)).toBeNull();
  });

  it('strikes a higher original, single or range', () => {
    expect(struckPrice(79_000, 79_000, 99_000, 99_000)).toBe('99.000₫');
    expect(struckPrice(459_000, 479_000, 459_000, 520_000)).toBe('459.000₫ - 520.000₫');
  });
});
