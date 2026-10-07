import { describe, expect, it } from 'vitest';
import { productListQuery } from '../api/seller';

// D6: every filter the product list offers reaches the API (the category filter used to be dropped)
describe('product list query', () => {
  it('sends the category and the stock / price ranges that are set, nothing for the empty ones', () => {
    const qs = productListQuery({ tab: 'All', q: 'ly', page: 2, pageSize: 20, categoryId: 'c-1', minStock: 0, maxStock: null, minPrice: 1000 });
    expect(Object.fromEntries(qs)).toEqual({ tab: 'All', q: 'ly', page: '2', pageSize: '20', categoryId: 'c-1', minStock: '0', minPrice: '1000' });
  });
});
