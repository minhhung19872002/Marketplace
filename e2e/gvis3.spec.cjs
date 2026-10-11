// UI upgrade G-VIS round 3: what the review of the live site at ~800 px found (docs/plans/G-VIS-3.md).
//   SH_E2E_BASE_URL=http://localhost:18300 npx playwright test gvis3.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api, registerViaApi, addAddressViaApi, apiLogin, apiAs, loginInBrowser } = require('./helpers.cjs');

const guest = { storageState: { cookies: [], origins: [] } };
const WIDTHS = [390, 768, 800, 1024, 1270, 1440];
// The home popup would cover what is measured: mark it seen
const noPopup = (context) => context.addInitScript(() => { try { localStorage.setItem('sh_popup_seen', JSON.stringify({ at: Date.now() })); } catch { /* */ } });

/** Texts of the header top bar that take more than one line (a wrapped label is taller than its single line). */
const wrappedTopBar = (page) => page.evaluate(() => [...document.querySelectorAll('.header-top a, .header-top button, .header-top-sep')]
  .filter((el) => el.offsetParent !== null)
  .filter((el) => {
    const lh = parseFloat(getComputedStyle(el).lineHeight) || parseFloat(getComputedStyle(el).fontSize) * 1.5;
    return el.getBoundingClientRect().height > lh * 1.6 || el.getClientRects().length > 1;
  })
  .map((el) => el.textContent.trim()));

const overflowOf = (page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

test.describe('G-VIS vòng 3', () => {
  test('Thanh trên cùng của header: mỗi mục một dòng ở mọi khổ có thanh ấy (khách và người đã đăng nhập)', async ({ browser, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Có Tên Khá Dài Để Thử');
    const problems = [];
    for (const signedIn of [false, true]) {
      const context = await browser.newContext(guest);
      await noPopup(context);
      const page = await context.newPage();
      if (signedIn) await loginInBrowser(page, buyer);
      for (const width of [769, 800, 900, 1024, 1270, 1440]) {
        await page.setViewportSize({ width, height: 900 });
        // A guest page is loaded at each width; the signed-in one stays (only resized) so the session is never reloaded
        if (!signedIn) await page.goto(BASE);
        await page.locator('.header-top').waitFor();
        if (signedIn) await expect(page.getByTestId('user-menu')).toBeVisible();
        await page.waitForTimeout(100);
        const wrapped = await wrappedTopBar(page);
        if (wrapped.length) problems.push(`${signedIn ? 'đăng nhập' : 'khách'} ${width}px: ${wrapped.join(' | ')}`);
      }
      await context.close();
    }
    expect(problems).toEqual([]);
  });

  test('Không trang nào cuộn ngang: trang chủ, chi tiết, danh mục, giỏ, thanh toán ở 390–1440 px', async ({ browser, request }) => {
    test.setTimeout(240_000);
    const product = (await api(request, '/search/products?q=ao&pageSize=1')).items[0];
    const buyer = await registerViaApi(request, 'Người Mua Khổ Màn Hình');
    await addAddressViaApi(request, buyer);
    const token = (await apiLogin(request, buyer.phone, buyer.password)).accessToken;
    const detail = await api(request, `/products/${product.id}`);
    await apiAs(request, token, 'POST', '/cart/items', { skuId: detail.skus.find((k) => k.available > 0).id, quantity: 1 });
    const context = await browser.newContext(guest);
    await noPopup(context);
    const page = await context.newPage();
    await loginInBrowser(page, buyer);
    const problems = [];
    for (const width of WIDTHS) {
      await page.setViewportSize({ width, height: 900 });
      for (const path of ['/', `/san-pham/${product.id}`, '/danh-muc/sac-dep', '/gio-hang', '/thanh-toan']) {
        await page.goto(`${BASE}${path}`);
        await page.waitForLoadState('networkidle');
        const px = await overflowOf(page);
        if (px > 0) problems.push(`${width}px ${path}: ${px}px`);
      }
    }
    expect(problems).toEqual([]);
  });

  // L194: a banner whose artwork carries its own text is never cropped. With object-fit: cover a crop happens exactly when
  // the frame's ratio differs from the picture's; contain never crops
  test('Banner có chữ trong ảnh không bao giờ bị cắt (trang chủ, 800–1440 px)', async ({ browser }) => {
    const context = await browser.newContext(guest);
    await noPopup(context);
    const page = await context.newPage();
    const problems = [];
    for (const width of [800, 900, 1024, 1270, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(BASE);
      await page.getByTestId('mall-brands').scrollIntoViewIfNeeded();
      await page.waitForLoadState('networkidle');
      const cropped = await page.evaluate(() => [...document.querySelectorAll('img[data-text-in-image="true"]')]
        .filter((img) => img.complete && img.naturalWidth > 0 && img.getClientRects().length > 0)
        .filter((img) => getComputedStyle(img).objectFit === 'cover' && getComputedStyle(img.closest('.mall-slide, .banner-art') ?? img).opacity !== '0')
        .map((img) => {
          const r = img.getBoundingClientRect();
          return { src: img.currentSrc.split('/').pop(), box: r.width / r.height, natural: img.naturalWidth / img.naturalHeight };
        })
        .filter((x) => Math.abs(x.box / x.natural - 1) > 0.03)
        .map((x) => `${x.src} khung ${x.box.toFixed(2)} / ảnh ${x.natural.toFixed(2)}`));
      for (const c of cropped) problems.push(`${width}px: ${c}`);
    }
    expect(problems).toEqual([]);
  });
});
