// Phase 9 — Flash Sale (spec section 9, scenario 8): 30 browser sessions press "Đặt hàng" at the same moment for 10
// flash units → exactly 10 orders. Needs the stack and an admin:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test flash.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, apiAs, apiLogin, loginInBrowser, registerViaApi, shopWithProduct } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;
const SESSIONS = 30;
const QUOTA = 10;

test.describe('Flash Sale', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD để duyệt shop');
  test.setTimeout(420_000);

  test(`${SESSIONS} phiên trình duyệt song song tranh ${QUOTA} suất → đúng ${QUOTA} đơn`, async ({ browser, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin, { stock: 100, price: 199000 });
    const product = await apiAs(request, shop.token, 'GET', `/products/${shop.productId}`);
    const skuId = product.skus[0].id;

    // The shop's own flash sale, live now: 10 units at ₫99.000, one per buyer
    await apiAs(request, shop.token, 'POST', `/seller/shops/${shop.shopId}/marketing/flash-sales`, {
      startAt: new Date(Date.now() - 5_000).toISOString(),
      endAt: new Date(Date.now() + 3_600_000).toISOString(),
      items: [{ skuId, flashPrice: 99000, quota: QUOTA, perUserLimit: 1 }],
    });
    const deals = await apiAs(request, shop.token, 'GET', `/products/${shop.productId}/deals`);
    expect(deals.flash.quota).toBe(QUOTA);

    // 30 buyers, each with an address, the flash SKU in the cart and a signed-in browser on the checkout page.
    // Setup goes one by one (the gateway allows 30 API calls / s per IP); only the final click is simultaneous.
    const pages = [];
    for (let i = 0; i < SESSIONS; i++) {
      const buyer = await registerViaApi(request, `Người Săn Sale ${i + 1}`);
      const { token } = await addAddressViaApi(request, buyer);
      await apiAs(request, token, 'POST', '/cart/items', { skuId, quantity: 1 });
      const page = await (await browser.newContext()).newPage();
      await loginInBrowser(page, buyer);
      await page.goto(`${BASE}/thanh-toan`);
      await expect(page.getByTestId('place-order')).toBeEnabled({ timeout: 20_000 });
      await expect(page.getByTestId('checkout-item').first()).toContainText('Flash Sale');
      pages.push(page);
    }

    await Promise.all(pages.map((p) => p.getByTestId('place-order').click()));

    // Each session ends either on the success page or back on checkout with the sold-out message
    const outcomes = await Promise.all(pages.map(async (p) => {
      await expect(async () => {
        const won = /\/dat-hang-thanh-cong/.test(p.url());
        const lost = await p.getByTestId('checkout-error').isVisible();
        expect(won || lost).toBeTruthy();
      }).toPass({ timeout: 60_000 });
      return /\/dat-hang-thanh-cong/.test(p.url()) ? 'won' : 'lost';
    }));
    expect(outcomes.filter((o) => o === 'won')).toHaveLength(QUOTA);
    for (const p of pages.filter((_, i) => outcomes[i] === 'lost'))
      await expect(p.getByTestId('checkout-error')).toContainText(/hết suất|thay đổi/);

    // The shop sees exactly 10 orders, and the flash sale shows 10/10 sold
    const orders = await apiAs(request, shop.token, 'GET', `/seller/shops/${shop.shopId}/orders?tab=All&pageSize=50`);
    expect(orders.totalCount).toBe(QUOTA);
    const after = await apiAs(request, shop.token, 'GET', `/products/${shop.productId}/deals`);
    expect(after.flash === null || after.flash.sold === QUOTA).toBeTruthy();
    const flash = await apiAs(request, shop.token, 'GET', `/seller/shops/${shop.shopId}/marketing/flash-sales`);
    expect(flash[0].items[0].sold).toBe(QUOTA);
  });
});
