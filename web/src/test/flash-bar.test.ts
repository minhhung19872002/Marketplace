import { describe, expect, it } from 'vitest';
import { flashBarLabel } from '../components/FlashSaleBlock';

// G2-A1 / G-VIS: the bar says what really happened to the slot's quota (written in sentence case, upper-cased by CSS)
describe('flashBarLabel', () => {
  it('says "Vừa mở bán" before the first unit sells, then counts, then "Đang bán chạy", then the units left', () => {
    expect(flashBarLabel({ sold: 0, quota: 20, soldPercent: 0 })).toBe('Vừa mở bán');
    expect(flashBarLabel({ sold: 5, quota: 20, soldPercent: 25 })).toBe('Đã bán 5');
    expect(flashBarLabel({ sold: 12, quota: 20, soldPercent: 60 })).toBe('Đang bán chạy');
    expect(flashBarLabel({ sold: 17, quota: 20, soldPercent: 85 })).toBe('Chỉ còn 3');
    expect(flashBarLabel({ sold: 20, quota: 20, soldPercent: 100 })).toBe('Đã bán hết');
  });
});
