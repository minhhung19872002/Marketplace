// Artwork of the sample data drawn as HTML/CSS and rendered by Chromium (Playwright of e2e/), with lucide icons and the
// Be Vietnam Pro font of web/node_modules — far better typography than drawing with Pillow. Run after build.py:
//   node backend/tools/seed-catalog/art-html.mjs && python backend/tools/seed-catalog/art-png-to-webp.py
// Writes PNG files to .cache/art-png/; art-png-to-webp.py turns them into Seed/Data/Art/*.webp.
// Every claim drawn on a banner matches the platform's real rules / sample vouchers (decision #180, G2-B5).
import { createRequire } from 'node:module';
import { mkdirSync, readFileSync, readdirSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..', '..', '..');
const webRequire = createRequire(join(root, 'web', 'package.json'));
const e2eRequire = createRequire(join(root, 'e2e', 'package.json'));
const React = webRequire('react');
const { renderToStaticMarkup } = webRequire('react-dom/server');
const icons = webRequire('lucide-react');
const { chromium } = e2eRequire('@playwright/test');

const DATA = join(root, 'backend', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data');
const CACHE = join(here, '.cache');
const OUT = join(CACHE, 'art-png');
mkdirSync(OUT, { recursive: true });

const font = (w) => pathToFileURL(join(root, 'web', 'node_modules', '@fontsource', 'be-vietnam-pro', 'files', `be-vietnam-pro-vietnamese-${w}-normal.woff2`)).href;
const fontLatin = (w) => pathToFileURL(join(root, 'web', 'node_modules', '@fontsource', 'be-vietnam-pro', 'files', `be-vietnam-pro-latin-${w}-normal.woff2`)).href;
const FONTS = [400, 600, 700, 800].map((w) => `
  @font-face { font-family: BVP; font-weight: ${w}; src: url(${fontLatin(w)}) format('woff2'); }
  @font-face { font-family: BVP; font-weight: ${w}; src: url(${font(w)}) format('woff2'); unicode-range: U+0102-0103, U+0110-0111, U+0128-0129, U+0168-0169, U+01A0-01A1, U+01AF-01B0, U+0300-0301, U+0303-0304, U+0308-0309, U+0323, U+0329, U+1EA0-1EF9, U+20AB; }`).join('');

const icon = (name, size, color = '#fff', stroke = 2) =>
  renderToStaticMarkup(React.createElement(icons[name], { size, color, strokeWidth: stroke }));

// ---- product cutouts (transparent studio photos downloaded by build.py) ----
const models = JSON.parse(readFileSync(join(DATA, 'product-models.json'), 'utf8'));
const catalogueFile = readdirSync(CACHE).find((f) => f.startsWith('dummyjson.com_products'));
const catalogue = JSON.parse(readFileSync(join(CACHE, catalogueFile), 'utf8')).products;
const cacheName = (url) => [...url.replace('https://', '')].map((c) => (/[A-Za-z0-9.-]/.test(c) ? c : '_')).join('');
const cut = (id, n = 0) => {
  const url = catalogue.find((p) => p.id === id).images[n] ?? catalogue.find((p) => p.id === id).images[0];
  const file = join(CACHE, cacheName(url));
  if (!existsSync(file)) throw new Error(`missing cutout ${id}`);
  return pathToFileURL(file).href;
};
const seed = JSON.parse(readFileSync(join(DATA, 'catalog-seed.json'), 'utf8'));

const page = (w, h, css, body) => `<!doctype html><html><head><meta charset="utf-8"><style>${FONTS}
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: ${w}px; height: ${h}px; overflow: hidden; font-family: BVP, sans-serif; }
  .p { position: absolute; object-fit: contain; filter: drop-shadow(0 18px 18px rgba(0,0,0,.28)); }
  ${css}</style></head><body>${body}</body></html>`;

const jobs = [];
const add = (name, w, h, html, transparent = false) => jobs.push({ name, w, h, html, transparent });

// ---- 1. shop logos: solid square in the shop colour, the industry icon and the name (safe inside a circle) ----
const INDUSTRY_ICON = {
  'Điện Thoại & Phụ Kiện': 'Smartphone', 'Máy Tính & Laptop': 'Laptop', 'Thiết Bị Điện Tử': 'Headphones', 'Đồng Hồ': 'Watch',
  'Thời Trang Nam': 'Shirt', 'Thời Trang Nữ': 'Shirt', 'Giày Dép Nam': 'Footprints', 'Giày Dép Nữ': 'Footprints', 'Túi Ví Nữ': 'ShoppingBag',
  'Nhà Cửa & Đời Sống': 'CookingPot', 'Sắc Đẹp': 'Sparkles', 'Sức Khỏe': 'HeartPulse', 'Thể Thao & Du Lịch': 'Dumbbell',
  'Ô Tô & Xe Máy': 'Bike', 'Bách Hóa Online': 'ShoppingBasket', 'Thú Cưng': 'PawPrint',
};
seed.shops.forEach((s, i) => {
  const name = s.name.replace(/^Mall /, '');
  const words = name.split(' ');
  const wordmark = name.length <= 12 ? name : words.length > 2 ? `${words.slice(0, 2).join(' ')}<br>${words.slice(2).join(' ')}` : name;
  add(`shop-${String(i).padStart(2, '0')}-logo`, 256, 256, page(256, 256, `
    body { background: linear-gradient(145deg, ${s.color}, color-mix(in srgb, ${s.color} 70%, #000)); color: #fff; display: grid; place-items: center; }
    .in { width: 190px; text-align: center; display: flex; flex-direction: column; align-items: center; gap: 8px; }
    .w { font-weight: 800; font-size: ${name.length <= 9 ? 30 : 22}px; line-height: 1.05; letter-spacing: -.02em; }
    .mall { position: absolute; top: 34px; padding: 2px 10px; border-radius: 99px; background: #fff; color: ${s.color}; font-weight: 800; font-size: 14px; letter-spacing: .08em; }`,
  `${s.mall ? '<span class="mall">MALL</span>' : ''}<div class="in" style="margin-top:${s.mall ? 24 : 0}px">${icon(INDUSTRY_ICON[s.sells[0]] ?? 'Store', 64, '#fff', 1.8)}<div class="w">${wordmark}</div></div>`));
});

// ---- 2. ShopHub Mall portrait slides (commitments that are true on the platform) ----
const mallSlides = [
  { bg: ['#b5121b', '#6d0a10'], items: [123, 100, 106], line: 'Thương hiệu chính hãng' },
  { bg: ['#4a148c', '#1f0a3d'], items: [8, 7, 6], line: 'Mỹ phẩm & nước hoa' },
  { bg: ['#0d47a1', '#071f47'], items: [88, 172, 186], line: 'Thời trang chính hãng' },
];
mallSlides.forEach((m, n) => {
  add(`mall-${n + 1}`, 480, 660, page(480, 660, `
    body { background: radial-gradient(circle at 70% 40%, ${m.bg[0]}, ${m.bg[1]} 75%); color: #fff; }
    .brand { position: absolute; left: 36px; top: 36px; font-size: 22px; font-weight: 600; opacity: .9; }
    .mall { position: absolute; left: 34px; top: 62px; font-size: 76px; font-weight: 800; letter-spacing: -.02em; }
    .tag { position: absolute; left: 38px; top: 158px; font-size: 22px; font-weight: 600; color: #ffe08a; }
    .list { position: absolute; left: 32px; right: 32px; bottom: 30px; display: grid; gap: 10px; }
    .row { display: flex; align-items: center; gap: 12px; padding: 12px 16px; border-radius: 14px; background: rgba(255,255,255,.12); font-size: 20px; font-weight: 600; }`,
  `<div class="brand">ShopHub</div><div class="mall">MALL</div><div class="tag">${m.line}</div>
   <img class="p" src="${cut(m.items[0])}" style="left:150px;top:200px;width:230px;height:230px">
   <img class="p" src="${cut(m.items[1])}" style="left:40px;top:250px;width:150px;height:180px">
   <img class="p" src="${cut(m.items[2])}" style="left:330px;top:270px;width:130px;height:160px">
   <div class="list">
     <div class="row">${icon('BadgeCheck', 26, '#ffe08a')} Chính hãng 100%</div>
     <div class="row">${icon('RotateCcw', 26, '#ffe08a')} Trả hàng trong 15 ngày</div>
     <div class="row">${icon('Truck', 26, '#ffe08a')} Voucher Freeship mỗi ngày</div>
   </div>`));
});

// ---- 3. home heroes: hot gradient, products, stickers (the title stays HTML on the left) ----
const sticker = (x, y, rot, html, bg = '#ffd23f', color = '#b71c1c', size = 120) =>
  `<div style="position:absolute;left:${x}px;top:${y}px;width:${size}px;height:${size}px;border-radius:50%;background:${bg};color:${color};display:grid;place-items:center;text-align:center;font-weight:800;transform:rotate(${rot}deg);box-shadow:0 10px 24px rgba(0,0,0,.25);line-height:1">${html}</div>`;
const ticket = (x, y, rot, top, big) =>
  `<div style="position:absolute;left:${x}px;top:${y}px;transform:rotate(${rot}deg);background:#fff;color:#c93d19;border-radius:12px;padding:12px 22px;box-shadow:0 10px 24px rgba(0,0,0,.25);border-left:6px dashed #c93d19;font-weight:800;text-align:center;line-height:1.1"><div style="font-size:16px;letter-spacing:.06em">${top}</div><div style="font-size:30px">${big}</div></div>`;
const heroes = [
  { bg: 'linear-gradient(120deg,#ff5f2e 0%,#e8361a 45%,#b8200f 100%)', items: [[123, 820, 70, 330, 470], [100, 1110, 260, 210, 210], [106, 1180, 60, 200, 200], [105, 700, 300, 160, 220]],
    stickers: sticker(1260, 330, 12, '<div><div style="font-size:18px">FLASH SALE</div><div style="font-size:40px">-50%</div></div>') + ticket(1040, 470, -6, 'MÃ GIẢM', '₫100K') },
  { bg: 'linear-gradient(120deg,#ff4f8b 0%,#c2185b 50%,#7b1fa2 100%)', items: [[8, 820, 90, 240, 420], [7, 1050, 120, 230, 400], [4, 760, 330, 150, 220], [2, 1240, 330, 180, 160]],
    stickers: sticker(1250, 80, -10, '<div><div style="font-size:16px">GIẢM ĐẾN</div><div style="font-size:42px">40%</div></div>', '#fff176', '#ad1457') + ticket(980, 470, 5, 'FREESHIP', '₫30K') },
  { bg: 'linear-gradient(120deg,#26c281 0%,#0f7b4a 50%,#0b5d39 100%)', items: [[71, 760, 160, 300, 300], [51, 1040, 70, 220, 420], [12, 900, 370, 320, 200], [47, 1260, 140, 140, 300]],
    stickers: sticker(1230, 420, 10, '<div><div style="font-size:16px">GIA DỤNG</div><div style="font-size:30px">GIÁ TỐT</div></div>', '#ffd23f', '#0b5d39') + ticket(760, 470, -5, 'MÃ GIẢM', '₫50K') },
];
heroes.forEach((h, n) => {
  add(`hero-${n + 1}`, 1440, 600, page(1440, 600, `
    body { background: ${h.bg}; }
    .ring { position: absolute; border-radius: 50%; border: 40px solid rgba(255,255,255,.08); }
    .dots { position: absolute; inset: 0; background-image: radial-gradient(rgba(255,255,255,.18) 2px, transparent 2px); background-size: 26px 26px; mask-image: linear-gradient(90deg, transparent 40%, #000 80%); }`,
  `<div class="dots"></div><div class="ring" style="left:760px;top:-180px;width:640px;height:640px"></div><div class="ring" style="left:1100px;top:280px;width:420px;height:420px"></div>
   ${h.items.map(([id, x, y, w, ht]) => `<img class="p" src="${cut(id)}" style="left:${x}px;top:${y}px;width:${w}px;height:${ht}px">`).join('')}${h.stickers}`));
});

// ---- 4. campaign 10.10: event banner + popup (headline drawn in) ----
const tenTen = (w, h, layout) => page(w, h, `
  body { background: radial-gradient(circle at 75% 30%, #ff7a3d, #e8361a 45%, #9c1408 100%); color: #fff; }
  .burst { position: absolute; inset: 0; background: repeating-conic-gradient(from 0deg at 75% 40%, rgba(255,255,255,.07) 0 6deg, transparent 6deg 18deg); }
  .h1 { position: absolute; font-weight: 800; letter-spacing: -.03em; line-height: .95; text-shadow: 0 6px 0 rgba(0,0,0,.18); }
  .y { color: #ffe08a; }
  .cta { position: absolute; background: #fff; color: #c62828; font-weight: 800; border-radius: 99px; }`, layout);
add('event-1010', 1440, 480, tenTen(1440, 480, `<div class="burst"></div>
  <div class="h1" style="left:80px;top:80px;font-size:104px">SIÊU SALE <span class="y">10.10</span></div>
  <div style="position:absolute;left:84px;top:214px;font-size:32px;font-weight:600">Mã giảm đến ₫100.000 · Freeship đến ₫30.000</div>
  <div class="cta" style="left:84px;top:290px;padding:16px 36px;font-size:30px">Săn deal ngay</div>
  <img class="p" src="${cut(78)}" style="left:1010px;top:40px;width:260px;height:220px">
  <img class="p" src="${cut(123)}" style="left:1250px;top:60px;width:150px;height:320px">
  <img class="p" src="${cut(172)}" style="left:990px;top:240px;width:200px;height:200px">
  <img class="p" src="${cut(6)}" style="left:1180px;top:290px;width:120px;height:160px">
  ${sticker(1300, 340, 12, '<div><div style="font-size:15px">FLASH SALE</div><div style="font-size:34px">-50%</div></div>', '#ffd23f', '#b71c1c', 112)}`));
add('popup', 720, 720, tenTen(720, 720, `<div class="burst"></div>
  <div class="h1" style="left:0;right:0;top:56px;text-align:center;font-size:96px">SIÊU SALE</div>
  <div class="h1 y" style="left:0;right:0;top:150px;text-align:center;font-size:120px">10.10</div>
  <img class="p" src="${cut(124)}" style="left:60px;top:300px;width:200px;height:300px">
  <img class="p" src="${cut(88)}" style="left:250px;top:360px;width:230px;height:220px">
  <img class="p" src="${cut(186)}" style="left:460px;top:330px;width:200px;height:260px">
  <div class="cta" style="left:150px;right:150px;top:612px;padding:16px 0;text-align:center;font-size:30px">Mã giảm đến ₫100.000</div>`));

// ---- 5. campaign frame (transparent): a band along the bottom of product photos taking part in 10.10 ----
add('frame-1010', 800, 800, page(800, 800, `
  body { background: transparent; }
  .band { position: absolute; left: 0; right: 0; bottom: 0; height: 120px; background: linear-gradient(90deg, #c62828, #ff5722); display: flex; align-items: center; justify-content: space-between; padding: 0 30px; color: #fff; }
  .big { font-size: 60px; font-weight: 800; letter-spacing: -.02em; }
  .big span { color: #ffe08a; }
  .side { text-align: right; font-weight: 700; font-size: 26px; line-height: 1.1; }
  .edge { position: absolute; inset: 0; border: 10px solid #ff5722; border-bottom: 0; }`,
  `<div class="edge"></div><div class="band"><div class="big"><span>10.10</span> SIÊU SALE</div><div class="side">ShopHub<br>Giá sốc</div></div>`), true);

// ---- 6. marketing covers for ~30 % of the models: colour, product, one benefit line taken from the model's data ----
const TOP_COLOUR = {
  'Điện Thoại & Phụ Kiện': ['#e3f2fd', '#1565c0'], 'Máy Tính & Laptop': ['#e8eaf6', '#283593'], 'Thiết Bị Điện Tử': ['#ede7f6', '#4527a0'],
  'Đồng Hồ': ['#fff8e1', '#8d6e00'], 'Thời Trang Nam': ['#e0f2f1', '#00695c'], 'Thời Trang Nữ': ['#fce4ec', '#ad1457'],
  'Giày Dép Nam': ['#e3f2fd', '#0d47a1'], 'Giày Dép Nữ': ['#fce4ec', '#880e4f'], 'Túi Ví Nữ': ['#fbe9e7', '#bf360c'],
  'Nhà Cửa & Đời Sống': ['#e8f5e9', '#1b5e20'], 'Sắc Đẹp': ['#fce4ec', '#ad1457'], 'Thể Thao & Du Lịch': ['#fff3e0', '#e65100'],
  'Bách Hóa Online': ['#f1f8e9', '#33691e'],
};
// Only facts of the model (attributes, origin, variants) — never a promise like "hàng có sẵn" that stock may break
const benefit = (m) => {
  const a = m.attributes;
  const origin = m.origin && m.origin !== 'Khác' ? `Xuất xứ ${m.origin}` : '';
  const options = m.variant ? `${m.variant.options.length} lựa chọn ${m.variant.tier.toLowerCase()}` : '';
  if (a['Bảo hành']?.[0] && a['Bảo hành'][0] !== 'Không bảo hành') return [`Bảo hành ${a['Bảo hành'][0]}`, options || origin];
  if (a['Chất liệu']?.[0] && a['Chất liệu'][0] !== 'Khác') return [`Chất liệu ${a['Chất liệu'][0]}`, options || origin];
  if (a['Khối lượng']?.[0]) return [`${Number(a['Khối lượng'][0]) >= 1000 ? `${Number(a['Khối lượng'][0]) / 1000}kg` : `${a['Khối lượng'][0]}g`}`, origin];
  return [m.category.split('/')[2], options || origin];
};
// Covers are drawn from the transparent dummyjson cut-outs only: the open-licence photos (key 195 on) have none
const covered = models.filter((m, i) => TOP_COLOUR[m.category.split('/')[0]] && i % 3 === 0 && catalogue.some((p) => p.id === m.key));
for (const m of covered) {
  const [bg, fg] = TOP_COLOUR[m.category.split('/')[0]];
  const [b1, b2] = benefit(m);
  const label = m.category.split('/')[1];
  add(`cover-p${String(m.key).padStart(3, '0')}`, 800, 800, page(800, 800, `
    body { background: linear-gradient(160deg, #fff 0%, ${bg} 55%, ${bg} 100%); color: ${fg}; }
    .blob { position: absolute; right: -120px; bottom: -120px; width: 620px; height: 620px; border-radius: 50%; background: ${fg}; opacity: .1; }
    .k { position: absolute; left: 48px; top: 48px; padding: 8px 18px; border-radius: 99px; background: ${fg}; color: #fff; font-weight: 700; font-size: 26px; }
    .b1 { position: absolute; left: 48px; top: 118px; right: 48px; font-weight: 800; font-size: 54px; line-height: 1.05; letter-spacing: -.02em; }
    .b2 { position: absolute; left: 50px; top: 186px; font-weight: 600; font-size: 30px; opacity: .85; }
    .mark { position: absolute; left: 48px; bottom: 42px; font-weight: 800; font-size: 26px; opacity: .6; }`,
  `<div class="blob"></div><div class="k">${label}</div><div class="b1">${b1}</div><div class="b2">${b2}</div>
   <img class="p" src="${cut(m.key)}" style="left:150px;top:250px;width:560px;height:500px"><div class="mark">ShopHub</div>`));
}

// ---- render ----
const browser = await chromium.launch();
const ctx = await browser.newContext({ deviceScaleFactor: 1 });
const tab = await ctx.newPage();
for (const j of jobs) {
  const file = join(OUT, `${j.name}.html`);
  writeFileSync(file, j.html);
  await tab.setViewportSize({ width: j.w, height: j.h });
  await tab.goto(pathToFileURL(file).href);
  await tab.evaluate(() => document.fonts.ready);
  await tab.waitForLoadState('networkidle');
  await tab.screenshot({ path: join(OUT, `${j.name}.png`), omitBackground: j.transparent });
}
await browser.close();
writeFileSync(join(OUT, 'covers.json'), JSON.stringify(covered.map((m) => m.key)));
console.log(`art-html: ${jobs.length} images, ${covered.length} marketing covers`);
