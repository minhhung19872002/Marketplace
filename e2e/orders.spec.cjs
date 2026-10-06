// Phase 6 — orders & shipping (spec section 9, scenarios 5 (up to "Đã nhận"), 6 and 11). Needs the stack and an admin:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test orders.spec.cjs
const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, apiAs, apiLogin, loginInBrowser, registerViaApi } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;
const SAMPLE_PNG = path.join(__dirname, 'fixtures', 'sample.png');

test.describe.configure({ mode: 'serial' });

/** A brand-new approved shop with one approved product (stock 50), built through the public + admin APIs. */
async function shopWithProduct(request, admin) {
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
    warehouse: { contactName: 'Kho', phone: seller.phone, provinceCode: '79', districtCode: '760', wardCode: '26734', street: '1 Nguyễn Huệ' },
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
    skus: [{ option1: null, option2: null, sellerSku: 'E2E-AO', price: 159000, originalPrice: 199000, stock: 50, weightG: null, isActive: true }],
  };
  const productId = await apiAs(request, token, 'POST', `/seller/shops/${shopId}/products`, input);
  await apiAs(request, token, 'POST', `/seller/shops/${shopId}/products/${productId}/actions/submit`);
  await apiAs(request, admin.accessToken, 'POST', `/admin/products/${productId}/approve`);
  return { seller, token, shopId, shopName, productId, name, input };
}

/** Buyer with an address places a COD order for the product through the UI; returns the order code. */
async function buyCod(page, request, productId, buyer) {
  await loginInBrowser(page, buyer);
  await page.goto(`${BASE}/san-pham/${productId}`);
  await page.waitForLoadState('networkidle');
  await Promise.all([
    page.waitForResponse((r) => r.url().includes('/api/cart/items') && r.request().method() === 'POST' && r.ok()),
    page.locator('[data-testid="add-to-cart"]').click(),
  ]);
  await page.goto(`${BASE}/thanh-toan`);
  await expect(page.locator('[data-testid="place-order"]')).toBeEnabled();
  await page.locator('[data-testid="place-order"]').click();
  await page.waitForURL(/\/dat-hang-thanh-cong/);
  const code = (await page.locator('[data-testid="result-order"] strong').first().textContent()).trim();
  return code;
}

async function newBuyer(request, name) {
  const account = await registerViaApi(request, name);
  const { token } = await addAddressViaApi(request, account);
  return { ...account, token };
}

async function sellerLogin(page, seller) {
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.goto(`${BASE}/seller/`);
  await page.getByLabel('Tên đăng nhập').fill(seller.phone);
  await page.getByLabel('Mật khẩu').fill(seller.password);
  await page.getByTestId('login-submit').click();
  await expect(page.getByTestId('todo-confirm')).toBeVisible();
}

