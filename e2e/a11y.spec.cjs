// Phase 13 — WCAG 2.1 AA (spec 6.5): axe on the main pages of the three apps; serious / critical findings fail.
//   SH_E2E_BASE_URL=http://localhost:18000 npx playwright test a11y.spec.cjs
const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;
const { BASE, findProduct, withoutTiers } = require('./helpers.cjs');

const audit = async (page) => {
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  return result.violations
    .filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id} (${v.impact}): ${v.nodes.slice(0, 3).map((n) => n.target.join(' ')).join(' | ')}`);
};

test.describe('Tiếp cận (WCAG AA)', () => {
  test.setTimeout(180_000);

  test('Trang người mua không có lỗi tiếp cận nghiêm trọng', async ({ page, request }) => {
    const product = await findProduct(request, withoutTiers);
    const problems = [];
    for (const path of ['/', `/san-pham/${product.id}`, '/tim-kiem?q=ao', '/gio-hang', '/dang-nhap', '/dang-ky', '/tro-giup', '/trang/chinh-sach-bao-mat']) {
      await page.goto(`${BASE}${path}`);
      await page.waitForLoadState('networkidle');
      for (const v of await audit(page)) problems.push(`${path} → ${v}`);
    }
    expect(problems).toEqual([]);
  });

  test('Màn đăng nhập Kênh Người Bán và Quản trị không có lỗi tiếp cận nghiêm trọng', async ({ page }) => {
    const problems = [];
    for (const path of ['/seller/', '/admin/']) {
      await page.goto(`${BASE}${path}`);
      await page.waitForLoadState('networkidle');
      for (const v of await audit(page)) problems.push(`${path} → ${v}`);
    }
    expect(problems).toEqual([]);
  });

  test('Đi được bằng bàn phím: Tab tới ô tìm kiếm và có viền focus', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    let reached = false;
    for (let i = 0; i < 30 && !reached; i++) {
      await page.keyboard.press('Tab');
      reached = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') === 'Tìm kiếm sản phẩm');
    }
    expect(reached).toBeTruthy();
    const outline = await page.evaluate(() => {
      const s = getComputedStyle(document.activeElement);
      return s.outlineStyle !== 'none' || s.boxShadow !== 'none';
    });
    expect(outline).toBeTruthy();
  });
});
