// Phase 14 D3 — a rejected shop sees why, sends its papers again and waits for the review; until it is active the
// selling menu stays closed. Needs the stack and an admin:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test shop-review.spec.cjs
const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');
const { BASE, apiAs, apiLogin, registerViaApi } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;
const SAMPLE_PNG = path.join(__dirname, 'fixtures', 'sample.png');

test.describe('Duyệt hồ sơ bán hàng', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');

  test('Shop bị từ chối thấy lý do, gửi lại hồ sơ rồi chờ duyệt; chưa hoạt động thì không có menu bán hàng', async ({ page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const seller = await registerViaApi(request, 'Người Bán Bị Từ Chối');
    const login = await apiLogin(request, seller.phone, seller.password);
    const upload = async () => {
      const res = await request.post(`${BASE}/api/media/kyc`, {
        headers: { Authorization: `Bearer ${login.accessToken}` },
        multipart: { file: { name: 'sample.png', mimeType: 'image/png', buffer: fs.readFileSync(SAMPLE_PNG) } },
      });
      return (await res.json()).data;
    };
    const [front, back] = [await upload(), await upload()];
    const shopId = await apiAs(request, login.accessToken, 'POST', '/seller/shops', {
      name: `Shop Từ Chối ${seller.phone.slice(-6)}`, type: 'Personal', description: 'Shop kiểm thử duyệt hồ sơ',
      warehouse: { contactName: 'Kho', phone: seller.phone, provinceCode: '79', districtCode: '760', wardCode: '26734', street: '1 Nguyễn Huệ' },
      personal: { legalName: 'Người Bán Bị Từ Chối', idCardNumber: '079200012345', frontAssetId: front.id, backAssetId: back.id },
      bank: { bankCode: 'VCB', accountNo: '0011002233445', accountName: 'NGUOI BAN BI TU CHOI' },
    });
    await apiAs(request, admin.accessToken, 'POST', `/admin/shops/${shopId}/reject`, { reason: 'Ảnh CCCD bị mờ, vui lòng chụp lại rõ nét' });

    await page.setViewportSize({ width: 1366, height: 768 });
    await page.goto(`${BASE}/seller/`);
    await page.getByLabel('Tên đăng nhập').fill(seller.phone);
    await page.getByLabel('Mật khẩu').fill(seller.password);
    await page.getByTestId('login-submit').click();
    await expect(page.getByTestId('shop-rejected')).toBeVisible();
    await expect(page.getByTestId('reject-reason')).toContainText('Ảnh CCCD bị mờ');
    await expect(page.getByRole('menuitem', { name: 'Sản phẩm', exact: true })).toHaveCount(0);

    await page.getByLabel('Họ tên trên CCCD').fill('Người Bán Bị Từ Chối');
    await page.getByLabel('Số CCCD (12 số)').fill('079200054321');
    await page.locator('[data-testid="kyc-front"] input[type="file"]').setInputFiles(SAMPLE_PNG);
    await page.locator('[data-testid="kyc-back"] input[type="file"]').setInputFiles(SAMPLE_PNG);
    await expect(page.getByTestId('resubmit-shop')).toBeEnabled();
    await page.getByTestId('resubmit-shop').click();
    await expect(page.getByTestId('shop-pending')).toBeVisible();
    await expect(page.getByRole('menuitem', { name: 'Sản phẩm', exact: true })).toHaveCount(0);
  });
});
