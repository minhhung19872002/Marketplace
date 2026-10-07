// E4 (spec II.4): the photo opens full screen; "Mua ngay" goes straight to checkout with only that line; the shop block
// shows the shop's rating.
//   SH_E2E_BASE_URL=http://localhost:18000 npx playwright test product-page.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, apiAs, apiLogin, findProduct, loginInBrowser, registerViaApi, withoutTiers } = require('./helpers.cjs');

test.describe('Trang chi tiết sản phẩm', () => {
  test('Phóng to ảnh: mở, chuyển ảnh, Esc để đóng; khối shop có điểm đánh giá', async ({ page, request }) => {
    const product = await findProduct(request, withoutTiers);
    await page.goto(`${BASE}/san-pham/${product.id}`);
    await page.getByTestId('pd-zoom').click();
    await expect(page.getByTestId('lightbox')).toBeVisible();
    await expect(page.getByTestId('lightbox-image')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('lightbox')).toHaveCount(0);
    await expect(page.getByTestId('shop-rating')).toContainText('Đánh Giá');
  });

  test('Mua ngay: sang thẳng thanh toán chỉ với dòng ấy, dòng khác trong giỏ không bị đặt kèm', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Ngay');
    await addAddressViaApi(request, buyer);
    // "Mua ngay" on a product without variants; something else (any in-stock SKU) already in the cart
    const second = await findProduct(request, withoutTiers);
    const first = await findProduct(request, (p) => p.id !== second.id && p.skus.some((s) => s.available > 0));
    const login = await apiLogin(request, buyer.phone, buyer.password);
    await apiAs(request, login.accessToken, 'POST', '/cart/items', { skuId: first.skus.find((s) => s.available > 0).id, quantity: 1 });
    await loginInBrowser(page, buyer);
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');

    await page.goto(`${BASE}/san-pham/${second.id}`);
    await page.getByTestId('buy-now').click();
    await page.waitForURL(`${BASE}/thanh-toan`);
    await expect(page.getByTestId('checkout-item')).toHaveCount(1);
    await expect(page.getByTestId('checkout-item')).toContainText(second.name);
    // The other line is still in the cart, just not ticked
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('2');
  });
});
