// UI upgrade G2 (P1): variant chips → cart, filters and sort kept in the URL, the mobile buy sheet, the hidden-category
// page and a COD checkout through to the success page — on the seeded data.
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test g2-ui.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, api, apiAs, apiLogin, findProduct, loginInBrowser, registerViaApi, withTiers, withoutTiers } = require('./helpers.cjs');

/** Exactly this option's text ("S" must not pick "XS"). */
const exact = (text) => new RegExp(`^\\s*${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);

test.describe('G2 — trang người mua', () => {
  test('Chọn phân loại: chip được chọn có dấu tích, thêm vào giỏ đúng SKU', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Phân Loại');
    const product = await findProduct(request, withTiers);
    const sku = product.skus.find((s) => s.available > 2);
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/san-pham/${product.id}`);

    // Without a variant the add is refused with the tier names
    await page.getByTestId('add-to-cart').click();
    await expect(page.getByTestId('variant-error')).toBeVisible();

    const option1 = page.getByTestId('variant-option').filter({ hasText: exact(sku.option1) }).first();
    await option1.click();
    await expect(option1).toHaveAttribute('aria-pressed', 'true');
    await expect(option1.locator('.pd-variant-tick')).toHaveCount(1);
    if (sku.option2) await page.getByTestId('variant-option').filter({ hasText: exact(sku.option2) }).last().click();
    await page.getByTestId('add-to-cart').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');

    await page.goto(`${BASE}/gio-hang`);
    await expect(page.getByTestId('cart-item')).toContainText(product.name);
    await expect(page.getByTestId('cart-variant')).toContainText(sku.option1);
  });

  test('Bộ lọc và sắp xếp nằm trên URL: mở lại link giữ nguyên, chip "Đang lọc" gỡ được', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem?q=ao&sort=PriceAsc&minRating=4`);
    await expect(page.getByTestId('sort-price')).toContainText('Giá: thấp đến cao');
    const chip = page.getByTestId('active-filter').filter({ hasText: 'Từ 4 sao' });
    await expect(chip).toBeVisible();
    await expect(page.getByTestId('filter-rating').nth(1)).toHaveAttribute('aria-pressed', 'true');

    // Prices come back sorted
    await expect(async () => {
      const prices = (await page.getByTestId('product-card-price').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
      expect(prices.length).toBeGreaterThan(1);
      expect(prices).toEqual([...prices].sort((x, y) => x - y));
    }).toPass({ timeout: 5_000 });

    // Removing the chip drops the filter from the URL, the sort stays
    await chip.click();
    await expect(page).not.toHaveURL(/minRating/);
    await expect(page).toHaveURL(/sort=PriceAsc/);

    // The pager writes the page into the URL; the sort menu switches direction
    const pager = page.getByTestId('search-pager');
    if (await pager.count()) {
      await pager.getByRole('button', { name: 'Trang 2' }).click();
      await expect(page).toHaveURL(/page=2/);
    }
    await page.getByTestId('sort-price').click();
    await page.getByTestId('sort-price-desc').click();
    await expect(page).toHaveURL(/sort=PriceDesc/);
    await expect(page).not.toHaveURL(/page=/);
  });

  test('Danh mục đang ẩn: mở link thấy "Danh mục đang cập nhật" và gợi ý, không phải 404', async ({ page, request }) => {
    const tree = await api(request, '/categories');
    const hidden = tree.find((c) => !c.isVisible);
    test.skip(!hidden, 'Không có ngành nào đang ẩn trong dữ liệu gieo');
    await page.goto(`${BASE}/danh-muc/${hidden.slug}`);
    await expect(page.getByTestId('category-hidden')).toContainText('Danh mục đang cập nhật');
    await expect(page.locator('[data-testid="product-card"]').first()).toBeVisible();
    // ...and the home grid never lists it
    await page.goto(BASE);
    await expect(page.getByTestId('category-item').filter({ hasText: hidden.name })).toHaveCount(0);
  });

  test('Điện thoại: thanh mua dính đáy mở bảng chọn phân loại rồi thêm vào giỏ', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Di Động');
    const product = await findProduct(request, withTiers);
    const sku = product.skus.find((s) => s.available > 2);
    // Signed in on a wide screen (the account menu lives in the top bar), then the phone width
    await loginInBrowser(page, buyer);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${BASE}/san-pham/${product.id}`);
    await expect(page.getByTestId('add-to-cart')).toBeHidden();

    await page.getByTestId('mobile-add-to-cart').click();
    const sheet = page.getByTestId('pd-sheet');
    await expect(sheet).toBeVisible();
    await sheet.getByTestId('variant-option').filter({ hasText: exact(sku.option1) }).first().click();
    if (sku.option2) await sheet.getByTestId('variant-option').filter({ hasText: exact(sku.option2) }).last().click();
    await sheet.getByTestId('pd-sheet-confirm').click();
    await expect(sheet).toHaveCount(0);
    // No sideways scroll with the bar on screen
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('Thanh toán COD từ giỏ tới trang đặt hàng thành công', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua COD');
    await addAddressViaApi(request, buyer);
    const product = await findProduct(request, withoutTiers);
    const login = await apiLogin(request, buyer.phone, buyer.password);
    await apiAs(request, login.accessToken, 'POST', '/cart/items', { skuId: product.skus[0].id, quantity: 1 });
    await loginInBrowser(page, buyer);

    await page.goto(`${BASE}/gio-hang`);
    await page.getByTestId('checkout').click();
    await page.waitForURL(`${BASE}/thanh-toan`);
    await page.getByTestId('method-Cod').click();
    await expect(page.getByTestId('method-Cod')).toHaveClass(/active/);
    const total = await page.getByTestId('checkout-total').textContent();
    await page.getByTestId('place-order').click();

    await page.waitForURL(/\/dat-hang-thanh-cong/);
    await expect(page.getByTestId('order-success')).toBeVisible();
    await expect(page.getByTestId('result-order')).toHaveCount(1);
    await expect(page.getByTestId('success-total')).toHaveText(total);
  });

  test('Đánh giá "Hữu ích": khách bị mời đăng nhập, người mua bấm thì số tăng', async ({ page, request }) => {
    // A product with reviews (the seed writes them on completed orders)
    const product = await findProduct(request, (p) => p.ratingCount > 0, 'sort=BestSelling&pageSize=60');
    await page.goto(`${BASE}/san-pham/${product.id}`);
    const first = page.getByTestId('review').first();
    await first.scrollIntoViewIfNeeded();
    await first.getByTestId('review-helpful').click();
    await page.waitForURL(/\/dang-nhap/);

    const buyer = await registerViaApi(request, 'Người Thấy Hữu Ích');
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/san-pham/${product.id}`);
    const button = page.getByTestId('review').first().getByTestId('review-helpful');
    await button.click();
    await expect(button).toHaveAttribute('aria-pressed', 'true');
    await expect(button).toContainText(/Hữu ích \(\d+\)/);
  });
});
