import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { rel, sourceFiles, stripComments } from './scan';

// G3 C1: labels, headings and buttons are written in sentence case ("Có thể bạn cũng thích"), never ALL CAPS — in the
// text itself or through CSS text-transform. Acronyms (COD, SKU, OTP…) are fine.
const ACRONYMS = new Set(['COD', 'VND', 'SKU', 'OTP', 'CCCD', 'API', 'FAQ', 'KYC', 'QR', 'ID', 'VAT', 'GHN', 'GHTK', 'SPX', 'VNPAY', 'MOMO',
  'JCB', 'PDF', 'CSV', 'URL', 'SMS', 'HTML', 'SEO', 'HTTP', 'HTTPS', 'UTC', 'XL', 'XXL', 'USB', 'LED', 'TV', 'SSD', 'RAM', 'GB', 'PC', 'OK',
  'VIP', 'MST', 'ATM', 'JSON', 'KB', 'MB', 'SIM', 'NFC', 'GPS', 'VN', 'TP', 'HCM', 'KG', 'ML', 'UV', 'IOS', 'POS', 'CSKH', 'ĐVVC']);
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

  it('has no text-transform: uppercase in CSS', () => {
    const offenders = sourceFiles(['.css']).flatMap((file) =>
      readFileSync(file, 'utf8').split('\n').flatMap((line, i) => (/text-transform:\s*uppercase/.test(line) ? [`${rel(file)}:${i + 1}`] : [])));
    expect(offenders).toEqual([]);
  });
});
