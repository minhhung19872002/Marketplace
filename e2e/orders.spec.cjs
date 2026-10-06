// Phase 6/7 — orders, shipping, reviews and returns (spec section 9, scenarios 5, 6, 7 and 11). Needs the stack and an admin:
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

/** The simulated carrier pushes picked → in transit → out for delivery → delivered (steps sped up for the test). */
async function deliverViaSimulator(request, admin, buyer, code) {
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

    await deliverViaSimulator(request, admin, buyer, code);

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

    // Review with a photo (≥ 50 characters → ShopHub Xu reward) → the product page shows the new rating
    await buyerPage.goto(`${BASE}/tai-khoan/don-mua/${code}`);
    await buyerPage.getByTestId('review-order').click();
    const item = buyerPage.getByTestId('review-item').first();
    await item.getByTestId('star-4').click();
    await item.getByLabel('Nội dung đánh giá').fill('Áo mặc rất thoải mái, vải dày dặn, đường may chắc chắn, giao hàng nhanh và đóng gói cẩn thận.');
    await Promise.all([
      buyerPage.waitForResponse((r) => r.url().includes('/api/media/review') && r.ok()),
      item.getByTestId('review-media-input').setInputFiles(SAMPLE_PNG),
    ]);
    await expect(item.locator('.media-picker-item')).toHaveCount(1);
    await item.getByTestId('review-submit').click();
    await expect(item.getByTestId('review-done')).toBeVisible();
    await buyerPage.goto(`${BASE}/san-pham/${shop.productId}`);
    await expect(async () => {
      await buyerPage.reload();
      await expect(buyerPage.getByTestId('rating-average')).toHaveText('4.0', { timeout: 2_000 });
    }).toPass({ timeout: 20_000 });
    const shown = buyerPage.getByTestId('review').first();
    await expect(shown).toContainText('vải dày dặn');
    await expect(shown.locator('.pd-review-media img')).toHaveCount(1);
    await expect(async () => {
      expect((await apiAs(request, buyer.token, 'GET', '/account/coins')).balance).toBeGreaterThan(0);
    }).toPass({ timeout: 20_000 });

    // Finance: the shop sees the order "chờ giải ngân" → release (return window over) → withdraw to its verified bank account
    await sellerPage.goto(`${BASE}/seller/tai-chinh`);
    await expect(async () => {
      await sellerPage.reload();
      await expect(sellerPage.getByTestId('earning-net').first()).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 30_000 });
    // The ledger follows the order events through the outbox (dispatched every minute — run it now)
    let summary;
    await expect(async () => {
      await apiAs(request, admin.accessToken, 'POST', '/admin/job-runs/sys.outbox-dispatch');
      summary = await apiAs(request, shop.token, 'GET', `/seller/shops/${shop.shopId}/finance/summary`);
      expect(summary.pending).toBeGreaterThan(0);
    }).toPass({ timeout: 30_000 });
    const window = (await apiAs(request, admin.accessToken, 'GET', '/admin/system-parameters?group=RETURN')).find((p) => p.key === 'RETURN.WINDOW_DAYS');
    await apiAs(request, admin.accessToken, 'PUT', `/admin/system-parameters/${window.key}`, { value: '0', version: window.version });
    try {
      await apiAs(request, admin.accessToken, 'POST', '/admin/job-runs/finance.settlement');
      await expect(async () => {
        const s = await apiAs(request, shop.token, 'GET', `/seller/shops/${shop.shopId}/finance/summary`);
        expect(s.available).toBeGreaterThanOrEqual(summary.pending);
      }).toPass({ timeout: 30_000 });
    } finally {
      const now = (await apiAs(request, admin.accessToken, 'GET', '/admin/system-parameters?group=RETURN')).find((p) => p.key === window.key);
      await apiAs(request, admin.accessToken, 'PUT', `/admin/system-parameters/${window.key}`, { value: window.value, version: now.version });
    }
    await sellerPage.reload();
    await sellerPage.getByRole('tab', { name: 'Đã giải ngân' }).click();
    await expect(sellerPage.getByTestId('earning-net').first()).toBeVisible();
    await sellerPage.getByTestId('finance-withdraw').click();
    await sellerPage.getByTestId('withdraw-amount').fill('50000');
    await sellerPage.getByRole('button', { name: 'Rút tiền', exact: true }).last().click();
    await expect(sellerPage.getByRole('dialog')).toHaveCount(0);
    await sellerPage.getByRole('tab', { name: 'Lịch sử rút tiền' }).click();
    await expect(sellerPage.getByTestId('withdrawal-status').first()).toHaveText('Đã chuyển');
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

  test('Trả 1 phần → shop từ chối → khiếu nại → sàn phân xử → hoàn đúng phần đã trả sau giảm giá', async ({ browser, request }) => {
    // Two units with SHOPHUB50 (−₫50.000 on ₫318.000): each unit cost the buyer (318.000 − 50.000) / 2 = ₫134.000
    const buyer = await newBuyer(request, 'Người Mua Trả Hàng');
    const product = await apiAs(request, buyer.token, 'GET', `/products/${shop.productId}`);
    await apiAs(request, buyer.token, 'POST', '/cart/items', { skuId: product.skus[0].id, quantity: 2 });
    const choice = { addressId: null, shops: [{ shopId: shop.shopId, carrierCode: null, voucherCode: null, note: null }],
      platformVoucherCode: 'SHOPHUB50', freeshipVoucherCode: null, useCoins: false, paymentMethod: 'Cod' };
    const quote = await apiAs(request, buyer.token, 'POST', '/checkout/quote', choice);
    expect(quote.platformDiscount).toBe(50_000);
    const placed = await request.post(`${BASE}/api/checkout`, {
      headers: { Authorization: `Bearer ${buyer.token}`, 'Idempotency-Key': `e2e-return-${buyer.phone}` },
      data: { checkout: choice, expectedGrandTotal: quote.grandTotal },
    });
    expect(placed.ok()).toBeTruthy();
    const orders = await apiAs(request, buyer.token, 'GET', '/orders?tab=All');
    const code = orders.items[0].code;
    const order = await apiAs(request, buyer.token, 'GET', `/orders/${code}`);
    await apiAs(request, shop.token, 'POST', `/seller/shops/${shop.shopId}/orders/prepare`, { orderIds: [order.id], pickupMethod: 'DropOff', pickupSlot: null });
    await deliverViaSimulator(request, admin, buyer, code);

    // Buyer asks to refund ONE unit with a photo as evidence
    const page = await (await browser.newContext()).newPage();
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/tai-khoan/don-mua/${code}`);
    await page.getByTestId('return-order').click();
    await page.getByTestId('return-qty').fill('1');
    await page.getByTestId('return-reason').selectOption('Damaged');
    await page.getByTestId('return-description').fill('Áo bị rách một đường ở tay áo khi mở hộp.');
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/media/evidence') && r.ok()),
      page.getByTestId('return-evidence-input').setInputFiles(SAMPLE_PNG),
    ]);
    await expect(page.getByTestId('return-estimate')).toHaveText('₫134.000');
    await page.getByTestId('return-submit').click();
    await expect(page.getByTestId('return-status')).toHaveText(/Chờ shop phản hồi/);
    const returnCode = page.url().split('/').pop();

    // Seller rejects in the seller centre
    const sellerPage = await (await browser.newContext()).newPage();
    await sellerLogin(sellerPage, shop.seller);
    await sellerPage.getByRole('menuitem', { name: 'Trả hàng / Hoàn tiền' }).click();
    await sellerPage.locator('.ant-table-row', { hasText: returnCode }).click();
    await sellerPage.getByTestId('return-note').fill('Ảnh không cho thấy lỗi của sản phẩm');
    await sellerPage.getByTestId('return-reject').click();
    await expect(sellerPage.locator('.ant-drawer')).toHaveCount(0);

    // Buyer disputes
    await page.reload();
    await expect(page.getByTestId('return-status')).toHaveText(/từ chối/i);
    await page.getByTestId('open-dispute').click();
    await page.getByTestId('dispute-reason').fill('Ảnh chụp rõ vết rách, shop không chịu nhận.');
    await page.getByTestId('dispute-submit').click();
    await expect(page.getByTestId('return-status')).toHaveText(/khiếu nại/i);

    // The platform decides for the buyer (full requested amount)
    const adminPage = await (await browser.newContext()).newPage();
    await adminPage.setViewportSize({ width: 1366, height: 768 });
    await adminPage.goto(`${BASE}/admin/`);
    await adminPage.getByLabel('Tên đăng nhập').fill(ADMIN_USER);
    await adminPage.getByLabel('Mật khẩu').fill(ADMIN_PASSWORD);
    await adminPage.getByTestId('login-submit').click();
    await adminPage.getByRole('menuitem', { name: 'Khiếu nại trả hàng' }).click();
    await adminPage.locator('.ant-table-row', { hasText: returnCode }).click();
    await adminPage.getByTestId('decide-reason').fill('Bằng chứng cho thấy sản phẩm lỗi.');
    await adminPage.getByTestId('decide-submit').click();
    await expect(adminPage.locator('.ant-drawer')).toHaveCount(0);

    await page.reload();
    await expect(page.getByTestId('return-status')).toHaveText(/hoàn tiền/i);
    await expect(page.getByTestId('refund-amount')).toContainText('₫134.000');
    const final = await apiAs(request, buyer.token, 'GET', `/returns/${returnCode}`);
    expect(final.refundAmount).toBe(134_000);
    expect(final.items).toHaveLength(1);
    expect(final.items[0].quantity).toBe(1);
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
