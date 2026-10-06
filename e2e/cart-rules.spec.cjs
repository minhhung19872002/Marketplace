// Spec 3.4 / II.4 on the full stack: per-buyer purchase limit (product page, cart) and "Sản phẩm tương tự" for a
// sold-out cart line. Needs an admin to approve the test shop:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test cart-rules.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, apiAs, apiLogin, loginInBrowser, registerViaApi, shopWithProduct } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

test.describe('Luật giỏ hàng', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');

  test('Giới hạn mua mỗi người: trang sản phẩm chặn số lượng, giỏ từ chối vượt; hết hàng → sản phẩm tương tự', async ({ page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin, { stock: 20 });
    const detail = await apiAs(request, shop.token, 'GET', `/seller/shops/${shop.shopId}/products/${shop.productId}`);
    await apiAs(request, shop.token, 'PUT', `/seller/shops/${shop.shopId}/products/${shop.productId}`,
      { input: { ...shop.input, maxPerBuyer: 2 }, version: detail.version });

    const buyer = await registerViaApi(request, 'Người Mua Giới Hạn');
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/san-pham/${shop.productId}`);
    await expect(page.getByTestId('pd-limit')).toHaveText('Mỗi người mua tối đa 2 sản phẩm');
    const qty = page.getByLabel('Số lượng', { exact: true });
    await page.getByRole('button', { name: 'Tăng' }).click();
    await page.getByRole('button', { name: 'Tăng' }).click();
    await expect(qty).toHaveValue('2');

    // Through the API the cart refuses a third one
    const login = await apiLogin(request, buyer.phone, buyer.password);
    await apiAs(request, login.accessToken, 'POST', '/cart/items', { skuId: detail.skus[0].id, quantity: 2 });
    const third = await request.post(`${BASE}/api/cart/items`, {
      headers: { Authorization: `Bearer ${login.accessToken}` }, data: { skuId: detail.skus[0].id, quantity: 1 },
    });
    expect(third.status()).toBe(409);
    expect((await third.json()).message).toContain('tối đa 2');

    // The shop runs out: the line says so and offers products of the same category
    await apiAs(request, shop.token, 'PUT', `/seller/shops/${shop.shopId}/skus/${detail.skus[0].id}`, { stock: 0 });
    await page.goto(`${BASE}/gio-hang`);
    await expect(page.getByTestId('cart-item-problem')).toContainText('Hết hàng');
    await page.getByTestId('cart-similar').click();
    await expect(page.getByTestId('cart-similar-item').first()).toBeVisible();
    await page.getByTestId('cart-similar-item').first().click();
    await expect(page).toHaveURL(/\/san-pham\/.+-i\./);
  });
});
