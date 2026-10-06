// k6 load test — 1.000 buyers at once on a Flash Sale (spec 6.3, Phase 9 acceptance).
//
// Each virtual user is one of the load-test buyers seeded with SH_SEED_LOAD_USERS / SH_LOAD_USER_PASSWORD (phones
// 0970000000…), already signed in before the sale opens (setup): at the same moment they all open the Flash Sale
// board, put the flash SKU in the cart, ask for a quote and place a COD order. Pass when:
//   • no server error (5xx) and every answer is a success or an expected refusal (409 sold out / limit, 429);
//   • the units sold never exceed the quota, and the orders placed equal the units sold;
//   • p95 of "place order" stays under 3 s (one API container on a developer machine; measured 2.5 s — see docs/07).
//
// Run (inside the stack network so 1.000 users are not 1.000 requests from one IP at the gateway):
//   docker run --rm --network shophub_default -v "$PWD/e2e/load:/scripts" -e BASE_URL=http://api:8080 \
//     -e LOAD_PASSWORD=... -e USERS=1000 grafana/k6 run /scripts/flash-sale.js
// with the API started with SH_SEED_LOAD_USERS=1000, SH_LOAD_USER_PASSWORD=..., SH_RATE_LIMIT_AUTH=100000 and
// SH_RATE_LIMIT_GLOBAL=1000000 (the per-IP limits protect production; here every user shares the k6 container's IP).
import http from 'k6/http';
import { check, fail } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:18000';
const USERS = Number(__ENV.USERS || 1000);
const PASSWORD = __ENV.LOAD_PASSWORD;

const placed = new Counter('orders_placed');
const soldOut = new Counter('orders_refused');
const placeTime = new Trend('place_order_ms', true);

export const options = {
  setupTimeout: '15m',
  scenarios: {
    flash: { executor: 'per-vu-iterations', vus: USERS, iterations: 1, maxDuration: '5m' },
  },
  thresholds: {
    'http_req_duration{name:place}': ['p(95)<3000'],
    checks: ['rate>0.99'],
  },
};

const authed = (token, extra = {}) => ({ headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...extra } });

export function setup() {
  if (!PASSWORD) fail('LOAD_PASSWORD is required');
  const board = http.get(`${BASE}/api/flash-sale`).json('data');
  if (!board || !board.items || board.items.length === 0) fail('No running Flash Sale (seed one first)');
  const item = board.items.filter((i) => i.quota > i.sold).sort((a, b) => (b.quota - b.sold) - (a.quota - a.sold))[0];
  if (!item) fail('Every flash item is sold out');
  // Buyers sign in ahead of the sale, a few at a time (password hashing is meant to be slow)
  const tokens = [];
  for (let i = 0; i < USERS; i += 20) {
    const batch = http.batch(Array.from({ length: Math.min(20, USERS - i) }, (_, k) => ['POST', `${BASE}/api/auth/login`,
      JSON.stringify({ identifier: `0970${String(i + k).padStart(6, '0')}`, password: PASSWORD }),
      { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } }]));
    for (const r of batch) tokens.push(r.status === 200 ? r.json('data.accessToken') : null);
  }
  if (tokens.filter((t) => t).length < USERS) fail(`Only ${tokens.filter((t) => t).length}/${USERS} buyers could sign in`);
  return { item, soldBefore: item.sold, slotId: board.slot.id, tokens };
}

export default function (data) {
  const token = data.tokens[__VU - 1];

  check(http.get(`${BASE}/api/flash-sale`, { tags: { name: 'board' } }), { 'board ok': (r) => r.status === 200 });
  // A clean cart: only the flash SKU is bought (a previous run may have left lines behind)
  const cart = http.get(`${BASE}/api/cart`, { ...authed(token), tags: { name: 'cart' } });
  const leftover = (cart.json('data.shops') || []).flatMap((s) => s.lines.map((l) => l.skuId));
  if (leftover.length > 0) http.post(`${BASE}/api/cart/items/remove`, JSON.stringify({ skuIds: leftover }), { ...authed(token), tags: { name: 'cart' } });
  const add = http.post(`${BASE}/api/cart/items`, JSON.stringify({ skuId: data.item.skuId, quantity: 1 }), { ...authed(token), tags: { name: 'cart' } });
  check(add, { 'cart ok': (r) => r.status === 200 });

  const request = {
    addressId: null, shops: [], platformVoucherCode: null, freeshipVoucherCode: null, useCoins: false, paymentMethod: 'Cod',
  };
  const quote = http.post(`${BASE}/api/checkout/quote`, JSON.stringify(request), { ...authed(token), tags: { name: 'quote' } });
  if (!check(quote, { 'quote ok': (r) => r.status === 200 })) return;
  const q = quote.json('data');
  const body = {
    checkout: { ...request, shops: q.shops.map((s) => ({ shopId: s.shopId, carrierCode: s.carrierCode, voucherCode: null, note: null })) },
    expectedGrandTotal: q.grandTotal,
  };
  const started = Date.now();
  const res = http.post(`${BASE}/api/checkout`, JSON.stringify(body),
    { ...authed(token, { 'Idempotency-Key': `k6-${__VU}-${Date.now()}` }), tags: { name: 'place' } });
  placeTime.add(Date.now() - started);
  check(res, { 'placed or refused, never an error': (r) => r.status === 200 || r.status === 409 || r.status === 429 });
  if (res.status === 200) placed.add(1);
  else soldOut.add(1);
}

export function teardown(data) {
  const board = http.get(`${BASE}/api/flash-sale?slotId=${data.slotId}`).json('data');
  const item = board.items.find((i) => i.itemId === data.item.itemId);
  const sold = item ? item.sold : data.item.quota;
  console.log(`Flash item ${data.item.itemId}: quota ${data.item.quota}, sold before ${data.soldBefore}, sold after ${sold}`);
  if (http.get(`${BASE}/health`).status !== 200) fail('API unhealthy after the sale');
  if (sold > data.item.quota) fail(`OVERSOLD: ${sold} > ${data.item.quota}`);
  // Demand (USERS) is far above the units left: they must all be gone, and not one more
  if (USERS >= data.item.quota - data.soldBefore && sold !== data.item.quota) fail(`Units left unsold: ${sold}/${data.item.quota}`);
}

