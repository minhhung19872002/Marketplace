// Performance with a large catalogue (spec 6.3): search < 500 ms, product / category / home data < 300 ms (p95),
// measured through the gateway of the perf stack (docker-compose.perf.yml, SH_SEED=perf):
//   docker run --rm --network shophub-perf_default -v "$PWD/e2e/load:/scripts" -e BASE_URL=http://nginx grafana/k6 run /scripts/perf.js
import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:18100';
const VUS = Number(__ENV.VUS || 20);

const search = new Trend('t_search', true);
const searchFacet = new Trend('t_search_filtered', true);
const product = new Trend('t_product', true);
const category = new Trend('t_category', true);
const home = new Trend('t_home', true);

export const options = {
  scenarios: { browse: { executor: 'constant-vus', vus: VUS, duration: __ENV.DURATION || '60s' } },
  thresholds: {
    t_search: ['p(95)<500'],
    t_search_filtered: ['p(95)<500'],
    t_product: ['p(95)<300'],
    t_category: ['p(95)<300'],
    t_home: ['p(95)<300'],
    http_req_failed: ['rate<0.01'],
  },
};

const WORDS = ['ao thun', 'dien thoai', 'tai nghe', 'giay the thao', 'balo', 'noi chien', 'son moi', 'dong ho', 'binh giu nhiet', 'ban phim',
  'ao so mi cotton', 'tai nghe khong day', 'op lung', 'sac du phong', 'kem chong nang', 'vot cau long', 'den ngu', 'chao chong dinh'];

export function setup() {
  const res = http.get(`${BASE}/api/search/products?q=hieu%20nang&pageSize=100`);
  const items = res.json('data.items') || [];
  const tree = http.get(`${BASE}/api/categories`).json('data') || [];
  const slugs = [];
  for (const t of tree) for (const m of t.children || []) { slugs.push(m.slug); for (const l of m.children || []) slugs.push(l.slug); }
  const total = http.get(`${BASE}/api/search/products?pageSize=1`).json('data.totalCount');
  console.log(`catalogue: ${total} products, ${slugs.length} categories`);
  return { ids: items.map((i) => i.id), slugs, cats: tree.map((t) => t.id) };
}

const pick = (a) => a[Math.floor(Math.random() * a.length)];

export default function (data) {
  const q = encodeURIComponent(pick(WORDS));
  let r = http.get(`${BASE}/api/search/products?q=${q}&page=${1 + Math.floor(Math.random() * 3)}`, { tags: { name: 'search' } });
  check(r, { 'search 200': (x) => x.status === 200 });
  search.add(r.timings.duration);

  r = http.get(`${BASE}/api/search/products?q=${q}&minPrice=50000&maxPrice=900000&minRating=4&sort=PriceAsc`, { tags: { name: 'search-filtered' } });
  check(r, { 'filtered 200': (x) => x.status === 200 });
  searchFacet.add(r.timings.duration);

  r = http.get(`${BASE}/api/products/${pick(data.ids)}`, { tags: { name: 'product' } });
  check(r, { 'product 200': (x) => x.status === 200 });
  product.add(r.timings.duration);

  r = http.get(`${BASE}/api/search/products?categoryId=${pick(data.cats)}&sort=BestSelling`, { tags: { name: 'category' } });
  check(r, { 'category 200': (x) => x.status === 200 });
  category.add(r.timings.duration);

  r = http.get(`${BASE}/api/home/recommendations?page=${1 + Math.floor(Math.random() * 5)}`, { tags: { name: 'home' } });
  check(r, { 'home 200': (x) => x.status === 200 });
  home.add(r.timings.duration);
}
