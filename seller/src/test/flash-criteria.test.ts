import { describe, expect, it } from 'vitest';
import { flashCriteriaText } from '../pages/flashCriteria';

// D6: a shop must see every rule of a platform Flash Sale slot — the category rule was hidden, so registrations were
// refused for a reason the shop could not see
describe('flash sale criteria', () => {
  const names = new Map([['c-dt', 'Điện Thoại & Phụ Kiện'], ['c-mt', 'Máy Tính & Laptop']]);

  it('lists the categories with the discount and rating rules', () => {
    expect(flashCriteriaText({ minDiscountBp: 1500, minRating: 4.5, categoryIds: ['c-dt', 'c-mt'] }, names))
      .toBe('Giảm ≥ 15% · đánh giá ≥ 4.5★ · ngành: Điện Thoại & Phụ Kiện, Máy Tính & Laptop');
  });

  it('says every category when the slot has none', () => {
    expect(flashCriteriaText({ minDiscountBp: 1000, minRating: 0, categoryIds: [] }, names)).toBe('Giảm ≥ 10% · đánh giá ≥ 0★ · mọi ngành hàng');
  });
});
