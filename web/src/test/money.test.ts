import { describe, expect, it } from 'vitest';
import { formatPrice, formatSold, priceParts } from '../lib/money';

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
