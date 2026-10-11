import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { findLines, rel, sourceFiles, stripComments } from './scan';

// Capitalisation policy (docs/00 #209). Labels, headings and buttons are written in sentence case ("Có thể bạn cũng thích"),
// never ALL CAPS and never Title Case — in the text itself or through CSS / inline styles. Acronyms (COD, SKU, OTP…) are fine.
// The UPPER CASE section titles of G-VIS belong to the buyer site's home page only; this app has none.
const ACRONYMS = new Set(['COD', 'VND', 'SKU', 'OTP', 'CCCD', 'API', 'FAQ', 'KYC', 'QR', 'ID', 'VAT', 'GHN', 'GHTK', 'SPX', 'VNPAY', 'MOMO',
  'JCB', 'PDF', 'CSV', 'URL', 'SMS', 'HTML', 'SEO', 'HTTP', 'HTTPS', 'UTC', 'XL', 'XXL', 'USB', 'LED', 'TV', 'SSD', 'RAM', 'GB', 'PC', 'OK',
  'VIP', 'MST', 'ATM', 'JSON', 'KB', 'MB', 'SIM', 'NFC', 'GPS', 'VN', 'TP', 'HCM', 'KG', 'ML', 'UV', 'IOS', 'POS', 'CSKH', 'ĐVVC']);
// G4: nor Title Case Per Word ("Quản Trị Sàn") — three or more capitalised words in a row, proper names excepted
const TITLE_RUN = /(?<![\p{L}\d])\p{Lu}\p{Ll}+(?:[  ]+\p{Lu}\p{Ll}+){2,}(?![\p{L}\d])/gu;
const PROPER_NAMES = new Set(['Be Vietnam Pro', 'Ant Design', 'Hồ Chí Minh', 'Thành Phố Hồ Chí Minh']);
// Product names that may follow a capitalised first word ("Khung Flash Sale")
const PRODUCT_NAMES = /Flash Sale|ShopHub Mall|ShopHub Xu|Ví ShopHub|Freeship Xtra|Voucher Xtra/g;
const CAPS_RUN = /(?<![\p{L}\d.\-/_])\p{Lu}{2,}(?:[  ]+(?:&[  ]+)?\p{Lu}{2,})+(?![\p{L}\d])/gu;

describe('sentence case', () => {
  it('has no ALL-CAPS phrase in TSX', () => {
    const offenders = sourceFiles(['.tsx']).flatMap((file) =>
      stripComments(readFileSync(file, 'utf8')).split('\n').flatMap((line, i) =>
        [...line.matchAll(CAPS_RUN)]
          .filter((m) => !m[0].split(/[  &]+/).every((w) => !w || ACRONYMS.has(w)))
          .map((m) => `${rel(file)}:${i + 1}: ${m[0]}`)));
    expect(offenders).toEqual([]);
  });

  it('has no Title Case Per Word phrase in TSX', () => {
    const offenders = sourceFiles(['.tsx']).flatMap((file) =>
      stripComments(readFileSync(file, 'utf8')).split('\n').flatMap((line, i) =>
        [...line.replace(PRODUCT_NAMES, 'x').matchAll(TITLE_RUN)].filter((m) => !PROPER_NAMES.has(m[0])).map((m) => `${rel(file)}:${i + 1}: ${m[0]}`)));
    expect(offenders).toEqual([]);
  });

  it('has no text-transform: uppercase in CSS', () => {
    const offenders = sourceFiles(['.css']).flatMap((file) =>
      readFileSync(file, 'utf8').split('\n').flatMap((line, i) => (/text-transform:\s*uppercase/.test(line) ? [`${rel(file)}:${i + 1}`] : [])));
    expect(offenders).toEqual([]);
  });

  it('has no other case transform (capitalize = Title Case, small-caps) in CSS or inline styles', () => {
    const css = findLines(sourceFiles(['.css']), /text-transform:\s*capitalize|font-variant(-caps)?:\s*[^;]*small-caps/);
    const inline = findLines(sourceFiles(['.tsx', '.ts']), /textTransform|fontVariant(Caps)?\s*:/);
    expect([...css, ...inline]).toEqual([]);
  });
});
