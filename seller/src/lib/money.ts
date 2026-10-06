// Money helpers — amounts are integer VND
export const formatPrice = (value: number): string => `₫${value.toLocaleString('vi-VN')}`

export const formatRange = (min: number, max: number): string =>
  min === max ? formatPrice(min) : `${formatPrice(min)} – ${formatPrice(max)}`

export const formatNumber = (value: number): string => value.toLocaleString('vi-VN')

export const formatPercentBp = (bp: number): string => `${(bp / 100).toLocaleString('vi-VN')}%`
