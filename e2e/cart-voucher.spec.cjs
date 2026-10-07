// E6 (spec II.6): a shop voucher picked right in the cart block shows what the shop takes off (priced by the server) and
// is the voucher the checkout uses. Needs an admin to approve the test shop:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test cart-voucher.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, apiAs, apiLogin, loginInBrowser, registerViaApi, shopWithProduct } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

test.describe('Voucher shop trong giỏ', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');
  test.setTimeout(90_000);

  test('Chọn voucher của shop ngay trong khối giỏ → thấy số tiền giảm → trang thanh toán dùng đúng mã ấy', async ({ page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin, { price: 200_000 });
    const code = `GIO${shop.seller.phone.slice(-6)}`;
    const now = Date.now();
    await apiAs(request, shop.token, 'POST', `/seller/shops/${shop.shopId}/vouchers`, {
      code, name: 'Giảm 20k trong giỏ', type: 'Amount', discountValue: 20_000, discountPercentBp: 0, maxDiscount: null, minOrder: 100_000,
      audience: 'Everyone', categoryIds: [], productIds: [], startAt: new Date(now - 60_000).toISOString(),
      endAt: new Date(now + 86_400_000).toISOString(), totalQuota: null, perUserLimit: 1, isPublic: true, channel: 'All',
    });

    const buyer = await registerViaApi(request, 'Người Mua Voucher Giỏ');
    await addAddressViaApi(request, buyer);
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/san-pham/${shop.productId}`);
    await page.getByTestId('add-to-cart').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');

    await page.goto(`${BASE}/gio-hang`);
    const block = page.getByTestId('cart-shop').filter({ hasText: shop.shopName });
    await block.getByTestId('cart-voucher-select').selectOption(code);
    await expect(block.getByTestId('cart-voucher-saving')).toContainText('20.000');

    await page.getByTestId('checkout').click();
    await page.waitForURL(`${BASE}/thanh-toan`);
    await expect(page.getByTestId('shop-voucher-select')).toHaveValue(code);
  });
});
