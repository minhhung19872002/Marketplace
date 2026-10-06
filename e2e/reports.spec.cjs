// Phase 12 — admin overview / reports / order intervention and the shop's analytics, in the browser. Needs the stack and an admin:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test reports.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, addAddressViaApi, apiAs, apiLogin, registerViaApi, shopWithProduct } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

async function placeCod(request, shop) {
  const buyer = await registerViaApi(request, 'Người Mua Báo Cáo');
  const { token } = await addAddressViaApi(request, buyer);
  const addressId = null; // the default address
  const product = await apiAs(request, token, 'GET', `/products/${shop.productId}`);
  await apiAs(request, token, 'POST', '/cart/items', { skuId: product.skus[0].id, quantity: 1 });
  const checkout = {
    addressId, shops: [{ shopId: shop.shopId, voucherCode: null, carrierCode: null, note: null }],
    platformVoucherCode: null, freeshipVoucherCode: null, useCoins: false, paymentMethod: 'Cod',
  };
  const quote = await apiAs(request, token, 'POST', '/checkout/quote', checkout);
  const res = await request.post(`${BASE}/api/checkout`, {
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': `e2e-${Date.now()}-${Math.random()}` },
    data: { checkout, expectedGrandTotal: quote.grandTotal },
  });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).data.orders[0].code;
}

test.describe('Quản trị & báo cáo', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');
  test.setTimeout(180_000);

  test('Tổng quan có số liệu, báo cáo GMV xuất Excel, quản trị tra đơn và huỷ có lý do; shop xem phân tích', async ({ browser, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin, { price: 175000 });
    const code = await placeCod(request, shop);
    // A second order that stays: the shop's analytics count orders that were not cancelled
    await placeCod(request, shop);

    const page = await (await browser.newContext({ viewport: { width: 1366, height: 768 }, acceptDownloads: true })).newPage();
    await page.goto(`${BASE}/admin/`);
    await page.getByLabel('Tên đăng nhập').fill(ADMIN_USER);
    await page.getByLabel('Mật khẩu').fill(ADMIN_PASSWORD);
    await page.getByTestId('login-submit').click();
    // VI.1 KPIs come from the API, not placeholders
    await expect(page.getByTestId('kpi-orders')).not.toContainText('—', { timeout: 20_000 });

    // VI.10: report table + export
    await page.getByRole('menuitem', { name: 'Báo cáo', exact: true }).click();
    await expect(page.getByTestId('report-table')).toContainText('Tổng');
    const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('export-xlsx').click()]);
    expect(download.suggestedFilename()).toMatch(/\.xlsx$/);

    // VI.5: find the order, cancel it with a reason; the history shows the platform did it
    await page.getByRole('menuitem', { name: 'Đơn hàng', exact: true }).click();
    await page.getByTestId('order-search').fill(code);
    await page.getByTestId('order-search').press('Enter');
    await page.locator('.ant-table-row', { hasText: code }).click();
    await page.getByTestId('admin-cancel-order').click();
    await page.getByTestId('admin-cancel-reason').fill('Người mua yêu cầu qua tổng đài, xác minh danh tính');
    await page.getByRole('button', { name: 'Huỷ đơn', exact: true }).click();
    await expect(page.locator('.ant-drawer')).toContainText('Sàn huỷ: Người mua yêu cầu qua tổng đài', { timeout: 15_000 });
    const order = await apiAs(request, admin.accessToken, 'GET', `/admin/orders/${code}`);
    expect(order.order.status).toBe('Cancelled');

    // III.8: the shop's analytics page
    const seller = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();
    await seller.goto(`${BASE}/seller/`);
    await seller.getByLabel('Tên đăng nhập').fill(shop.seller.phone);
    await seller.getByLabel('Mật khẩu').fill(shop.seller.password);
    await seller.getByTestId('login-submit').click();
    await seller.getByRole('menuitem', { name: 'Dữ liệu & phân tích' }).click();
    await expect(seller.getByTestId('figure-orders')).toContainText('1');
    await seller.getByRole('tab', { name: 'Hiệu quả hoạt động' }).click();
    await expect(seller.getByText('Điểm phạt hiện tại: 0')).toBeVisible();
  });

  test('Trang pháp lý, trung tâm trợ giúp và thông tin pháp nhân ở footer', async ({ page }) => {
    await page.goto(`${BASE}/tro-giup`);
    await page.getByTestId('help-search').fill('huy don');
    await page.getByTestId('help-search').press('Enter');
    await page.getByRole('link', { name: 'Tôi muốn huỷ đơn hàng' }).click();
    await expect(page.getByTestId('cms-page')).toContainText('Chờ xác nhận');
    await page.goto(`${BASE}/trang/quy-che-hoat-dong`);
    await expect(page.getByTestId('cms-page')).toContainText('Quy chế hoạt động sàn');
    await expect(page.getByTestId('footer-legal')).toContainText('Mã số doanh nghiệp');
  });
});
