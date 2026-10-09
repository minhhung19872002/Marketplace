import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { rel, sourceFiles, stripComments } from './scan';

// G3 C1: labels, headings and buttons are written in sentence case ("Có thể bạn cũng thích"), never ALL CAPS — in the
// text itself or through CSS text-transform. Acronyms (COD, SKU, OTP…) are fine.
const ACRONYMS = new Set(['COD', 'VND', 'SKU', 'OTP', 'CCCD', 'API', 'FAQ', 'KYC', 'QR', 'ID', 'VAT', 'GHN', 'GHTK', 'SPX', 'VNPAY', 'MOMO',
  'JCB', 'PDF', 'CSV', 'URL', 'SMS', 'HTML', 'SEO', 'HTTP', 'HTTPS', 'UTC', 'XL', 'XXL', 'USB', 'LED', 'TV', 'SSD', 'RAM', 'GB', 'PC', 'OK',
  'VIP', 'MST', 'ATM', 'JSON', 'KB', 'MB', 'SIM', 'NFC', 'GPS', 'VN', 'TP', 'HCM', 'KG', 'ML', 'UV', 'IOS', 'POS', 'CSKH', 'ĐVVC']);
// G4 C: no "Title Case Per Word" either ("Đang Theo Dõi" → "Đang theo dõi"). A run of 3+ capitalised words is flagged unless it
// is one of the proper nouns below; common 2-word UI labels that used to be title-cased are listed so they cannot come back.
const PROPER_NOUNS = ['Kênh Người Bán', 'ShopHub Mall', 'ShopHub Xu', 'Ví ShopHub', 'Flash Sale', 'Freeship+', 'Voucher Plus',
  'Bộ Công Thương', 'Việt Nam', 'Hà Nội', 'Hồ Chí Minh', 'Be Vietnam Pro', 'Google Identity Services'];
const TITLE_PAIRS = ['Theo Dõi', 'Mua Ngay', 'Chat Ngay', 'Đăng Nhập', 'Đăng Ký', 'Đăng Xuất', 'Thông Báo', 'Hỗ Trợ', 'Đã Bán', 'Xem Shop',
  'Tất Cả', 'Hồ Sơ', 'Giỏ Hàng', 'Thanh Toán', 'Đặt Hàng', 'Mua Hàng', 'Xem Thêm', 'Đánh Giá', 'Sản Phẩm', 'Yêu Thích', 'Đã Thích',
  'Liên Quan', 'Mới Nhất', 'Bán Chạy', 'Khuyến Mãi', 'Hoạt Động', 'Đơn Mua', 'Trả Hàng', 'Hoàn Tiền', 'Địa Chỉ', 'Vận Chuyển',
  'Số Lượng', 'Đơn Giá', 'Số Tiền', 'Thao Tác', 'Tham Gia', 'Nơi Bán', 'Thương Hiệu', 'Khoảng Giá', 'Tình Trạng', 'Dịch Vụ',
  'Đặt Trước', 'Ví Voucher', 'Ưu Đãi', 'Mật Khẩu', 'Gần Đây', 'Danh Mục'];
// A capitalised word: an upper-case letter followed by a lower-case one (acronyms such as COD are not counted)
const CAP_WORD = String.raw`\p{Lu}\p{Ll}\p{L}*`;
const TITLE_RUN = new RegExp(String.raw`(?<![\p{L}\d_.])${CAP_WORD}(?:[  ]+${CAP_WORD}){2,}(?![\p{L}\d_])`, 'gu');
const TITLE_PAIR = new RegExp(String.raw`(?<![\p{L}\d_])(?:${TITLE_PAIRS.join('|')})(?![\p{L}\d_])`, 'gu');
// Proper nouns are blanked out (with a non-letter) before matching, so they neither match nor extend a run
const maskProperNouns = (line: string): string => PROPER_NOUNS.reduce((l, p) => l.split(p).join('|'), line);
const CAPS_RUN =/(?<![\p{L}\d.\-/_])\p{Lu}{2,}(?:[  ]+(?:&[  ]+)?\p{Lu}{2,})+(?![\p{L}\d])/gu;

describe('sentence case', () => {
  it('has no ALL-CAPS phrase in TSX', () => {
    const offenders = sourceFiles(['.tsx']).flatMap((file) =>
      stripComments(readFileSync(file, 'utf8')).split('\n').flatMap((line, i) =>
        [...line.matchAll(CAPS_RUN)]
          .filter((m) => !m[0].split(/[  &]+/).every((w) => !w || ACRONYMS.has(w)))
          .map((m) => `${rel(file)}:${i + 1}: ${m[0]}`)));
    expect(offenders).toEqual([]);
  });

  it('has no Title Case phrase in TS/TSX', () => {
    const offenders = sourceFiles(['.ts', '.tsx']).flatMap((file) =>
      stripComments(readFileSync(file, 'utf8')).split('\n').flatMap((line, i) => {
        const masked = maskProperNouns(line);
        return [...masked.matchAll(TITLE_RUN), ...masked.matchAll(TITLE_PAIR)].map((m) => `${rel(file)}:${i + 1}: ${m[0]}`);
      }));
    expect(offenders).toEqual([]);
  });

  // G-VIS (docs/00 #203): UPPER CASE is allowed only for the home / category section titles and the Flash Sale wordmark and
  // bar labels — through text-transform on exactly these rules; every other rule stays sentence case
  const UPPERCASE_ALLOWED = [
    'pages/HomePage.css .home-page .sh-section-title',
    'pages/HomePage.css .home-daily-title',
    'components/FlashSaleBlock.css .flash-sale-wordmark',
    'components/FlashSaleBlock.css .flash-item-bar-text',
  ];

  it('has text-transform: uppercase only on the allowed rules', () => {
    const used = sourceFiles(['.css']).flatMap((file) =>
      [...readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^{}]*)\}/g)]
        .filter((m) => /text-transform:\s*uppercase/.test(m[2]))
        .flatMap((m) => m[1].split(',').map((sel) => `${rel(file)} ${sel.trim().replace(/\s+/g, ' ')}`)));
    expect(used.filter((u) => !UPPERCASE_ALLOWED.includes(u))).toEqual([]);
  });
});
