import { describe, expect, it } from 'vitest';
import { formatPrice, formatSold, priceParts, priceTextParts, struckPrice } from '../lib/money';

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

  // L207: the "₫" sign has a bar under it that read as a link underline at price size — shown prices write a plain "đ"
  it('splits a price into digits and a plain "đ"', () => {
    expect(priceParts(1_250_000)).toEqual({ currency: 'đ', amount: '1.250.000' });
  });

  it('cuts a formatted price or range into digits and plain "đ" signs', () => {
    expect(priceTextParts('158.000₫')).toEqual([{ text: '158.000', currency: false }, { text: 'đ', currency: true }]);
    expect(priceTextParts('79.000₫ - 99.000₫').map((p) => p.text).join('')).toBe('79.000đ - 99.000đ');
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
