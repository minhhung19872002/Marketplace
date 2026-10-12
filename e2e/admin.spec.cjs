const { test, expect } = require('@playwright/test');
const { newPhone, latestOtp } = require('./helpers.cjs');

// Admin console checks — need the full stack behind the gateway (/admin/ + /api/)
const BASE = process.env.SH_E2E_BASE_URL;

async function registerBuyer(request) {
  const phone = newPhone();
  const password = 'Matkhau123';
  const sent = await request.post(`${BASE}/api/auth/otp/send`, { data: { target: phone, purpose: 'Register' } });
  if (!sent.ok()) throw new Error(`Gửi OTP thất bại: ${await sent.text()}`);
  const code = await latestOtp(request, phone);
  const verify = await (await request.post(`${BASE}/api/auth/otp/verify`, { data: { target: phone, purpose: 'Register', code } })).json();
  const res = await request.post(`${BASE}/api/auth/register`, {
    data: { target: phone, ticket: verify.data.ticket, password, fullName: 'Người Mua Thử', acceptTerms: true },
  });
  if (!res.ok()) throw new Error(`Đăng ký thất bại: ${await res.text()}`);
  return { phone, password };
}

test.describe('Quản trị sàn', () => {
  test.skip(!BASE, 'Cần SH_E2E_BASE_URL trỏ vào gateway (vd http://localhost:18000)');

  test('Sai mật khẩu quản trị bị từ chối', async ({ page, request }) => {
    // A throwaway account: hammering the real 'admin' would trip its lockout
    const someone = await registerBuyer(request);
    await page.goto(`${BASE}/admin/`);
    await page.getByLabel('Tên đăng nhập').fill(someone.phone);
    await page.getByLabel('Mật khẩu').fill('KhongPhaiMatKhau1');
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('login-error')).toContainText('Thông tin đăng nhập không đúng.');
  });

  test('Người mua thường không vào được trang quản trị', async ({ page, request }) => {
    const buyer = await registerBuyer(request);
    await page.goto(`${BASE}/admin/`);
    await page.getByLabel('Tên đăng nhập').fill(buyer.phone);
    await page.getByLabel('Mật khẩu').fill(buyer.password);
    await page.getByTestId('login-submit').click();

    await expect(page.getByText('Tài khoản không có quyền quản trị')).toBeVisible();
    // And the API agrees, whatever the UI shows
    const res = await page.request.get(`${BASE}/api/admin/users`);
    expect(res.status()).toBe(401);
  });
});
