#!/usr/bin/env node
// G1 (Phase 14): one real round with a provider's SANDBOX keys — pay → IPN → refund, and a parcel → carrier webhook.
// It only reports what it observed: a step it could not see happen is "CHƯA THẤY", never "OK". Nothing is faked.
//
//   SH_SANDBOX_BASE_URL=https://<public address of the gateway> \
//   SH_E2E_ADMIN_USER=... SH_E2E_ADMIN_PASSWORD=... \
//   SH_SANDBOX_BUYER=<phone/email> SH_SANDBOX_BUYER_PASSWORD=... \
//   SH_SANDBOX_SELLER=<phone/email> SH_SANDBOX_SELLER_PASSWORD=... SH_SANDBOX_SHOP_ID=<shop id> SH_SANDBOX_SKU_ID=<sku of that shop> \
//   SH_SANDBOX_METHOD=VnPay|MoMo|ZaloPay SH_SANDBOX_CARRIER=GHN|GHTK \
//   node e2e/tools/sandbox-check.cjs
//
// The buyer needs a default address; the SKU must be in stock. The payment page of the sandbox is opened by a person
// (test card / test wallet of the provider, docs/04 "Chạy với sandbox thật"); the script waits for the IPN.
const crypto = require('crypto');

const env = (name, fallback) => {
  const v = process.env[name] ?? fallback;
  if (v === undefined || v === '') {
    console.error(`Thiếu biến ${name}.`);
    process.exit(2);
  }
  return v;
};

const BASE = env('SH_SANDBOX_BASE_URL').replace(/\/$/, '');
const METHOD = env('SH_SANDBOX_METHOD', 'VnPay');
const CARRIER = env('SH_SANDBOX_CARRIER', 'GHN');
const WAIT_MINUTES = Number(process.env.SH_SANDBOX_WAIT_MINUTES ?? 10);
const results = [];
const record = (step, outcome, detail) => {
  results.push({ step, outcome, detail });
  console.log(`[${outcome}] ${step}${detail ? ` — ${detail}` : ''}`);
};

