// Phase 13 — spec 6.6: crawlers get pre-rendered HTML from the gateway, people get the SPA.
//   SH_E2E_BASE_URL=http://localhost:18000 npx playwright test seo.spec.cjs
const { test, expect } = require('@playwright/test');
const { BASE, findProduct } = require('./helpers.cjs');

const BOT = { 'User-Agent': 'Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)' };

test.describe('SEO cho máy thu thập', () => {
  test('Trang sản phẩm: HTML render sẵn có title, Open Graph, canonical, JSON-LD; đường cũ chuyển hướng 301', async ({ request }) => {
    const product = await findProduct(request, () => true);
    const canonical = `/san-pham/${product.slug}-i.${product.shop.id}.${product.id}`;

    const res = await request.get(`${BASE}${canonical}`, { headers: BOT });
    expect(res.status()).toBe(200);
    const html = await res.text();
    expect(html).toContain(`<title>${product.name}`);
    expect(html).toContain('property="og:image"');
    expect(html).toContain(`rel="canonical" href="`);
    expect(html).toContain(canonical);
    const ld = [...html.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)].map((m) => JSON.parse(m[1]));
    const item = ld.find((x) => x['@type'] === 'Product');
    expect(item.offers.priceCurrency).toBe('VND');
    expect(Number(item.offers.lowPrice ?? item.offers.price)).toBe(product.minPrice);
    expect(ld.some((x) => x['@type'] === 'BreadcrumbList')).toBe(true);

    const old = await request.get(`${BASE}/san-pham/${product.id}`, { headers: BOT, maxRedirects: 0 });
    expect(old.status()).toBe(301);
    expect(old.headers()['location']).toContain(canonical);

    // A person on the same URL gets the single-page app, not the crawler page
    const human = await (await request.get(`${BASE}${canonical}`)).text();
    expect(human).toContain('id="root"');
    expect(human).not.toContain('application/ld+json');
  });

  test('sitemap.xml chia tệp, robots.txt trỏ tới sitemap', async ({ request }) => {
    const robots = await request.get(`${BASE}/robots.txt`);
    expect(robots.status()).toBe(200);
    expect(await robots.text()).toMatch(/Sitemap: .*\/sitemap\.xml/);

    const index = await (await request.get(`${BASE}/sitemap.xml`)).text();
    expect(index).toContain('<sitemapindex');
    const files = [...index.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => new URL(m[1]).pathname);
    const productsFile = files.find((f) => /products-\d+\.xml$/.test(f));
    expect(productsFile).toBeTruthy();
    const urls = await (await request.get(`${BASE}${productsFile}`)).text();
    expect(urls).toContain('<urlset');
    expect(urls).toMatch(/\/san-pham\/[^<]+-i\.[0-9a-f-]+\.[0-9a-f-]+<\/loc>/);
  });
});
