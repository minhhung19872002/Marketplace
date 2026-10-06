const { test, expect } = require('@playwright/test');

// Full-stack smoke checks — only meaningful behind the Nginx gateway (SH_E2E_BASE_URL set)
const BASE = process.env.SH_E2E_BASE_URL;

test.describe('Stack smoke', () => {
  test.skip(!BASE, 'Cần SH_E2E_BASE_URL trỏ vào gateway (vd http://localhost:18000)');

  for (const [path, title] of [['/seller/', 'Kênh Người Bán'], ['/admin/', 'Quản Trị Sàn']]) {
    test(`${title} tải được và thấy API hoạt động`, async ({ page }) => {
      await page.goto(`${BASE}${path}`);
      await expect(page.getByRole('heading', { name: title })).toBeVisible();
      await expect(page.getByTestId('api-health')).toHaveText('Hoạt động');
    });
  }

  test('Gateway chuyển /health tới API', async ({ request }) => {
    const res = await request.get(`${BASE}/health`);
    expect(res.status()).toBe(200);
    expect(await res.text()).toBe('Healthy');
  });
});
