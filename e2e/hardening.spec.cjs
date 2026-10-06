// Phase 13 — spec section 9, scenarios 12 and 13. Needs the stack (and an admin for 12):
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test hardening.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, apiAs, apiLogin, findProduct, loginInBrowser, registerViaApi, shopWithProduct, withoutTiers } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

test.describe('Khoá tài khoản cắt phiên ngay', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');

  test('Quản trị khoá người mua đang đăng nhập → yêu cầu kế tiếp bị từ chối', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Sẽ Bị Khoá');
    const { userId } = await addAddressViaApi(request, buyer);
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/tai-khoan/ho-so`);
    await expect(page.getByRole('heading', { name: /Hồ Sơ/i })).toBeVisible();

    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    await apiAs(request, admin.accessToken, 'POST', `/admin/users/${userId}/lock`, { reason: 'Kiểm thử khoá phiên' });

    // The very next call with the access token the browser still holds is refused (no waiting for expiry)
    const status = await page.evaluate(async () => {
      const res = await fetch('/api/account/me', { credentials: 'include' });
      return res.status;
    });
    expect(status).toBe(401);
    await page.goto(`${BASE}/tai-khoan/don-mua`);
    await expect(page).toHaveURL(/dang-nhap/);
    // And the password no longer opens a session
    const login = await request.post(`${BASE}/api/auth/login`, { data: { identifier: buyer.phone, password: buyer.password } });
    expect(login.status()).not.toBe(200);
  });
});

/** Horizontal overflow of the document, in CSS pixels (0 = no sideways scroll). */
const overflow = (page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

test.describe('Giao diện ở 375 px và 1366 × 768', () => {
  test.setTimeout(240_000);

  test('Mọi trang người mua ở 375 px không cuộn ngang', async ({ browser, request }) => {
    const product = await findProduct(request, withoutTiers);
    const shopSlug = product.shop.slug;
    const buyer = await registerViaApi(request, 'Người Mua Điện Thoại');
    await addAddressViaApi(request, buyer);
    const context = await browser.newContext({ viewport: { width: 375, height: 812 }, isMobile: true, hasTouch: true });
    const page = await context.newPage();
    const pages = ['/', `/san-pham/${product.id}`, '/tim-kiem?q=ao', `/shop/${shopSlug}`, '/gio-hang', '/tra-cuu-van-don', '/tro-giup',
      '/trang/dieu-khoan-su-dung', '/dang-nhap', '/dang-ky', '/quen-mat-khau'];
    const signedIn = ['/thanh-toan', '/tai-khoan/don-mua', '/tai-khoan/ho-so', '/tai-khoan/dia-chi', '/tai-khoan/vi', '/tai-khoan/xu',
      '/tai-khoan/voucher', '/tai-khoan/thong-bao', '/thong-bao', '/yeu-thich', '/chat'];
    const problems = [];
    for (const path of pages) {
      await page.goto(`${BASE}${path}`);
      await page.waitForLoadState('networkidle');
      const px = await overflow(page);
      if (px > 0) problems.push(`${path}: ${px}px`);
    }
    // On a phone the account lives behind the header icon (the desktop top bar is hidden)
    await page.goto(`${BASE}/dang-nhap`);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(buyer.phone);
    await page.locator('input[aria-label="Mật khẩu"]').fill(buyer.password);
    await page.locator('[data-testid="login-submit"]').click();
    await expect(page.getByTestId('mobile-account')).toHaveAttribute('href', '/tai-khoan');
    const product2 = await apiAs(request, (await apiLogin(request, buyer.phone, buyer.password)).accessToken, 'GET', `/products/${product.id}`);
    await apiAs(request, (await apiLogin(request, buyer.phone, buyer.password)).accessToken, 'POST', '/cart/items', { skuId: product2.skus[0].id, quantity: 1 });
    for (const path of signedIn) {
      await page.goto(`${BASE}${path}`);
      await page.waitForLoadState('networkidle');
      const px = await overflow(page);
      if (px > 0) problems.push(`${path}: ${px}px`);
    }
    expect(problems).toEqual([]);
  });

  test('Kênh Người Bán và Quản trị ở 1366 × 768 không cuộn ngang', async ({ browser, request }) => {
    test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần tài khoản quản trị');
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const page = await context.newPage();
    const problems = [];
    await page.goto(`${BASE}/admin/`);
    await page.getByLabel('Tên đăng nhập').fill(ADMIN_USER);
    await page.getByLabel('Mật khẩu').fill(ADMIN_PASSWORD);
    await page.getByTestId('login-submit').click();
    await expect(page.getByTestId('admin-menu')).toBeVisible();
    for (const path of ['/', '/bao-cao', '/don-hang', '/duyet-san-pham', '/shop', '/nganh-hang', '/voucher', '/marketing', '/tai-chinh',
      '/nguoi-dung', '/vai-tro', '/tham-so', '/nhat-ky', '/noi-dung', '/nha-cung-cap', '/bao-cao-san-pham', '/khieu-nai']) {
      await page.goto(`${BASE}/admin${path}`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(1_200);
      const px = await overflow(page);
      if (px > 0) problems.push(`admin${path}: ${px}px`);
    }

    // A fresh seller with an approved shop
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const seller = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();
    await seller.goto(`${BASE}/seller/`);
    await seller.getByLabel('Tên đăng nhập').fill(shop.seller.phone);
    await seller.getByLabel('Mật khẩu').fill(shop.seller.password);
    await seller.getByTestId('login-submit').click();
    await expect(seller.getByRole('menuitem', { name: 'Đơn hàng' })).toBeVisible();
    for (const path of ['/tong-quan', '/don-hang', '/tra-hang', '/danh-gia', '/chat', '/tai-chinh', '/phan-tich', '/san-pham', '/san-pham/moi',
      '/ma-giam-gia', '/marketing', '/thiet-lap']) {
      await seller.goto(`${BASE}/seller${path}`);
      await seller.waitForLoadState('domcontentloaded');
      await seller.waitForTimeout(1_200);
      const px = await overflow(seller);
      if (px > 0) problems.push(`seller${path}: ${px}px`);
    }
    expect(problems).toEqual([]);
  });
});
