import { describe, expect, it } from 'vitest';
import { campaignChip } from '../components/ProductCard';
import { topSoldLine } from '../components/TopCategories';
import { mallOffer } from '../components/MallBrands';
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
    expect(topSoldLine(6)).toBe('Bán 6+ / tháng');
    expect(topSoldLine(27_400)).toBe('Bán 27k+ / tháng');
    // G-VIS round 2: never the all-time total; no line at all without a sale in 30 days
    expect(topSoldLine(0)).toBeNull();
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

describe('mallOffer', () => {
  it("shows the shop's programme, else its deepest discount, else the Mall promise", () => {
    expect(mallOffer(40, 'Mua 2 giảm 10%')).toBe('Mua 2 giảm 10%');
    expect(mallOffer(40, null)).toBe('Giảm đến 40%');
    expect(mallOffer(0, '  ')).toBe('Chính hãng 100%');
  });
});
