// UI upgrade G-VIS round 5: home grid photos, Mall tiles, product page price and add-on row, category chips and filters,
// cart free-shipping line and coins, checkout totals at the bottom (docs/plans/G-VIS-5.md).
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test gvis5.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api, registerViaApi, addAddressViaApi, apiLogin, apiAs, loginInBrowser } = require('./helpers.cjs');

const guest = { storageState: { cookies: [], origins: [] } };
const noPopup = (context) => context.addInitScript(() => { try { localStorage.setItem('sh_popup_seen', JSON.stringify({ at: Date.now() })); } catch { /* */ } });
const isCover = (src) => /-ad_\d+\.webp/.test(src || '');

const guestPage = async (browser, width = 1440) => {
  const context = await browser.newContext({ ...guest, viewport: { width, height: 900 } });
  await noPopup(context);
  return { context, page: await context.newPage() };
};

/** A signed-in buyer with an address and one line from each of two shops in the cart. */
const buyerWithCart = async (browser, request, width = 1440) => {
  const buyer = await registerViaApi(request, 'Người Mua Vòng Năm');
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
  const context = await browser.newContext({ ...guest, viewport: { width: 1440, height: 900 } });
  await noPopup(context);
  const page = await context.newPage();
  await loginInBrowser(page, buyer);
  await page.setViewportSize({ width, height: width < 600 ? 844 : 900 });
  return { page, context };
};

