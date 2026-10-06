// Money helpers — amounts are integer VND
export const formatPrice = (value: number): string => `₫${value.toLocaleString('vi-VN')}`

export const formatRange = (min: number, max: number): string =>
  min === max ? formatPrice(min) : `${formatPrice(min)} – ${formatPrice(max)}`

/** Basis points as a Vietnamese percentage: 250 → "2,5%". */
export const formatPercentBp = (bp: number): string => `${(bp / 100).toLocaleString('vi-VN')}%`
export const formatNumber = (value: number): string => value.toLocaleString('vi-VN')
