// Screenshots for the user guides (docs/01–03). Run by hand against the demo stack with sample data:
//   cd e2e && SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... node tools/screenshots.cjs
// The first sample shop owner (0900000101) gets a temporary password through the admin "Đặt lại mật khẩu" and then a
// random one; both stay in memory only. Images are written to docs/images/ (JPEG, viewport only).
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { chromium, request: playwrightRequest } = require('@playwright/test');

const BASE = process.env.SH_E2E_BASE_URL || 'http://localhost:18000';
const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;
const OUT = path.join(__dirname, '..', '..', 'docs', 'images');
const OWNER_PHONE = '0900000101';

if (!ADMIN_USER || !ADMIN_PASSWORD) {
  console.error('Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');
  process.exit(1);
}

async function api(ctx, method, url, { token, data } = {}) {
  const res = await ctx.fetch(`${BASE}/api${url}`, { method, headers: token ? { Authorization: `Bearer ${token}` } : {}, data });
  const body = await res.json().catch(() => null);
  if (!res.ok()) throw new Error(`${method} ${url} → ${res.status()}: ${JSON.stringify(body)}`);
  return body.data;
}

async function shot(page, name) {
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(OUT, `${name}.jpg`), type: 'jpeg', quality: 70 });
  console.log('  ✓', name);
}

