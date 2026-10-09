// UI upgrade G3: what the review of the deployed G2 site found — a guest's pages without console errors, the home
// popup that waits for the visitor, and the phone cart bar on one row.
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test g3-ui.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, api, apiAs, apiLogin, findProduct, registerViaApi, withoutTiers } = require('./helpers.cjs');

test.describe('G3 — trang người mua', () => {
  test('Khách chưa đăng nhập mở 5 trang công khai: không có lỗi nào trong console (A2)', async ({ browser, request }) => {
    const product = await findProduct(request, withoutTiers);
    // A fresh browser: never signed in, so no session to refresh
    const page = await (await browser.newContext({ storageState: { cookies: [], origins: [] } })).newPage();
    const errors = [];
    const refreshCalls = [];
    page.on('console', (m) => { if (m.type() === 'error') errors.push(`${page.url()}: ${m.text()}`); });
    page.on('pageerror', (e) => errors.push(`${page.url()}: ${e.message}`));
    page.on('request', (r) => { if (r.url().includes('/api/auth/refresh')) refreshCalls.push(page.url()); });
    for (const path of ['/', `/san-pham/${product.id}`, '/tim-kiem?q=ao', `/shop/${product.shop.slug}`, '/gio-hang']) {
      await page.goto(`${BASE}${path}`);
      await page.waitForLoadState('networkidle');
    }
    expect(refreshCalls).toEqual([]);
    expect(errors).toEqual([]);
  });

  test('Đã đăng nhập thì tải lại trang vẫn giữ phiên (cờ phiên không chặn nhầm)', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Giữ Phiên');
    await page.goto(`${BASE}/dang-nhap`);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(buyer.phone);
    await page.locator('input[aria-label="Mật khẩu"]').fill(buyer.password);
    await page.locator('[data-testid="login-submit"]').click();
    await expect(page.locator('[data-testid="user-menu"]')).toBeAttached();
    await page.reload();
    await expect(page.locator('[data-testid="user-menu"]')).toBeAttached();
  });

  test('Giỏ ở 375 px: thanh tổng dính đáy một hàng, nút Chat nổi nằm trên thanh (A1, C2)', async ({ browser, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Giỏ Điện Thoại');
    await addAddressViaApi(request, buyer);
    const token = (await apiLogin(request, buyer.phone, buyer.password)).accessToken;
    const shops = new Set();
    for (const card of (await api(request, '/search/products?inStock=true&sort=BestSelling&pageSize=60')).items) {
      if (shops.size >= 2 || shops.has(card.shopId)) continue;
      const detail = await api(request, `/products/${card.id}`);
      const sku = detail.skus.find((k) => k.available > 2);
      if (!sku) continue;
      await apiAs(request, token, 'POST', '/cart/items', { skuId: sku.id, quantity: 2 });
      shops.add(card.shopId);
    }
    expect(shops.size, 'dữ liệu mẫu có hàng của 2 shop').toBe(2);
    const context = await browser.newContext({ viewport: { width: 375, height: 812 }, isMobile: true, hasTouch: true });
    const page = await context.newPage();
    await page.goto(`${BASE}/dang-nhap`);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(buyer.phone);
    await page.locator('input[aria-label="Mật khẩu"]').fill(buyer.password);
    await page.locator('[data-testid="login-submit"]').click();
    // Signed in only once the phone account icon leads to the account (it shows for guests too)
    await expect(page.getByTestId('mobile-account')).toHaveAttribute('href', '/tai-khoan');
    await page.goto(`${BASE}/gio-hang`);
    await expect(page.getByTestId('cart-item')).toHaveCount(2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(375);
    const bar = await page.locator('.cart-footer').boundingBox();
    expect(bar.y + bar.height).toBeCloseTo(812, 0);
    expect(bar.height).toBeLessThanOrEqual(64);
    await expect(page.getByTestId('checkout')).toHaveText(/Mua hàng \(4\)/);
    const chat = await page.getByTestId('chat-launcher').boundingBox();
    expect(chat.y + chat.height).toBeLessThanOrEqual(bar.y);
  });
});
