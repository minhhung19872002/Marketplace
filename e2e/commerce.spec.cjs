// Phase 5 — cart, checkout, payment (spec section 9, scenarios 2, 3, 4). Needs the stack and an admin account:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test commerce.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api, apiAs, apiLogin, addAddressViaApi, findProduct, loginInBrowser, registerViaApi, withoutTiers } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

const vnd = (n) => `₫${n.toLocaleString('vi-VN')}`;
const money = (text) => Number(String(text).replace(/[^\d]/g, ''));

/** Two in-stock single-SKU products from two different shops that run a shop voucher, each ≥ ₫150.000. */
async function twoShopsWithVouchers(request) {
  const result = await api(request, '/search/products?inStock=true&minPrice=150000&maxPrice=3000000&sort=BestSelling&pageSize=60');
  const picked = [];
  for (const card of result.items) {
    if (picked.some((p) => p.shopId === card.shopId)) continue;
    const vouchers = (await api(request, `/vouchers?shopId=${card.shopId}`)).map((w) => w.voucher).filter((v) => v.type === 'Amount' && v.minOrder <= card.minPrice);
    if (vouchers.length === 0) continue;
    const page = await api(request, `/products/${card.id}`);
    if (!withoutTiers(page) || !page.purchasable) continue;
    picked.push({ product: page, shopId: card.shopId, voucher: vouchers[0] });
    if (picked.length === 2) return picked;
  }
  throw new Error('Không tìm thấy hai shop có voucher trong dữ liệu mẫu');
}

async function buyerWithAddress(request, name) {
  const account = await registerViaApi(request, name);
  const { token, userId } = await addAddressViaApi(request, account);
  return { ...account, token, userId };
}

async function addToCartInBrowser(page, productId) {
  await page.goto(`${BASE}/san-pham/${productId}`);
  await page.waitForLoadState('networkidle');
  await page.locator('[data-testid="add-to-cart"]').click();
  await expect(page.locator('[data-testid="pd-toast"]')).toContainText('Đã thêm vào giỏ hàng');
}

