// III.9 / II.5 on the full stack: sub-accounts with a permission-filtered menu, shop categories and decoration
// (Kênh Người Bán) → the shop page tabs a buyer sees. Needs an admin to approve the test shop:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test shop-design.spec.cjs
const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');
const { BASE, apiAs, apiLogin, registerViaApi, shopWithProduct } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;
const SAMPLE_PNG = path.join(__dirname, 'fixtures', 'sample.png');

async function sellerLogin(browser, account) {
  const page = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();
  await page.goto(`${BASE}/seller/`);
  await page.getByLabel('Tên đăng nhập').fill(account.phone);
  await page.getByLabel('Mật khẩu').fill(account.password);
  await page.getByTestId('login-submit').click();
  await expect(page.getByRole('menuitem').first()).toBeVisible();
  return page;
}

test.describe('Thiết lập & trang trí shop', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');

  test('Tài khoản phụ: chủ shop thêm CSKH → nhân viên chỉ thấy mục được cấp quyền; gỡ là mất quyền ngay', async ({ browser, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const staff = await registerViaApi(request, 'Nhân Viên CSKH');

    const owner = await sellerLogin(browser, shop.seller);
    await owner.getByRole('menuitem', { name: 'Tài khoản phụ' }).click();
    await owner.getByTestId('staff-add').click();
    await owner.getByTestId('staff-login').fill(staff.phone);
    await owner.getByTestId('staff-role').click();
    await owner.locator('.ant-select-item-option', { hasText: 'Chăm sóc khách hàng' }).click();
    await owner.getByRole('button', { name: 'Lưu' }).click();
    await expect(owner.getByText('Đã thêm nhân viên.')).toBeVisible();
    await expect(owner.getByRole('row', { name: /Nhân Viên CSKH/ })).toContainText('Chăm sóc khách hàng');

    // The staff member signs in with their own account: chat / reviews / orders, no finance, settings, staff
    const cs = await sellerLogin(browser, staff);
    const menu = cs.getByRole('menu');
    await expect(menu.getByRole('menuitem', { name: 'Chat' })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: 'Đánh giá' })).toBeVisible();
    for (const hidden of ['Tài chính', 'Thiết lập shop', 'Tài khoản phụ', 'Kênh Marketing']) {
      await expect(menu.getByRole('menuitem', { name: hidden, exact: true })).toHaveCount(0);
    }

    // Removed → the next call is refused (404 "not your shop")
    await owner.getByRole('row', { name: /Nhân Viên CSKH/ }).getByTestId('staff-remove').click();
    await owner.locator('.ant-popconfirm').getByRole('button', { name: 'Gỡ', exact: true }).click();
    await expect(owner.getByText('Đã gỡ nhân viên khỏi shop.')).toBeVisible();
    const login = await apiLogin(request, staff.phone, staff.password);
    const res = await request.get(`${BASE}/api/seller/shops/${shop.shopId}/orders`, { headers: { Authorization: `Bearer ${login.accessToken}` } });
    expect(res.status()).toBe(404);
  });

  test('Danh mục + trang trí shop → người mua thấy tab Dạo, tab danh mục; lưu hồ sơ không mất giới thiệu', async ({ browser, page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const seller = await sellerLogin(browser, shop.seller);

    // Saving the profile with only a new logo keeps the saved description
    await seller.getByRole('menuitem', { name: 'Thiết lập shop' }).click();
    await expect(seller.getByTestId('shop-description')).toHaveValue('Shop kiểm thử đơn hàng');
    await seller.locator('input[type=file]').first().setInputFiles(SAMPLE_PNG);
    await expect(seller.getByAltText('Logo')).toBeVisible();
    await seller.getByRole('button', { name: 'Lưu hồ sơ' }).click();
    await expect(seller.getByText('Đã lưu hồ sơ shop.')).toBeVisible();
    const mine = await apiAs(request, shop.token, 'GET', '/seller/shops');
    expect(mine.find((s) => s.id === shop.shopId).description).toBe('Shop kiểm thử đơn hàng');

    // A shop category holding the product
    await seller.getByRole('menuitem', { name: 'Danh mục của shop' }).click();
    await seller.getByTestId('shop-category-add').click();
    await seller.getByTestId('shop-category-name').fill('Áo mùa hè');
    await seller.getByRole('button', { name: 'Lưu' }).click();
    await expect(seller.getByText('Đã thêm danh mục.')).toBeVisible();
    await seller.getByTestId('shop-category-products').click();
    await seller.getByRole('row', { name: new RegExp(shop.name) }).getByRole('checkbox').check();
    await seller.getByRole('button', { name: /Chọn \(1\)/ }).click();
    await expect(seller.getByText('Đã lưu sản phẩm của danh mục.')).toBeVisible();

    // Decoration: a banner, a featured-products block, a text block
    await seller.getByRole('menuitem', { name: 'Trang trí shop' }).click();
    const addBlock = async (label) => {
      await seller.getByTestId('block-add').click();
      await seller.getByRole('menuitem', { name: label }).click();
    };
    await addBlock('Banner');
    await seller.getByTestId('banner-upload').locator('..').locator('input[type=file]').setInputFiles(SAMPLE_PNG);
    await expect(seller.getByAltText('Ảnh 1')).toBeVisible();
    await addBlock('Sản phẩm nổi bật');
    await seller.getByTestId('block-pick-products').click();
    await seller.getByRole('row', { name: new RegExp(shop.name) }).getByRole('checkbox').check();
    await seller.getByRole('button', { name: /Chọn \(1\)/ }).click();
    await addBlock('Đoạn chữ');
    await seller.getByTestId('block-text').fill('Giao trong 2 giờ nội thành.');
    // Move the text block to the top with the keyboard-friendly buttons
    await seller.getByTestId('decoration-block').nth(2).getByRole('button', { name: 'Lên' }).click();
    await seller.getByTestId('decoration-block').nth(1).getByRole('button', { name: 'Lên' }).click();
    await seller.getByTestId('decoration-save').click();
    await expect(seller.getByText('Đã đăng trang trí shop.')).toBeVisible();

    // The buyer: "Dạo" is the default tab, blocks in the seller's order; the category is its own tab
    const slug = (await apiAs(request, shop.token, 'GET', '/seller/shops')).find((s) => s.id === shop.shopId).slug;
    await page.goto(`${BASE}/shop/${slug}`);
    await expect(page.getByTestId('shop-nav').first()).toHaveText('Dạo');
    await expect(page.getByTestId('shop-block')).toHaveCount(3);
    await expect(page.getByTestId('shop-block').first()).toContainText('Giao trong 2 giờ nội thành.');
    await expect(page.getByTestId('shop-block').nth(2)).toContainText(shop.name);
    await page.getByTestId('shop-nav').filter({ hasText: 'Áo mùa hè' }).click();
    await expect(page).toHaveURL(/tab=dm-/);
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(1);
    await page.getByTestId('shop-nav').filter({ hasText: 'Hồ Sơ Shop' }).click();
    await expect(page.getByTestId('shop-profile')).toContainText('Shop kiểm thử đơn hàng');
  });

  test('Đăng ký bán hàng: ảnh CCCD (bucket riêng tư) xem trước được ngay trên trang', async ({ browser, request }) => {
    const applicant = await registerViaApi(request, 'Người Đăng Ký Shop');
    const page = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();
    await page.goto(`${BASE}/seller/`);
    await page.getByLabel('Tên đăng nhập').fill(applicant.phone);
    await page.getByLabel('Mật khẩu').fill(applicant.password);
    await page.getByTestId('login-submit').click();
    await page.locator('input[type=file]').first().setInputFiles(SAMPLE_PNG);
    const preview = page.getByAltText('Mặt trước CCCD');
    await expect(preview).toBeVisible();
    // Private files have no public URL: the local preview must actually render (a blob: image is blocked by the CSP)
    await expect.poll(() => preview.evaluate((img) => img.complete && img.naturalWidth > 0)).toBe(true);
  });

  test('Excel hàng loạt: tệp mẫu theo ngành, tệp giá & tồn kho chạy nền (Hangfire thật) và báo kết quả', async ({ browser, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const seller = await sellerLogin(browser, shop.seller);
    await seller.getByRole('menuitem', { name: 'Excel hàng loạt' }).click();

    await seller.getByTestId('bulk-category').locator('input').fill('Áo Thun');
    await seller.locator('.ant-cascader-menu-item').filter({ hasText: /Áo Thun$/ }).first().click();
    const [template] = await Promise.all([seller.waitForEvent('download'), seller.getByTestId('bulk-template').click()]);
    const bytes = fs.readFileSync(await template.path());
    expect(bytes.subarray(0, 2).toString()).toBe('PK');   // an .xlsx (zip) file

    const [sheet] = await Promise.all([seller.waitForEvent('download'), seller.getByTestId('bulk-price-download').click()]);
    const sheetPath = await sheet.path();
    await seller.getByTestId('bulk-price-upload').locator('..').locator('input[type=file]').setInputFiles({
      name: 'gia-ton-kho.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: fs.readFileSync(sheetPath),
    });
    // The work runs in a Hangfire worker of the stack; the page follows it until it is done
    await expect(seller.getByTestId('bulk-status').first()).toHaveText('Xong', { timeout: 90_000 });
    await expect(seller.getByTestId('bulk-message').first()).toHaveText('Đã cập nhật 0 SKU.');
  });
});
