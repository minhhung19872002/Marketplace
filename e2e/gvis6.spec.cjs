// UI upgrade G-VIS round 6: no "0 đã bán" on cards and the product page, Mall tiles with even gaps and photos
// (docs/plans/G-VIS-6.md).
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test gvis6.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api } = require('./helpers.cjs');

const guest = { storageState: { cookies: [], origins: [] } };
const noPopup = (context) => context.addInitScript(() => { try { localStorage.setItem('sh_popup_seen', JSON.stringify({ at: Date.now() })); } catch { /* */ } });

const guestPage = async (browser, width = 1440) => {
  const context = await browser.newContext({ ...guest, viewport: { width, height: 900 } });
  await noPopup(context);
  return { context, page: await context.newPage() };
};

/** Sold lines reading zero, and the heights of the price row (and meta row of detailed cards) of every card in view. */
const cardReport = (page) => page.locator('.product-card').evaluateAll((els) => els.map((el) => ({
  zero: [...el.querySelectorAll('.product-card-sold')].some((s) => /^(0 đã bán|Đã bán 0)$/.test(s.textContent.trim())),
  sold: !!el.querySelector('.product-card-sold'),
  row: Math.round(el.querySelector('.product-card-price-row').getBoundingClientRect().height),
  meta: Math.round(el.querySelector('.product-card-meta')?.getBoundingClientRect().height ?? 0),
})));

test.describe('G-VIS vòng 6', () => {
  // L210: tiles of products nobody has bought yet read "0 đã bán"
  test('Thẻ sản phẩm: số đã bán = 0 thì không hiện dòng ấy, hàng giá giữ nguyên chiều cao', async ({ browser, request }) => {
    const newest = (await api(request, '/search/products?sort=Newest&pageSize=60')).items;
    expect(newest.some((p) => p.soldCount === 0), 'có sản phẩm chưa bán trong trang mới nhất').toBe(true);
    const { page, context } = await guestPage(browser);
    for (const path of ['/tim-kiem?sort=Newest', '/']) {
      await page.goto(`${BASE}${path}`);
      await expect(page.getByTestId('product-card').first()).toBeVisible();
      const cards = await cardReport(page);
      expect(cards.filter((c) => c.zero), path).toEqual([]);
      if (path !== '/') {
        // Cards with and without the sold line are the same height
        expect(cards.some((c) => !c.sold) && cards.some((c) => c.sold), path).toBe(true);
        expect(new Set(cards.map((c) => c.meta)).size, `${path} meta`).toBe(1);
      }
      const withSold = cards.filter((c) => c.sold).map((c) => c.row);
      const without = cards.filter((c) => !c.sold).map((c) => c.row);
      if (withSold.length && without.length) expect(Math.max(...without), path).toBe(Math.max(...withSold));
    }
    await context.close();
  });

  test('Trang chi tiết: sản phẩm chưa bán không ghi "0 đã bán"', async ({ browser, request }) => {
    const fresh = (await api(request, '/search/products?sort=Newest&pageSize=60')).items.find((p) => p.soldCount === 0);
    test.skip(!fresh, 'Không có sản phẩm chưa bán');
    const { page, context } = await guestPage(browser);
    await page.goto(`${BASE}/san-pham/${fresh.id}`);
    await expect(page.getByTestId('pd-name')).toBeVisible();
    await expect(page.locator('.product-detail-stats')).not.toContainText('đã bán');
    await expect(page.getByTestId('pd-sold')).toHaveCount(0);
    await context.close();
  });

  // L211: the tiles touched (light photo backgrounds ran together) and the "Xem tất cả" tile's picture had its own size
  test('Ô thương hiệu Mall: cách đều ~10px, mọi ảnh cùng cỡ ở cả 2 hàng', async ({ browser }) => {
    for (const width of [1440, 1120, 800, 390]) {
      const { page, context } = await guestPage(browser, width);
      await page.goto(`${BASE}/`);
      const tiles = page.locator('[data-testid="mall-brand"], [data-testid="mall-brand-all"]');
      await tiles.first().scrollIntoViewIfNeeded();
      const boxes = await tiles.evaluateAll((els) => els.map((el) => {
        const t = el.getBoundingClientRect();
        const p = el.querySelector('.mall-brand-photo').getBoundingClientRect();
        return { x: t.x, y: t.y, w: t.width, h: t.height, pw: p.width, ph: p.height };
      }));
      expect(boxes.length, `${width}px`).toBeGreaterThan(3);
      for (const b of boxes) {
        expect(Math.abs(b.pw - boxes[0].pw), `${width}px ảnh rộng`).toBeLessThanOrEqual(1);
        expect(Math.abs(b.ph - boxes[0].ph), `${width}px ảnh cao`).toBeLessThanOrEqual(1);
      }
      // Neighbours in a row, and the rows below each other, are 8–12 px apart
      const rows = [...new Set(boxes.map((b) => Math.round(b.y)))].sort((a, b) => a - b);
      expect(rows.length, `${width}px`).toBeGreaterThan(1);
      for (const y of rows) {
        const row = boxes.filter((b) => Math.round(b.y) === y).sort((a, b) => a.x - b.x);
        for (let i = 1; i < row.length; i++) {
          const gap = row[i].x - (row[i - 1].x + row[i - 1].w);
          expect(gap, `${width}px khoảng ngang`).toBeGreaterThanOrEqual(8);
          expect(gap, `${width}px khoảng ngang`).toBeLessThanOrEqual(12);
        }
      }
      for (let i = 1; i < rows.length; i++) {
        const above = boxes.find((b) => Math.round(b.y) === rows[i - 1]);
        const gap = rows[i] - (above.y + above.h);
        expect(gap, `${width}px khoảng dọc`).toBeGreaterThanOrEqual(7);
        expect(gap, `${width}px khoảng dọc`).toBeLessThanOrEqual(13);
      }
      await context.close();
    }
  });
});