test.describe('G-VIS vòng 5', () => {
  // L203: covers side by side, and covers wearing the 10.10 strip over their own text
  test('Gợi ý hôm nay: tối đa 1/4 ảnh bìa, không 2 ảnh bìa liền nhau, ảnh bìa không bị đè dải chiến dịch', async ({ browser }) => {
    const { page, context } = await guestPage(browser);
    await page.goto(`${BASE}/`);
    const grid = page.getByTestId('daily-grid');
    await expect(grid.getByTestId('product-card').first()).toBeVisible();
    const cards = await grid.locator('.product-card').evaluateAll((els) => els.map((el) => ({
      src: el.querySelector('.product-card-img img')?.getAttribute('src') ?? '',
      strip: !!el.querySelector('[data-testid="product-card-campaign"]'),
    })));
    expect(cards.length).toBeGreaterThan(8);
    const covers = cards.map((c) => isCover(c.src));
    expect(covers.filter(Boolean).length).toBeLessThanOrEqual(cards.length / 4);
    expect(covers.findIndex((c, i) => c && covers[i - 1])).toBe(-1);
    expect(cards.filter((c) => isCover(c.src) && c.strip)).toEqual([]);
    await context.close();
  });

  // L204: the photo was a 110 px square in the middle of a ~200 px tile, often an ad cover whose text did not read
  test('Ô thương hiệu Mall: ảnh sản phẩm thường lấp hết bề ngang ô', async ({ browser }) => {
    for (const width of [1440, 1024]) {
      const { page, context } = await guestPage(browser, width);
      await page.goto(`${BASE}/`);
      const tiles = page.getByTestId('mall-brand');
      await tiles.first().scrollIntoViewIfNeeded();
      await expect(tiles.first().locator('img').first()).toBeVisible();
      const sizes = await tiles.evaluateAll((els) => els.map((el) => {
        const img = el.querySelector('.mall-brand-photo img');
        return { tile: el.getBoundingClientRect().width, photo: img.getBoundingClientRect().width, src: img.getAttribute('src'), fit: getComputedStyle(img).objectFit };
      }));
      for (const s of sizes) {
        expect(s.photo, `${width}px`).toBeGreaterThanOrEqual(s.tile * 0.95);
        expect(isCover(s.src), s.src).toBe(false);
        // G8 C: the tile is wider than the square photo — the product shows whole, never cut by "cover"
        expect(s.fit, s.src).toBe('contain');
      }
      await context.close();
    }
  });

  // L207 + #219: the "₫" of the price read as a link underline; the add-on deal was loose orange boxes
  test('Trang chi tiết: giá viết "đ" thường, khối Mua kèm deal sốc gọn một hàng', async ({ browser, request }) => {
    let productId = null;
    for (const p of (await api(request, '/search/products?sort=BestSelling&pageSize=60')).items) {
      const deals = await api(request, `/products/${p.id}/deals`);
      if (deals.offers.some((o) => o.type === 'AddOn' && (o.items ?? []).some((i) => i.skuId))) { productId = p.id; break; }
    }
    test.skip(!productId, 'Không có sản phẩm nào đang có Mua kèm deal sốc');
    const { page, context } = await guestPage(browser);
    await page.goto(`${BASE}/san-pham/${productId}`);
    const price = page.getByTestId('pd-price').first();
    await expect(price).toBeVisible();
    // What the eye sees: digits then a plain "đ"; screen readers get "158.000₫" from the hidden copy
    const shown = await price.evaluate((el) => (el.querySelector('.sh-price-shown') ?? el).textContent);
    expect(shown).not.toContain('₫');
    expect(shown).toMatch(/\dđ$/);
    expect(await price.locator('.sh-visually-hidden').textContent()).toContain('₫');

    const row = page.getByTestId('pd-offer-item').first();
    await expect(row).toBeVisible();
    const parts = await row.evaluate((el) => [...el.children].map((c) => {
      const r = c.getBoundingClientRect();
      return { cls: c.className, mid: r.top + r.height / 2, height: r.height };
    }));
    // Tag, photo, name, price, "Thêm" — side by side on one line
    expect(parts.map((p) => p.cls)).toEqual(['pd-offer', '', 'pd-offer-name', 'pd-offer-price', 'pd-offer-add']);
    for (const p of parts) expect(Math.abs(p.mid - parts[1].mid), p.cls).toBeLessThanOrEqual(4);
    expect((await row.boundingBox()).height).toBeLessThanOrEqual(48);
    await expect(row.getByTestId('pd-offer-add')).toHaveText('Thêm');
    await context.close();
  });

  // L208 + #220: a sub-category drew a letter instead of a photo; a Mall shop sat among the brands; four chips left a gap
  test('Trang danh mục: danh mục con đều có ảnh, dạng chip ngang; nhóm lọc Shop tách khỏi Thương hiệu', async ({ browser }) => {
    const { page, context } = await guestPage(browser);
    await page.goto(`${BASE}/danh-muc/sac-dep`);
    const chips = page.getByTestId('category-child');
    await expect(chips.first()).toBeVisible();
    const layout = await chips.evaluateAll((els) => els.map((el) => {
      const img = el.querySelector('img');
      const name = el.querySelector('.category-child-name').getBoundingClientRect();
      return { hasImg: !!img, imgRight: img ? img.getBoundingClientRect().right : 0, nameLeft: name.left };
    }));
    for (const c of layout) {
      expect(c.hasImg).toBe(true);
      expect(c.imgRight).toBeLessThanOrEqual(c.nameLeft + 1);
    }
    const shops = page.getByTestId('facet-shops');
    await expect(shops).toBeVisible();
    const shopNames = (await shops.locator('.filter-cat-name').allInnerTexts()).map((t) => t.replace(/^Mall\s+/, '').trim());
    const brandNames = (await page.getByTestId('facet-brands').locator('.filter-cat-name').allInnerTexts()).map((t) => t.trim());
    expect(shopNames.length).toBeGreaterThan(0);
    expect(brandNames.filter((b) => shopNames.includes(b))).toEqual([]);
    await context.close();
  });

  // #221: the shop's free-shipping line and ShopHub Xu in the total bar
  test('Giỏ hàng: dòng giảm phí vận chuyển của từng shop, Xu ở thanh tổng', async ({ browser, request }) => {
    const { page, context } = await buyerWithCart(browser, request);
    await page.goto(`${BASE}/gio-hang`);
    await expect(page.getByTestId('cart-shop').first()).toBeVisible();
    await expect(page.getByTestId('cart-shop-ship').first()).toContainText(/Giảm [\d.]+₫ phí vận chuyển đơn tối thiểu [\d.]+₫/);
    const coins = page.getByTestId('cart-coins');
    await expect(coins).toContainText('ShopHub Xu');
    // A new buyer has no coins: the switch is there, off and disabled
    await expect(page.getByTestId('cart-use-coins')).toBeDisabled();
    await expect(page.getByTestId('cart-use-coins')).toHaveAttribute('aria-checked', 'false');
    await context.close();
  });

  // #222: the totals sat in a column on the right; the reference marketplace closes the page with them
  test('Thanh toán: bảng tổng tiền ở cuối trang, dưới phương thức thanh toán', async ({ browser, request }) => {
    for (const width of [1440, 390]) {
      const { page, context } = await buyerWithCart(browser, request, width);
      await page.goto(`${BASE}/thanh-toan`);
      const summary = page.getByTestId('checkout-summary');
      await expect(summary).toBeVisible();
      const methods = await page.locator('.checkout-methods').boundingBox();
      const box = await summary.boundingBox();
      expect(box.y, `${width}px`).toBeGreaterThan(methods.y + methods.height);
      const viewport = page.viewportSize();
      expect(box.width, `${width}px`).toBeGreaterThan(viewport.width * 0.6);
      if (width >= 1024) {
        const button = await page.getByTestId('place-order').boundingBox();
        expect(button.x + button.width).toBeGreaterThan(box.x + box.width - 60);
      }
      await context.close();
    }
  });
});
