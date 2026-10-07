import { describe, expect, it } from 'vitest';
import { toFormValues, toVoucherInput } from '../pages/voucherForm';
import type { Voucher } from '../api/vouchers';

// D6: the shop voucher form sends the chosen products / categories (it used to send []) and an opened voucher
// comes back into the form unchanged, so "Sửa" saves what the seller saw
describe('voucher form', () => {
  const voucher: Voucher = {
    id: 'v1', code: 'SHOPLY10', name: 'Ly giảm 10%', type: 'Percent', discountValue: 0, discountPercentBp: 1000, maxDiscount: 30_000,
    minOrder: 100_000, audience: 'ShopFollowers', categoryIds: ['c-leaf'], productIds: ['p-1', 'p-2'],
    startAt: '2026-10-08T01:00:00.000Z', endAt: '2026-10-20T16:59:00.000Z', totalQuota: 50, perUserLimit: 2, isPublic: false, channel: 'All',
    usedCount: 3, stats: null, isActive: true, state: 'Sắp diễn ra',
  };

  it('round-trips a voucher through the form', () => {
    const input = toVoucherInput(toFormValues(voucher));
    expect(input).toEqual({
      code: 'SHOPLY10', name: 'Ly giảm 10%', type: 'Percent', discountValue: 0, discountPercentBp: 1000, maxDiscount: 30_000, minOrder: 100_000,
      audience: 'ShopFollowers', categoryIds: ['c-leaf'], productIds: ['p-1', 'p-2'], startAt: voucher.startAt, endAt: voucher.endAt,
      totalQuota: 50, perUserLimit: 2, isPublic: false, channel: 'All',
    });
  });
});
