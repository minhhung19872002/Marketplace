// UI upgrade G-VIS round 2: what the review of the deployed G-VIS site found (docs/plans/G-VIS-2.md).
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test gvis2.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api } = require('./helpers.cjs');

const guest = { storageState: { cookies: [], origins: [] } };
const amounts = (text) => (text.match(/[\d.]+(?=₫)/g) || []).map((n) => Number(n.replace(/\./g, '')));

test.describe('G-VIS vòng 2', () => {
  test('Giá gạch luôn cao hơn giá bán — quét 50 sản phẩm ngẫu nhiên (L181)', async ({ browser, request }) => {
    test.setTimeout(300_000);
    // The reported case first (same price struck and charged), then random pages of the catalogue
    const ids = new Set((await api(request, '/search/products?q=bomber&pageSize=6')).items.map((c) => c.id));
    const total = (await api(request, '/search/products?pageSize=1')).totalCount;
    const pages = Math.max(1, Math.floor(total / 60));
    while (ids.size < 50) {
      const page = 1 + Math.floor(Math.random() * pages);
      const items = (await api(request, `/search/products?pageSize=60&page=${page}`)).items;
      for (const c of items.sort(() => Math.random() - 0.5).slice(0, 10)) ids.add(c.id);
    }
    const page = await (await browser.newContext(guest)).newPage();
    const bad = [];
    for (const id of [...ids].slice(0, 50)) {
      const box = page.locator('.product-detail-price-box').first();
      // The gateway may answer 429 to a fast scan: one more try after a pause before calling the page broken
      let loaded = false;
      for (let attempt = 0; attempt < 2 && !loaded; attempt++) {
        await page.goto(`${BASE}/san-pham/${id}`);
        loaded = await box.getByTestId('pd-price').waitFor({ timeout: 15_000 }).then(() => true, () => false);
        if (!loaded) await page.waitForTimeout(3000);
      }
      if (!loaded) { bad.push(`${id}: trang không hiện giá`); continue; }
      const struck = box.locator('.price-original');
      if ((await struck.count()) === 0) continue;
      const was = amounts(await struck.innerText());
      const now = amounts(await box.getByTestId('pd-price').innerText());
      const [wasMin, wasMax] = [was[0], was[was.length - 1]];
      const [nowMin, nowMax] = [now[0], now[now.length - 1]];
      if (!(wasMin >= nowMin && wasMax >= nowMax && (wasMin > nowMin || wasMax > nowMax))) bad.push(`${id}: gạch ${was.join('-')} / bán ${now.join('-')}`);
    }
    expect(bad).toEqual([]);
  });

  test('Thanh "Gợi ý hôm nay" chỉ dính trong section, không che "Xem thêm" và chân trang (L184)', async ({ browser }) => {
    const page = await (await browser.newContext({ ...guest, viewport: { width: 1280, height: 800 } })).newPage();
    await page.goto(BASE);
    await page.getByTestId('daily-grid').waitFor();
    const overlap = async (selector) => page.evaluate((sel) => {
      const bar = document.querySelector('.home-daily-bar').getBoundingClientRect();
      const other = document.querySelector(sel).getBoundingClientRect();
      return bar.bottom > other.top + 1 && bar.top < other.bottom - 1;
    }, selector);
    const more = page.getByTestId('load-more');
    // The box the bar sticks in ends with the grid: "Xem thêm" lies outside it, so the bar has scrolled away before it
    if (await more.count()) {
      expect(await page.evaluate(() => document.querySelector('.home-daily-bar').parentElement.contains(document.querySelector('[data-testid="load-more"]')))).toBe(false);
      // "Xem thêm" brought to the top of the screen, where the stuck bar would sit
      await page.evaluate(() => { const b = document.querySelector('[data-testid="load-more"]'); window.scrollTo(0, window.scrollY + b.getBoundingClientRect().top - 90); });
      await page.waitForTimeout(200);
      expect(await overlap('[data-testid="load-more"]')).toBe(false);
    }
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
    await page.waitForTimeout(200);
    expect(await overlap('footer')).toBe(false);
  });

  test('Ô Mall: logo chữ đủ tên, không bị cắt; ưu đãi vừa một dòng (L185)', async ({ browser }) => {
    const page = await (await browser.newContext({ ...guest, viewport: { width: 1270, height: 900 } })).newPage();
    await page.goto(BASE);
    const tiles = page.getByTestId('mall-brand');
    await tiles.first().scrollIntoViewIfNeeded();
    await tiles.first().locator('[data-testid="shop-wordmark"]').waitFor();
    const report = await page.evaluate(() => [...document.querySelectorAll('[data-testid="mall-brand"]')].map((tile) => {
      const img = tile.querySelector('[data-testid="shop-wordmark"]');
      const pill = tile.querySelector('.mall-brand-pill').getBoundingClientRect();
      const offer = tile.querySelector('.mall-brand-offer');
      const box = img?.getBoundingClientRect();
      return {
        wordmark: !!img && img.complete && img.naturalWidth > 0 && getComputedStyle(img).objectFit === 'contain'
          && box.left >= pill.left - 0.5 && box.right <= pill.right + 0.5 && box.top >= pill.top - 0.5 && box.bottom <= pill.bottom + 0.5,
        offerFits: offer.scrollWidth <= offer.clientWidth + 1,
        offer: offer.textContent,
      };
    }));
    expect(report.length).toBeGreaterThan(0);
    expect(report.filter((r) => !r.wordmark)).toEqual([]);
    expect(report.filter((r) => !r.offerFits).map((r) => r.offer)).toEqual([]);
  });

  test('Gợi ý hôm nay: một tin mỗi mẫu, không 2 ô liền nhau cùng ngành (L186)', async ({ request }) => {
    test.setTimeout(120_000);
    const modelKey = (name) => name.split(' - ')[0].normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
    const items = [];
    for (const n of [1, 2]) items.push(...(await api(request, `/home/recommendations?page=${n}&pageSize=24`)).items);
    const keys = items.map((i) => modelKey(i.name));
    expect(keys.filter((k, i) => keys.indexOf(k) !== i)).toEqual([]);
    const industries = [];
    for (const item of items.slice(0, 18)) industries.push((await api(request, `/products/${item.id}`)).breadcrumb[0].name);
    const runs = industries.map((g, i) => (i > 0 && g === industries[i - 1] ? `${i}: ${g}` : null)).filter(Boolean);
    expect(runs).toEqual([]);
  });

  test('Tìm kiếm hàng đầu: ảnh thường, không ảnh bìa có chữ (L182)', async ({ page }) => {
    await page.goto(BASE);
    await page.getByTestId('top-category').first().waitFor();
    const srcs = await page.locator('[data-testid="top-category"] img').evaluateAll((imgs) => imgs.map((i) => i.getAttribute('src')));
    expect(srcs.filter((s) => /-ad[_.]/.test(s))).toEqual([]);
  });

  test('Ba vị trí đầu trang không trùng ý: banner phụ không nói Freeship', async ({ request }) => {
    const banners = await api(request, '/home/banners');
    expect((banners.side || []).map((b) => b.title).filter((t) => /freeship/i.test(t))).toEqual([]);
  });

  test('Thanh Flash Sale: một nhãn, một màu (L183)', async ({ page }) => {
    await page.goto(BASE);
    const bars = page.locator('.flash-item-bar');
    await bars.first().waitFor({ timeout: 15_000 }).catch(() => {});
    test.skip((await bars.count()) === 0, 'Không có khung Flash Sale đang chạy');
    const labels = await bars.evaluateAll((all) => all.map((b) => {
      const texts = b.querySelectorAll('.flash-item-bar-text');
      return { count: texts.length, color: getComputedStyle(texts[0]).color, bg: getComputedStyle(texts[0]).backgroundColor };
    }));
    expect(labels.filter((l) => l.count !== 1)).toEqual([]);
    expect(new Set(labels.map((l) => `${l.color}|${l.bg}`)).size).toBe(1);
  });

  test('Trang chi tiết: gallery ≥ 4 ảnh; áo có màu trong tên có chip Màu Sắc kèm ảnh', async ({ page, request }) => {
    const card = (await api(request, '/search/products?q=bomber&pageSize=6')).items[0];
    const product = await api(request, `/products/${card.id}`);
    expect(product.media.filter((m) => m.type === 'Image').length).toBeGreaterThanOrEqual(4);
    const colour = product.tiers.find((t) => t.name === 'Màu Sắc');
    expect(colour, 'tầng Màu Sắc').toBeTruthy();
    await page.goto(`${BASE}/san-pham/${card.id}`);
    const chip = page.locator('.pd-variants[aria-label="Màu Sắc"] [data-testid="variant-option"]').first();
    await expect(chip.locator('img.pd-variant-img')).toBeVisible();
    // A single colour is chosen already: only the size is left to pick
    await expect(chip).toHaveAttribute('aria-pressed', 'true');
  });
});

