// Self-check of a deployed site (G4-A): the things a review easily misses, measured by the machine.
//   1. no page scrolls sideways at 375 px — as a guest and signed in with real data (cart from 2 shops, orders);
//   2. no console error / uncaught exception on any of those pages;
//   3. the checkout money table shows exactly what PricingEngine returned (/api/checkout/quote), and adds up;
//   4. colour contrast (axe, WCAG AA) of the seller centre in dark mode (and the admin when SH_AUDIT_ADMIN_* is set).
// Read-mostly: the only writes are cart lines for the sample buyer when the cart has fewer than 2 shops. Never changes
// a password. Usage:
//   cd e2e && SH_E2E_BASE_URL=https://shophub.example.vn SH_UI_BUYER=0900000001 SH_UI_BUYER_PASSWORD=... \
//     SH_UI_SELLER=0900000101 SH_UI_SELLER_PASSWORD=... node tools/site-audit.cjs > audit.json
const { chromium, request: playwrightRequest } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;

const BASE = (process.env.SH_E2E_BASE_URL || 'http://localhost:18000').replace(/\/$/, '');
const BUYER = { id: process.env.SH_UI_BUYER, password: process.env.SH_UI_BUYER_PASSWORD };
const SELLER = { id: process.env.SH_UI_SELLER, password: process.env.SH_UI_SELLER_PASSWORD };
const ADMIN = process.env.SH_AUDIT_ADMIN_USER && { id: process.env.SH_AUDIT_ADMIN_USER, password: process.env.SH_AUDIT_ADMIN_PASSWORD };

const results = { base: BASE, at: new Date().toISOString(), overflow: [], console: [], checkout: null, contrast: [] };

async function api(ctx, path, token, method = 'GET', data) {
  const res = await ctx.fetch(`${BASE}/api${path}`, { method, data, headers: token ? { Authorization: `Bearer ${token}` } : {} });
  const body = await res.json().catch(() => null);
  if (!res.ok()) throw new Error(`${method} ${path} → ${res.status()} ${body?.message ?? ''}`);
  return body.data;
}

/** Visits each path, records sideways scroll (px) and console errors / page errors. */
async function sweep(page, label, paths) {
  for (const path of paths) {
    const errors = [];
    const onConsole = (m) => { if (m.type() === 'error') errors.push(m.text().slice(0, 200)); };
    const onError = (e) => errors.push(`pageerror: ${e.message.slice(0, 200)}`);
    page.on('console', onConsole);
    page.on('pageerror', onError);
    await page.goto(`${BASE}${path}`);
    await page.waitForLoadState('networkidle').catch(() => {});
    await page.waitForTimeout(600);
    const px = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    page.off('console', onConsole);
    page.off('pageerror', onError);
    results.overflow.push({ who: label, path, px, ok: px <= 0 });
    results.console.push({ who: label, path, errors, ok: errors.length === 0 });
  }
}

async function loginUi(page, url, account) {
  await page.goto(url);
  await page.locator('input[aria-label="Tên đăng nhập"]').fill(account.id);
  await page.locator('input[aria-label="Mật khẩu"]').fill(account.password);
  await page.locator('[data-testid="login-submit"]').click();
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(1200);
}

