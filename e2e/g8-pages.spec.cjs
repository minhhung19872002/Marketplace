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
});
