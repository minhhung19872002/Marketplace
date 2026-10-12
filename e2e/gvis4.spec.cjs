// UI upgrade G-VIS round 4: the cart and the checkout against the reference marketplace (docs/plans/G-VIS-4.md).
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test gvis4.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api, registerViaApi, addAddressViaApi, apiLogin, apiAs, loginInBrowser } = require('./helpers.cjs');

const guest = { storageState: { cookies: [], origins: [] } };
const noPopup = (context) => context.addInitScript(() => { try { localStorage.setItem('sh_popup_seen', JSON.stringify({ at: Date.now() })); } catch { /* */ } });

/** A signed-in buyer with an address and one line from each of two shops in the cart, on a page of the given width. */
const buyerWithCart = async (browser, request, width) => {
  const buyer = await registerViaApi(request, 'Người Mua Giỏ Hàng');
  await addAddressViaApi(request, buyer);
  const token = (await apiLogin(request, buyer.phone, buyer.password)).accessToken;
  const shops = new Set();
  for (const p of (await api(request, '/search/products?q=ao&pageSize=12')).items) {
    if (shops.has(p.shopId) || shops.size === 2) continue;
    const sku = (await api(request, `/products/${p.id}`)).skus.find((k) => k.available > 2);
    if (!sku) continue;
    await apiAs(request, token, 'POST', '/cart/items', { skuId: sku.id, quantity: 1 });
    shops.add(p.shopId);
  }
  // Signed in at desktop width (the account menu is in the top bar), then resized: no reload, the session stays
  const context = await browser.newContext({ ...guest, viewport: { width: 1440, height: 900 } });
  await noPopup(context);
  const page = await context.newPage();
  await loginInBrowser(page, buyer);
  await page.setViewportSize({ width, height: width < 600 ? 844 : 900 });
  return { page, context };
};

/** Check boxes and radios the buyer sees that still wear the browser's own colour (blue) instead of the brand's. */
const unthemedControls = (page) => page.evaluate(() => [...document.querySelectorAll('input[type="checkbox"], input[type="radio"]')]
  .filter((el) => el.offsetParent !== null && getComputedStyle(el).opacity !== '0' && getComputedStyle(el).appearance !== 'none')
  .filter((el) => getComputedStyle(el).accentColor === 'auto')
  .map((el) => el.getAttribute('aria-label') || el.name || el.dataset.testid || el.type));

/** Voucher rows whose action dropped under the title instead of sitting on its line. */
const brokenVoucherRows = (page) => page.evaluate(() => [...document.querySelectorAll('.checkout-voucher')]
  .filter((row) => row.offsetParent !== null)
  .filter((row) => {
    const title = row.querySelector('.checkout-voucher-title').getBoundingClientRect();
    const action = row.querySelector('.voucher-trigger').getBoundingClientRect();
    return Math.abs(title.top + title.height / 2 - (action.top + action.height / 2)) > 4;
  })
  .map((row) => row.querySelector('.checkout-voucher-title').textContent.trim()));

test.describe('G-VIS vòng 4: giỏ hàng và thanh toán', () => {
  // L199: the shop's check box in the cart and the carrier radios at checkout were the browser's blue
  test('Mọi ô chọn ở giỏ và thanh toán mang màu thương hiệu, không màu xanh mặc định', async ({ browser, request }) => {
    const { page, context } = await buyerWithCart(browser, request, 1440);
    const problems = [];
    for (const path of ['/gio-hang', '/thanh-toan']) {
      await page.goto(`${BASE}${path}`);
      await page.waitForLoadState('networkidle');
      for (const c of await unthemedControls(page)) problems.push(`${path}: ${c}`);
    }
    expect(problems).toEqual([]);
    // The carrier list is open while one is being changed
    await page.getByTestId('carrier-change').first().click();
    for (const c of await unthemedControls(page)) problems.push(`/thanh-toan (đổi vận chuyển): ${c}`);
    expect(problems).toEqual([]);
    await context.close();
  });

  // L200: at phone width a checkout line kept half the row, its name cut to two words
  test('Thanh toán ở 375 px: tên sản phẩm có đủ chỗ (≥ 2 dòng, không bị ép nửa hàng)', async ({ browser, request }) => {
    const { page, context } = await buyerWithCart(browser, request, 375);
    await page.goto(`${BASE}/thanh-toan`);
    await expect(page.getByTestId('checkout-item').first()).toBeVisible();
    const widths = await page.locator('.checkout-product-name').evaluateAll((els) => els.map((el) => Math.round(el.getBoundingClientRect().width)));
    expect(widths.length).toBeGreaterThan(0);
    for (const w of widths) expect(w, 'bề ngang tên sản phẩm').toBeGreaterThanOrEqual(180);
    const clamp = await page.locator('.checkout-product-name').first().evaluate((el) => getComputedStyle(el).webkitLineClamp);
    expect(Number(clamp)).toBeGreaterThanOrEqual(2);
    await context.close();
  });

  // L201: "Chọn hoặc nhập mã" / "1 mã dùng được" fell under its title on a phone
  test('Dòng voucher: tiêu đề và nút chọn mã trên cùng một hàng (375 px và 1440 px)', async ({ browser, request }) => {
    const problems = [];
    for (const width of [375, 1440]) {
      const { page, context } = await buyerWithCart(browser, request, width);
      for (const path of ['/gio-hang', '/thanh-toan']) {
        await page.goto(`${BASE}${path}`);
        await expect(page.locator('.checkout-voucher').first()).toBeVisible();
        for (const r of await brokenVoucherRows(page)) problems.push(`${width}px ${path}: ${r}`);
      }
      await context.close();
    }
    expect(problems).toEqual([]);
  });

  // Decision #216: one carrier line per shop as on the reference marketplace; "Thay đổi" opens the list
  test('Vận chuyển: một dòng đơn vị đã chọn, "Thay đổi" mở danh sách và đổi được phí', async ({ browser, request }) => {
    const { page, context } = await buyerWithCart(browser, request, 1440);
    await page.goto(`${BASE}/thanh-toan`);
    const shop = page.getByTestId('checkout-shop').first();
    await expect(shop.getByTestId('carrier-current')).toBeVisible();
    await expect(shop.getByTestId('carrier-option')).toHaveCount(0);
    await shop.getByTestId('carrier-change').click();
    const options = shop.getByTestId('carrier-option');
    expect(await options.count()).toBeGreaterThan(1);
    const before = await page.getByTestId('checkout-total').textContent();
    const other = options.filter({ hasNot: page.locator('input:checked') }).first();
    const otherName = (await other.locator('strong').textContent()).trim();
    await other.click();
    await expect(shop.getByTestId('carrier-option')).toHaveCount(0);
    await expect(shop.getByTestId('carrier-current')).toContainText(otherName);
    await expect(page.getByTestId('checkout-total')).not.toHaveText(before);
    await context.close();
  });

  // Decision #217: the checkout keeps the buyer on the order - the slim header (logo, page name), no search or top bar
  test('Trang thanh toán dùng header gọn: logo + "Thanh toán", không thanh trên cùng, không ô tìm kiếm', async ({ browser, request }) => {
    const { page, context } = await buyerWithCart(browser, request, 1440);
    await page.goto(`${BASE}/thanh-toan`);
    await expect(page.locator('.auth-header')).toBeVisible();
    await expect(page.locator('.auth-header-title')).toHaveText('Thanh toán');
    await expect(page.locator('.header-top')).toHaveCount(0);
    await expect(page.locator('header input[type="search"], header input[aria-label*="Tìm"]')).toHaveCount(0);
    await context.close();
  });
});
