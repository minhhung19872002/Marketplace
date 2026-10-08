const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');

// Seller Center + admin moderation on the full stack. Needs the gateway URL and an admin account that can review
// shops and products (never committed): SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD.
const BASE = process.env.SH_E2E_BASE_URL;
const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;
const SAMPLE_PNG = path.join(__dirname, 'fixtures', 'sample.png');

let phoneSeq = (Date.now() + 9000) % 100000000;
const newPhone = () => `09${String(++phoneSeq % 100000000).padStart(8, '0')}`;

async function api(request, method, url, { token, data, multipart } = {}) {
  const res = await request.fetch(`${BASE}/api${url}`, {
    method,
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    data,
    multipart,
  });
  const body = await res.json();
  if (!res.ok()) throw new Error(`${method} ${url} → ${res.status()}: ${JSON.stringify(body)}`);
  return body.data;
}

async function registerUser(request) {
  const phone = newPhone();
  const password = 'Matkhau123';
  await api(request, 'POST', '/auth/otp/send', { data: { target: phone, purpose: 'Register' } });
  let code = null;
  for (let i = 0; i < 40 && !code; i++) {
    const sms = await api(request, 'GET', `/dev/sms?to=${phone}`);
    code = sms[0]?.content.match(/\b\d{6}\b/)?.[0] ?? null;
    if (!code) await new Promise((r) => setTimeout(r, 500));
  }
  const { ticket } = await api(request, 'POST', '/auth/otp/verify', { data: { target: phone, purpose: 'Register', code } });
  const login = await api(request, 'POST', '/auth/register', {
    data: { target: phone, ticket, password, fullName: 'Người Bán E2E', acceptTerms: true },
  });
  return { phone, password, token: login.accessToken };
}

async function upload(request, token, purpose) {
  return api(request, 'POST', `/media/${purpose}`, {
    token,
    multipart: { file: { name: 'sample.png', mimeType: 'image/png', buffer: fs.readFileSync(SAMPLE_PNG) } },
  });
}

test.describe('Kênh Người Bán', () => {
  test.skip(!BASE || !ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_BASE_URL, SH_E2E_ADMIN_USER, SH_E2E_ADMIN_PASSWORD');
  test.setTimeout(120_000);

  test('Đăng ký shop → duyệt → đăng sản phẩm 2 tầng phân loại → quản trị duyệt', async ({ page, request }) => {
    // 1. A buyer applies to sell (API), an admin approves the shop (API)
    const seller = await registerUser(request);
    const front = await upload(request, seller.token, 'kyc');
    const back = await upload(request, seller.token, 'kyc');
    const shopName = `Shop E2E ${phoneSeq}`;
    const shopId = await api(request, 'POST', '/seller/shops', {
      token: seller.token,
      data: {
        name: shopName,
        type: 'Personal',
        description: 'Shop kiểm thử đầu cuối',
        warehouse: { contactName: 'Kho', phone: seller.phone, provinceCode: '01', wardCode: '00004', street: '1 Phố Thử' },
        personal: { legalName: 'Người Bán E2E', idCardNumber: '001200012345', frontAssetId: front.id, backAssetId: back.id },
        bank: { bankCode: 'VCB', accountNo: '0011002233445', accountName: 'NGUOI BAN E2E' },
      },
    });
    const admin = await api(request, 'POST', '/auth/login', { data: { identifier: ADMIN_USER, password: ADMIN_PASSWORD } });
    await api(request, 'POST', `/admin/shops/${shopId}/approve`, { token: admin.accessToken });

    // 2. Seller Center UI: create a product with Màu × Size and submit it
    await page.setViewportSize({ width: 1366, height: 768 });
    await page.goto(`${BASE}/seller/`);
    await page.getByLabel('Tên đăng nhập').fill(seller.phone);
    await page.getByLabel('Mật khẩu').fill(seller.password);
    await page.getByTestId('login-submit').click();
    // The seller centre opens on the dashboard (III.2)
    await page.getByRole('menuitem', { name: 'Sản phẩm', exact: true }).click();
    await page.getByTestId('add-product').click();

    await page.locator('[data-testid="product-upload"] input[type="file"]').setInputFiles(SAMPLE_PNG);
    await expect(page.locator('.media-tile')).toHaveCount(1);
    const productName = `Áo Thun E2E ${phoneSeq}`;
    await page.getByLabel('Tên sản phẩm').fill(productName);
    await page.getByLabel('Danh mục').first().click();
    await page.keyboard.type('Áo Thun');
    await page.locator('.ant-cascader-menu-item', { hasText: 'Thời Trang Nam / Áo / Áo Thun' }).first().click();

    await page.getByLabel('Xuất xứ').first().click();
    await page.locator('.ant-select-item-option', { hasText: 'Việt Nam' }).click();
    await page.getByLabel('Chất liệu').first().click();
    await page.locator('.ant-select-item-option', { hasText: 'Cotton' }).last().click();

    await page.getByTestId('has-variants').click();
    await page.getByLabel('Tên phân loại 1').fill('Màu sắc');
    const opts1 = page.getByLabel('Lựa chọn phân loại 1').last();
    for (const v of ['Đen', 'Trắng']) { await opts1.fill(v); await opts1.press('Enter'); }
    await page.getByRole('button', { name: '+ Thêm phân loại 2' }).click();
    await page.getByLabel('Tên phân loại 2').fill('Size');
    const opts2 = page.getByLabel('Lựa chọn phân loại 2').last();
    for (const v of ['M', 'L', 'XL']) { await opts2.fill(v); await opts2.press('Enter'); }
    await page.keyboard.press('Escape');

    const skuRows = page.locator('[data-testid="sku-table"] tbody tr.ant-table-row');
    await expect(skuRows).toHaveCount(6);
    await page.getByPlaceholder('Giá', { exact: true }).fill('99000');
    await page.getByPlaceholder('Tồn kho', { exact: true }).fill('20');
    await page.getByRole('button', { name: 'Áp dụng', exact: true }).click();
    await page.getByLabel('Cân nặng').fill('300');
    await page.getByTestId('save-submit').click();

    await expect(page.getByTestId('product-name').filter({ hasText: productName })).toBeVisible();

    // 3. Admin UI: approve from the review queue
    await page.getByTestId('logout').click();
    await page.goto(`${BASE}/admin/`);
    await page.getByLabel('Tên đăng nhập').fill(ADMIN_USER);
    await page.getByLabel('Mật khẩu').fill(ADMIN_PASSWORD);
    await page.getByTestId('login-submit').click();
    await page.getByRole('menuitem', { name: 'Duyệt sản phẩm' }).click();
    const row = page.locator('.ant-table-row', { hasText: productName });
    await expect(row).toBeVisible();
    await row.getByTestId('approve').click();
    await expect(row).toHaveCount(0);

    // 4. The product is selling with 6 SKUs
    const list = await api(request, 'GET', `/seller/shops/${shopId}/products?tab=Active`, { token: seller.token });
    const created = list.items.find((p) => p.name === productName);
    expect(created).toBeTruthy();
    expect(created.status).toBe('Active');
    expect(created.skuCount).toBe(6);
    expect(created.totalStock).toBe(120);
  });
});
