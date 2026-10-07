// L135–L138 (spec 3.6, 3.10, VIII): the platform voucher form sets audience (member tiers), channel, categories and
// products; the platform Flash Sale slot sets its categories. Checked through the admin screens, then read back by API.
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test admin-vouchers.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, api, apiAs, apiLogin } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

/** First leaf of the category tree: vouchers match a product's own (leaf) category. */
function firstLeaf(nodes) {
  for (const n of nodes) {
    if (!n.children || n.children.length === 0) return n;
    const leaf = firstLeaf(n.children);
    if (leaf) return leaf;
  }
  return null;
}

async function signIn(page) {
  await page.goto(`${BASE}/admin/`);
  await page.getByLabel('Tên đăng nhập').fill(ADMIN_USER);
  await page.getByLabel('Mật khẩu').fill(ADMIN_PASSWORD);
  await page.getByTestId('login-submit').click();
  await expect(page.getByTestId('kpi-orders')).toBeVisible({ timeout: 20_000 });
}

/** Tick a category by name in the open CategoryPicker. */
async function pickCategory(page, dialog, name) {
  await dialog.getByTestId('category-picker').click();
  await page.keyboard.type(name);
  await page.locator('.ant-select-tree-treenode', { hasText: name }).locator('.ant-select-tree-checkbox').first().click();
  // Escape would close the whole modal: click its title to fold the dropdown
  await dialog.locator('.ant-modal-title').click();
}

test.describe('Quản trị: voucher sàn và khung Flash Sale', () => {
  test.skip(!BASE, 'Cần SH_E2E_BASE_URL trỏ vào gateway (vd http://localhost:18000)');
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD');
  test.setTimeout(120_000);

  test('Voucher sàn chọn được hạng thành viên, kênh, ngành hàng; khung Flash Sale chọn được ngành hàng', async ({ page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const leaf = firstLeaf(await api(request, '/categories'));
    expect(leaf).not.toBeNull();

    // Seeded tier vouchers exist (VIII: quyền lợi theo hạng)
    const all = await apiAs(request, admin.accessToken, 'GET', '/admin/vouchers?pageSize=100');
    expect(all.items.map((v) => v.audience)).toEqual(expect.arrayContaining(['MemberGold', 'MemberDiamond']));

    await signIn(page);
    await page.goto(`${BASE}/admin/voucher`);
    await page.getByRole('button', { name: 'Tạo voucher' }).click();
    const dialog = page.getByRole('dialog', { name: 'Tạo voucher của sàn' });
    const code = `HANG${Date.now() % 1_000_000}`;
    await dialog.getByLabel('Mã').fill(code);
    await dialog.getByLabel('Tên').fill('Thử voucher hạng Vàng trên ứng dụng');
    await dialog.getByLabel('Số tiền giảm (₫)').fill('15000');
    await dialog.getByTestId('voucher-audience').click();
    await page.getByTitle('Thành viên Vàng trở lên').click();
    await dialog.getByTestId('voucher-channel').click();
    await page.getByTitle('Chỉ ứng dụng di động').click();
    await pickCategory(page, dialog, leaf.name);
    await dialog.getByRole('button', { name: 'Lưu' }).click();
    await expect(dialog).toBeHidden();

    const saved = (await apiAs(request, admin.accessToken, 'GET', `/admin/vouchers?q=${code}`)).items[0];
    expect(saved.audience).toBe('MemberGold');
    expect(saved.channel).toBe('App');
    expect(saved.categoryIds).toEqual([leaf.id]);

    // Flash Sale slot with a category criterion, on a day far enough not to clash with another slot
    await page.goto(`${BASE}/admin/marketing`);
    await page.getByRole('button', { name: 'Mở khung Flash Sale' }).click();
    const slotDialog = page.getByRole('dialog', { name: 'Mở khung Flash Sale' });
    const day = new Date(Date.now() + (40 + (Date.now() % 300)) * 86_400_000);
    const ddmmyyyy = `${String(day.getDate()).padStart(2, '0')}/${String(day.getMonth() + 1).padStart(2, '0')}/${day.getFullYear()}`;
    await slotDialog.getByLabel('Ngày').fill(ddmmyyyy);
    await page.keyboard.press('Enter');
    await pickCategory(page, slotDialog, leaf.name);
    await slotDialog.getByRole('button', { name: 'Mở khung' }).click();
    await expect(slotDialog).toBeHidden();
    const slots = await apiAs(request, admin.accessToken, 'GET', '/admin/marketing/flash-slots');
    expect(slots.some((s) => s.categoryIds.length === 1 && s.categoryIds[0] === leaf.id)).toBe(true);
  });
});
