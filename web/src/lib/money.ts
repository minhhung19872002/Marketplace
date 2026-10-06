// Money helpers — amounts are integer VND
export const formatPrice = (value: number): string => `₫${value.toLocaleString('vi-VN')}`;

export const formatSold = (sold: number): string => {
  if (sold >= 1000) return `${(sold / 1000).toFixed(1).replace('.0', '')}k`;
  return String(sold);
};
