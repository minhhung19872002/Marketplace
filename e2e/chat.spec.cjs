// Phase 10 — realtime chat buyer ↔ shop in two browser sessions (spec section 9, scenario 10). Needs the stack and an admin:
//   SH_E2E_BASE_URL=http://localhost:18000 SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... npx playwright test chat.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, apiAs, apiLogin, loginInBrowser, registerViaApi, shopWithProduct } = require('./helpers.cjs');

const ADMIN_USER = process.env.SH_E2E_ADMIN_USER;
const ADMIN_PASSWORD = process.env.SH_E2E_ADMIN_PASSWORD;

test.describe('Chat', () => {
  test.skip(!ADMIN_USER || !ADMIN_PASSWORD, 'Cần SH_E2E_ADMIN_USER / SH_E2E_ADMIN_PASSWORD để duyệt shop');
  test.setTimeout(180_000);

  test('người mua và shop chat thời gian thực: tin nhắn, thẻ sản phẩm, đang gõ, đã xem, câu trả lời nhanh', async ({ browser, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    await apiAs(request, shop.token, 'POST', `/seller/shops/${shop.shopId}/chat/quick-replies`, {
      shortcut: 'conhang', content: 'Dạ sản phẩm vẫn còn hàng ạ, bạn đặt ngay nhé!',
    });
    const buyer = await registerViaApi(request, 'Người Mua Hỏi Chat');

    // Two independent sessions: the buyer on the storefront, the seller in the seller centre
    const buyerPage = await (await browser.newContext()).newPage();
    const sellerPage = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();

    await sellerPage.goto(`${BASE}/seller/`);
    await sellerPage.getByLabel('Tên đăng nhập').fill(shop.seller.phone);
    await sellerPage.getByLabel('Mật khẩu').fill(shop.seller.password);
    await sellerPage.getByTestId('login-submit').click();
    await sellerPage.getByRole('menuitem', { name: 'Chat', exact: true }).click();
    await expect(sellerPage.getByText('Chưa có cuộc trò chuyện nào')).toBeVisible();

    // Buyer: "Chat ngay" on the product page opens the floating window on this shop and offers the product card
    await loginInBrowser(buyerPage, buyer);
    await buyerPage.goto(`${BASE}/san-pham/${shop.productId}`);
    await buyerPage.getByTestId('chat-now').click();
    const thread = buyerPage.getByTestId('chat-thread');
    await expect(thread).toContainText(shop.shopName);
    await buyerPage.getByTestId('chat-send-product').click();
    await expect(buyerPage.getByTestId('chat-product-card')).toContainText(shop.name);
    await buyerPage.getByTestId('chat-input').fill('Shop ơi sản phẩm này còn hàng không?');
    await buyerPage.getByTestId('chat-send').click();

    // Seller: the conversation shows up live (no reload), opening it marks the buyer's messages as read
    const item = sellerPage.getByTestId('inbox-item').filter({ hasText: buyer.fullName });
    await expect(item).toBeVisible({ timeout: 15_000 });
    await item.click();
    const sellerMessages = sellerPage.getByTestId('chat-messages');
    await expect(sellerMessages).toContainText('Shop ơi sản phẩm này còn hàng không?');
    await expect(sellerMessages).toContainText(shop.name);

    // "Đã xem" reaches the buyer in real time
    await expect(buyerPage.getByTestId('chat-seen')).toBeVisible({ timeout: 15_000 });

    // Seller typing → buyer sees "đang soạn tin"; "/" opens the quick replies
    await sellerPage.getByTestId('chat-input').fill('/con');
    await expect(buyerPage.getByTestId('chat-typing')).toBeVisible({ timeout: 15_000 });
    await sellerPage.getByTestId('quick-reply-suggestion').click();
    await sellerPage.getByTestId('chat-send').click();

    // The reply appears on the buyer's side without reloading
    await expect(buyerPage.getByTestId('chat-messages')).toContainText('Dạ sản phẩm vẫn còn hàng ạ', { timeout: 15_000 });
    await expect(buyerPage.getByTestId('chat-typing')).toBeHidden();

    // Contact details outside the platform are delivered with a warning, never blocked
    await buyerPage.getByTestId('chat-input').fill('Cho mình xin zalo 0912345678 nhé');
    await buyerPage.getByTestId('chat-send').click();
    const flagged = buyerPage.getByTestId('chat-message').filter({ hasText: 'zalo 0912345678' });
    await expect(flagged).toContainText('thông tin liên hệ ngoài sàn');
    await expect(sellerMessages.getByTestId('chat-message').filter({ hasText: 'zalo 0912345678' })).toContainText('ngoài sàn', { timeout: 15_000 });

    // The full-page chat lists the same conversation
    await buyerPage.goto(`${BASE}/chat`);
    await expect(buyerPage.getByTestId('chat-conversation').filter({ hasText: shop.shopName })).toBeVisible();
  });

  test('Người mua chặn và báo cáo shop trong chat → quản trị đọc hội thoại, ghi điểm phạt', async ({ browser, page, request }) => {
    const admin = await apiLogin(request, ADMIN_USER, ADMIN_PASSWORD);
    const shop = await shopWithProduct(request, admin);
    const buyer = await registerViaApi(request, 'Người Mua Báo Cáo Chat');

    await loginInBrowser(page, buyer);
    await page.goto(`${BASE}/san-pham/${shop.productId}`);
    await page.getByTestId('chat-now').click();
    await page.getByTestId('chat-input').fill('Shop có bán ngoài sàn rẻ hơn không?');
    await page.getByTestId('chat-send').click();
    await expect(page.getByTestId('chat-message')).toHaveCount(1);
    await page.getByTestId('chat-block').click();
    await expect(page.getByText('Bạn đã chặn shop này')).toBeVisible();
    await page.getByTestId('chat-report').click();
    await page.getByTestId('chat-report-reason').selectOption('Yêu cầu giao dịch / chuyển khoản ngoài sàn');
    await page.getByTestId('chat-report-send').click();
    await expect(page.getByTestId('chat-thread').getByText('Đã gửi báo cáo, sàn sẽ xem xét.')).toBeVisible();

    // The blocked shop can no longer write to the buyer
    const conversations = await apiAs(request, shop.token, 'GET', `/seller/shops/${shop.shopId}/chat/conversations`);
    const conversationId = conversations.items[0].id;
    const res = await request.post(`${BASE}/api/seller/shops/${shop.shopId}/chat/conversations/${conversationId}/messages`,
      { headers: { Authorization: `Bearer ${shop.token}` }, data: { type: 'Text', text: 'Bạn chuyển khoản trước nhé' } });
    expect(res.status()).toBe(409);

    // Admin: the report, the conversation, a penalty
    const adminPage = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();
    await adminPage.goto(`${BASE}/admin/`);
    await adminPage.getByLabel('Tên đăng nhập').fill(ADMIN_USER);
    await adminPage.getByLabel('Mật khẩu').fill(ADMIN_PASSWORD);
    await adminPage.getByTestId('login-submit').click();
    await adminPage.getByRole('menuitem', { name: 'Chat bị báo cáo' }).click();
    await adminPage.getByRole('row', { name: new RegExp(shop.shopName) }).getByTestId('chat-report-open').click();
    await expect(adminPage.getByTestId('chat-review')).toContainText('Shop có bán ngoài sàn rẻ hơn không?');
    await adminPage.getByTestId('chat-report-resolution').fill('Không thấy shop mời giao dịch ngoài — nhắc nhở');
    await adminPage.getByTestId('chat-report-penalize').click();
    await adminPage.getByTestId('chat-report-resolve').click();
    await expect(adminPage.getByText('Đã ghi 2 điểm phạt cho shop.')).toBeVisible();
    const penalties = await apiAs(request, admin.accessToken, 'GET', `/admin/shops/${shop.shopId}/penalties`);
    expect(penalties.status.points).toBe(2);
  });
});
