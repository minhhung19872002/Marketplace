import { describe, expect, it } from 'vitest';
import { mixCovers } from '../lib/covers';
import type { ProductCard } from '../types';

const card = (i: number, cover: boolean): ProductCard => ({
  id: `p${i}`, name: `P${i}`, slug: `p${i}`, imageUrl: cover ? `c${i}-ad_600.webp` : `p${i}.webp`, minPrice: 1, maxPrice: 1, originalPrice: 1,
  discountPercent: 0, ratingAvg: 0, ratingCount: 0, soldCount: 0, inStock: true, shopId: 's', shopName: 's', isMall: false, isPreferred: false,
  provinceName: null, imageHasText: cover, photoUrl: cover ? `p${i}.webp` : null,
});

// G-VIS round 5 (L203): "Gợi ý hôm nay" showed covers side by side, many in a row
describe('mixCovers', () => {
  it('keeps at most a quarter of a grid as ad covers, never two side by side', () => {
    const shown = mixCovers(Array.from({ length: 60 }, (_, i) => card(i, true)));
    const covers = shown.map((c) => !!c.imageHasText);
    expect(covers.filter(Boolean).length).toBeLessThanOrEqual(15);
    expect(covers.some((c, i) => c && covers[i - 1])).toBe(false);
    for (let n = 1; n <= covers.length; n++) expect(covers.slice(0, n).filter(Boolean).length).toBeLessThanOrEqual(n / 4);
    // A swapped card shows its plain photo
    expect(shown[0].imageUrl).toBe('p0.webp');
  });

  it('decides each card once: loading the next page never changes a card already shown', () => {
    const all = Array.from({ length: 48 }, (_, i) => card(i, i % 2 === 0));
    expect(mixCovers(all).slice(0, 24)).toEqual(mixCovers(all.slice(0, 24)));
  });

  it('leaves a cover that has no plain photo, and plain photos untouched', () => {
    const lone = { ...card(1, true), photoUrl: null };
    expect(mixCovers([lone])[0]).toEqual(lone);
    const plain = card(2, false);
    expect(mixCovers([plain])[0]).toBe(plain);
  });
});
