import { describe, expect, it } from 'vitest';
import { flashBarLabel } from '../components/FlashSaleBlock';

// G2-A1: the bar says (sentence case since G3 C1) what really happened to the slot's quota
describe('flashBarLabel', () => {
  it('says "Vừa mở bán" before the first unit sells, then counts, then warns', () => {
    expect(flashBarLabel({ sold: 0, quota: 20, soldPercent: 0 })).toBe('Vừa mở bán');
    expect(flashBarLabel({ sold: 5, quota: 20, soldPercent: 25 })).toBe('Đã bán 5');
    expect(flashBarLabel({ sold: 17, quota: 20, soldPercent: 85 })).toBe('Sắp cháy hàng');
    expect(flashBarLabel({ sold: 20, quota: 20, soldPercent: 100 })).toBe('Đã hết');
  });
});
