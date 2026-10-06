import { describe, expect, it } from 'vitest';
import { searchQueryString } from '../api/storefront';

describe('searchQueryString', () => {
  it('repeats array keys and drops defaults', () => {
    const qs = searchQueryString({
      q: '  dien thoai ',
      provinces: ['01', '79'],
      attrs: ['Chất liệu=Cotton'],
      mall: true,
      inStock: false,
      sort: 'Relevance',
      page: 1,
      minPrice: 0,
    });
    const p = new URLSearchParams(qs);
    expect(p.get('q')).toBe('dien thoai');
    expect(p.getAll('provinces')).toEqual(['01', '79']);
    expect(p.getAll('attrs')).toEqual(['Chất liệu=Cotton']);
    expect(p.get('mall')).toBe('true');
    expect(p.has('inStock')).toBe(false);
    expect(p.has('sort')).toBe(false);
    expect(p.has('page')).toBe(false);
    // 0 is a real lower bound, not "unset"
    expect(p.get('minPrice')).toBe('0');
  });
});