(async () => {
  const ctx = await playwrightRequest.newContext();
  const browser = await chromium.launch();
  const phone = { viewport: { width: 375, height: 812 }, isMobile: true, hasTouch: true };

  // Sample data to visit
  const list = await api(ctx, '/search/products?inStock=true&sort=BestSelling&pageSize=40');
  const product = await api(ctx, `/products/${list.items[0].id}`);
  const publicPaths = ['/', `/san-pham/${product.id}`, '/tim-kiem?q=ao', '/tim-kiem?q=tai%20nghe', `/shop/${product.shop.slug}`,
    '/flash-sale', '/gio-hang', '/dang-nhap', '/dang-ky', '/tra-cuu-van-don', '/tro-giup'];

  // 1 + 2 — guest
  const guest = await (await browser.newContext({ ...phone, storageState: { cookies: [], origins: [] } })).newPage();
  await sweep(guest, 'khách', publicPaths);

  // Buyer: cart from at least 2 shops (only write of the audit)
  const token = (await api(ctx, '/auth/login', null, 'POST', { identifier: BUYER.id, password: BUYER.password })).accessToken;
  let cart = await api(ctx, '/cart', token);
  if (cart.shops.length < 2) {
    const shops = new Set(cart.shops.map((s) => s.shopId));
    for (const card of list.items) {
      if (shops.size >= 2) break;
      if (shops.has(card.shopId)) continue;
      const detail = await api(ctx, `/products/${card.id}`);
      const sku = detail.skus.find((k) => k.available > 2);
      if (!sku) continue;
      await api(ctx, '/cart/items', token, 'POST', { skuId: sku.id, quantity: 1 });
      shops.add(card.shopId);
    }
    cart = await api(ctx, '/cart', token);
  }
  await api(ctx, '/cart/selection', token, 'PUT', { shopId: null, selected: true });
  const orders = await api(ctx, '/orders?tab=All&page=1&pageSize=1', token).catch(() => null);
  const orderCode = orders?.items?.[0]?.code;

  const buyerPhone = await (await browser.newContext(phone)).newPage();
  await loginUi(buyerPhone, `${BASE}/dang-nhap`, BUYER);
  const signedIn = [...publicPaths.filter((p) => !p.startsWith('/dang-')), '/thanh-toan', '/tai-khoan/don-mua',
    ...(orderCode ? [`/tai-khoan/don-mua/${orderCode}`] : []), '/tai-khoan/ho-so', '/tai-khoan/dia-chi', '/tai-khoan/vi', '/tai-khoan/xu',
    '/tai-khoan/voucher', '/tai-khoan/thong-bao', '/tai-khoan/da-xem', '/tai-khoan/shop-theo-doi', '/tai-khoan/tra-hang', '/thong-bao',
    '/yeu-thich', '/chat'];
  await sweep(buyerPhone, 'người mua', signedIn);

  // 3 — checkout money table vs the engine's quote (desktop)
  const desk = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
  await loginUi(desk, `${BASE}/dang-nhap`, BUYER);
  const quoteResponse = desk.waitForResponse((r) => r.url().includes('/api/checkout/quote') && r.request().method() === 'POST');
  await desk.goto(`${BASE}/thanh-toan`);
  const quote = (await (await quoteResponse).json()).data;
  await desk.waitForTimeout(1200);
  const rows = await desk.locator('[data-testid="checkout-summary"] .checkout-summary-row').evaluateAll((els) =>
    els.map((e) => [...e.querySelectorAll('span')].map((s) => s.textContent.trim())));
  const num = (s) => Number((s || '').replace(/[^\d]/g, '')) * (s.trim().startsWith('−') ? -1 : 1);
  const shown = Object.fromEntries(rows.map(([k, v]) => [k.replace(/\s*\(.*\)$/, ''), num(v)]));
  const sum = (k) => quote.shops.reduce((t, s) => t + s[k], 0);
  const identity = quote.subtotal + quote.shippingFee - quote.shippingDiscount - quote.comboDiscount - quote.shopDiscount
    - quote.platformDiscount - quote.coinUsed;
  results.checkout = {
    shops: quote.shops.length,
    shown,
    quote: { subtotal: quote.subtotal, shippingFee: quote.shippingFee, shippingDiscount: quote.shippingDiscount, comboDiscount: quote.comboDiscount,
      shopDiscount: quote.shopDiscount, platformDiscount: quote.platformDiscount, coinUsed: quote.coinUsed, grandTotal: quote.grandTotal },
    checks: {
      'Tổng tiền hàng = quote.subtotal': shown['Tổng tiền hàng'] === quote.subtotal,
      'Phí vận chuyển = quote.shippingFee': shown['Tổng tiền phí vận chuyển'] === quote.shippingFee,
      'Tổng thanh toán = quote.grandTotal': shown['Tổng thanh toán'] === quote.grandTotal,
      'grandTotal = tiền hàng + ship − các khoản giảm': identity === quote.grandTotal,
      'tổng các shop = tổng checkout': sum('total') === quote.grandTotal && sum('subtotal') === quote.subtotal && sum('shippingFee') === quote.shippingFee,
      'mỗi shop: total = subtotal − giảm + ship': quote.shops.every((s) => s.total === s.subtotal - s.comboDiscount - s.shopDiscount + s.shippingFee
        - s.shippingDiscount - s.platformDiscount - s.coinUsed),
    },
  };

  // 3b — the same engine with discounts: platform voucher (best usable one) + coins, through the API; every identity holds
  const plain = await api(ctx, '/checkout/quote', token, 'POST', { addressId: quote.address?.id ?? null,
    shops: quote.shops.map((s) => ({ shopId: s.shopId })), platformVoucherCode: null, freeshipVoucherCode: null, useCoins: true, paymentMethod: 'Cod' });
  const voucher = plain.platformVouchers.find((v) => v.usable) ?? null;
  const freeship = plain.freeshipVouchers.find((v) => v.usable) ?? null;
  const q2 = await api(ctx, '/checkout/quote', token, 'POST', { addressId: quote.address?.id ?? null,
    shops: quote.shops.map((s) => ({ shopId: s.shopId })), platformVoucherCode: voucher?.code ?? null, freeshipVoucherCode: freeship?.code ?? null,
    useCoins: true, paymentMethod: 'Cod' });
  const sum2 = (k) => q2.shops.reduce((t, s) => t + s[k], 0);
  results.checkoutDiscounts = {
    voucher: voucher?.code ?? null, freeship: freeship?.code ?? null,
    quote: { subtotal: q2.subtotal, shippingFee: q2.shippingFee, shippingDiscount: q2.shippingDiscount, shopDiscount: q2.shopDiscount,
      platformDiscount: q2.platformDiscount, coinUsed: q2.coinUsed, grandTotal: q2.grandTotal },
    checks: {
      'grandTotal = tiền hàng + ship − các khoản giảm': q2.subtotal + q2.shippingFee - q2.shippingDiscount - q2.comboDiscount - q2.shopDiscount
        - q2.platformDiscount - q2.coinUsed === q2.grandTotal,
      'giảm của sàn chia hết xuống các shop (không lệch 1 đồng)': sum2('platformDiscount') === q2.platformDiscount && sum2('coinUsed') === q2.coinUsed
        && sum2('shippingDiscount') === q2.shippingDiscount,
      'tổng các shop = tổng checkout': sum2('total') === q2.grandTotal,
      'không âm': q2.grandTotal >= 0 && q2.shops.every((s) => s.total >= 0),
      'có áp được ít nhất một khoản giảm': q2.platformDiscount + q2.shippingDiscount + q2.coinUsed > 0 || (!voucher && !freeship && q2.coins.max === 0),
    },
  };

  // 4 — dark mode contrast of the seller centre (and admin)
  const axeCheck = async (label, loginUrl, account, themeKey, paths) => {
    const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
    await page.addInitScript((k) => { try { localStorage.setItem(k, 'dark'); } catch { /* ignore */ } }, themeKey);
    await loginUi(page, loginUrl, account);
    for (const path of paths) {
      await page.goto(`${BASE}${path}`);
      await page.waitForLoadState('networkidle').catch(() => {});
      await page.waitForTimeout(800);
      const axe = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze();
      const nodes = axe.violations.flatMap((v) => v.nodes.map((n) => `${n.target.join(' ')} — ${(n.any[0]?.message || '').slice(0, 120)}`));
      results.contrast.push({ who: label, path, violations: nodes.length, sample: nodes.slice(0, 5), ok: nodes.length === 0 });
    }
  };
  if (SELLER.id) await axeCheck('seller tối', `${BASE}/seller/`, SELLER, 'sh_seller_theme',
    ['/seller/', '/seller/san-pham', '/seller/don-hang', '/seller/tai-chinh', '/seller/phan-tich', '/seller/chat']);
  if (ADMIN) await axeCheck('admin tối', `${BASE}/admin/`, ADMIN, 'sh_admin_theme', ['/admin/', '/admin/don-hang', '/admin/shop']);

  await browser.close();
  const summary = {
    overflow: `${results.overflow.filter((r) => r.ok).length}/${results.overflow.length}`,
    console: `${results.console.filter((r) => r.ok).length}/${results.console.length}`,
    checkout: Object.values(results.checkout.checks).every(Boolean) ? 'đạt' : 'chưa',
    checkoutDiscounts: Object.values(results.checkoutDiscounts.checks).every(Boolean) ? 'đạt' : 'chưa',
    contrast: `${results.contrast.filter((r) => r.ok).length}/${results.contrast.length}`,
  };
  console.log(JSON.stringify({ summary, ...results }, null, 2));
})().catch((e) => { console.error(e); process.exit(1); });
