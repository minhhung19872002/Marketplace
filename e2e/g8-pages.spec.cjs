// G8 D: the buyer pages synced with the G-VIS look (docs/plans/G8.md), and what they must never do.
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test g8-pages.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, registerViaApi, loginInBrowser } = require('./helpers.cjs');

const guest = { storageState: { cookies: [], origins: [] } };
const SOME_CHECKOUT = '00000000-0000-4000-8000-000000000000';

test.describe('G8 D — trang người mua', () => {
  // L215: opened straight from a link (gateway return, e-mail), the result pages asked for the checkout before the
  // session was restored — 401s in the console and a slower page
  test('Trang kết quả thanh toán mở thẳng từ liên kết: không gọi API trước khi khôi phục phiên', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Mở Liên Kết');
    await loginInBrowser(page, buyer);
    for (const path of [`/thanh-toan/ket-qua/${SOME_CHECKOUT}`, `/dat-hang-thanh-cong?checkout=${SOME_CHECKOUT}`]) {
      const unauthorised = [];
      const listen = (r) => { if (r.status() === 401 && r.url().includes('/api/') && !r.url().includes('/api/auth/')) unauthorised.push(r.url()); };
      page.on('response', listen);
      await page.goto(`${BASE}${path}`);
      await expect(page.getByText('Không tìm thấy đơn hàng.')).toBeVisible();
      page.off('response', listen);
      expect(unauthorised, path).toEqual([]);
    }
  });

  test('Khách chưa đăng nhập mở trang kết quả → sang đăng nhập', async ({ browser }) => {
    const context = await browser.newContext(guest);
    const page = await context.newPage();
    await page.goto(`${BASE}/thanh-toan/ket-qua/${SOME_CHECKOUT}`);
    await expect(page).toHaveURL(/\/dang-nhap/);
    await context.close();
  });

  // The round Chat button sat on top of prices and card buttons on a phone
  test('Điện thoại: nút nổi ẩn khi cuộn xuống, hiện lại khi cuộn lên; máy tính luôn hiện', async ({ browser }) => {
    const context = await browser.newContext({ ...guest, viewport: { width: 375, height: 812 } });
    const page = await context.newPage();
    await page.goto(`${BASE}/`);
    const fab = page.getByTestId('fab-stack');
    await expect(fab).not.toHaveClass(/is-tucked/);
    await page.mouse.wheel(0, 900);
    await expect(fab).toHaveClass(/is-tucked/);
    await page.mouse.wheel(0, -100);
    await expect(fab).not.toHaveClass(/is-tucked/);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.mouse.wheel(0, 900);
    await page.waitForTimeout(300);
    await expect(fab).not.toHaveClass(/is-tucked/);
    await context.close();
  });

  test('375 px: bảy ngày điểm danh một hàng, các tab ví voucher một hàng', async ({ browser, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Điện Thoại');
    const context = await browser.newContext(guest);
    const page = await context.newPage();
    // Signed in at the desktop width, where the header names the account
    await loginInBrowser(page, buyer);
    await page.setViewportSize({ width: 375, height: 812 });
    const rows = (selector) => page.locator(selector).evaluateAll((els) => new Set(els.map((e) => Math.round(e.getBoundingClientRect().top))).size);

    await page.goto(`${BASE}/tai-khoan/xu`);
    await expect(page.locator('.checkin-day')).toHaveCount(7);
    expect(await rows('.checkin-day')).toBe(1);
    // Each label on one line: every day is as tall as the first
    expect(await page.locator('.checkin-day').evaluateAll((els) => new Set(els.map((e) => e.offsetHeight)).size)).toBe(1);

    await page.goto(`${BASE}/tai-khoan/voucher`);
    await expect(page.getByTestId('vouchers-page')).toBeVisible();
    expect(await rows('[data-testid="vouchers-page"] .account-tab')).toBe(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    await context.close();
  });
});