test.describe('Giỏ hàng & thanh toán', () => {
  test('Khách thêm giỏ → đăng nhập → giỏ được gộp vào tài khoản', async ({ page, request }) => {
    const product = await findProduct(request, withoutTiers);
    const account = await registerViaApi(request, 'Người Gộp Giỏ');

    await addToCartInBrowser(page, product.id);
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');
    await loginInBrowser(page, account);
    await page.goto(`${BASE}/gio-hang`);

    await expect(page.locator('[data-testid="cart-item"]')).toHaveCount(1);
    await expect(page.locator('[data-testid="cart-item"]')).toContainText(product.name);
    // The line now lives in the account: visible from a fresh API session too
    const login = await apiLogin(request, account.phone, account.password);
    const cart = await apiAs(request, login.accessToken, 'GET', '/cart');
    expect(cart.lineCount).toBe(1);
  });

  test('Giỏ 2 shop + voucher shop + voucher sàn + xu → COD → 2 đơn, tổng khớp từng đồng', async ({ page, request }) => {
    test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD để cộng xu');
    const [a, b] = await twoShopsWithVouchers(request);
    const buyer = await buyerWithAddress(request, 'Người Mua Hai Shop');
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    await apiAs(request, admin.accessToken, 'POST', `/admin/users/${buyer.userId}/coins`, { delta: 20000, reason: 'E2E: xu để thanh toán' });

    await loginInBrowser(page, buyer);
    await addToCartInBrowser(page, a.product.id);
    await addToCartInBrowser(page, b.product.id);
    await page.goto(`${BASE}/gio-hang`);
    await expect(page.locator('[data-testid="cart-shop"]')).toHaveCount(2);
    await page.locator('[data-testid="checkout"]').click();
    await page.waitForURL(/\/thanh-toan/);
    await expect(page.locator('[data-testid="checkout-shop"]')).toHaveCount(2);

    // Shop voucher for the first shop block, the platform's SHOPHUB50, and xu
    const firstShop = page.locator('[data-testid="checkout-shop"]').filter({ hasText: a.product.shop.name });
    await firstShop.locator('[data-testid="shop-voucher-select"]').selectOption(a.voucher.code);
    await expect(firstShop.locator('.checkout-shop-total')).not.toHaveText(/^$/);
    await page.locator('[data-testid="platform-voucher-select"]').selectOption('SHOPHUB50');
    await page.locator('[data-testid="use-coins"]').check();
    await expect(page.locator('[data-testid="checkout-summary"]')).toContainText('Voucher của shop');
    await expect(page.locator('[data-testid="checkout-summary"]')).toContainText('ShopHub Voucher');
    await expect(page.locator('[data-testid="checkout-summary"]')).toContainText('ShopHub Xu');
    await expect(page.locator('[data-testid="place-order"]')).toBeEnabled();
    const shown = money(await page.locator('[data-testid="checkout-total"]').textContent());

    await page.locator('[data-testid="place-order"]').click();
    await page.waitForURL(/\/dat-hang-thanh-cong\?checkout=/);
    await expect(page.locator('[data-testid="result-order"]')).toHaveCount(2);
    const orderTotals = (await page.locator('[data-testid="result-order-total"]').allTextContents()).map(money);
    expect(orderTotals.reduce((x, y) => x + y, 0)).toBe(shown);
    await expect(page.locator('[data-testid="success-total"]')).toHaveText(vnd(shown));

    // Server-side check of every discount on the two orders
    const orders = (await apiAs(request, buyer.token, 'GET', '/orders?tab=All&page=1&pageSize=10')).items;
    expect(orders).toHaveLength(2);
    const details = await Promise.all(orders.map((o) => apiAs(request, buyer.token, 'GET', `/orders/${o.code}`)));
    expect(details.reduce((s, d) => s + d.grandTotal, 0)).toBe(shown);
    expect(details.find((d) => d.shopId === a.shopId).shopDiscount).toBe(a.voucher.discountValue);
    expect(details.reduce((s, d) => s + d.platformDiscount, 0)).toBe(50000);
    expect(details.reduce((s, d) => s + d.coinUsed, 0)).toBe(20000);
    for (const d of details) {
      expect(d.status).toBe('PendingConfirmation');
      expect(d.paymentMethod).toBe('Cod');
      expect(d.items.reduce((s, i) => s + i.paidAmount, 0) + d.shippingFee - d.shippingDiscount).toBe(d.grandTotal);
    }
  });

  test('Thanh toán online qua cổng giả lập: thành công / thất bại (thanh toán lại)', async ({ page, request }) => {
    // Stock is asserted exactly: use a product the other (parallel) specs do not pick
    const product = await findProduct(request, withoutTiers, 'inStock=true&sort=PriceAsc&minPrice=20000&pageSize=60');
    const buyer = await buyerWithAddress(request, 'Người Trả Online');
    await loginInBrowser(page, buyer);

    // ---- thất bại, rồi thanh toán lại thành công ----
    await addToCartInBrowser(page, product.id);
    await page.goto(`${BASE}/thanh-toan`);
    await page.locator('[data-testid="method-Simulated"] input').check();
    await expect(page.locator('[data-testid="place-order"]')).toBeEnabled();
    const total = await page.locator('[data-testid="checkout-total"]').textContent();
    await page.locator('[data-testid="place-order"]').click();
    await page.waitForURL(/\/cong-thanh-toan\//);
    await expect(page.locator('[data-testid="gateway-amount"]')).toHaveText(total);
    await page.locator('[data-testid="gateway-fail"]').click();
    await page.waitForURL(/\/thanh-toan\/ket-qua\//);
    await expect(page.locator('[data-testid="payment-state"]')).toHaveText('Thanh toán không thành công');

    const held = await api(request, `/products/${product.id}`);
    expect(held.skus[0].available).toBe(product.skus[0].available - 1);

    await page.locator('[data-testid="retry-payment"]').click();
    await page.waitForURL(/\/cong-thanh-toan\//);
    await page.locator('[data-testid="gateway-success"]').click();
    await page.waitForURL(/\/thanh-toan\/ket-qua\//);
    await expect(page.locator('[data-testid="payment-state"]')).toHaveText('Thanh toán thành công');

    const orders = (await apiAs(request, buyer.token, 'GET', '/orders?tab=All&page=1&pageSize=10')).items;
    expect(orders[0].status).toBe('PendingConfirmation');
    expect(orders[0].paymentStatus).toBe('Paid');
  });

  test('Bỏ dở thanh toán online → quá hạn → huỷ đơn và nhả kho', async ({ page, request }) => {
    test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD để rút ngắn hạn thanh toán');
    test.setTimeout(180_000);
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const setTimeoutMinutes = async (value) => {
      const params = await apiAs(request, admin.accessToken, 'GET', '/admin/system-parameters?group=PAYMENT');
      const p = params.find((x) => x.key === 'PAYMENT.TIMEOUT_MINUTES');
      await apiAs(request, admin.accessToken, 'PUT', `/admin/system-parameters/${p.key}`, { value: String(value), version: p.version });
    };

    const product = await findProduct(request, withoutTiers, 'inStock=true&sort=Newest&pageSize=60');
    const before = product.skus[0].available;
    const buyer = await buyerWithAddress(request, 'Người Bỏ Dở');
    await setTimeoutMinutes(1);
    try {
      await loginInBrowser(page, buyer);
      await addToCartInBrowser(page, product.id);
      await page.goto(`${BASE}/thanh-toan`);
      await page.locator('[data-testid="method-Simulated"] input').check();
      await expect(page.locator('[data-testid="place-order"]')).toBeEnabled();
      await page.locator('[data-testid="place-order"]').click();
      await page.waitForURL(/\/cong-thanh-toan\//);
    } finally {
      await setTimeoutMinutes(15);
    }
    await page.locator('[data-testid="gateway-abandon"]').click();
    await page.waitForURL(/\/thanh-toan\/ket-qua\//);
    await expect(page.locator('[data-testid="payment-state"]')).toHaveText('Đang chờ thanh toán');
    expect((await api(request, `/products/${product.id}`)).skus[0].available).toBe(before - 1);

    // Past the 1-minute window: run the expiry job now instead of waiting for its schedule
    await page.waitForTimeout(62_000);
    await apiAs(request, admin.accessToken, 'POST', '/admin/job-runs/sales.payment-expiry');
    await expect(async () => {
      await page.reload();
      await expect(page.locator('[data-testid="payment-state"]')).toHaveText('Đơn hàng đã huỷ do quá hạn thanh toán', { timeout: 2_000 });
    }).toPass({ timeout: 30_000 });
    expect((await api(request, `/products/${product.id}`)).skus[0].available).toBe(before);
    const orders = (await apiAs(request, buyer.token, 'GET', '/orders?tab=Cancelled&page=1&pageSize=10')).items;
    expect(orders).toHaveLength(1);
  });
});
