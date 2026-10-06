const { test, expect } = require('@playwright/test');

const BASE = process.env.SH_E2E_BASE_URL || 'http://localhost:5173';

// Unique VN mobile number per test run
let phoneSeq = Date.now() % 100000000;
const newPhone = () => `09${String(++phoneSeq % 100000000).padStart(8, '0')}`;

// Newest OTP texted by the simulated SMS provider (dev-only inbox)
async function latestOtp(request, phone) {
  for (let i = 0; i < 40; i++) {
    const res = await request.get(`${BASE}/api/dev/sms?to=${phone}`);
    const body = await res.json();
    const text = body.data?.[0]?.content;
    const code = text && text.match(/\b\d{6}\b/);
    if (code) return code[0];
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`Không nhận được OTP cho ${phone}`);
}

// Real account through the public API: OTP → ticket → register
async function registerViaApi(request, fullName = 'Khách Thử E2E') {
  const phone = newPhone();
  const password = 'Matkhau123';
  const sent = await request.post(`${BASE}/api/auth/otp/send`, { data: { target: phone, purpose: 'Register' } });
  if (!sent.ok()) throw new Error(`Gửi OTP thất bại (${sent.status()}): ${await sent.text()}`);
  const code = await latestOtp(request, phone);
  const verify = await (await request.post(`${BASE}/api/auth/otp/verify`, { data: { target: phone, purpose: 'Register', code } })).json();
  const res = await request.post(`${BASE}/api/auth/register`, {
    data: { target: phone, ticket: verify.data.ticket, password, fullName, acceptTerms: true },
  });
  if (!res.ok()) throw new Error(`Đăng ký thất bại: ${await res.text()}`);
  return { phone, password, fullName };
}

const filterRealErrors = (errors) =>
  errors.filter(
    (e) =>
      !e.includes('Warning') &&
      !e.includes('warn') &&
      !e.includes('Failed to load resource') &&
      !e.includes('favicon') &&
      !e.includes('loremflickr') &&
      !e.includes('dummyjson')
  );

