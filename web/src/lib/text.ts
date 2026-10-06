// Strip Vietnamese tones for accent-insensitive matching ("ao thun" matches "Áo Thun")
const COMBINING = new RegExp('[\u0300-\u036f]', 'g');

export const removeTones = (str: string): string =>
  str.normalize('NFD').replace(COMBINING, '').replace(/đ/g, 'd').replace(/Đ/g, 'D');
