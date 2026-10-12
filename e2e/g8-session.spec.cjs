// G8 A (L212): a reload right after signing in must keep the session — in web, seller and admin (docs/plans/G8.md).
//   SH_E2E_BASE_URL=http://localhost:18300 [SH_E2E_ADMIN_USER=… SH_E2E_ADMIN_PASSWORD=…] npx playwright test g8-session.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, registerViaApi, apiLogin } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;
const guest = { storageState: { cookies: [], origins: [] } };

/**
 * A reload in the middle of the page's own /auth/refresh: the server rotates the token but the browser never sees the
 * answer, so it keeps the old cookie (what F5 at the wrong moment does — L187). Playwright lets the request reach the
 * server and then drops the response.
 */
async function reloadMidRefresh(page) {
  const context = page.context();
  const before = await context.cookies();
  let reached;
  const rotated = new Promise((r) => { reached = r; });
  await page.route('**/api/auth/refresh', async (route) => {
    const response = await route.fetch();
    // The page never gets the answer: the request dies as a reload kills it (left pending, unroute would let it through)
    await route.abort('aborted');
    reached(response.status());
  });
  await page.reload({ waitUntil: 'commit' });
  expect(await rotated, 'máy chủ đã xoay token').toBe(200);
  await page.unrouteAll({ behavior: 'ignoreErrors' });
  // route.fetch() shares the context's cookie jar: put back the cookie the browser held, as a lost answer leaves it
  await context.clearCookies();
  await context.addCookies(before);
  // The next load refreshes with the cookie the browser still holds — the one the server has just replaced
  await page.reload();
}

/** The session is alive server-side too: a refresh from the page's cookie and /api/account/me both answer 200. */
async function expectServerSession(page) {
  const statuses = await page.evaluate(async () => {
    const r = await fetch('/api/auth/refresh', { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    if (!r.ok) return [r.status, 0];
    const token = (await r.json()).data.accessToken;
    const me = await fetch('/api/account/me', { headers: { Authorization: `Bearer ${token}` } });
    return [r.status, me.status];
  });
  expect(statuses).toEqual([200, 200]);
}

/** Signed in → reload mid-refresh → reload after 1 s → reload after 1 min: still signed in every time. */
async function survivesReloads(page, signedIn) {
  await reloadMidRefresh(page);
  await expect(signedIn, 'tải lại ngay khi đang làm mới phiên').toBeVisible({ timeout: 15_000 });
  await page.waitForTimeout(1000);
  await page.reload();
  await expect(signedIn, 'tải lại sau 1 giây').toBeVisible({ timeout: 15_000 });
  await page.waitForTimeout(60_000);
  await page.reload();
  await expect(signedIn, 'tải lại sau 1 phút').toBeVisible({ timeout: 15_000 });
  await expectServerSession(page);
}

const signIn = async (page, path, identifier, password) => {
  await page.goto(`${BASE}${path}`);
  await page.getByLabel('Tên đăng nhập').fill(identifier);
  await page.getByLabel('Mật khẩu').fill(password);
  await page.getByTestId('login-submit').click();
};

test.describe('G8 A — tải lại ngay sau đăng nhập vẫn giữ phiên', () => {
  test.describe.configure({ timeout: 180_000 });

  test('web người mua', async ({ browser, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Tải Lại');
    const context = await browser.newContext(guest);
    const page = await context.newPage();
    await signIn(page, '/dang-nhap', buyer.phone, buyer.password);
    const menu = page.getByTestId('user-menu');
    await expect(menu).toBeVisible();
    await survivesReloads(page, menu);
    await context.close();
  });

  test('Kênh Người Bán', async ({ browser, request }) => {
    // Any account opens the seller centre (a buyer without a shop gets the shop sign-up)
    const seller = await registerViaApi(request, 'Người Bán Tải Lại');
    const context = await browser.newContext({ ...guest, viewport: { width: 1366, height: 768 } });
    const page = await context.newPage();
    await signIn(page, '/seller/', seller.phone, seller.password);
    const menu = page.getByTestId('user-menu');
    await expect(menu).toBeVisible();
    await survivesReloads(page, menu);
    await context.close();
  });

  test('Quản trị', async ({ browser }) => {
    test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');
    const context = await browser.newContext({ ...guest, viewport: { width: 1366, height: 768 } });
    const page = await context.newPage();
    await signIn(page, '/admin/', ADMIN_USER, ADMIN_PASSWORD);
    const menu = page.getByTestId('user-menu');
    await expect(menu).toBeVisible();
    await survivesReloads(page, menu);
    await context.close();
  });

  // The theft rule stays: an old token used again after its successor has been used ends the session
  test('Token cũ dùng lại sau khi token mới đã được dùng → thu hồi cả phiên', async ({ request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Chống Trộm');
    const first = await apiLogin(request, buyer.phone, buyer.password);
    const refresh = (token) => request.post(`${BASE}/api/auth/refresh`, { data: { refreshToken: token } });
    const second = (await (await refresh(first.refreshToken)).json()).data;
    const third = await refresh(second.refreshToken);
    expect(third.status()).toBe(200);
    const thirdToken = (await third.json()).data.refreshToken;
    // The first token's successor has been used: this is a stolen copy
    expect((await refresh(first.refreshToken)).status()).toBe(401);
    expect((await refresh(thirdToken)).status(), 'cả chuỗi đã bị thu hồi').toBe(401);
  });
});
