// Money helpers — amounts are integer VND
export const formatPrice = (value: number): string => `₫${value.toLocaleString('vi-VN')}`;

/** "Đã bán" counts the Vietnamese way: 1,2k · 10k · 999. */
export const formatSold = (sold: number): string => {
  if (sold >= 10_000) return `${Math.floor(sold / 1000)}k`;
  if (sold >= 1000) return `${(Math.floor(sold / 100) / 10).toFixed(1).replace('.0', '').replace('.', ',')}k`;
  return String(sold);
};

/** A price split for display: the ₫ sign smaller than the digits (cards, Flash Sale). */
export const priceParts = (value: number): { currency: string; amount: string } => ({ currency: '₫', amount: value.toLocaleString('vi-VN') });

/** Plain count with Vietnamese thousands separators (1.234). */
export const formatCount = (value: number): string => value.toLocaleString('vi-VN');
