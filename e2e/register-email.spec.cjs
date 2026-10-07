// E1 (spec I.1): sign up with an email address — the code arrives by mail (Mailpit in dev / CI), then the account
// signs in with that email.
//   SH_E2E_BASE_URL=http://localhost:18000 npx playwright test register-email.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE } = require('./helpers.cjs');

const MAILPIT = process.env.SH_E2E_MAILPIT_URL || 'http://localhost:18025';

/** The 6-digit code of the newest mail sent to the address (Mailpit API), waiting for the outbox to deliver it. */
async function mailedOtp(request, address) {
  for (let i = 0; i < 30; i++) {
    const res = await request.get(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:${address}`)}&limit=1`);
    if (res.ok()) {
      const found = await res.json();
      if (found.messages && found.messages.length > 0) {
        const message = await (await request.get(`${MAILPIT}/api/v1/message/${found.messages[0].ID}`)).json();
        const code = /\b(\d{6})\b/.exec(`${message.Subject} ${message.Text}`);
        if (code) return code[1];
      }
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`Không thấy thư mã xác thực gửi tới ${address}`);
}

test.describe('Đăng ký bằng email', () => {
  test('Chọn Email → mã gửi qua thư → đặt mật khẩu → đăng nhập lại bằng email', async ({ page, request }) => {
    const email = `e2e.${Date.now()}.${Math.floor(Math.random() * 1e6)}@example.com`;
    await page.goto(`${BASE}/dang-ky`);
    await page.getByTestId('register-channel-email').click();
    await page.locator('input[aria-label="Email"]').fill(email);
    await page.locator('[data-testid="register-send-otp"]').click();
    await expect(page.getByText(`Mã xác thực đã được gửi tới hộp thư ${email}`)).toBeVisible();

    await page.locator('input[aria-label="Mã xác thực"]').fill(await mailedOtp(request, email));
    await page.locator('[data-testid="register-verify"]').click();
    await page.locator('input[aria-label="Họ và tên"]').fill('Người Mua Đăng Ký Email');
    await page.locator('input[aria-label="Mật khẩu"]').fill('Matkhau123');
    await page.locator('input[aria-label="Đồng ý điều khoản"]').check();
    await page.locator('[data-testid="register-submit"]').click();
    await page.waitForURL(BASE + '/');
    await expect(page.locator('[data-testid="user-menu"]')).toContainText('Người Mua Đăng Ký Email');

    // Signed out, the same email + password signs in again
    await page.locator('[data-testid="user-menu"]').click();
    await page.locator('[data-testid="logout"]').click();
    await page.goto(`${BASE}/dang-nhap`);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(email);
    await page.locator('input[aria-label="Mật khẩu"]').fill('Matkhau123');
    await page.locator('[data-testid="login-submit"]').click();
    await expect(page.locator('[data-testid="user-menu"]')).toContainText('Người Mua Đăng Ký Email');
  });
});
