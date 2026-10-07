import { describe, expect, it } from 'vitest';
import { flashFormValues, flashInput, promotionFormValues, promotionInput } from '../pages/promotionForm';
import type { FlashSlot, Promotion } from '../api/marketing';

// D6: "Sửa" opens a programme / shop Flash Sale in the form exactly as it is and saves it back unchanged
describe('promotion and shop flash sale forms', () => {
  it('round-trips a combo', () => {
    const combo: Promotion = {
      id: 'p1', type: 'Combo', typeLabel: 'Combo khuyến mãi', name: 'Mua 3 giảm 10%', startAt: '2026-10-09T02:00:00.000Z', endAt: '2026-10-16T16:59:00.000Z',
      status: 'Active', state: 'Sắp diễn ra', productIds: ['a', 'b'], productNames: ['Ấm', 'Cốc'], skus: [], minQuantity: 3, discountBp: 1000,
      discountAmount: 0, maxAddOnQuantity: 0, minSpend: 0, giftSkuId: null, giftName: null, giftQuantity: 0,
    };
    expect(promotionInput(promotionFormValues(combo))).toEqual({
      type: 'Combo', name: 'Mua 3 giảm 10%', startAt: combo.startAt, endAt: combo.endAt, productIds: ['a', 'b'], skus: [], minQuantity: 3,
      discountBp: 1000, discountAmount: 0, maxAddOnQuantity: 0, minSpend: 0, giftSkuId: null, giftQuantity: 0,
    });
  });

  it('round-trips a discount with its per-buyer limit and quota (L139)', () => {
    const discount: Promotion = {
      id: 'p2', type: 'Discount', typeLabel: 'Chương trình giảm giá', name: 'Giảm ấm', startAt: '2026-10-09T02:00:00.000Z', endAt: '2026-10-16T16:59:00.000Z',
      status: 'Active', state: 'Sắp diễn ra', productIds: [], productNames: [],
      skus: [{ skuId: 'k1', productName: 'Ấm', variant: null, price: 150_000, basePrice: 200_000, perUserLimit: 2, quota: 50, sold: 0 },
        { skuId: 'k2', productName: 'Cốc', variant: null, price: 40_000, basePrice: 50_000, perUserLimit: null, quota: null, sold: 0 }],
      minQuantity: 0, discountBp: 0, discountAmount: 0, maxAddOnQuantity: 0, minSpend: 0, giftSkuId: null, giftName: null, giftQuantity: 0,
    };
    expect(promotionInput(promotionFormValues(discount)).skus).toEqual([
      { skuId: 'k1', price: 150_000, perUserLimit: 2, quota: 50 },
      { skuId: 'k2', price: 40_000, perUserLimit: null, quota: null },
    ]);
  });

  it('round-trips a shop flash sale', () => {
    const slot: FlashSlot = {
      id: 's1', owner: 'Shop', startAt: '2026-10-09T05:00:00.000Z', endAt: '2026-10-09T07:00:00.000Z', minDiscountBp: 0, minRating: 0, categoryIds: [],
      state: 'Sắp diễn ra',
      items: [{ id: 'i1', skuId: 'k1', productName: 'Ấm', variant: null, flashPrice: 120_000, basePrice: 200_000, quota: 5, sold: 0, perUserLimit: 1, status: 'Approved', rejectReason: null }],
    };
    expect(flashInput(flashFormValues(slot))).toEqual({
      startAt: slot.startAt, endAt: slot.endAt, items: [{ skuId: 'k1', flashPrice: 120_000, quota: 5, perUserLimit: 1 }],
    });
  });
});