test.describe('Đơn hàng & vận chuyển', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD để duyệt shop và đẩy trạng thái hãng giả lập');
  test.setTimeout(180_000);

  let admin;
  let shop;
  test.beforeAll(async ({ playwright }) => {
    const request = await playwright.request.newContext();
    admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    shop = await shopWithProduct(request, admin);
    await request.dispose();
  });

  test('Người bán xác nhận → in phiếu giao (PDF hợp lệ) → hãng giả lập giao → người mua "Đã nhận"', async ({ browser, request }) => {
    const buyer = await newBuyer(request, 'Người Mua Nhận Hàng');
    const buyerPage = await (await browser.newContext()).newPage();
    const code = await buyCod(buyerPage, request, shop.productId, buyer);

    // Seller centre: prepare (pickup slot) then print the label
    const sellerPage = await (await browser.newContext({ acceptDownloads: true })).newPage();
    await sellerLogin(sellerPage, shop.seller);
    await sellerPage.getByRole('menuitem', { name: 'Đơn hàng' }).click();
    await sellerPage.getByRole('tab', { name: 'Chờ xác nhận' }).click();
    const row = sellerPage.locator('.ant-table-row', { hasText: code });
    await row.getByTestId('prepare-order').click();
    await sellerPage.getByTestId('pickup-slot').click();
    await sellerPage.locator('.ant-select-item-option').first().click();
    await sellerPage.getByTestId('prepare-confirm').click();
    await sellerPage.getByRole('tab', { name: 'Chờ lấy hàng' }).click();
    const ready = sellerPage.locator('.ant-table-row', { hasText: code });
    await expect(ready.getByTestId('order-row-status')).toHaveText('Chờ lấy hàng');
    const [download] = await Promise.all([sellerPage.waitForEvent('download'), ready.getByTestId('print-label').click()]);
    const pdf = fs.readFileSync(await download.path());
    expect(pdf.subarray(0, 5).toString()).toBe('%PDF-');
    expect(pdf.length).toBeGreaterThan(2000);

    // The simulated carrier pushes picked → in transit → out for delivery → delivered
    const params = await apiAs(request, admin.accessToken, 'GET', '/admin/system-parameters?group=LOGISTICS');
    const step = params.find((p) => p.key === 'LOGISTICS.SIM_STEP_SECONDS');
    await apiAs(request, admin.accessToken, 'PUT', `/admin/system-parameters/${step.key}`, { value: '0', version: step.version });
    try {
      for (const expected of ['Shipping', 'Shipping', 'Shipping', 'Delivered']) {
        await apiAs(request, admin.accessToken, 'POST', '/admin/job-runs/logistics.carrier-simulator');
        await expect(async () => {
          const order = await apiAs(request, buyer.token, 'GET', `/orders/${code}`);
          expect(order.status).toBe(expected);
          expect(order.shipment.events.length).toBeGreaterThan(1);
        }).toPass({ timeout: 20_000 });
      }
    } finally {
      const now = (await apiAs(request, admin.accessToken, 'GET', '/admin/system-parameters?group=LOGISTICS')).find((p) => p.key === step.key);
      await apiAs(request, admin.accessToken, 'PUT', `/admin/system-parameters/${step.key}`, { value: step.value, version: now.version });
    }

    // Buyer sees the journey and confirms receipt
    await buyerPage.goto(`${BASE}/tai-khoan/don-mua/${code}`);
    await expect(buyerPage.getByTestId('order-detail-status')).toHaveText('Đã giao');
    await expect(buyerPage.getByTestId('shipment-timeline').locator('li')).toHaveCount(5);
    await buyerPage.getByTestId('confirm-received').click();
    await expect(buyerPage.getByTestId('order-detail-status')).toHaveText('Hoàn thành');

    // The public tracking page shows the same journey without personal data
    const tracking = (await buyerPage.getByTestId('tracking-no').textContent()).trim();
    await buyerPage.goto(`${BASE}/tra-cuu-van-don/${tracking}`);
    await expect(buyerPage.getByTestId('tracking-status')).toHaveText('Giao hàng thành công');
    await expect(buyerPage.getByTestId('tracking-result')).not.toContainText('Người Nhận E2E');
  });

  test('Huỷ trước xác nhận / yêu cầu huỷ sau xác nhận / shop từ chối', async ({ browser, request }) => {
    const buyer = await newBuyer(request, 'Người Mua Huỷ Đơn');
    const page = await (await browser.newContext()).newPage();

    // 1) Before the shop confirms: cancel directly
    const first = await buyCod(page, request, shop.productId, buyer);
    await page.goto(`${BASE}/tai-khoan/don-mua/${first}`);
    await page.getByTestId('cancel-order').click();
    await page.getByTestId('cancel-confirm').click();
    await expect(page.getByTestId('order-detail-status')).toHaveText('Đã huỷ');

    // 2) After the shop confirms: only a request, which the shop refuses
    const second = await buyCod(page, request, shop.productId, buyer);
    const order = await apiAs(request, buyer.token, 'GET', `/orders/${second}`);
    await apiAs(request, shop.token, 'POST', `/seller/shops/${shop.shopId}/orders/prepare`, { orderIds: [order.id], pickupMethod: 'DropOff', pickupSlot: null });
    await page.goto(`${BASE}/tai-khoan/don-mua/${second}`);
    await expect(page.getByTestId('cancel-order')).toHaveCount(0);
    await page.getByTestId('request-cancel').click();
    await page.getByTestId('cancel-confirm').click();
    await expect(page.getByTestId('cancel-request-status')).toContainText('đang chờ shop phản hồi');

    const sellerPage = await (await browser.newContext()).newPage();
    await sellerLogin(sellerPage, shop.seller);
    await expect(sellerPage.getByTestId('todo-cancel')).toContainText('1');
    await sellerPage.getByTestId('todo-cancel').click();
    await sellerPage.locator('.ant-table-row', { hasText: second }).getByTestId('order-code').click();
    await sellerPage.getByTestId('reject-cancel').click();
    await sellerPage.getByTestId('reject-reason').fill('Hàng đã giao cho đơn vị vận chuyển');
    await sellerPage.getByTestId('reject-confirm').click();
    await expect(sellerPage.getByTestId('cancel-request')).toHaveCount(0);

    await page.reload();
    await expect(page.getByTestId('cancel-request-status')).toContainText('shop đã từ chối');
    await expect(page.getByTestId('order-detail-status')).toHaveText('Chờ lấy hàng');
    await expect(page.getByTestId('request-cancel')).toHaveCount(0);
  });

  test('IDOR: người mua A mở đơn của B → 404; nhân viên shop X sửa sản phẩm shop Y → 404', async ({ browser, request }) => {
    const owner = await newBuyer(request, 'Chủ Đơn');
    const page = await (await browser.newContext()).newPage();
    const code = await buyCod(page, request, shop.productId, owner);

    const stranger = await newBuyer(request, 'Người Lạ');
    const strangerPage = await (await browser.newContext()).newPage();
    await loginInBrowser(strangerPage, stranger);
    await strangerPage.goto(`${BASE}/tai-khoan/don-mua/${code}`);
    await expect(strangerPage.getByText('Không tìm thấy đơn hàng.')).toBeVisible();
    const res = await request.get(`${BASE}/api/orders/${code}`, { headers: { Authorization: `Bearer ${stranger.token}` } });
    expect(res.status()).toBe(404);

    // A seller of another shop cannot touch this shop's product, nor read its orders
    const other = await shopWithProduct(request, admin);
    const edit = await request.put(`${BASE}/api/seller/shops/${other.shopId}/products/${shop.productId}`, {
      // A valid body, so the only reason to refuse is ownership
      headers: { Authorization: `Bearer ${other.token}` }, data: { input: { ...shop.input, name: 'Bị sửa trộm' }, version: null },
    });
    expect(edit.status()).toBe(404);
    const orders = await request.get(`${BASE}/api/seller/shops/${shop.shopId}/orders`, { headers: { Authorization: `Bearer ${other.token}` } });
    expect(orders.status()).toBe(404);
  });
});