async function main() {
  fs.mkdirSync(OUT, { recursive: true });
  const ctx = await playwrightRequest.newContext();
  const admin = await api(ctx, 'POST', '/auth/login', { data: { identifier: ADMIN_USER, password: ADMIN_PASSWORD } });

  // Sample shop owner: temporary password → a random one of our own (never printed)
  const users = await api(ctx, 'GET', `/admin/users?q=${OWNER_PHONE}`, { token: admin.accessToken });
  const owner = users.items.find((u) => u.phone === OWNER_PHONE);
  const { temporaryPassword } = await api(ctx, 'POST', `/admin/users/${owner.id}/reset-password`, { token: admin.accessToken });
  const tempLogin = await api(ctx, 'POST', '/auth/login', { data: { identifier: OWNER_PHONE, password: temporaryPassword } });
  const ownerPassword = `Shop${crypto.randomBytes(9).toString('base64url')}9`;
  await api(ctx, 'PUT', '/account/password', { token: tempLogin.accessToken, data: { currentPassword: temporaryPassword, newPassword: ownerPassword } });

  // A buyer with an address
  const phone = `09${String(Date.now()).slice(-8)}`;
  await api(ctx, 'POST', '/auth/otp/send', { data: { target: phone, purpose: 'Register' } });
  let code = null;
  for (let i = 0; i < 40 && !code; i++) {
    const sms = await api(ctx, 'GET', `/dev/sms?to=${phone}`);
    code = sms[0]?.content.match(/\b\d{6}\b/)?.[0] ?? null;
    if (!code) await new Promise((r) => setTimeout(r, 500));
  }
  const { ticket } = await api(ctx, 'POST', '/auth/otp/verify', { data: { target: phone, purpose: 'Register', code } });
  const buyerPassword = `Mua${crypto.randomBytes(9).toString('base64url')}9`;
  const buyer = await api(ctx, 'POST', '/auth/register', { data: { target: phone, ticket, password: buyerPassword, fullName: 'Nguyễn Văn An', acceptTerms: true } });
  const ward = (await api(ctx, 'GET', '/admin-divisions?parent=001'))[0];
  await api(ctx, 'POST', '/account/addresses', {
    token: buyer.accessToken,
    data: { receiverName: 'Nguyễn Văn An', phone, provinceCode: '01', districtCode: '001', wardCode: ward.code, street: '12 Phố Hàng Bài', type: 'Home', isDefault: true },
  });

  const browser = await chromium.launch();

  // ---------- 01: người mua ----------
  console.log('01 — người mua');
  const web = await (await browser.newContext({ viewport: { width: 1280, height: 800 }, locale: 'vi-VN' })).newPage();
  await web.goto(`${BASE}/`);
  await web.locator('.home-popup-close, [aria-label="Đóng"]').first().click({ timeout: 3000 }).catch(() => undefined);
  await shot(web, '01-trang-chu');
  await web.goto(`${BASE}/tim-kiem?q=dien%20thoai`);
  await shot(web, '01-tim-kiem');
  await web.locator('[data-testid="product-card"]').first().click();
  await web.waitForURL(/\/san-pham\//);
  await shot(web, '01-chi-tiet-san-pham');

  await web.goto(`${BASE}/dang-nhap`);
  await shot(web, '01-dang-nhap');
  await web.locator('input[aria-label="Tên đăng nhập"]').fill(phone);
  await web.locator('input[aria-label="Mật khẩu"]').fill(buyerPassword);
  await web.locator('[data-testid="login-submit"]').click();
  await web.locator('[data-testid="user-menu"]').waitFor();

  // Products of the Mall sample shops (the dev database also holds the e2e suite's test shops)
  const cards = await api(ctx, 'GET', '/search/products?inStock=true&mall=true&sort=BestSelling&pageSize=60');
  for (const card of cards.items) {
    const p = await api(ctx, 'GET', `/products/${card.id}`);
    if (p.purchasable && p.tiers.length === 0 && p.skus[0].available > 2) {
      await api(ctx, 'POST', '/cart/items', { token: buyer.accessToken, data: { skuId: p.skus[0].id, quantity: 1 } });
      if ((await api(ctx, 'GET', '/cart', { token: buyer.accessToken })).shops.length >= 2) break;
    }
  }
  await web.goto(`${BASE}/gio-hang`);
  await shot(web, '01-gio-hang');
  await web.goto(`${BASE}/thanh-toan`);
  await web.locator('[data-testid="place-order"]').waitFor();
  await shot(web, '01-thanh-toan');
  await web.locator('[data-testid="place-order"]').click();
  await web.waitForURL(/\/dat-hang-thanh-cong/);
  await shot(web, '01-dat-hang-thanh-cong');
  const orderCode = (await web.locator('[data-testid="result-order"] strong').first().textContent()).trim();
  await web.goto(`${BASE}/tai-khoan/don-mua`);
  await shot(web, '01-don-mua');
  await web.goto(`${BASE}/tai-khoan/don-mua/${orderCode}`);
  await shot(web, '01-chi-tiet-don');
  await web.getByTestId('contact-shop').click();
  await web.getByTestId('chat-thread').waitFor();
  await shot(web, '01-chat');
  await web.goto(`${BASE}/tai-khoan/vi`);
  await shot(web, '01-vi-shophub');
  await web.goto(`${BASE}/tai-khoan/quyen-rieng-tu`);
  await shot(web, '01-quyen-rieng-tu');
  await web.goto(`${BASE}/shop/shophub-official-store`);
  await shot(web, '01-trang-shop');
  const phoneView = await (await browser.newContext({ viewport: { width: 375, height: 760 }, isMobile: true, locale: 'vi-VN' })).newPage();
  await phoneView.goto(`${BASE}/`);
  await phoneView.locator('.home-popup-close, [aria-label="Đóng"]').first().click({ timeout: 3000 }).catch(() => undefined);
  await shot(phoneView, '01-dien-thoai');

  // ---------- 02: người bán ----------
  console.log('02 — người bán');
  const seller = await (await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'vi-VN' })).newPage();
  await seller.goto(`${BASE}/seller/`);
  await shot(seller, '02-dang-nhap');
  await seller.getByLabel('Tên đăng nhập').fill(OWNER_PHONE);
  await seller.getByLabel('Mật khẩu').fill(ownerPassword);
  await seller.getByTestId('login-submit').click();
  await seller.getByRole('menuitem').first().waitFor();
  // The owner's shop with the most orders makes the fullest screens
  const ownerLogin = await api(ctx, 'POST', '/auth/login', { data: { identifier: OWNER_PHONE, password: ownerPassword } });
  const myShops = await api(ctx, 'GET', '/seller/shops', { token: ownerLogin.accessToken });
  let busiest = myShops[0];
  let most = -1;
  for (const s of myShops) {
    const list = await api(ctx, 'GET', `/seller/shops/${s.id}/orders?tab=All&page=1&pageSize=1`, { token: ownerLogin.accessToken });
    if (list.totalCount > most) { most = list.totalCount; busiest = s; }
  }
  await seller.locator('.app-header .ant-select').click();
  await seller.locator('.ant-select-item-option', { hasText: busiest.name }).click();
  await seller.waitForTimeout(800);
  const pages = [
    ['Bảng điều khiển', '02-bang-dieu-khien'], ['Đơn hàng', '02-don-hang'], ['Trả hàng / Hoàn tiền', '02-tra-hang'], ['Sản phẩm', '02-san-pham'],
    ['Thêm sản phẩm', '02-them-san-pham'], ['Excel hàng loạt', '02-excel-hang-loat'], ['Mã giảm giá', '02-ma-giam-gia'], ['Kênh Marketing', '02-marketing'],
    ['Chat', '02-chat'], ['Đánh giá', '02-danh-gia'], ['Tài chính', '02-tai-chinh'], ['Dữ liệu & phân tích', '02-phan-tich'],
    ['Thiết lập shop', '02-thiet-lap'], ['Trang trí shop', '02-trang-tri'], ['Danh mục của shop', '02-danh-muc-shop'], ['Tài khoản phụ', '02-tai-khoan-phu'],
  ];
  for (const [label, name] of pages) {
    await seller.getByRole('menuitem', { name: label, exact: true }).click();
    await shot(seller, name);
  }

  // ---------- 03: quản trị ----------
  console.log('03 — quản trị');
  const adminPage = await (await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'vi-VN' })).newPage();
  await adminPage.goto(`${BASE}/admin/`);
  await shot(adminPage, '03-dang-nhap');
  await adminPage.getByLabel('Tên đăng nhập').fill(ADMIN_USER);
  await adminPage.getByLabel('Mật khẩu').fill(ADMIN_PASSWORD);
  await adminPage.getByTestId('login-submit').click();
  await adminPage.getByRole('menuitem').first().waitFor();
  const adminPages = [
    ['Tổng quan', '03-tong-quan'], ['Báo cáo', '03-bao-cao'], ['Đơn hàng', '03-don-hang'], ['Duyệt sản phẩm', '03-duyet-san-pham'],
    ['Shop', '03-shop'], ['Ngành hàng', '03-nganh-hang'], ['Voucher của sàn', '03-voucher'], ['Marketing', '03-marketing'],
    ['Khiếu nại trả hàng', '03-khieu-nai'], ['Chat bị báo cáo', '03-chat-bao-cao'], ['Tài chính', '03-tai-chinh'], ['Người dùng', '03-nguoi-dung'],
    ['Vai trò & quyền', '03-vai-tro'], ['Tham số hệ thống', '03-tham-so'], ['Nội dung & mẫu tin', '03-noi-dung'], ['Nhật ký thao tác', '03-nhat-ky'],
  ];
  for (const [label, name] of adminPages) {
    await adminPage.getByRole('menuitem', { name: label, exact: true }).click();
    await shot(adminPage, name);
  }

  await browser.close();
  await ctx.dispose();
  console.log(`Ảnh trong ${OUT}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
