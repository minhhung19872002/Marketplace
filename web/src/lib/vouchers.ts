import type { VoucherInfo } from '../api/commerce';
import { formatCount, formatPrice } from './money';

/** One line saying what a voucher gives: "Giảm ₫20.000 · Đơn từ ₫99.000". */
export const describeVoucher = (v: VoucherInfo) => {
  const value = v.type === 'Amount' ? `Giảm ${formatPrice(v.discountValue)}`
    : v.type === 'FreeShipping' ? `Miễn phí vận chuyển tối đa ${formatPrice(v.maxDiscount ?? 0)}`
    : v.type === 'CoinCashback' ? `Hoàn ${v.discountPercentBp / 100}% xu, tối đa ${formatCount(v.maxDiscount ?? 0)} xu`
    : `Giảm ${v.discountPercentBp / 100}% tối đa ${formatPrice(v.maxDiscount ?? 0)}`;
  return `${value}${v.minOrder > 0 ? ` · Đơn từ ${formatPrice(v.minOrder)}` : ''}`;
};
