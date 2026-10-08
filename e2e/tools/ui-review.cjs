// UI review screenshots: every main page of web + seller at 1440 / 1024 / 390 px (full page, JPEG).
// Used for before/after comparisons of a UI change. Run by hand against any stack with sample data:
//   cd e2e && SH_E2E_BASE_URL=https://shophub.example.vn SH_UI_BUYER=0900000001 SH_UI_BUYER_PASSWORD=... \
//     SH_UI_SELLER=0900000101 SH_UI_SELLER_PASSWORD=... SH_UI_OUT=../.ui-review/before node tools/ui-review.cjs
// Pages that need an account are skipped when its credentials are missing. Also reports pages that scroll sideways.
const fs = require('fs');
const path = require('path');
const { chromium, request: playwrightRequest } = require('@playwright/test');

const BASE = (process.env.SH_E2E_BASE_URL || 'http://localhost:18000').replace(/\/$/, '');
const OUT = path.resolve(process.env.SH_UI_OUT || path.join(__dirname, '..', '..', '.ui-review', 'latest'));
const BUYER = process.env.SH_UI_BUYER && { id: process.env.SH_UI_BUYER, password: process.env.SH_UI_BUYER_PASSWORD };
const SELLER = process.env.SH_UI_SELLER && { id: process.env.SH_UI_SELLER, password: process.env.SH_UI_SELLER_PASSWORD };
const ONLY = process.env.SH_UI_ONLY ? process.env.SH_UI_ONLY.split(',') : null;
const WIDTHS = (process.env.SH_UI_WIDTHS || '1440,1024,390').split(',').map(Number);

async function apiData(ctx, url) {
  const res = await ctx.get(`${BASE}/api${url}`);
  if (!res.ok()) throw new Error(`GET ${url} → ${res.status()}`);
  return (await res.json()).data;
}

async function login(page, url, account) {
  await page.goto(url);
  await page.locator('input[aria-label="Tên đăng nhập"]').fill(account.id);
  await page.locator('input[aria-label="Mật khẩu"]').fill(account.password);
  await page.locator('[data-testid="login-submit"]').click();
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(1500);
}

async function main() {
  fs.mkdirSync(OUT, { recursive: true });
  const ctx = await playwrightRequest.newContext();
  const search = await apiData(ctx, '/search/products?q=dien%20thoai&pageSize=1');
  const product = search.items[0];
  const categories = await apiData(ctx, '/categories');
  const shopSlug = process.env.SH_UI_SHOP || 'shophub-official-store';
  const campaignSlug = process.env.SH_UI_CAMPAIGN || 'sieu-sale-10-10';

  const pages = [
    { name: 'home', url: '/' },
    { name: 'product', url: `/san-pham/${product.slug}-i.${product.shopId}.${product.id}` },
    { name: 'category', url: `/danh-muc/${categories[0].slug}` },
    { name: 'search', url: '/tim-kiem?q=ao' },
    { name: 'shop', url: `/shop/${shopSlug}` },
    { name: 'flash-sale', url: '/flash-sale' },
    { name: 'event', url: `/su-kien/${campaignSlug}` },
    { name: 'cart', url: '/gio-hang', as: 'buyer' },
    { name: 'checkout', url: '/thanh-toan', as: 'buyer' },
    { name: 'account', url: '/tai-khoan/ho-so', as: 'buyer' },
    { name: 'orders', url: '/tai-khoan/don-mua', as: 'buyer' },
    { name: 'seller', url: '/seller/', as: 'seller' },
  ].filter((p) => !ONLY || ONLY.includes(p.name));

  const browser = await chromium.launch();
  const overflow = [];
  for (const width of WIDTHS) {
    const height = width < 600 ? 844 : 900;
    const sessions = {};
    const sessionFor = async (as) => {
      if (sessions[as]) return sessions[as];
      const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1, locale: 'vi-VN' });
      const page = await context.newPage();
      if (as === 'buyer') await login(page, `${BASE}/dang-nhap`, BUYER);
      if (as === 'seller') await login(page, `${BASE}/seller/dang-nhap`, SELLER);
      sessions[as] = page;
      return page;
    };
    for (const p of pages) {
      const as = p.as || 'guest';
      if ((as === 'buyer' && !BUYER) || (as === 'seller' && !SELLER)) continue;
      const page = await sessionFor(as);
      await page.goto(`${BASE}${p.url}`, { waitUntil: 'networkidle' }).catch(() => {});
      // The home promo popup would cover the page: close it like a visitor would
      const popupClose = page.locator('[data-testid="home-popup"] button').first();
      if (await popupClose.isVisible().catch(() => false)) await popupClose.click().catch(() => {});
      // Let lazy images enter the viewport once, then come back to the top
      await page.evaluate(async () => {
        for (let y = 0; y < document.body.scrollHeight; y += 600) { window.scrollTo(0, y); await new Promise((r) => setTimeout(r, 80)); }
        window.scrollTo(0, 0);
      });
      await page.waitForTimeout(800);
      const sideways = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      if (sideways > 1) overflow.push(`${p.name}@${width}: +${sideways}px`);
      await page.screenshot({ path: path.join(OUT, `${p.name}-${width}.jpg`), fullPage: true, quality: 70, type: 'jpeg' });
      console.log(`${p.name}@${width}`);
    }
    for (const page of Object.values(sessions)) await page.context().close();
  }
  await browser.close();
  console.log(overflow.length ? `Cuộn ngang: ${overflow.join(', ')}` : 'Không trang nào cuộn ngang.');
}

main().catch((e) => { console.error(e); process.exit(1); });
