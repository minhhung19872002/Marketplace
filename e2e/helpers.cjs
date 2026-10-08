// Shared helpers for the e2e specs: real accounts through the public API, real catalogue lookups.
const { expect } = require('@playwright/test');

const BASE = process.env.SH_E2E_BASE_URL || 'http://localhost:5173';

// Random VN mobile number: parallel workers load this module separately, so a shared counter would collide
const newPhone = () => `09${String(require('node:crypto').randomInt(0, 100_000_000)).padStart(8, '0')}`;

// Newest OTP texted by the simulated SMS provider (dev-only inbox)
async function latestOtp(request, phone) {
  for (let i = 0; i < 40; i++) {
    const res = await request.get(`${BASE}/api/dev/sms?to=${phone}`);
    const body = await res.json();
    const text = body.data?.[0]?.content;
    const code = text && text.match(/\b\d{6}\b/);
    if (code) return code[0];
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`Không nhận được OTP cho ${phone}`);
}

// Real account through the public API: OTP → ticket → register
async function registerViaApi(request, fullName = 'Khách Thử E2E') {
  const phone = newPhone();
  const password = 'Matkhau123';
  const sent = await request.post(`${BASE}/api/auth/otp/send`, { data: { target: phone, purpose: 'Register' } });
  if (!sent.ok()) throw new Error(`Gửi OTP thất bại (${sent.status()}): ${await sent.text()}`);
  const code = await latestOtp(request, phone);
  const verify = await (await request.post(`${BASE}/api/auth/otp/verify`, { data: { target: phone, purpose: 'Register', code } })).json();
  const res = await request.post(`${BASE}/api/auth/register`, {
    data: { target: phone, ticket: verify.data.ticket, password, fullName, acceptTerms: true },
  });
  if (!res.ok()) throw new Error(`Đăng ký thất bại: ${await res.text()}`);
  return { phone, password, fullName };
}


// ---------- real catalogue lookups (seeded sample data, no fixed ids) ----------

async function api(request, path) {
  const res = await request.get(`${BASE}/api${path}`);
  if (!res.ok()) throw new Error(`GET ${path} → ${res.status()}: ${await res.text()}`);
  return (await res.json()).data;
}

/**
 * First in-stock, purchasable product whose page matches the predicate. Pages through the results: which products sell
 * best depends on the sample orders, so on a freshly seeded install the first page alone may hold no match (L122).
 */
async function findProduct(request, predicate, query = 'inStock=true&sort=BestSelling&pageSize=60') {
  for (let page = 1; page <= 10; page++) {
    const result = await api(request, `/search/products?${query}&page=${page}`);
    for (const card of result.items) {
      const product = await api(request, `/products/${card.id}`);
      if (product.purchasable && predicate(product)) return product;
    }
    if (page * result.pageSize >= result.totalCount) break;
  }
  throw new Error('Không tìm thấy sản phẩm phù hợp trong dữ liệu mẫu');
}

const withTiers = (p) => p.tiers.length > 0 && p.skus.some((s) => s.available > 2);
const withoutTiers = (p) => p.tiers.length === 0 && p.skus[0].available > 2;

async function loginInBrowser(page, account) {
  await page.goto(`${BASE}/dang-nhap`);
  await page.locator('input[aria-label="Tên đăng nhập"]').fill(account.phone);
  await page.locator('input[aria-label="Mật khẩu"]').fill(account.password);
  await page.locator('[data-testid="login-submit"]').click();
  await expect(page.locator('[data-testid="user-menu"]')).toBeVisible();
}

const stripTones = (s) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase();

/** Default delivery address (Hà Nội → Phường Ba Đình, two levels since 2025-07-01) for an account created by registerViaApi. */
async function addAddressViaApi(request, account) {
  const login = await (await request.post(`${BASE}/api/auth/login`, { data: { identifier: account.phone, password: account.password } })).json();
  const token = login.data.accessToken;
  const div = async (parent) => (await (await request.get(`${BASE}/api/admin-divisions${parent ? `?parent=${parent}` : ''}`)).json()).data;
  const province = (await div()).find((d) => d.name === 'Thành phố Hà Nội');
  const ward = (await div(province.code)).find((d) => d.name === 'Phường Ba Đình');
  const res = await request.post(`${BASE}/api/account/addresses`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { receiverName: 'Người Nhận E2E', phone: account.phone, provinceCode: province.code, wardCode: ward.code,
      street: '1 Phố Thử', type: 'Home', isDefault: true },
  });
  if (!res.ok()) throw new Error(`Thêm địa chỉ thất bại: ${await res.text()}`);
  return { token, userId: login.data.user.id };
}

/** Sign in through the API (for calls the UI does not expose, e.g. reading back totals). */
async function apiLogin(request, identifier, password) {
  const res = await request.post(`${BASE}/api/auth/login`, { data: { identifier, password } });
  if (!res.ok()) throw new Error(`POST /auth/login → ${res.status()}: ${await res.text()}`);
  return (await res.json()).data;
}

