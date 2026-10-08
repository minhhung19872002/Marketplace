import { describe, expect, it } from 'vitest';
import { pageWindow } from '../lib/text';
import { starShare } from '../components/ProductReviews';
import { shopFacts } from '../pages/ProductDetail';
import type { ProductPage } from '../types';

describe('pageWindow', () => {
  it('lists every page when there are few', () => {
    expect(pageWindow(1, 1)).toEqual([1]);
    expect(pageWindow(3, 7)).toEqual([1, 2, 3, 4, 5, 6, 7]);
  });

  it('keeps the first, the last and two either side of the current page', () => {
    expect(pageWindow(1, 20)).toEqual([1, 2, 3, 4, 5, 6, '…', 20]);
    expect(pageWindow(10, 20)).toEqual([1, '…', 8, 9, 10, 11, 12, '…', 20]);
    expect(pageWindow(20, 20)).toEqual([1, '…', 15, 16, 17, 18, 19, 20]);
  });
});

describe('starShare', () => {
  it('is the share of the reviews with that many stars', () => {
    expect(starShare({ 5: 3, 4: 1 }, 5, 4)).toBe(75);
    expect(starShare({ 5: 3 }, 1, 4)).toBe(0);
    expect(starShare({}, 5, 0)).toBe(0);
  });
});

describe('shopFacts', () => {
  const shop = { ratingAvg: 4.8, ratingCount: 0, productCount: 12, followerCount: 0, joinedAt: '2025-01-15T03:00:00Z' } as ProductPage['shop'];

  it('leaves out the figures with no data instead of showing 0', () => {
    expect(shopFacts(shop).map((f) => f.key)).toEqual(['products', 'joined']);
    expect(shopFacts({ ...shop, ratingCount: 5, followerCount: 3 }).map((f) => f.key)).toEqual(['rating', 'products', 'followers', 'joined']);
  });
});
