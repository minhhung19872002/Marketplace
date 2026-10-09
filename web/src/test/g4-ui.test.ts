import { describe, expect, it } from 'vitest';
import { campaignChip } from '../components/ProductCard';
import { topSoldLine } from '../components/TopCategories';
import { bannerOverlay } from '../pages/CampaignPage';
import { anchorId } from '../pages/ContentPages';
import type { CampaignBlock } from '../api/marketing';

// G4 review fixes: the small rules behind the UI changes
describe('campaignChip', () => {
  it('shows the sale date when the campaign name has one', () => {
    expect(campaignChip('Siêu Sale 10.10')).toBe('10.10');
    expect(campaignChip('Sale 9.9 giữa tháng')).toBe('9.9');
  });

  it('keeps a short name, cuts a long one to two words (never an ellipsis)', () => {
    expect(campaignChip('Tết')).toBe('Tết');
    expect(campaignChip('Ngày hội gia dụng')).toBe('Ngày hội');
  });
});

describe('topSoldLine', () => {
  it('uses one wording for the whole row', () => {
    expect(topSoldLine(6, 40, true)).toBe('Bán 6+ / tháng');
    expect(topSoldLine(0, 3, false)).toBe('Đã bán 3');
    expect(topSoldLine(6, 40, false)).toBe('Đã bán 40');
  });
});

describe('bannerOverlay', () => {
  const block = (b: Partial<CampaignBlock>): CampaignBlock =>
    ({ type: 'Banner', title: 'Siêu sale', imageUrl: null, link: null, vouchers: null, flashSale: null, products: null, ...b });

  it('draws the title only on a banner without artwork, or when the artwork is flagged text-free', () => {
    expect(bannerOverlay(block({}))).toBe(true);
    expect(bannerOverlay(block({ imageUrl: 'https://cdn.example/e.webp' }))).toBe(false);
    expect(bannerOverlay(block({ imageUrl: 'https://cdn.example/e.webp', hasTextInImage: false }))).toBe(true);
    expect(bannerOverlay(block({ title: null }))).toBe(false);
  });
});

describe('anchorId', () => {
  it('turns a Vietnamese heading into a plain anchor', () => {
    expect(anchorId('3. Giao dịch')).toBe('3-giao-dich');
    expect(anchorId('Đổi trả & hoàn tiền')).toBe('doi-tra-hoan-tien');
    expect(anchorId('—')).toBe('muc');
  });
});