async function apiAs(request, token, method, path, data) {
  const res = await request.fetch(`${BASE}/api${path}`, { method, headers: { Authorization: `Bearer ${token}` }, data });
  if (!res.ok()) throw new Error(`${method} ${path} → ${res.status()}: ${await res.text()}`);
  return (await res.json()).data;
}

const fs = require('fs');
const SAMPLE_PNG = require('path').join(__dirname, 'fixtures', 'sample.png');

/** A brand-new approved shop with one approved product, built through the public + admin APIs. */
async function shopWithProduct(request, admin, { stock = 50, price = 159000 } = {}) {
  const seller = await registerViaApi(request, 'Người Bán Đơn Hàng');
  const login = await apiLogin(request, seller.phone, seller.password);
  const token = login.accessToken;
  const upload = async (purpose) => {
    const res = await request.post(`${BASE}/api/media/${purpose}`, {
      headers: { Authorization: `Bearer ${token}` },
      multipart: { file: { name: 'sample.png', mimeType: 'image/png', buffer: fs.readFileSync(SAMPLE_PNG) } },
    });
    if (!res.ok()) throw new Error(`upload ${purpose}: ${await res.text()}`);
    return (await res.json()).data;
  };
  const [front, back, photo] = [await upload('kyc'), await upload('kyc'), await upload('product')];
  const shopName = `Shop Đơn ${seller.phone.slice(-6)}`;
  const shopId = await apiAs(request, token, 'POST', '/seller/shops', {
    name: shopName,
    type: 'Personal',
    description: 'Shop kiểm thử đơn hàng',
    warehouse: { contactName: 'Kho', phone: seller.phone, provinceCode: '79', wardCode: '26740', street: '1 Nguyễn Huệ' },
    personal: { legalName: 'Người Bán Đơn Hàng', idCardNumber: '079200012345', frontAssetId: front.id, backAssetId: back.id },
    bank: { bankCode: 'VCB', accountNo: '0011002233445', accountName: 'NGUOI BAN DON HANG' },
  });
  await apiAs(request, admin.accessToken, 'POST', `/admin/shops/${shopId}/approve`);

  const tree = (await (await request.get(`${BASE}/api/categories`)).json()).data;
  const leaf = tree.flatMap((t) => t.children).flatMap((m) => m.children).find((c) => c.name === 'Áo Thun');
  const attrs = (await (await request.get(`${BASE}/api/categories/${leaf.id}/attributes`)).json()).data;
  const name = `Áo Thun Đơn E2E ${seller.phone.slice(-6)}`;
  const input = {
    categoryId: leaf.id, brandId: null, name, description: '<p>Áo thun kiểm thử</p>', condition: 'New',
    weightG: 300, lengthMm: 0, widthMm: 0, heightMm: 0, isPreorder: false, preorderDays: 0,
    attributes: attrs.filter((a) => a.isRequired).map((a) => ({ attributeId: a.id, values: [a.options[0] ?? '1'] })),
    media: [{ assetId: photo.id, optionValue: null }],
    tiers: [],
    skus: [{ option1: null, option2: null, sellerSku: 'E2E-AO', price, originalPrice: Math.round(price * 1.25), stock, weightG: null, isActive: true }],
  };
  const productId = await apiAs(request, token, 'POST', `/seller/shops/${shopId}/products`, input);
  await apiAs(request, token, 'POST', `/seller/shops/${shopId}/products/${productId}/actions/submit`);
  await apiAs(request, admin.accessToken, 'POST', `/admin/products/${productId}/approve`);
  return { seller, token, shopId, shopName, productId, name, input };
}

/** Pick an option of the buyer site's searchable select (components/ui/SearchSelect) by its accessible label. */
async function pickOption(scope, label, option) {
  // The trigger's name is "<label>: <current choice>" (the visible text is part of it, WCAG 2.5.3)
  await scope.getByRole('button', { name: new RegExp(`^${label}:`) }).click();
  await scope.getByRole('combobox', { name: `Tìm ${label}` }).fill(option);
  await scope.getByRole('option', { name: option, exact: true }).click();
}

/** Province → ward of the buyer address form (two levels since 2025-07-01). */
async function pickAddress(scope, province = 'Thành phố Hà Nội', ward = 'Phường Ba Đình') {
  await pickOption(scope, 'Tỉnh/Thành phố', province);
  await pickOption(scope, 'Phường/Xã', ward);
}

/** Choose a voucher in the ticket dialog of components/VoucherPicker (G2-B4): open, tick the code's ticket, OK. */
async function pickVoucher(scope, testId, code) {
  const page = typeof scope.page === 'function' ? scope.page() : scope;
  await scope.getByTestId(`${testId}-open`).click();
  const dialog = page.getByTestId('voucher-dialog');
  await dialog.getByTestId('voucher-ticket').filter({ hasText: `Mã ${code}` }).click();
  await dialog.getByTestId('voucher-confirm').click();
  await scope.getByTestId(`${testId}-value`).filter({ hasText: code }).waitFor();
}

module.exports = {
  BASE, newPhone, latestOtp, registerViaApi, api, findProduct, withTiers, withoutTiers, loginInBrowser, stripTones,
  addAddressViaApi, apiLogin, apiAs, shopWithProduct, pickOption, pickAddress, pickVoucher,
};
