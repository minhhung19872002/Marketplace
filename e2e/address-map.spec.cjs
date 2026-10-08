// E7 (spec I.2): an address can be pinned on the map (OpenStreetMap through Leaflet) — optional, saved as lat / lng.
//   SH_E2E_BASE_URL=http://localhost:18000 npx playwright test address-map.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, apiLogin, loginInBrowser, registerViaApi, pickAddress } = require('./helpers.cjs');

test('Sổ địa chỉ: ghim vị trí trên bản đồ, lưu lại và bỏ ghim được', async ({ page, request }) => {
  const buyer = await registerViaApi(request, 'Người Mua Ghim Bản Đồ');
  await loginInBrowser(page, buyer);
  await page.goto(`${BASE}/tai-khoan/dia-chi`);
  await page.getByTestId('address-add').click();
  await page.locator('input[aria-label="Tên người nhận"]').fill('Người Mua Ghim Bản Đồ');
  await page.locator('input[aria-label="Số điện thoại người nhận"]').fill(buyer.phone);
  await pickAddress(page);
  await page.locator('input[aria-label="Địa chỉ cụ thể"]').fill('12 Phố Ghim');

  // Optional: open the map and drop the pin (the click works whether or not the map tiles load)
  await page.getByTestId('address-pin-toggle').click();
  const map = page.getByTestId('address-map');
  await expect(map).toBeVisible();
  await map.click({ position: { x: 150, y: 140 } });
  await expect(page.getByTestId('address-pin-coords')).toContainText('Đã ghim');
  await page.getByTestId('address-save').click();
  await expect(page.locator('[data-testid="address-item"]')).toHaveCount(1);
  // The write says how it went in the shared toast (F4)
  await expect(page.getByTestId('toast').first()).toBeVisible();

  const login = await apiLogin(request, buyer.phone, buyer.password);
  const res = await request.get(`${BASE}/api/account/addresses`, { headers: { Authorization: `Bearer ${login.accessToken}` } });
  const [address] = (await res.json()).data;
  expect(address.lat).not.toBeNull();
  expect(address.lng).not.toBeNull();
  expect(address.lat).toBeGreaterThanOrEqual(-90);
  expect(address.lat).toBeLessThanOrEqual(90);
});