test.describe('ShopHub Marketplace', () => {
  test('Trang chủ load không lỗi và hiển thị đủ các section', async ({ page }) => {
    const errors = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') errors.push(msg.text());
    });

    await page.goto(BASE);
    await page.waitForLoadState('networkidle');

    await expect(page.locator('header')).toBeVisible();
    await expect(page.locator('.header-logo-text')).toContainText('ShopHub');
    await expect(page.locator('.banner')).toBeVisible();
    await expect(page.locator('.feature-shortcuts')).toBeVisible();
    await expect(page.locator('.category-grid-section')).toBeVisible();
    await expect(page.locator('.flash-sale')).toBeVisible();
    await expect(page.locator('.mall-brands')).toBeVisible();
    await expect(page.locator('.product-grid-section')).toBeVisible();

    expect(filterRealErrors(errors)).toHaveLength(0);
  });

  test('Các section trang chủ có đủ dữ liệu', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');

    await expect(page.locator('[data-testid="feature-shortcut"]')).toHaveCount(10);
    await expect(page.locator('[data-testid="category-item"]')).toHaveCount(18);
    await expect(page.locator('[data-testid="flash-item"]')).toHaveCount(8);
    await expect(page.locator('[data-testid="mall-brand"]')).toHaveCount(6);
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(24);
  });

  test('Nút Xem Thêm tải thêm sản phẩm', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(24);
    await page.locator('[data-testid="load-more"]').click();
    const count = await page.locator('[data-testid="product-card"]').count();
    expect(count).toBeGreaterThan(24);
  });

  test('Flash sale có bộ đếm ngược 3 ô', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.flash-sale-countdown .countdown-box')).toHaveCount(3);
  });

  test('Gợi ý tìm kiếm hiển thị khi gõ từ khóa', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await page.locator('.header-search-input').fill('áo');
    await expect(page.locator('[data-testid="search-suggest"]')).toBeVisible();
    await expect(page.locator('.header-suggest-item').first()).toBeVisible();
  });

  test('Click sản phẩm mở trang chi tiết đầy đủ', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="product-card"]').first().click();
    await page.waitForURL(/\/san-pham\/\d+/);

    await expect(page.locator('.product-detail-name')).toBeVisible();
    await expect(page.locator('.price-current')).toBeVisible();
    await expect(page.locator('.btn-add-cart')).toBeVisible();
    await expect(page.locator('.btn-buy-now')).toBeVisible();
    await expect(page.locator('.pd-shop')).toBeVisible();
    await expect(page.locator('.pd-specs')).toBeVisible();
    await expect(page.locator('[data-testid="reviews"]')).toBeVisible();
  });

  test('Bắt buộc chọn phân loại trước khi thêm giỏ (SP #1 có biến thể)', async ({ page }) => {
    await page.goto(`${BASE}/san-pham/1`);
    await page.waitForLoadState('networkidle');

    await expect(page.locator('[data-testid="variant-option"]').first()).toBeVisible();
    await page.locator('[data-testid="add-to-cart"]').click();
    await expect(page.locator('[data-testid="variant-error"]')).toBeVisible();
    await expect(page.locator('.header-cart-badge')).toHaveCount(0);

    await page.locator('[data-testid="variant-option"]').first().click();
    await page.locator('[data-testid="add-to-cart"]').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');
  });

  test('Đăng nhập cập nhật trạng thái header', async ({ page, request }) => {
    const account = await registerViaApi(request, 'Nguyễn Văn E2E');
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');

    await page.locator('[data-testid="login-link"]').click();
    await page.waitForURL(/\/dang-nhap/);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(account.phone);
    await page.locator('input[aria-label="Mật khẩu"]').fill(account.password);
    await page.locator('[data-testid="login-submit"]').click();

    await page.waitForURL(BASE + '/');
    await expect(page.locator('[data-testid="user-menu"]')).toContainText('Nguyễn Văn E2E');

    // Session survives a reload (refresh token cookie)
    await page.reload();
    await expect(page.locator('[data-testid="user-menu"]')).toContainText('Nguyễn Văn E2E');
  });

  test('Sai mật khẩu bị từ chối', async ({ page, request }) => {
    const account = await registerViaApi(request);
    await page.goto(`${BASE}/dang-nhap`);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(account.phone);
    await page.locator('input[aria-label="Mật khẩu"]').fill('SaiMatKhau9');
    await page.locator('[data-testid="login-submit"]').click();

    await expect(page.locator('[data-testid="auth-error"]')).toHaveText('Thông tin đăng nhập không đúng.');
    await expect(page.locator('[data-testid="login-link"]')).toBeVisible();
  });

  test('Đăng ký bằng SĐT + OTP → đăng nhập → thêm địa chỉ', async ({ page, request }) => {
    const phone = newPhone();
    await page.goto(`${BASE}/dang-ky`);
    await page.locator('input[aria-label="Số điện thoại"]').fill(phone);
    await page.locator('[data-testid="register-send-otp"]').click();
    const code = await latestOtp(request, phone);
    await page.locator('input[aria-label="Mã xác thực"]').fill(code);
    await page.locator('[data-testid="register-verify"]').click();
    await page.locator('input[aria-label="Họ và tên"]').fill('Trần Thị Đăng Ký');
    await page.locator('input[aria-label="Mật khẩu"]').fill('Matkhau123');
    await page.locator('input[aria-label="Đồng ý điều khoản"]').check();
    await page.locator('[data-testid="register-submit"]').click();

    await page.waitForURL(BASE + '/');
    await expect(page.locator('[data-testid="user-menu"]')).toContainText('Trần Thị Đăng Ký');

    // Log out, log back in with the new password
    await page.locator('[data-testid="user-menu"]').click();
    await page.locator('[data-testid="logout"]').click();
    await expect(page.locator('[data-testid="login-link"]')).toBeVisible();
    await page.goto(`${BASE}/dang-nhap`);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(phone);
    await page.locator('input[aria-label="Mật khẩu"]').fill('Matkhau123');
    await page.locator('[data-testid="login-submit"]').click();
    await page.waitForURL(BASE + '/');

    // Address book: Tỉnh → Quận → Phường
    await page.goto(`${BASE}/tai-khoan/dia-chi`);
    await page.locator('[data-testid="address-add"]').click();
    await page.locator('input[aria-label="Tên người nhận"]').fill('Trần Thị Đăng Ký');
    await page.locator('input[aria-label="Số điện thoại người nhận"]').fill(phone);
    await page.locator('select[aria-label="Tỉnh/Thành phố"]').selectOption({ label: 'Thành phố Hà Nội' });
    await page.locator('select[aria-label="Quận/Huyện"]').selectOption({ label: 'Quận Ba Đình' });
    await page.locator('select[aria-label="Phường/Xã"]').selectOption({ label: 'Phường Phúc Xá' });
    await page.locator('input[aria-label="Địa chỉ cụ thể"]').fill('12 Phố Thử');
    await page.locator('[data-testid="address-save"]').click();

    const item = page.locator('[data-testid="address-item"]');
    await expect(item).toHaveCount(1);
    await expect(item).toContainText('Phường Phúc Xá, Quận Ba Đình, Thành phố Hà Nội');
    await expect(item).toContainText('Mặc định');
  });

  test('Yêu thích: tim trên thẻ sản phẩm cập nhật danh sách', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');

    await page.locator('[data-testid="card-heart"]').first().click();
    await expect(page.locator('[data-testid="wishlist-link"] .header-cart-badge')).toHaveText('1');

    await page.locator('[data-testid="wishlist-link"]').click();
    await page.waitForURL(/\/yeu-thich/);
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(1);
  });

  test('Giỏ hàng: checkbox chọn item quyết định tổng tiền', async ({ page }) => {
    await page.goto(`${BASE}/san-pham/1`);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="variant-option"]').first().click();
    await page.locator('[data-testid="add-to-cart"]').click();

    await page.goto(`${BASE}/san-pham/9`);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="add-to-cart"]').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('2');

    await page.goto(`${BASE}/gio-hang`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="cart-item"]')).toHaveCount(2);

    const totalAll = await page.locator('[data-testid="cart-total"]').textContent();
    expect(totalAll).not.toBe('₫0');

    await page.locator('[data-testid="select-all"]').uncheck();
    await expect(page.locator('[data-testid="cart-total"]')).toHaveText('₫0');

    await page.locator('[data-testid="select-item"]').first().check();
    await expect(page.locator('[data-testid="cart-total"]')).not.toHaveText('₫0');
  });

  test('Luồng thanh toán: địa chỉ + voucher + đặt hàng thành công', async ({ page }) => {
    await page.goto(`${BASE}/san-pham/9`); // không biến thể
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="add-to-cart"]').click();

    await page.goto(`${BASE}/gio-hang`);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="checkout"]').click();
    await page.waitForURL(/\/thanh-toan/);

    await expect(page.locator('[data-testid="checkout-item"]')).toHaveCount(1);

    // Áp voucher hợp lệ
    await page.locator('input[aria-label="Mã giảm giá"]').fill('SHOPHUB50');
    await page.locator('[data-testid="apply-voucher"]').click();
    await expect(page.locator('[data-testid="voucher-msg"]')).toContainText('SHOPHUB50');

    // Thiếu địa chỉ -> báo lỗi
    await page.locator('[data-testid="place-order"]').click();
    await expect(page.locator('[data-testid="checkout-error"]')).toBeVisible();

    // Nhập địa chỉ rồi đặt hàng
    await page.locator('input[aria-label="Họ và tên"]').fill('Nguyễn Văn A');
    await page.locator('input[aria-label="Số điện thoại"]').fill('0901234567');
    await page.locator('input[aria-label="Địa chỉ"]').fill('123 Lê Lợi, Q1, TP.HCM');
    await page.locator('[data-testid="place-order"]').click();

    await page.waitForURL(/\/dat-hang-thanh-cong/);
    await expect(page.locator('[data-testid="order-success"]')).toBeVisible();
    // Giỏ hàng đã được xóa sau khi đặt
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveCount(0);
  });

  test('Cập nhật số lượng và xóa trong giỏ hàng', async ({ page }) => {
    await page.goto(`${BASE}/san-pham/9`);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="add-to-cart"]').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');

    await page.goto(`${BASE}/gio-hang`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="cart-item"]')).toHaveCount(1);

    await page.locator('.cart-item-qty button[aria-label="Tăng"]').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('2');

    await page.locator('.cart-item-remove').click();
    await expect(page.locator('[data-testid="cart-empty"]')).toBeVisible();
  });

  test('Giỏ hàng trống hiển thị đúng trạng thái', async ({ page }) => {
    await page.goto(`${BASE}/gio-hang`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="cart-empty"]')).toBeVisible();
    await expect(page.locator('.cart-empty-btn')).toBeVisible();
  });

  test('Tìm kiếm không dấu lọc ra kết quả đúng', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');

    await page.locator('.header-search-input').fill('áo');
    await page.locator('.header-search-btn').click();
    await page.waitForURL(/\/tim-kiem/);

    await expect(page.locator('[data-testid="search-heading"]')).toContainText('áo');
    const count = await page.locator('[data-testid="product-card"]').count();
    expect(count).toBeGreaterThan(0);

    const stripTones = (s) =>
      s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').toLowerCase();
    const names = await page.locator('.product-card-name').allTextContents();
    for (const name of names) {
      expect(stripTones(name)).toContain('ao');
    }
  });

  test('Sidebar lọc theo đánh giá', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="filter-sidebar"]')).toBeVisible();

    const before = await page.locator('[data-testid="product-card"]').count();
    await page.locator('[data-testid="filter-rating"]').first().click();
    await page.waitForTimeout(200);
    const after = await page.locator('[data-testid="product-card"]').count();
    expect(after).toBeLessThanOrEqual(before);

    await page.locator('[data-testid="filter-clear"]').click();
    await page.waitForTimeout(200);
    const cleared = await page.locator('[data-testid="product-card"]').count();
    expect(cleared).toBeGreaterThanOrEqual(after);
  });

  test('Sắp xếp theo giá tăng dần hoạt động', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem`);
    await page.waitForLoadState('networkidle');

    await page.locator('.search-sort-price').click();
    await page.waitForTimeout(200);

    const priceTexts = await page.locator('.product-card-price').allTextContents();
    const prices = priceTexts.map((t) => Number(t.replace(/[^\d]/g, '')));
    const sorted = [...prices].sort((a, b) => a - b);
    expect(prices).toEqual(sorted);
  });

  test('Click danh mục điều hướng tới trang lọc', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="category-item"]').first().click();
    await page.waitForURL(/\/tim-kiem\?category=/);
    await expect(page.locator('[data-testid="search-heading"]')).toContainText('Danh mục');
  });

  test('Trang thông báo và trang Shop hiển thị', async ({ page }) => {
    await page.goto(`${BASE}/thong-bao`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="noti-list"]')).toBeVisible();
    expect(await page.locator('[data-testid="noti-item"]').count()).toBeGreaterThan(0);

    await page.goto(`${BASE}/shop`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.shop-banner')).toBeVisible();
    expect(await page.locator('[data-testid="product-card"]').count()).toBeGreaterThan(0);
  });

  test('Footer hiển thị đầy đủ', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.footer')).toBeVisible();
    await expect(page.locator('.footer-col')).toHaveCount(4);
  });
});
