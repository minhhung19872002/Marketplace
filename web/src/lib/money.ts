// Money helpers — amounts are integer VND
// G-VIS: the ₫ sign comes after the number ("79.000₫"), as Vietnamese shoppers read prices
export const formatPrice = (value: number): string => `${value.toLocaleString('vi-VN')}₫`;

/** "Đã bán" counts the Vietnamese way: 999 · 1,2k · 10k+. */
export const formatSold = (sold: number): string => {
  if (sold >= 10_000) return `${Math.floor(sold / 1000)}k+`;
  if (sold >= 1000) return `${(Math.floor(sold / 100) / 10).toFixed(1).replace('.0', '').replace('.', ',')}k`;
  return String(sold);
};

/** A price split for display: the digits, then the ₫ sign smaller after them (cards, Flash Sale). */
export const priceParts = (value: number): { currency: string; amount: string } => ({ currency: '₫', amount: value.toLocaleString('vi-VN') });

/** Plain count with Vietnamese thousands separators (1.234). */
export const formatCount = (value: number): string => value.toLocaleString('vi-VN');

/** "79.000₫" or "79.000₫ - 99.000₫" when a product's SKUs differ in price. */
export const formatPriceRange = (min: number, max: number): string => (min === max ? formatPrice(min) : `${formatPrice(min)} - ${formatPrice(max)}`);

/**
 * The struck-through "was" price next to a selling price (or range). Only shown when the shopper really
 * saves money: every bound of the original is at or above the selling bound and at least one is higher.
 * Returns null otherwise, so the page never strikes a price equal to (or below) what it charges (L181).
 */
export const struckPrice = (min: number, max: number, originalMin: number, originalMax: number): string | null =>
  originalMin >= min && originalMax >= max && (originalMin > min || originalMax > max) ? formatPriceRange(originalMin, originalMax) : null;
