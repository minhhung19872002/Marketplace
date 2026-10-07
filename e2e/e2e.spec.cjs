const { test, expect } = require('@playwright/test');

const {
  BASE, newPhone, latestOtp, registerViaApi, api, findProduct, withTiers, withoutTiers, loginInBrowser, stripTones, addAddressViaApi,
} = require('./helpers.cjs');

const filterRealErrors = (errors) =>
  errors.filter((e) => !e.includes('Warning') && !e.includes('warn') && !e.includes('Failed to load resource') && !e.includes('favicon'));

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
    await expect(page.locator('.mall-brands')).toBeVisible();
    await expect(page.locator('.top-categories')).toBeVisible();
    await expect(page.locator('.product-grid-section')).toBeVisible();
    // Flash Sale of the platform: shown exactly when a slot runs now (the seeded one lasts until the next 3-hour mark,
    // a long-lived stack may be between slots), real items, countdown on the server's clock
    const board = (await (await page.request.get(`${BASE}/api/flash-sale`)).json()).data;
    if (board.slot && board.items.length > 0) {
      await expect(page.getByTestId('flash-sale')).toBeVisible();
      await expect(page.getByTestId('flash-item').first()).toBeVisible();
      await expect(page.getByTestId('countdown').first()).toHaveText(/\d{2}:\d{2}:\d{2}/);
    } else {
      await expect(page.getByTestId('flash-sale')).toHaveCount(0);
    }

    expect(filterRealErrors(errors)).toHaveLength(0);
  });

  test('Các section trang chủ lấy dữ liệu thật từ API', async ({ page, request }) => {
    const tree = await api(request, '/categories');
    const mall = await api(request, '/home/mall');
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');

    await expect(page.locator('[data-testid="feature-shortcut"]')).toHaveCount(10);
    await expect(page.locator('[data-testid="category-item"]')).toHaveCount(tree.filter((c) => c.isActive).length);
    await expect(page.locator('[data-testid="mall-brand"]')).toHaveCount(mall.length);
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(24);
    await expect(page.locator('[data-testid="category-item"]').first()).toContainText(tree[0].name);
  });

  test('Nút Xem Thêm tải thêm sản phẩm gợi ý', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(24);
    await page.locator('[data-testid="load-more"]').click();
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(48);
  });

  test('Gợi ý tìm kiếm từ máy chủ khi gõ không dấu', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await page.locator('.header-search-input').fill('ao thun');
    await expect(page.locator('[data-testid="search-suggest"]')).toBeVisible();
    const first = page.locator('[data-testid="suggest-product"]').first();
    await expect(first).toBeVisible();
    expect(stripTones(await first.textContent())).toContain('ao thun');
  });

  test('Click sản phẩm mở trang chi tiết đầy đủ', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    const name = (await page.locator('[data-testid="product-card-name"]').first().textContent()).replace('Yêu thích', '').trim();
    await page.locator('[data-testid="product-card"]').first().click();
    await page.waitForURL(/\/san-pham\/.+-i\.[0-9a-f-]{36}\.[0-9a-f-]{36}$/);

    await expect(page.locator('[data-testid="pd-name"]')).toContainText(name);
    await expect(page.locator('.price-current')).toBeVisible();
    await expect(page.locator('.btn-add-cart')).toBeVisible();
    await expect(page.locator('.btn-buy-now')).toBeVisible();
    await expect(page.locator('[data-testid="pd-shop"]')).toBeVisible();
    await expect(page.locator('[data-testid="pd-specs"]')).toBeVisible();
    await expect(page.locator('[data-testid="product-breadcrumb"] a')).toHaveCount(4); // Trang chủ + 3 cấp danh mục
    await expect(page.locator('[data-testid="reviews"]')).toBeVisible();
  });

  test('Bắt buộc chọn phân loại; phân loại hết hàng bị mờ', async ({ page, request }) => {
    const product = await findProduct(request, withTiers);
    await page.goto(`${BASE}/san-pham/${product.id}`);
    await page.waitForLoadState('networkidle');

    await page.locator('[data-testid="add-to-cart"]').click();
    await expect(page.locator('[data-testid="variant-error"]')).toBeVisible();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveCount(0);

    const options = page.locator('[data-testid="variant-option"]');
    const soldOut = product.tiers[0].options.filter((o) => !o.available).map((o) => o.value);
    for (const value of soldOut) await expect(options.filter({ hasText: value }).first()).toBeDisabled();

    const sku = product.skus.find((s) => s.available > 2);
    await options.filter({ hasText: sku.option1 }).first().click();
    if (sku.option2) await options.filter({ hasText: sku.option2 }).last().click();
    await expect(page.locator('[data-testid="pd-stock"]')).toContainText(`${sku.available} sản phẩm có sẵn`);
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

  test('Yêu thích: khách bị chuyển tới đăng nhập, người mua lưu trên máy chủ', async ({ page, request }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="card-heart"]').first().click();
    await page.waitForURL(/\/dang-nhap/);

    const account = await registerViaApi(request, 'Người Thích Hàng');
    await loginInBrowser(page, account);
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/account/wishlist/') && r.request().method() === 'POST' && r.ok()),
      page.locator('[data-testid="card-heart"]').first().click(),
    ]);
    await expect(page.locator('[data-testid="wishlist-link"] .header-cart-badge')).toHaveText('1', { timeout: 10_000 });

    // Survives a reload: it is on the server, not in localStorage
    await page.reload();
    await expect(page.locator('[data-testid="wishlist-link"] .header-cart-badge')).toHaveText('1');
    await page.locator('[data-testid="wishlist-link"]').click();
    await page.waitForURL(/\/yeu-thich/);
    await expect(page.locator('[data-testid="product-card"]')).toHaveCount(1);
  });

  test('Giỏ hàng: checkbox chọn item quyết định tổng tiền', async ({ page, request }) => {
    const a = await findProduct(request, withTiers);
    const b = await findProduct(request, withoutTiers);
    const skuA = a.skus.find((s) => s.available > 2);

    await page.goto(`${BASE}/san-pham/${a.id}`);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="variant-option"]').filter({ hasText: skuA.option1 }).first().click();
    if (skuA.option2) await page.locator('[data-testid="variant-option"]').filter({ hasText: skuA.option2 }).last().click();
    await page.locator('[data-testid="add-to-cart"]').click();
    // The cart is on the server: wait for the answer before navigating away (that would abort the request)
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');

    await page.goto(`${BASE}/san-pham/${b.id}`);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="add-to-cart"]').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('2');

    await page.goto(`${BASE}/gio-hang`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="cart-item"]')).toHaveCount(2);
    const expected = skuA.price + b.skus[0].price;
    await expect(page.locator('[data-testid="cart-total"]')).toHaveText(`₫${expected.toLocaleString('vi-VN')}`);

    // The boxes are driven by the cart state (updated a few ms after the click): click, then wait for the state —
    // .check() / .uncheck() read it back at once and fail whenever the re-render is a little slower
    await page.locator('[data-testid="select-all"]').click();
    await expect(page.locator('[data-testid="select-all"]')).not.toBeChecked();
    await expect(page.locator('[data-testid="cart-total"]')).toHaveText('₫0');

    await page.locator('[data-testid="select-item"]').first().click();
    await expect(page.locator('[data-testid="select-item"]').first()).toBeChecked();
    await expect(page.locator('[data-testid="cart-total"]')).not.toHaveText('₫0');
  });

  test('Thanh toán cần đăng nhập; COD tạo đơn thật có mã đơn', async ({ page, request }) => {
    const product = await findProduct(request, withoutTiers);
    const account = await registerViaApi(request, 'Người Mua COD');
    await addAddressViaApi(request, account);

    await page.goto(`${BASE}/san-pham/${product.id}`);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="add-to-cart"]').click();
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveText('1');
    await page.goto(`${BASE}/gio-hang`);
    await page.locator('[data-testid="checkout"]').click();

    // Guests are asked to sign in first; the guest cart follows them into the account
    await page.waitForURL(/\/dang-nhap/);
    await page.locator('input[aria-label="Tên đăng nhập"]').fill(account.phone);
    await page.locator('input[aria-label="Mật khẩu"]').fill(account.password);
    await page.locator('[data-testid="login-submit"]').click();
    await page.waitForURL(/\/thanh-toan/);

    await expect(page.locator('[data-testid="checkout-item"]')).toHaveCount(1);
    await expect(page.locator('[data-testid="checkout-address"]')).toContainText('Người Nhận E2E');
    const total = await page.locator('[data-testid="checkout-total"]').textContent();
    await page.locator('[data-testid="place-order"]').click();

    await page.waitForURL(/\/dat-hang-thanh-cong\?checkout=/);
    await expect(page.locator('[data-testid="order-success"]')).toBeVisible();
    await expect(page.locator('[data-testid="result-order"]')).toHaveCount(1);
    await expect(page.locator('[data-testid="success-total"]')).toHaveText(total);
    await expect(page.locator('.header-cart .header-cart-badge')).toHaveCount(0);

    await page.locator('[data-testid="view-orders"]').click();
    await expect(page.locator('[data-testid="order-card"]').first()).toBeVisible();
    await expect(page.locator('[data-testid="order-status"]').first()).toHaveText('Chờ xác nhận');
  });

  test('Cập nhật số lượng và xóa trong giỏ hàng', async ({ page, request }) => {
    const product = await findProduct(request, withoutTiers);
    await page.goto(`${BASE}/san-pham/${product.id}`);
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

  test('Tìm "dien thoai" (không dấu) ra "Điện Thoại…"', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await page.locator('.header-search-input').fill('dien thoai');
    await page.locator('.header-search-btn').click();
    await page.waitForURL(/\/tim-kiem\?q=dien/);

    await expect(page.locator('[data-testid="search-heading"]')).toContainText('dien thoai');
    await expect(page.locator('[data-testid="product-card"]').first()).toBeVisible();
    const names = await page.locator('[data-testid="product-card-name"]').allTextContents();
    expect(names.length).toBeGreaterThan(0);
    // Name matches rank first; accessories of "Điện Thoại & Phụ Kiện" follow (category-name match)
    for (const name of names.slice(0, 5)) expect(name).toContain('Điện Thoại');
  });

  test('Số trên facet khớp số kết quả khi bấm lọc', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem?q=ao`);
    await page.waitForLoadState('networkidle');
    const option = page.locator('[data-testid="facet-provinces"] label').first();
    await expect(option).toBeVisible();
    const facetCount = Number((await option.locator('.filter-count').textContent()).replace(/[^\d]/g, ''));

    await option.locator('input').check();
    await expect(page).toHaveURL(/provinces=/);
    await expect(page.locator('[data-testid="search-count"]')).toHaveText(`${facetCount.toLocaleString('vi-VN')} sản phẩm`);
  });

  test('Khoảng giá sai báo lỗi rõ ràng', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem`);
    await page.waitForLoadState('networkidle');
    await page.locator('input[aria-label="Giá từ"]').fill('500000');
    await page.locator('input[aria-label="Giá đến"]').fill('100000');
    await page.locator('[data-testid="price-apply"]').click();
    await expect(page.locator('[data-testid="price-error"]')).toContainText('Khoảng giá không hợp lệ');
  });

  test('Sidebar lọc theo đánh giá và xoá bộ lọc', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="filter-sidebar"]')).toBeVisible();

    await page.locator('[data-testid="filter-rating"]').first().click();
    await expect(page).toHaveURL(/minRating=5/);
    await page.locator('[data-testid="filter-clear"]').click();
    await expect(page).not.toHaveURL(/minRating/);
    await expect(page.locator('[data-testid="product-card"]').first()).toBeVisible();
  });

  test('Sắp xếp theo giá tăng dần hoạt động', async ({ page }) => {
    await page.goto(`${BASE}/tim-kiem`);
    await page.waitForLoadState('networkidle');

    // The grid keeps the previous results on screen while the sorted page loads: wait for the sorted response, then read
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/search/products') && r.url().includes('sort=PriceAsc') && r.ok()),
      page.locator('[data-testid="sort-price"]').click(),
    ]);
    await expect(page).toHaveURL(/sort=PriceAsc/);
    await expect(async () => {
      const priceTexts = await page.locator('[data-testid="product-card-price"]').allTextContents();
      const prices = priceTexts.map((t) => Number(t.replace(/[^\d]/g, '')));
      expect(prices.length).toBeGreaterThan(1);
      expect(prices).toEqual([...prices].sort((x, y) => x - y));
    }).toPass({ timeout: 5_000 });
  });

  test('Sắp xếp theo giá vẫn đúng khi có từ khoá', async ({ page }) => {
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/search/products') && r.url().includes('sort=PriceDesc') && r.ok()),
      page.goto(`${BASE}/tim-kiem?q=dien+thoai&sort=PriceDesc`),
    ]);
    await page.waitForLoadState('networkidle');
    const prices = (await page.locator('[data-testid="product-card-price"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
    expect(prices.length).toBeGreaterThan(1);
    expect(prices).toEqual([...prices].sort((x, y) => y - x));
  });

  test('Click danh mục mở trang danh mục có breadcrumb', async ({ page, request }) => {
    const tree = await api(request, '/categories');
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await page.locator('[data-testid="category-item"]').first().click();
    await page.waitForURL(new RegExp(`/danh-muc/${tree[0].slug}$`));
    await expect(page.locator('[data-testid="search-heading"]')).toHaveText(tree[0].name);
    await expect(page.locator('[data-testid="category-children"] a')).toHaveCount(tree[0].children.filter((c) => c.isActive).length);
    await expect(page.locator('[data-testid="product-card"]').first()).toBeVisible();
  });

  test('Trang Shop: theo dõi tăng số người theo dõi', async ({ page, request }) => {
    const [first] = await api(request, '/home/mall');
    await page.goto(`${BASE}/shop/${first.slug}`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="shop-name"]')).toContainText(first.name);
    await expect(page.locator('[data-testid="product-card"]').first()).toBeVisible();

    const account = await registerViaApi(request, 'Người Theo Dõi');
    await loginInBrowser(page, account);
    await page.goto(`${BASE}/shop/${first.slug}`);
    const before = Number((await page.locator('[data-testid="shop-followers"]').textContent()).replace(/[^\d]/g, ''));
    await page.locator('[data-testid="shop-follow"]').click();
    await expect(page.locator('[data-testid="shop-follow"]')).toContainText('Đang Theo Dõi');
    await expect(page.locator('[data-testid="shop-followers"]')).toHaveText(String(before + 1));
  });

  test('Trang thông báo cần đăng nhập và hiển thị thông báo thật', async ({ page, request }) => {
    await page.goto(`${BASE}/thong-bao`);
    await page.waitForURL(/\/dang-nhap/);
    const account = await registerViaApi(request, 'Người Xem Thông Báo');
    await loginInBrowser(page, account);
    await page.goto(`${BASE}/thong-bao`);
    await expect(page.locator('[data-testid="noti-list"]')).toBeVisible();
    await expect(page.locator('[data-testid="noti-empty"]')).toBeVisible();
  });

  test('Popup quảng cáo hiện cho khách mới, đóng được và không hiện lại khi tải lại', async ({ browser }) => {
    // A fresh browser (no remembered popup), unlike the returning visitor of the other tests (config storageState)
    const page = await (await browser.newContext({ storageState: { cookies: [], origins: [] } })).newPage();
    await page.goto(BASE);
    await expect(page.getByTestId('home-popup')).toBeVisible();
    await page.getByRole('button', { name: 'Đóng' }).click();
    await expect(page.getByTestId('home-popup')).toHaveCount(0);
    await page.reload();
    await expect(page.getByTestId('home-banners')).toBeVisible();
    await expect(page.getByTestId('home-popup')).toHaveCount(0);
  });

  test('Footer hiển thị đầy đủ', async ({ page }) => {
    await page.goto(BASE);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.footer')).toBeVisible();
    await expect(page.locator('.footer-col')).toHaveCount(4);
  });
});
