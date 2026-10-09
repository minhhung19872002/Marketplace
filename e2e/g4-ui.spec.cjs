// UI upgrade G4: what the review of the deployed G3 site found — no sideways scroll at 390 / 800 / 1024 / 1440 px on the
// public pages, pages inside the standard side margin, one sales wording on the top tiles, the guest's empty cart, the
// reading layout of the static pages.
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test g4-ui.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api, findProduct, withTiers } = require('./helpers.cjs');

const WIDTHS = [390, 800, 1024, 1440];
const guest = { storageState: { cookies: [], origins: [] } };

test.describe('G4 — trang người mua', () => {
  test('Không trang công khai nào cuộn ngang ở 390 / 800 / 1024 / 1440 px (A1)', async ({ browser, request }) => {
    test.setTimeout(240_000);
    const product = await findProduct(request, withTiers);
    const tree = await api(request, '/categories');
    const category = tree.find((c) => c.isVisible !== false);
    const paths = ['/', `/danh-muc/${category.slug}`, '/tim-kiem?q=ao', `/san-pham/${product.id}`, `/shop/${product.shop.slug}`,
      '/flash-sale', '/su-kien/sieu-sale-10-10', '/gio-hang', '/tro-giup', '/trang/dieu-khoan-su-dung'];
    const overflow = [];
    for (const width of WIDTHS) {
      const context = await browser.newContext({ ...guest, viewport: { width, height: 900 }, isMobile: width < 500, hasTouch: width < 500 });
      const page = await context.newPage();
      for (const path of paths) {
        await page.goto(`${BASE}${path}`);
        await page.waitForLoadState('networkidle');
        const px = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        if (px > 0) overflow.push(`${width}px ${path}: ${px}px`);
      }
      await context.close();
    }
    expect(overflow).toEqual([]);
  });

  test('Flash Sale, sự kiện, danh mục đang cập nhật: nội dung cách mép trái như các trang khác (A3)', async ({ browser, request }) => {
    const hidden = (await api(request, '/categories')).find((c) => c.isVisible === false);
    const page = await (await browser.newContext({ ...guest, viewport: { width: 1024, height: 900 } })).newPage();
    for (const path of ['/flash-sale', '/su-kien/sieu-sale-10-10']) {
      await page.goto(`${BASE}${path}`);
      const box = await page.locator('h1').first().boundingBox();
      expect(box.x, path).toBeGreaterThanOrEqual(12);
    }
    // The slot bar of /flash-sale: no second list of the next slots under it
    await page.goto(`${BASE}/flash-sale`);
    await expect(page.locator('.flash-sale-next')).toHaveCount(0);
    if (hidden) {
      await page.goto(`${BASE}/danh-muc/${hidden.slug}`);
      const box = await page.getByTestId('category-hidden').locator('.search-empty').boundingBox();
      expect(box.x).toBeGreaterThanOrEqual(12);
      await expect(page.getByText('Có thể bạn cũng thích')).toBeVisible();
    }
  });

  test('"Tìm kiếm hàng đầu": mọi ô cùng một cách ghi lượt bán (A1)', async ({ page }) => {
    await page.goto(BASE);
    await page.getByTestId('top-category').first().waitFor();
    const lines = await page.locator('.top-category-sold').allTextContents();
    test.skip(lines.length === 0, 'Không có ô "Tìm kiếm hàng đầu"');
    const monthly = lines.filter((l) => /\/ tháng/.test(l)).length;
    expect([0, lines.length]).toContain(monthly);
  });

  test('Thẻ sản phẩm chưa có đánh giá chỉ hiện "Đã bán", không có chữ bị cắt (A2)', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem?q=ao`);
    await expect(page.getByTestId('product-card').first()).toBeVisible();
    await expect(page.getByTestId('product-card').filter({ hasText: 'Chưa có đánh giá' })).toHaveCount(0);
  });

  test('Khách mở giỏ trống thấy nút "Đăng nhập để xem giỏ hàng của bạn" (A4)', async ({ browser }) => {
    const page = await (await browser.newContext(guest)).newPage();
    await page.goto(`${BASE}/gio-hang`);
    await expect(page.getByTestId('cart-empty')).toBeVisible();
    await page.getByTestId('cart-empty-login').click();
    await expect(page).toHaveURL(/\/dang-nhap/);
  });

  test('Trang tĩnh: mục lục từ các tiêu đề, bấm vào cuộn tới đúng mục; điện thoại gập lại (B)', async ({ browser }) => {
    const page = await (await browser.newContext({ ...guest, viewport: { width: 1440, height: 900 } })).newPage();
    await page.goto(`${BASE}/trang/dieu-khoan-su-dung`);
    const toc = page.getByTestId('content-toc');
    await expect(toc.locator('a').first()).toBeVisible();
    const headings = await page.locator('[data-testid="cms-page"] h2').count();
    await expect(toc.locator('a')).toHaveCount(headings);
    const last = toc.locator('a').last();
    const target = (await last.getAttribute('href')).slice(1);
    await last.click();
    await expect(page).toHaveURL(new RegExp(`#${target}$`));
    await expect(page.locator(`[id="${target}"]`)).toBeInViewport();
    // Text column of at most 72 characters (CSS ch: the width of "0" in the text font)
    const chars = await page.locator('.content-body p').first().evaluate((p) => {
      const probe = document.createElement('span');
      probe.textContent = '0'.repeat(10);
      p.appendChild(probe);
      const ch = probe.getBoundingClientRect().width / 10;
      probe.remove();
      return p.getBoundingClientRect().width / ch;
    });
    expect(chars).toBeLessThanOrEqual(72.5);

    const phone = await (await browser.newContext({ ...guest, viewport: { width: 390, height: 800 }, isMobile: true, hasTouch: true })).newPage();
    await phone.goto(`${BASE}/trang/dieu-khoan-su-dung`);
    await expect(phone.getByTestId('content-toc').locator('a').first()).toBeHidden();
    await phone.getByTestId('content-toc').locator('summary').click();
    await expect(phone.getByTestId('content-toc').locator('a').first()).toBeVisible();
  });
});
