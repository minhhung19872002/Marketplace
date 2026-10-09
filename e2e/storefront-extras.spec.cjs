// II.2–II.9 pieces on the full stack: shop vouchers saved from the product page, shipping estimate to a picked province,
// share, the shop's "online" line and in-shop search, the shop matching a keyword, the category's featured brands,
// "Đã xem" / "Shop theo dõi" pages and "Bạn có thể thích" in the cart. Needs an admin to approve the test shop:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test storefront-extras.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, apiAs, apiLogin, loginInBrowser, registerViaApi, shopWithProduct, pickAddress, pickOption } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

test.describe('Trang người mua — phần bổ sung', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');
  test.setTimeout(120_000);

  test('Trang sản phẩm: lưu voucher shop, phí ship theo tỉnh, chia sẻ; trang shop tìm trong shop; đã xem, theo dõi, gợi ý trong giỏ', async ({ page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const code = `SHOP${shop.seller.phone.slice(-6)}`;
    const now = Date.now();
    await apiAs(request, shop.token, 'POST', `/seller/shops/${shop.shopId}/vouchers`, {
      code, name: 'Giảm 10k', type: 'Amount', discountValue: 10000, discountPercentBp: 0, maxDiscount: null, minOrder: 50000,
      audience: 'Everyone', categoryIds: [], productIds: [], startAt: new Date(now - 60_000).toISOString(),
      endAt: new Date(now + 7 * 86_400_000).toISOString(), totalQuota: 100, perUserLimit: 1, isPublic: true, channel: 'All',
    });

    const buyer = await registerViaApi(request, 'Người Mua Bổ Sung');
    await addAddressViaApi(request, buyer);
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/san-pham/${shop.productId}`);

    // Voucher của shop → Lưu → in the wallet
    const voucher = page.getByTestId('shop-voucher').filter({ hasText: 'Giảm 10.000₫' });
    await expect(voucher).toBeVisible();
    await voucher.getByTestId('save-shop-voucher').click();
    await expect(voucher.getByTestId('save-shop-voucher')).toHaveText('Đã lưu');

    // Shipping to my default address (Hà Nội), then to another province: the server quotes again
    const shipping = page.getByTestId('pd-shipping');
    await expect(shipping.getByTestId('pd-shipping-option').first()).toBeVisible();
    await expect(shipping).toContainText('Gửi từ');
    const before = await shipping.getByTestId('pd-shipping-option').first().textContent();
    await Promise.all([
      page.waitForResponse((r) => r.url().includes(`/products/${shop.productId}/shipping?province=`) && r.ok()),
      pickOption(shipping, 'Vận chuyển tới', 'Thành phố Hồ Chí Minh'),
    ]);
    await expect(shipping.getByTestId('pd-shipping-option').first()).not.toHaveText(before);
    await expect(page.getByTestId('share-facebook')).toHaveAttribute('href', /facebook\.com\/sharer/);

    // The shop page: search inside the shop
    await page.getByTestId('view-shop').click();
    await page.getByTestId('shop-search').fill('Áo Thun');
    await page.getByTestId('shop-search').press('Enter');
    await expect(page).toHaveURL(/q=%C3%81o\+Thun|q=Áo/);
    await expect(page.locator('[data-testid="product-card"]').first()).toContainText('Áo Thun');

    // Follow → listed in "Shop theo dõi"; "Đã xem" lists the product
    await page.getByTestId('shop-follow').click();
    await expect(page.getByTestId('shop-follow')).toContainText('Đang theo dõi');
    await page.goto(`${BASE}/tai-khoan/shop-theo-doi`);
    await expect(page.getByTestId('followed-shop').filter({ hasText: shop.shopName })).toBeVisible();
    await page.goto(`${BASE}/tai-khoan/da-xem`);
    await expect(page.locator('[data-testid="product-card"]', { hasText: shop.name })).toBeVisible();

    // The cart suggests more products
    const detail = await apiAs(request, (await apiLogin(request, buyer.phone, buyer.password)).accessToken, 'GET', `/products/${shop.productId}`);
    await apiAs(request, (await apiLogin(request, buyer.phone, buyer.password)).accessToken, 'POST', '/cart/items', { skuId: detail.skus[0].id, quantity: 1 });
    await page.goto(`${BASE}/gio-hang`);
    await expect(page.getByTestId('cart-suggestions')).toBeVisible();
  });

  test('Tìm theo tên shop hiện khối shop liên quan; trang ngành có thương hiệu nổi bật lọc được', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem?q=techzone`);
    const related = page.getByTestId('related-shop');
    await expect(related).toContainText('TechZone');
    await related.locator('a').click();
    await expect(page).toHaveURL(/\/shop\/mall-techzone/);

    await page.goto(`${BASE}/danh-muc/thoi-trang-nam`);
    const brand = page.getByTestId('category-brand').first();
    await expect(brand).toBeVisible();
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/search/products') && r.url().includes('brands=') && r.ok()),
      brand.click(),
    ]);
    await expect(brand).toHaveClass(/active/);
  });
  test('Facet đơn vị vận chuyển và dịch vụ: số đếm khớp số kết quả sau khi lọc', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem?q=ao`);
    await page.waitForLoadState('networkidle');
    const count = async (locator) => Number(((await locator.locator('.filter-count').textContent()) ?? '').replace(/[^\d]/g, ''));
    const results = async () => Number(((await page.getByTestId('search-count').textContent()) ?? '').replace(/[^\d]/g, ''));

    // Other scenarios add products while this one runs: compare the total with the facet count of the SAME filtered answer
    const cod = page.getByTestId('facet-services').locator('label', { has: page.getByTestId('filter-cod') });
    expect(await count(cod)).toBeGreaterThan(0);
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/search/products') && r.url().includes('cod=true') && r.ok()),
      page.getByTestId('filter-cod').click(),
    ]);
    await expect(async () => expect(await results()).toBe(await count(cod))).toPass({ timeout: 5_000 });

    const carrier = page.getByTestId('facet-carriers').locator('label').first();
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/search/products') && r.url().includes('carriers=') && r.ok()),
      carrier.locator('input').click(),
    ]);
    await expect(async () => expect(await results()).toBe(await count(carrier))).toPass({ timeout: 5_000 });
  });
  test('Thanh toán: thêm địa chỉ mới ngay tại trang, đơn dùng địa chỉ vừa thêm', async ({ page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const buyer = await registerViaApi(request, 'Người Mua Thêm Địa Chỉ');
    const login = await apiLogin(request, buyer.phone, buyer.password);
    const detail = await apiAs(request, login.accessToken, 'GET', `/products/${shop.productId}`);
    await apiAs(request, login.accessToken, 'POST', '/cart/items', { skuId: detail.skus[0].id, quantity: 1 });
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/thanh-toan`);
    await expect(page.getByTestId('checkout-address')).toContainText('Bạn chưa có địa chỉ nhận hàng.');
    await page.getByTestId('checkout-address-add').click();
    const form = page.getByTestId('checkout-address-form');
    await form.getByLabel('Tên người nhận').fill('Người Nhận Tại Trang');
    await form.getByLabel('Số điện thoại người nhận').fill(buyer.phone);
    await pickAddress(form);
    await form.getByLabel('Địa chỉ cụ thể').fill('7 Phố Thanh Toán');
    await form.getByTestId('address-save').click();
    await expect(page.getByTestId('checkout-address')).toContainText('Người Nhận Tại Trang');
    await expect(page.getByTestId('place-order')).toBeEnabled();
  });

  test('Thanh toán: chọn hình thức tại cổng; trả góp mờ dưới mức tối thiểu, hình thức đã chọn tới được cổng', async ({ page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const buyer = await registerViaApi(request, 'Người Mua Chọn Hình Thức');
    await addAddressViaApi(request, buyer);
    const login = await apiLogin(request, buyer.phone, buyer.password);
    const detail = await apiAs(request, login.accessToken, 'GET', `/products/${shop.productId}`);
    await apiAs(request, login.accessToken, 'POST', '/cart/items', { skuId: detail.skus[0].id, quantity: 1 });
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/thanh-toan`);
    await expect(page.getByTestId('payment-options')).toHaveCount(0);
    await page.locator('[data-testid="method-Simulated"] input').click();
    const options = page.getByTestId('payment-options');
    await expect(options).toBeVisible();
    // The test shop's product is far below the instalment minimum
    await expect(options.locator('[data-testid="option-Installment"] input')).toBeDisabled();
    await expect(options.getByTestId('option-Installment')).toContainText('Trả góp áp dụng cho đơn từ');
    await options.locator('[data-testid="option-QrCode"] input').click();
    await expect(options.locator('[data-testid="option-QrCode"] input')).toBeChecked();
    await expect(page.getByTestId('place-order')).toBeEnabled();
    await page.getByTestId('place-order').click();
    await page.waitForURL(/\/cong-thanh-toan\//);
    await expect(page.getByTestId('gateway-way')).toHaveText('Hình thức: Quét mã QR');
  });
});
