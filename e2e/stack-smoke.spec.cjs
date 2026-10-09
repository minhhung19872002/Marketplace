const { test, expect } = require('@playwright/test');

// Full-stack smoke checks — only meaningful behind the Nginx gateway (SH_E2E_BASE_URL set)
const BASE = process.env.SH_E2E_BASE_URL;

test.describe('Stack smoke', () => {
  test.skip(!BASE, 'Cần SH_E2E_BASE_URL trỏ vào gateway (vd http://localhost:18000)');

  test('Kênh Người Bán mở màn hình đăng nhập', async ({ page }) => {
    await page.goto(`${BASE}/seller/`);
    await expect(page.getByRole('heading', { name: 'Đăng nhập Kênh Người Bán' })).toBeVisible();
    await expect(page.getByTestId('login-submit')).toBeVisible();
  });

  test('Quản Trị Sàn mở màn hình đăng nhập', async ({ page }) => {
    await page.goto(`${BASE}/admin/`);
    await expect(page.getByRole('heading', { name: 'Đăng nhập quản trị sàn' })).toBeVisible();
    await expect(page.getByTestId('login-submit')).toBeVisible();
  });

  test('Gateway chuyển /health tới API', async ({ request }) => {
    const res = await request.get(`${BASE}/health`);
    expect(res.status()).toBe(200);
    expect(await res.text()).toBe('Healthy');
  });
});
