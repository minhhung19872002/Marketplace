// Spec I.2 on the full stack: avatar (crop ≤ 1 MB), "Tải dữ liệu của tôi", account deletion (blocked while an order
// is open, anonymised afterwards).
//   SH_E2E_BASE_URL=http://localhost:18000 npx playwright test account.spec.cjs
const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');
const { BASE, loginInBrowser, registerViaApi } = require('./helpers.cjs');

const SAMPLE_PNG = path.join(__dirname, 'fixtures', 'sample.png');

test.describe('Tài khoản: ảnh đại diện & quyền riêng tư', () => {
  test('Đổi ảnh đại diện bằng ảnh đã cắt → hiện ở header và trang tài khoản', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Ảnh Đại Diện');
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/tai-khoan/ho-so`);
    await page.getByTestId('avatar-file').setInputFiles(SAMPLE_PNG);
    await expect(page.locator('canvas.avatar-crop')).toBeVisible();
    await page.getByTestId('avatar-save').click();
    await expect(page.getByText('Đã cập nhật ảnh đại diện.')).toBeVisible();

    // The saved photo is what people see (not just a file in MinIO): header + sidebar load the image
    const header = page.locator('img.header-user-avatar');
    await expect(header).toBeVisible();
    await expect.poll(() => header.evaluate((img) => img.complete && img.naturalWidth > 0)).toBe(true);
    await page.reload();
    await expect(page.locator('img.account-avatar')).toBeVisible();
  });

  test('Tải dữ liệu của tôi, rồi xoá tài khoản → không đăng nhập được nữa', async ({ page, request }) => {
    const buyer = await registerViaApi(request, 'Người Mua Xoá Tài Khoản');
    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/tai-khoan/quyen-rieng-tu`);

    const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('privacy-export').click()]);
    const data = JSON.parse(fs.readFileSync(await download.path(), 'utf8'));
    expect(data.profile.phone).toBe(buyer.phone);
    expect(Array.isArray(data.orders)).toBe(true);

    await page.getByTestId('privacy-delete').click();
    await page.getByTestId('privacy-password').fill(buyer.password);
    await page.getByTestId('privacy-understood').check();
    await page.getByTestId('privacy-delete-confirm').click();
    await expect(page).toHaveURL(`${BASE}/`);

    const login = await request.post(`${BASE}/api/auth/login`, { data: { identifier: buyer.phone, password: buyer.password } });
    expect(login.status()).toBe(401);
  });
});