async function api(token, method, path, body, headers = {}) {
  const res = await fetch(`${BASE}/api${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* not JSON */ }
  return { status: res.status, ok: res.ok, data: json?.data, message: json?.message ?? text.slice(0, 200) };
}

async function login(identifier, password) {
  const r = await api(null, 'POST', '/auth/login', { identifier, password });
  if (!r.ok) throw new Error(`Đăng nhập ${identifier} thất bại (${r.status}): ${r.message}`);
  return r.data.accessToken;
}

async function poll(what, minutes, check) {
  const until = Date.now() + minutes * 60_000;
  while (Date.now() < until) {
    const value = await check();
    if (value) return value;
    await new Promise((r) => setTimeout(r, 5_000));
  }
  return null;
}

async function main() {
  // 1. The provider is really configured (keys in .env) — otherwise there is nothing to check
  const admin = await login(env('SH_E2E_ADMIN_USER'), env('SH_E2E_ADMIN_PASSWORD'));
  const providers = await api(admin, 'GET', '/admin/providers');
  if (!providers.ok) throw new Error(`Không đọc được danh sách nhà cung cấp (${providers.status}).`);
  const gateway = providers.data.gateways.find((g) => g.method === METHOD);
  const carrier = providers.data.carriers.find((c) => c.code === CARRIER);
  record('Cổng thanh toán có khoá & đang bật', gateway?.enabled ? 'OK' : 'LỖI', gateway ? `${gateway.name} (${gateway.provider})` : `không có ${METHOD}`);
  record('Hãng vận chuyển có khoá & đang bật', carrier?.providerConfigured && carrier?.isActive ? 'OK' : 'LỖI', carrier ? carrier.name : `không có ${CARRIER}`);
  if (!gateway?.enabled || !(carrier?.providerConfigured && carrier?.isActive)) return finish();

  const buyer = await login(env('SH_SANDBOX_BUYER'), env('SH_SANDBOX_BUYER_PASSWORD'));
  const shopId = env('SH_SANDBOX_SHOP_ID');
  const skuId = env('SH_SANDBOX_SKU_ID');

  // 2. Online payment: place, a person pays on the sandbox page, the IPN marks it paid
  const placeOrder = async (paymentMethod) => {
    const cart = await api(buyer, 'POST', '/cart/items', { skuId, quantity: 1 });
    if (!cart.ok) throw new Error(`Không thêm được vào giỏ (${cart.status}): ${cart.message}`);
    await api(buyer, 'PUT', '/cart/selection', { shopId: null, selected: false });
    await api(buyer, 'PUT', `/cart/items/${skuId}`, { selected: true });
    const request = {
      addressId: null, shops: [{ shopId, carrierCode: CARRIER, voucherCode: null, note: 'sandbox-check' }], platformVoucherCode: null,
      freeshipVoucherCode: null, useCoins: false, paymentMethod, paymentOption: 'Default',
    };
    const quote = await api(buyer, 'POST', '/checkout/quote', request);
    if (!quote.ok) throw new Error(`Báo giá lỗi (${quote.status}): ${quote.message}`);
    const placed = await api(buyer, 'POST', '/checkout', { checkout: request, expectedGrandTotal: quote.data.grandTotal, walletPin: null },
      { 'Idempotency-Key': crypto.randomUUID() });
    if (!placed.ok) throw new Error(`Đặt hàng lỗi (${placed.status}): ${placed.message}`);
    return placed.data;
  };

  const online = await placeOrder(METHOD);
  const code = online.orders[0].code;
  record('Đặt đơn thanh toán online', online.payment?.redirectUrl ? 'OK' : 'LỖI', `đơn ${code}`);
  if (online.payment?.redirectUrl) {
    console.log(`\n→ Mở trang thanh toán sandbox và trả bằng thẻ / ví thử của ${METHOD}:\n  ${online.payment.redirectUrl}\n  (chờ IPN tối đa ${WAIT_MINUTES} phút)\n`);
    const paid = await poll('IPN', WAIT_MINUTES, async () => {
      const c = await api(buyer, 'GET', `/checkout/${online.checkoutId}`);
      return c.ok && c.data.payment?.status === 'Succeeded' ? c.data : null;
    });
    record('IPN / callback xác nhận đã trả tiền (không dựa vào trang "quay về")', paid ? 'OK' : 'CHƯA THẤY',
      paid ? `payment ${paid.payment.paymentId}` : 'không nhận được IPN — kiểm URL IPN khai với cổng và SH_CALLBACK_BASE_URL');

    // 3. Refund back through the gateway: the buyer cancels the paid order before the shop confirms
    if (paid) {
      const cancelled = await api(buyer, 'POST', `/orders/${encodeURIComponent(code)}/cancel`, { reason: 'Kiểm thử hoàn tiền sandbox' });
      record('Huỷ đơn đã trả tiền', cancelled.ok ? 'OK' : 'LỖI', cancelled.message);
      const refunded = cancelled.ok && await poll('refund', WAIT_MINUTES, async () => {
        const o = await api(buyer, 'GET', `/orders/${encodeURIComponent(code)}`);
        return o.ok && o.data.paymentStatus === 'Refunded' ? o.data : null;
      });
      record('Hoàn tiền về cổng', refunded ? 'OK' : 'CHƯA THẤY', refunded ? 'đơn ở trạng thái đã hoàn tiền' : 'kiểm lịch sử hoàn tiền trên trang quản trị của cổng');
    }
  }

  // 4. Parcel with the real carrier: COD order → shop prepares → tracking number → carrier webhook moves the order
  const seller = await login(env('SH_SANDBOX_SELLER'), env('SH_SANDBOX_SELLER_PASSWORD'));
  const cod = await placeOrder('Cod');
  const orderId = cod.orders[0].id;
  const prepared = await api(seller, 'POST', `/seller/shops/${shopId}/orders/prepare`, { orderIds: [orderId], pickupMethod: 'DropOff', pickupSlot: null });
  const tracking = prepared.ok ? prepared.data[0]?.trackingNo : null;
  record(`Tạo vận đơn ${CARRIER}`, tracking ? 'OK' : 'LỖI', tracking ?? prepared.data?.[0]?.error ?? prepared.message);
  if (tracking) {
    console.log(`\n→ Đổi trạng thái vận đơn ${tracking} trên trang thử của ${CARRIER} (đã lấy hàng / đang giao…). Chờ webhook tối đa ${WAIT_MINUTES} phút.\n`);
    const moved = await poll('webhook', WAIT_MINUTES, async () => {
      const t = await api(null, 'GET', `/tracking/${encodeURIComponent(tracking)}`);
      return t.ok && (t.data.events?.length ?? 0) > 1 ? t.data : null;
    });
    record('Webhook hãng vận chuyển cập nhật hành trình', moved ? 'OK' : 'CHƯA THẤY',
      moved ? `${moved.events.length} sự kiện, mới nhất: ${moved.events[0]?.label ?? moved.status}` : 'kiểm URL webhook (kèm ?token=) khai với hãng');
  }
  return finish();
}

function finish() {
  const bad = results.filter((r) => r.outcome !== 'OK');
  console.log(`\nKết quả: ${results.length - bad.length}/${results.length} bước OK${bad.length ? ` — chưa đạt: ${bad.map((b) => b.step).join('; ')}` : ''}.`);
  console.log('Ghi kết quả thật vào docs/06 (KB41) kèm ngày giờ — không ghi "Đạt" cho bước "CHƯA THẤY".');
  process.exit(bad.length ? 1 : 0);
}

main().catch((e) => {
  console.error(`Dừng: ${e.message}`);
  process.exit(1);
});
