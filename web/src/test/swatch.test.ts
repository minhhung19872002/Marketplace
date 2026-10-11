import { describe, expect, it } from 'vitest';
import { isColourTier, swatchOf } from '../lib/swatch';

// G-VIS round 2: "Màu Sắc" chips show their photo, or a dot in the colour the name says
describe('swatchOf', () => {
  it('reads the colour of an option name, two-word names first', () => {
    expect(swatchOf('Xanh Navy')).toBe('navy');
    expect(swatchOf('Xanh')).toBe('blue');
    expect(swatchOf('Tím Mận')).toBe('plum');
    expect(swatchOf('Đỏ Đen')).toBe('red');
    expect(swatchOf('Hồng')).toBe('pink');
    expect(swatchOf('Cỡ lớn')).toBeNull();
  });

  it('knows a colour tier', () => {
    expect(isColourTier('Màu Sắc')).toBe(true);
    expect(isColourTier('Màu')).toBe(true);
    expect(isColourTier('Kích Cỡ')).toBe(false);
  });
});
