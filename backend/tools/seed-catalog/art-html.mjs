// Artwork of the sample data drawn as HTML/CSS and rendered by Chromium (Playwright of e2e/), with lucide icons and the
// Be Vietnam Pro font of web/node_modules — far better typography than drawing with Pillow. Run after build.py:
//   node backend/tools/seed-catalog/art-html.mjs && python backend/tools/seed-catalog/art-png-to-webp.py
// Writes PNG files to .cache/art-png/ (emptied first); art-png-to-webp.py turns them into Seed/Data/Art/*.webp and the
// ad covers into Seed/Data/ProductImages/pNNN-ad.webp.
// G-VIS: layout / size / density learnt from the big marketplaces, never their logos, photos, fonts or programme names.
// Every claim drawn matches the platform's rules or the sample data (decision #180, G2-B5): the platform vouchers
// SHOPHUB50 (₫50.000 off from ₫250.000), FREESHIP (shipping up to ₫30.000), SALE12 (12 % up to ₫100.000 from ₫500.000),
// Flash Sale items 10–50 % off (MarketingSeeder / FlashAutoOpener), returns within 15 days, COD, the 10.10 campaign.
// The shop logos are NOT drawn here: art.py --logos draws them (wordmarks, G4-A1).
import { createRequire } from 'node:module';
import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync, existsSync } from 'node:fs';
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
const CUTOUTS = join(CACHE, 'cutouts');
const OUT = join(CACHE, 'art-png');
rmSync(OUT, { recursive: true, force: true });  // no stale PNG (e.g. an old shop logo) may reach Seed/Data
mkdirSync(OUT, { recursive: true });

const fontDir = join(root, 'web', 'node_modules', '@fontsource', 'be-vietnam-pro', 'files');
const FONTS = [400, 500, 600, 700, 800, 900].filter((w) => existsSync(join(fontDir, `be-vietnam-pro-latin-${w}-normal.woff2`))).map((w) => `
  @font-face { font-family: BVP; font-weight: ${w}; src: url(${pathToFileURL(join(fontDir, `be-vietnam-pro-latin-${w}-normal.woff2`)).href}) format('woff2'); }
  @font-face { font-family: BVP; font-weight: ${w}; src: url(${pathToFileURL(join(fontDir, `be-vietnam-pro-vietnamese-${w}-normal.woff2`)).href}) format('woff2'); unicode-range: U+0102-0103, U+0110-0111, U+0128-0129, U+0168-0169, U+01A0-01A1, U+01AF-01B0, U+0300-0301, U+0303-0304, U+0308-0309, U+0323, U+0329, U+1EA0-1EF9, U+20AB; }`).join('');

const icon = (name, size, color = '#fff', stroke = 2.2) =>
  renderToStaticMarkup(React.createElement(icons[name], { size, color, strokeWidth: stroke }));

const models = JSON.parse(readFileSync(join(DATA, 'product-models.json'), 'utf8'));
const byKey = new Map(models.map((m) => [m.key, m]));
const pad = (k) => String(k).padStart(3, '0');
// Trimmed transparent cut-out made by build.py (photo.py), or null (a real-scene photo / a white object on white)
const cutout = (key, n = 0) => {
  const f = join(CUTOUTS, `p${pad(key)}-${n}.png`);
  return existsSync(f) ? pathToFileURL(f).href : null;
};
const cut = (key) => cutout(key) ?? (() => { throw new Error(`no cut-out for model ${key}`); })();
const photoOf = (key) => pathToFileURL(join(DATA, 'ProductImages', `p${pad(key)}-0.webp`)).href;

const page = (w, h, css, body) => `<!doctype html><html><head><meta charset="utf-8"><style>${FONTS}
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: ${w}px; height: ${h}px; overflow: hidden; font-family: BVP, sans-serif; position: relative; }
  .p { position: absolute; object-fit: contain; filter: drop-shadow(0 14px 16px rgba(0,0,0,.28)); }
  .burst { clip-path: polygon(50% 0%, 61% 13%, 77% 6%, 79% 23%, 96% 25%, 88% 40%, 100% 52%, 86% 62%, 92% 79%, 75% 79%, 69% 96%, 55% 86%, 41% 99%, 34% 83%, 17% 90%, 17% 73%, 0% 67%, 11% 53%, 2% 38%, 18% 32%, 18% 14%, 35% 17%); }
  ${css}</style></head><body>${body}<script>
  // Shrink every [data-w] text block until it fits its width (called by the renderer once the fonts are loaded)
  window.fit = () => document.querySelectorAll('[data-w]').forEach((el) => {
    const w = +el.dataset.w;
    const parts = el.children.length ? [...el.children] : [el];
    for (let k = 0; k < 40 && el.scrollWidth > w; k++)
      parts.forEach((d) => { d.style.fontSize = parseFloat(getComputedStyle(d).fontSize) * 0.95 + 'px'; });
  });
  </script></body></html>`;

const jobs = [];
const add = (name, w, h, html, scale = 1, transparent = false) => jobs.push({ name, w, h, html, scale, transparent });

// ============================== 1. ad covers (≈ 40 % of the models) ==============================
// Industry colours: main, dark, accent (text on main is white)
const TOP = {
  'Điện Thoại & Phụ Kiện': ['#1565c0', '#0a2f6b', '#ffd23f'], 'Máy Tính & Laptop': ['#3949ab', '#1a1f5c', '#7cf2ff'],
  'Thiết Bị Điện Tử': ['#00838f', '#003d44', '#ffe14d'], 'Máy Ảnh & Quay Phim': ['#37474f', '#151d21', '#ffb300'],
  'Đồng Hồ': ['#4e342e', '#1b0f0c', '#e8c76a'], 'Thời Trang Nam': ['#00695c', '#003b33', '#ffd54f'],
  'Thời Trang Nữ': ['#c2185b', '#6d0b33', '#ffe082'], 'Giày Dép Nam': ['#e64a19', '#7a2205', '#fff176'],
  'Giày Dép Nữ': ['#ad1457', '#5c0a2e', '#ffd1e3'], 'Túi Ví Nữ': ['#bf360c', '#5d1a05', '#ffe0b2'],
  'Mẹ & Bé': ['#0288d1', '#01467a', '#fff59d'], 'Nhà Cửa & Đời Sống': ['#2e7d32', '#123d16', '#ffeb3b'],
  'Sắc Đẹp': ['#d81b60', '#6e0a30', '#ffe57f'], 'Sức Khỏe': ['#00897b', '#00423b', '#fff176'],
  'Thể Thao & Du Lịch': ['#ef6c00', '#7a3300', '#fff59d'], 'Ô Tô & Xe Máy': ['#c62828', '#5e0f0f', '#ffd54f'],
  'Đồ Chơi': ['#7b1fa2', '#3a0b4f', '#ffeb3b'], 'Bách Hóa Online': ['#558b2f', '#273f14', '#fff176'],
  'Thú Cưng': ['#f57c00', '#7a3d00', '#fff9c4'],
};
const PALETTES = [
  ['#e8361a', '#8f0f05', '#ffe066'], ['#1452c4', '#0a2a6e', '#ffd23f'], ['#c2189b', '#5e0b52', '#fff176'], ['#11884f', '#0a4a2b', '#ffeb3b'],
  ['#00838f', '#003d44', '#ffe14d'], ['#6a1b9a', '#2e0a45', '#ffd1f0'], ['#ef6c00', '#7a3300', '#fff59d'], ['#263238', '#0d1317', '#ffc107'],
];
// How many listings an industry gets (ProductGenerator.Popularity): popular industries get more covers
const POPULAR = new Set(['Điện Thoại & Phụ Kiện', 'Thời Trang Nam', 'Thời Trang Nữ', 'Sắc Đẹp', 'Giày Dép Nam', 'Giày Dép Nữ', 'Túi Ví Nữ',
  'Máy Tính & Laptop', 'Thiết Bị Điện Tử']);
const topOf = (m) => m.category.split('/')[0];

// Pick ≈ 40 %: per industry, branded models and models with a clean cut-out first, then spread over the list
const covered = [];
const industries = [...new Set(models.map(topOf))];
for (const top of industries) {
  const list = models.filter((m) => topOf(m) === top);
  const want = Math.round(list.length * (POPULAR.has(top) ? 0.46 : 0.36));
  const ranked = list.map((m, i) => ({ m, score: (m.brand ? 2 : 0) + (cutout(m.key) ? 3 : 0) + ((i * 7) % 5) / 5 }))
    .sort((a, b) => b.score - a.score || a.m.key - b.m.key);
  covered.push(...ranked.slice(0, want).map((r) => r.m));
}
covered.sort((a, b) => a.key - b.key);

// The benefit lines a cover can carry: true for every listing of the model (platform vouchers and rules, the model's data)
const warranty = (m) => { const w = m.attributes['Bảo hành']?.[0]; return w && !/^Không/.test(w) ? w : null; };
const BENEFITS = {
  freeship: { big: ['FREESHIP'], sub: 'Mã FREESHIP giảm đến ₫30.000 phí ship', icon: 'Truck' },
  voucher50: { big: ['GIẢM', '₫50K'], sub: 'Mã SHOPHUB50 · đơn từ ₫250.000', icon: 'Ticket' },
  sale12: { big: ['GIẢM', '12%'], sub: 'Mã SALE12 · tối đa ₫100.000', icon: 'BadgePercent' },
  returns: { big: ['ĐỔI TRẢ', '15 NGÀY'], sub: 'Trả hàng, hoàn tiền dễ dàng', icon: 'RotateCcw' },
  cod: { big: ['COD'], sub: 'Nhận hàng rồi mới trả tiền', icon: 'Wallet' },
  genuine: (m) => ({ big: ['CHÍNH HÃNG'], sub: `Thương hiệu ${m.brand}`, icon: 'ShieldCheck' }),
  warranty: (m) => ({ big: ['BẢO HÀNH', warranty(m).toUpperCase()], sub: 'Bảo hành theo chính sách của shop', icon: 'ShieldCheck' }),
};
const CHIPS = {
  freeship: ['Truck', 'Freeship ₫30K'], voucher50: ['Ticket', 'Mã giảm ₫50K'], sale12: ['BadgePercent', 'Mã giảm 12%'],
  returns: ['RotateCcw', 'Đổi trả 15 ngày'], cod: ['Wallet', 'Thanh toán COD'], genuine: ['ShieldCheck', 'Chính hãng'],
  warranty: ['ShieldCheck', 'Có bảo hành'],
};
const benefitsOf = (m) => [m.brand ? 'genuine' : null, warranty(m) ? 'warranty' : null, 'freeship', 'voucher50', 'returns', 'sale12', 'cod']
  .filter(Boolean);

const chipRow = (keys, fg, bg, size = 24) => keys.map((k) => `<span class="chip" style="color:${fg};background:${bg}">${icon(CHIPS[k][0], size + 4, fg, 2.4)}${CHIPS[k][1]}</span>`).join('');
const brandMark = (m, color, bg, pos = 'left:30px;top:30px') =>
  `<div class="brand" style="color:${color};background:${bg};${pos}">${m.brand ?? `${icon('ShoppingBag', 24, color, 2.4)}ShopHub`}</div>`;
// The product: its cut-out, or — no clean cut-out — its photo in a rounded white card
const product = (m, x, y, w, h, extra = '') => {
  const c = cutout(m.key);
  return c
    ? `<img class="p" src="${c}" style="left:${x}px;top:${y}px;width:${w}px;height:${h}px;${extra}">`
    : `<div style="position:absolute;left:${x + w * 0.04}px;top:${y + h * 0.04}px;width:${Math.min(w, h) * 0.92}px;height:${Math.min(w, h) * 0.92}px;border-radius:28px;overflow:hidden;background:#fff;box-shadow:0 18px 40px rgba(0,0,0,.28);border:8px solid #fff"><img src="${photoOf(m.key)}" style="width:100%;height:100%;object-fit:cover"></div>`;
};
const bigText = (lines, size) => lines.map((l, i) => `<div style="font-size:${i === 0 && lines.length > 1 && l.length > 6 ? size * 0.62 : size}px">${l}</div>`).join('');

const COMMON = `
  .big { position: absolute; font-weight: 900; line-height: .92; letter-spacing: -.035em; text-transform: uppercase; white-space: nowrap; }
  .big > div { white-space: nowrap; }
  .sub { position: absolute; font-weight: 600; font-size: 28px; line-height: 1.2; }
  .chips { position: absolute; display: flex; flex-wrap: wrap; gap: 10px; }
  .chip { display: inline-flex; align-items: center; gap: 8px; padding: 9px 16px 9px 12px; border-radius: 99px; font-weight: 700; font-size: 24px; white-space: nowrap; }
  .brand { position: absolute; display: inline-flex; align-items: center; gap: 8px; padding: 8px 18px; border-radius: 99px; font-weight: 800; font-size: 26px; letter-spacing: -.01em; }`;

// Nine templates (800×800): colour block / gradient, the product large and off-centre, one huge benefit line, 2–3 icons
const TEMPLATES = [
  // 0 slab: a colour slab on the left with the benefit, the product overlapping it on the right
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: linear-gradient(160deg, #fff, ${main}22); }
    .slab { position: absolute; left: 0; top: 0; bottom: 0; width: 330px; background: linear-gradient(180deg, ${main}, ${dark}); }`,
  `<div class="slab"></div>${brandMark(m, main, '#fff')}
   <div class="big" data-w="280" style="left:34px;top:110px;color:#fff">${bigText(b.big, 96)}</div>
   <div class="sub" style="left:36px;top:${b.big.length > 1 ? 318 : 230}px;width:270px;color:${acc}">${b.sub}</div>
   ${product(m, 250, 120, 540, 560)}
   <div class="chips" style="left:34px;bottom:34px;flex-direction:column;align-items:flex-start">${chipRow(chips, dark, '#fff')}</div>`),
  // 1 band: the product big on top, a bottom band with the benefit
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: radial-gradient(circle at 50% 35%, #fff 0%, ${main}1f 70%); }
    .band { position: absolute; left: 0; right: 0; bottom: 0; height: 210px; background: linear-gradient(90deg, ${dark}, ${main}); }`,
  `${product(m, 110, 40, 580, 520)}<div class="band"></div>${brandMark(m, '#fff', main)}
   <div class="big" data-w="720" style="left:40px;bottom:${b.big.length > 1 ? 40 : 70}px;color:#fff;display:flex;gap:18px;align-items:flex-end">${b.big.map((l) => `<div style="font-size:${b.big.length > 1 ? 92 : 112}px">${l}</div>`).join('')}</div>
   <div class="sub" style="left:42px;bottom:${b.big.length > 1 ? 150 : 178}px;color:${acc};font-size:26px">${b.sub}</div>
   <div class="chips" style="right:28px;top:110px;flex-direction:column;align-items:flex-end">${chipRow(chips.slice(0, 2), '#fff', main)}</div>`),
  // 2 burst: full-bleed gradient, a light disc behind the product, a starburst sticker with the benefit
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: radial-gradient(circle at 70% 60%, ${main}, ${dark} 80%); }
    .disc { position: absolute; left: 250px; top: 190px; width: 560px; height: 560px; border-radius: 50%; background: radial-gradient(circle, #fff 0%, #ffffffcc 55%, #ffffff00 72%); }`,
  `<div class="disc"></div>${product(m, 240, 200, 540, 540)}
   <div class="burst" style="position:absolute;left:-14px;top:40px;width:400px;height:400px;background:${acc};transform:rotate(-8deg)"></div>
   <div class="big" data-w="280" style="left:65px;top:${b.big.length > 1 ? 150 : 190}px;width:280px;text-align:center;color:${dark};transform:rotate(-8deg)">${bigText(b.big, b.big[0].length > 8 ? 62 : 84)}</div>
   ${brandMark(m, dark, '#fff', 'right:30px;top:30px')}
   <div class="chips" style="left:30px;bottom:34px">${chipRow(chips.slice(0, 2), dark, '#fff')}</div>`),
  // 3 header: a colour header with the benefit, the product overlapping it, icons stacked on the left
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: #fff; }
    .head { position: absolute; left: 0; right: 0; top: 0; height: 250px; background: linear-gradient(120deg, ${main}, ${dark}); border-radius: 0 0 60px 60px; }`,
  `<div class="head"></div>
   <div class="big" data-w="720" style="left:40px;top:40px;color:#fff;display:flex;gap:16px;align-items:baseline">${b.big.map((l) => `<div style="font-size:${b.big.join('').length > 12 ? 80 : 100}px">${l}</div>`).join('')}</div>
   <div class="sub" style="left:42px;top:150px;color:${acc}">${b.sub}</div>
   ${product(m, 210, 210, 570, 570)}
   <div class="chips" style="left:30px;top:300px;flex-direction:column;align-items:flex-start">${chipRow(chips, '#fff', main)}</div>
   ${brandMark(m, main, '#fff', `left:30px;bottom:30px;border:3px solid ${main}`)}`),
  // 4 diagonal: a diagonal colour field, the benefit on it, the product huge at the bottom right
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: linear-gradient(135deg, ${acc}55, #fff 60%); }
    .diag { position: absolute; inset: 0; background: linear-gradient(135deg, ${main}, ${dark}); clip-path: polygon(0 0, 100% 0, 100% 18%, 0 62%); }`,
  `<div class="diag"></div>${brandMark(m, main, '#fff', 'right:30px;top:30px')}
   <div class="big" data-w="560" style="left:40px;top:60px;color:#fff">${bigText(b.big, 104)}</div>
   <div class="sub" style="left:42px;top:${b.big.length > 1 ? 262 : 180}px;color:${acc};width:440px">${b.sub}</div>
   ${product(m, 230, 250, 560, 540)}
   <div class="chips" style="left:30px;bottom:34px;flex-direction:column;align-items:flex-start">${chipRow(chips.slice(0, 2), '#fff', dark)}</div>`),
  // 5 ticket: the product on the left, a voucher ticket on the right
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: ${main}14; background-image: radial-gradient(${main}30 2px, transparent 2px); background-size: 28px 28px; }
    .ticket { position: absolute; right: 30px; top: 150px; width: 300px; height: 420px; border-radius: 24px; background: linear-gradient(180deg, ${main}, ${dark}); box-shadow: 0 16px 34px ${dark}55;
      -webkit-mask: radial-gradient(circle 22px at 0 50%, transparent 98%, #000) left / 51% 100% no-repeat, radial-gradient(circle 22px at 100% 50%, transparent 98%, #000) right / 51% 100% no-repeat; }
    .cutline { position: absolute; right: 52px; top: 360px; width: 256px; border-top: 4px dashed ${acc}; }`,
  `${product(m, 10, 120, 520, 600)}<div class="ticket"></div><div class="cutline"></div>
   <div class="big" data-w="300" style="right:30px;top:${b.big.length > 1 ? 190 : 225}px;width:300px;text-align:center;color:#fff">${bigText(b.big, b.big.join('').length > 10 ? 58 : 76)}</div>
   <div class="sub" style="right:50px;top:384px;width:260px;text-align:center;color:${acc};font-size:24px">${b.sub}</div>
   ${brandMark(m, '#fff', main)}
   <div class="chips" style="left:30px;right:30px;bottom:34px;justify-content:center">${chipRow(chips, dark, '#fff')}</div>`),
  // 6 frame: a thick colour frame, the benefit in the bottom bar
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: ${main}; }
    .in { position: absolute; left: 24px; top: 24px; right: 24px; bottom: 170px; border-radius: 26px; background: radial-gradient(circle at 50% 45%, #fff, ${acc}40); }`,
  `<div class="in"></div>${product(m, 90, 70, 620, 540)}
   <div class="big" data-w="450" style="left:36px;bottom:38px;color:#fff;display:flex;gap:16px;align-items:baseline">${b.big.map((l) => `<div style="font-size:${b.big.join('').length > 12 ? 72 : 96}px">${l}</div>`).join('')}</div>
   <div class="chips" style="right:30px;bottom:44px;flex-direction:column;align-items:flex-end">${chipRow(chips.slice(0, 2), dark, acc, 20)}</div>
   ${brandMark(m, '#fff', dark, 'left:44px;top:44px')}`),
  // 7 luxe: a dark gradient, gold benefit, the product with a glow
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: radial-gradient(circle at 62% 55%, ${main} 0%, ${dark} 55%, #0b0b0f 100%); }
    .glow { position: absolute; left: 260px; top: 200px; width: 520px; height: 520px; border-radius: 50%; background: radial-gradient(circle, ${acc}66, transparent 65%); }`,
  `<div class="glow"></div>${product(m, 250, 190, 530, 560)}
   <div class="big" data-w="440" style="left:40px;top:${b.big.length > 1 ? 70 : 100}px;color:${acc}">${bigText(b.big, 92)}</div>
   <div class="sub" style="left:42px;top:${b.big.length > 1 ? 250 : 200}px;color:#fff;width:300px;font-weight:500">${b.sub}</div>
   <div class="chips" style="left:30px;bottom:34px;flex-direction:column;align-items:flex-start">${chipRow(chips, acc, '#ffffff14')}</div>
   ${brandMark(m, acc, 'transparent', `right:30px;top:30px;border:2px solid ${acc}`)}`),
  // 8 circle: a pastel field, a big colour circle carrying the product, the benefit at the top left
  (m, [main, dark, acc], b, chips) => page(800, 800, `${COMMON}
    body { background: linear-gradient(180deg, ${acc}66, #fff); }
    .circle { position: absolute; right: -90px; bottom: -110px; width: 680px; height: 680px; border-radius: 50%; background: linear-gradient(135deg, ${main}, ${dark}); }`,
  `<div class="circle"></div>${product(m, 230, 210, 550, 560)}
   <div class="big" data-w="540" style="left:40px;top:${b.big.length > 1 ? 50 : 70}px;color:${dark}">${bigText(b.big, 100)}</div>
   <div class="sub" style="left:42px;top:${b.big.length > 1 ? 250 : 175}px;color:${dark};width:400px">${b.sub}</div>
   <div class="chips" style="left:30px;bottom:34px;flex-direction:column;align-items:flex-start">${chipRow(chips.slice(0, 2), '#fff', dark)}</div>
   ${brandMark(m, '#fff', main, 'right:30px;top:30px')}`),
];

const perTop = new Map();
const usage = new Array(TEMPLATES.length).fill(0);
for (const m of covered) {
  const top = topOf(m);
  const n = perTop.get(top) ?? 0;
  perTop.set(top, n + 1);
  const t = (industries.indexOf(top) * 4 + n * 5) % TEMPLATES.length;  // consecutive covers of an industry differ
  usage[t]++;
  const keys = benefitsOf(m);
  const main = keys[(m.key * 7 + n) % keys.length];
  const b = typeof BENEFITS[main] === 'function' ? BENEFITS[main](m) : BENEFITS[main];
  const chips = keys.filter((k) => k !== main && !(main === 'genuine' && k === 'warranty') && !(main === 'warranty' && k === 'genuine'))
    .slice(0, 3);
  // Colour: the industry's own on its first cover, then the shared palettes in turn (a row of covers never looks alike)
  const pal = n % 3 === 0 ? TOP[top] ?? PALETTES[0] : PALETTES[(industries.indexOf(top) + n * 3) % PALETTES.length];
  add(`cover-p${pad(m.key)}`, 800, 800, TEMPLATES[t](m, pal, b, chips));
}

// ============================== 2. home banners ==============================
const ticket = (top, big, color = '#d0011b') => `<div class="tk" style="color:${color}"><div class="tk-t">${top}</div><div class="tk-b">${big}</div></div>`;
const BANNER_CSS = `
  .ribbon { position: absolute; left: 22px; top: 0; display: flex; align-items: stretch; height: 30px; border-radius: 0 0 10px 10px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,.2); }
  .ribbon b { display: flex; align-items: center; padding: 0 12px; font-weight: 900; font-size: 16px; letter-spacing: -.01em; }
  .title { position: absolute; font-weight: 900; line-height: 1; letter-spacing: -.03em; text-transform: uppercase; text-shadow: 0 3px 0 rgba(0,0,0,.18); }
  .num { position: absolute; display: flex; align-items: center; gap: 10px; padding: 6px 18px 6px 16px; border-radius: 14px; box-shadow: 0 8px 18px rgba(0,0,0,.25); }
  .num .l { font-weight: 800; font-size: 18px; line-height: 1.02; text-transform: uppercase; }
  .num .n { font-weight: 900; font-size: 62px; line-height: 1; letter-spacing: -.04em; }
  .row { position: absolute; display: flex; gap: 10px; align-items: center; }
  .tk { position: relative; white-space: nowrap; background: #fff; border-radius: 8px; padding: 5px 8px 5px 14px; box-shadow: 0 5px 12px rgba(0,0,0,.22); border-left: 4px dashed currentColor; text-align: center; line-height: 1.05; }
  .tk-t { font-weight: 700; font-size: 11px; letter-spacing: .04em; text-transform: uppercase; opacity: .85; }
  .tk-b { font-weight: 900; font-size: 19px; letter-spacing: -.02em; }
  .note { position: absolute; font-size: 9px; font-weight: 500; opacity: .8; }
  .stk { position: absolute; display: grid; place-items: center; text-align: center; font-weight: 900; line-height: .95; }`;
const sticker = (x, y, size, rot, bg, color, html) =>
  `<div class="burst" style="position:absolute;left:${x}px;top:${y}px;width:${size}px;height:${size}px;background:${bg};transform:rotate(${rot}deg);filter:drop-shadow(0 6px 10px rgba(0,0,0,.25))"></div>
   <div class="stk" style="left:${x}px;top:${y}px;width:${size}px;height:${size}px;color:${color};transform:rotate(${rot}deg)">${html}</div>`;
const items = (list) => list.map(([k, x, y, w, h, r = 0]) => `<img class="p" src="${cut(k)}" style="left:${x}px;top:${y}px;width:${w}px;height:${h}px;transform:rotate(${r}deg)">`).join('');
const PAL = {
  red: { bg: 'linear-gradient(110deg,#ff5a1f 0%,#e8221b 48%,#a8100d 100%)', rib: ['#ffd23f', '#b71c1c'], rib2: ['#1a237e', '#fff'], num: ['#fff', '#d0011b'], title: '#fff', acc: '#ffe066' },
  blue: { bg: 'linear-gradient(110deg,#1e88e5 0%,#1452c4 55%,#0b2e86 100%)', rib: ['#ffd23f', '#0b2e86'], rib2: ['#e8221b', '#fff'], num: ['#ffd23f', '#0b2e86'], title: '#fff', acc: '#ffd23f' },
  pink: { bg: 'linear-gradient(110deg,#ff5fa2 0%,#c2189b 50%,#6a1b9a 100%)', rib: ['#fff', '#ad1457'], rib2: ['#ffd23f', '#6a1b9a'], num: ['#fff', '#ad1457'], title: '#fff', acc: '#fff176' },
  green: { bg: 'linear-gradient(110deg,#2bbf6a 0%,#118a4f 55%,#0b5d39 100%)', rib: ['#ffd23f', '#0b5d39'], rib2: ['#e8221b', '#fff'], num: ['#fff', '#0b7a43'], title: '#fff', acc: '#ffeb3b' },
};
const deco = `<div style="position:absolute;inset:0;background-image:radial-gradient(rgba(255,255,255,.16) 1.5px,transparent 1.5px);background-size:16px 16px;mask-image:linear-gradient(90deg,transparent 35%,#000 85%)"></div>
  <div style="position:absolute;left:420px;top:-120px;width:360px;height:360px;border-radius:50%;border:26px solid rgba(255,255,255,.08)"></div>`;

// Hero slides 797×235 drawn at 2× (1594×470): ribbon, two-line title, number block, small tickets, products, starburst
const HEROES = [
  { name: 'hero-1', pal: 'red', rib: ['FLASH SALE', 'MỖI NGÀY'], title: ['Săn deal chớp nhoáng', 'giá sốc mỗi khung giờ'], num: ['Giảm<br>đến', '50%'],
    tickets: [['Mã freeship', '₫30K'], ['Mã giảm', '₫50K']], items: [[123, 560, 20, 120, 210, -6], [100, 650, 110, 110, 110], [106, 690, 22, 92, 120, 8], [105, 510, 130, 80, 100, -10]],
    sticker: [455, 30, 92, -12, '#ffd23f', '#c62828', '<div><div style="font-size:12px">FLASH</div><div style="font-size:24px">SALE</div></div>'], note: 'Giảm 10–50% trên sản phẩm Flash Sale, số lượng có hạn.' },
  { name: 'hero-2', pal: 'blue', rib: ['FREESHIP', 'MỌI ĐƠN'], title: ['Miễn phí vận chuyển', 'cho mọi đơn hàng'], num: ['Freeship<br>đến', '₫30K'],
    tickets: [['Nhập mã', 'FREESHIP'], ['Mã giảm', '₫50K']], items: [[88, 560, 90, 170, 130, -12], [78, 600, 10, 170, 120], [172, 700, 100, 90, 110, 6]],
    sticker: [470, 20, 92, 10, '#ffd23f', '#0b2e86', '<div><div style="font-size:26px">0Đ</div><div style="font-size:11px">PHÍ SHIP*</div></div>'], note: '*Mã FREESHIP giảm phí vận chuyển tối đa ₫30.000 mỗi đơn.' },
  { name: 'hero-3', pal: 'pink', rib: ['MỸ PHẨM', 'CHÍNH HÃNG'], title: ['Đẹp chuẩn hãng', 'ưu đãi ngập tràn'], num: ['Mã<br>giảm', '₫50K'],
    tickets: [['Mã', 'SHOPHUB50'], ['Đơn từ', '₫250K']], items: [[8, 560, 18, 100, 200, -4], [7, 640, 40, 100, 180, 5], [4, 720, 120, 60, 100, 12], [2, 520, 150, 110, 80, -8]],
    sticker: [455, 22, 92, -10, '#fff176', '#ad1457', '<div><div style="font-size:12px">MỚI</div><div style="font-size:22px">HOT</div></div>'], note: 'Mã SHOPHUB50 giảm ₫50.000 cho đơn từ ₫250.000.' },
  { name: 'hero-4', pal: 'red', rib: ['SIÊU SALE', '10.10'], title: ['Siêu sale 10.10', 'deal lớn nhất tháng'], num: ['Giảm', '12%'],
    tickets: [['Mã', 'SALE12'], ['Tối đa', '₫100K'], ['Freeship', '₫30K']], items: [[6, 560, 30, 90, 180, -5], [124, 630, 20, 90, 190, 6], [186, 690, 120, 100, 100, -8], [176, 520, 140, 90, 90, 4]],
    sticker: [470, 26, 90, 12, '#ffd23f', '#b71c1c', '<div><div style="font-size:24px">10.10</div><div style="font-size:11px">SIÊU SALE</div></div>'], note: 'Mã SALE12 giảm 12% tối đa ₫100.000 cho đơn từ ₫500.000.' },
  { name: 'hero-5', pal: 'blue', rib: ['SHOPHUB', 'MALL'], title: ['Công nghệ chính hãng', 'trả hàng 15 ngày'], num: ['Đổi trả<br>trong', '15 NGÀY'],
    tickets: [['Hàng', 'Chính hãng'], ['Freeship', '₫30K']], items: [[78, 540, 30, 190, 140, -4], [101, 680, 100, 100, 110, 8], [123, 640, 10, 80, 150, 6], [159, 520, 150, 100, 80, -6]],
    sticker: [455, 24, 88, -8, '#ffd23f', '#0b2e86', '<div><div style="font-size:11px">CHÍNH</div><div style="font-size:20px">HÃNG</div></div>'], note: 'Cam kết của ShopHub Mall: hàng chính hãng, trả hàng trong 15 ngày.' },
  { name: 'hero-6', pal: 'green', rib: ['NHÀ CỬA', 'GIÁ TỐT'], title: ['Nhà đẹp mỗi ngày', 'deal dưới ₫200K'], num: ['Deal<br>dưới', '₫200K'],
    tickets: [['Mã giảm', '₫50K'], ['Freeship', '₫30K']], items: [[51, 560, 20, 110, 200, -4], [71, 640, 110, 130, 110, 4], [12, 640, 20, 150, 90, 0], [307, 520, 120, 90, 110, -6]],
    sticker: [455, 24, 90, 10, '#ffd23f', '#0b5d39', '<div><div style="font-size:12px">GIA</div><div style="font-size:20px">DỤNG</div></div>'], note: 'Hàng gia dụng, bếp, trang trí — nhiều lựa chọn dưới ₫200.000.' },
];
for (const h of HEROES) {
  const p = PAL[h.pal];
  add(h.name, 797, 235, page(797, 235, `${BANNER_CSS} body { background: ${p.bg}; color: ${p.title}; }`,
    `${deco}
     <div class="ribbon"><b style="background:${p.rib[0]};color:${p.rib[1]}">${h.rib[0]}</b><b style="background:${p.rib2[0]};color:${p.rib2[1]}">${h.rib[1]}</b></div>
     <div class="title" style="left:24px;top:44px;font-size:30px">${h.title[0]}</div>
     <div class="title" style="left:24px;top:78px;font-size:22px;color:${p.acc}">${h.title[1]}</div>
     <div class="num" style="left:24px;top:110px;background:${p.num[0]};color:${p.num[1]}"><div class="l">${h.num[0]}</div><div class="n">${h.num[1]}</div></div>
     <div class="row" style="left:24px;top:186px">${h.tickets.map(([t, b]) => ticket(t, b, p.num[1])).join('')}</div>
     ${items(h.items)}${sticker(...h.sticker)}
     <div class="note" style="right:10px;bottom:3px">${h.note}</div>`), 2);
}

// Side banners 398×115 at 2×, same style
const SIDES = [
  { name: 'side-1', pal: 'pink', rib: 'MÃ GIẢM GIÁ CỦA SÀN', title: 'Voucher mỗi ngày', num: ['Mã<br>giảm', '₫50K'], tk: ['SHOPHUB50'], items: [[8, 300, 18, 46, 90, -6], [10, 335, 30, 50, 80, 6]] },
  { name: 'side-2', pal: 'blue', rib: 'FREESHIP MỌI ĐƠN', title: 'Miễn phí vận chuyển', num: ['Freeship<br>đến', '₫30K'], tk: ['FREESHIP'], items: [[88, 270, 34, 110, 70, -10], [78, 300, 6, 80, 50, 0]] },
];
for (const s of SIDES) {
  const p = PAL[s.pal];
  add(s.name, 398, 115, page(398, 115, `${BANNER_CSS} body { background: ${p.bg}; color: #fff; }
    .ribbon { height: 20px; left: 12px; } .ribbon b { font-size: 11px; padding: 0 8px; }
    .num { padding: 3px 10px; border-radius: 9px; } .num .l { font-size: 10px; } .num .n { font-size: 34px; }
    .tk { padding: 2px 6px 2px 9px; border-left-width: 3px; } .tk-b { font-size: 12px; }`,
  `${deco}<div class="ribbon"><b style="background:${p.rib[0]};color:${p.rib[1]}">${s.rib}</b></div>
   <div class="title" style="left:12px;top:27px;font-size:17px">${s.title}</div>
   <div class="row" style="left:12px;top:52px;gap:6px"><div class="num" style="position:relative;background:${p.num[0]};color:${p.num[1]}"><div class="l">${s.num[0]}</div><div class="n">${s.num[1]}</div></div>
   ${s.tk.map((t) => `<div class="tk" style="color:${p.num[1]}"><div class="tk-b">${t}</div></div>`).join('')}</div>
   ${items(s.items)}`), 2);
}

// Strip of three ≈ 390×100 banners (between the Flash Sale and the categories), at 2×
const STRIPS = [
  { name: 'strip-1', bg: 'linear-gradient(100deg,#b3131b,#e8301f 60%,#ff6a2b)', small: 'Deal thương hiệu', big: 'GIẢM ĐẾN', num: '50%', color: '#ffe066', items: [[6, 250, 8, 40, 84, -6], [106, 288, 20, 52, 70, 6]] },
  { name: 'strip-2', bg: 'linear-gradient(100deg,#0b2e86,#1452c4 60%,#1e88e5)', small: 'ShopHub Mall', big: 'CHÍNH HÃNG', num: '100%', color: '#ffd23f', mall: true, items: [[123, 262, 10, 40, 80, -6], [100, 294, 40, 48, 50, 0]] },
  { name: 'strip-3', bg: 'linear-gradient(100deg,#7a4b12,#c58a2a 55%,#f2c46b)', small: 'Thời trang thu đông', big: 'MÃ GIẢM', num: '₫50K', color: '#fff7d6', items: [[239, 250, 6, 64, 88, -4], [238, 300, 10, 60, 84, 6]] },
];
for (const s of STRIPS) {
  add(s.name, 390, 100, page(390, 100, `${BANNER_CSS} body { background: ${s.bg}; color: #fff; }
    .arrow { position: absolute; right: 10px; top: 33px; width: 34px; height: 34px; border-radius: 50%; background: #fff; display: grid; place-items: center; box-shadow: 0 3px 8px rgba(0,0,0,.25); }`,
  `<div style="position:absolute;inset:0;background-image:radial-gradient(rgba(255,255,255,.14) 1.2px,transparent 1.2px);background-size:12px 12px"></div>
   ${s.mall ? `<div style="position:absolute;left:12px;top:12px;padding:3px 10px;border-radius:6px;background:#fff;color:#0b2e86;font-weight:900;font-size:15px">ShopHub Mall</div>`
    : `<div class="title" style="left:12px;top:12px;font-size:17px">${s.small}</div>`}
   <div class="title" style="left:12px;top:44px;font-size:15px;line-height:1.05;width:70px">${s.big}</div>
   <div class="title" style="left:${s.big.length > 8 ? 108 : 86}px;top:34px;font-size:46px;color:${s.color}">${s.num}</div>
   ${items(s.items)}<div class="arrow">${icon('ChevronRight', 24, '#333', 3)}</div>`), 2);
}

// Mall banner: portrait at the left of the "ShopHub Mall" block (the web frames it 480:660), at 1.5×
add('mall-deal', 480, 660, page(480, 660, `${BANNER_CSS}
  body { background: radial-gradient(circle at 50% 30%, #ff9a3c 0%, #ff5a1f 40%, #c9180f 100%); color: #fff; }
  .rays { position: absolute; inset: 0; background: repeating-conic-gradient(from 0deg at 50% 32%, rgba(255,255,255,.10) 0 7deg, transparent 7deg 18deg); }
  .h { position: absolute; left: 0; right: 0; text-align: center; font-weight: 900; letter-spacing: -.03em; line-height: .9; text-transform: uppercase; text-shadow: 0 6px 0 #a10e0a, 0 10px 18px rgba(0,0,0,.25); }
  .vt { position: absolute; background: #fff; color: #d0011b; border-radius: 18px; text-align: center; box-shadow: 0 14px 28px rgba(0,0,0,.3); border-left: 8px dashed #ff5a1f; }`,
`<div class="rays"></div>
 <div style="position:absolute;left:0;right:0;top:34px;text-align:center"><span style="display:inline-block;padding:6px 20px;border-radius:99px;background:#fff;color:#d0011b;font-weight:900;font-size:24px">ShopHub Mall</span></div>
 <div class="h" style="top:104px;font-size:84px">Săn deal</div>
 <div class="h" style="top:190px;font-size:96px;color:#ffe066">siêu hot</div>
 ${items([[8, 40, 300, 120, 190, -10], [123, 170, 290, 120, 210, 0], [106, 300, 320, 120, 150, 10]])}
 <div class="vt" style="left:26px;top:506px;width:244px;padding:12px 8px;transform:rotate(-6deg);white-space:nowrap"><div style="font-weight:800;font-size:14px;letter-spacing:.05em">DEAL FLASH SALE</div><div style="font-weight:900;font-size:27px;line-height:1.05">GIẢM ĐẾN 50%</div></div>
 <div class="vt" style="left:280px;top:520px;width:170px;padding:12px 8px;transform:rotate(5deg);white-space:nowrap"><div style="font-weight:800;font-size:14px;letter-spacing:.05em">VOUCHER</div><div style="font-weight:900;font-size:27px;line-height:1.05">₫50K</div></div>
 ${sticker(372, 410, 96, 14, '#ffd23f', '#c62828', '<div><div style="font-size:14px">CHÍNH</div><div style="font-size:23px">HÃNG</div></div>')}
 <div class="note" style="left:0;right:0;bottom:8px;text-align:center;font-size:12px">Flash Sale giảm 10–50% · Mã SHOPHUB50 cho đơn từ ₫250.000</div>`), 1.5);

// ============================== 3. campaign 10.10: event banner, popup, product frame ==============================
const tenTen = (w, h, layout) => page(w, h, `
  body { background: radial-gradient(circle at 75% 30%, #ff7a3d, #e8361a 45%, #9c1408 100%); color: #fff; }
  .rays { position: absolute; inset: 0; background: repeating-conic-gradient(from 0deg at 75% 40%, rgba(255,255,255,.07) 0 6deg, transparent 6deg 18deg); }
  .h1 { position: absolute; font-weight: 800; letter-spacing: -.03em; line-height: .95; text-shadow: 0 6px 0 rgba(0,0,0,.18); }
  .y { color: #ffe08a; }
  .cta { position: absolute; background: #fff; color: #c62828; font-weight: 800; border-radius: 99px; }`, layout);
add('event-1010', 1440, 480, tenTen(1440, 480, `<div class="rays"></div>
  <div class="h1" style="left:80px;top:80px;font-size:104px">SIÊU SALE <span class="y">10.10</span></div>
  <div style="position:absolute;left:84px;top:214px;font-size:32px;font-weight:600">Mã giảm đến ₫100.000 · Freeship đến ₫30.000</div>
  <div class="cta" style="left:84px;top:290px;padding:16px 36px;font-size:30px">Săn deal ngay</div>
  ${items([[78, 1000, 40, 270, 220], [123, 1260, 50, 130, 320], [172, 990, 240, 200, 200], [6, 1190, 290, 110, 160]])}
  ${sticker(1290, 330, 120, 12, '#ffd23f', '#b71c1c', '<div><div style="font-size:15px">FLASH SALE</div><div style="font-size:30px">-50%</div></div>')}`));
add('popup', 720, 720, tenTen(720, 720, `<div class="rays"></div>
  <div class="h1" style="left:0;right:0;top:56px;text-align:center;font-size:96px">SIÊU SALE</div>
  <div class="h1 y" style="left:0;right:0;top:150px;text-align:center;font-size:120px">10.10</div>
  ${items([[124, 70, 300, 180, 290], [88, 250, 360, 230, 210], [186, 470, 330, 190, 250]])}
  <div class="cta" style="left:150px;right:150px;top:612px;padding:16px 0;text-align:center;font-size:30px">Mã giảm đến ₫100.000</div>`));
add('frame-1010', 800, 800, page(800, 800, `
  body { background: transparent; }
  .band { position: absolute; left: 0; right: 0; bottom: 0; height: 120px; background: linear-gradient(90deg, #c62828, #ff5722); display: flex; align-items: center; justify-content: space-between; padding: 0 30px; color: #fff; }
  .big { font-size: 60px; font-weight: 800; letter-spacing: -.02em; }
  .big span { color: #ffe08a; }
  .side { text-align: right; font-weight: 700; font-size: 26px; line-height: 1.1; }
  .edge { position: absolute; inset: 0; border: 10px solid #ff5722; border-bottom: 0; }`,
  `<div class="edge"></div><div class="band"><div class="big"><span>10.10</span> SIÊU SALE</div><div class="side">ShopHub<br>Giá sốc</div></div>`), 1, true);

// ============================== render ==============================
// ART_ONLY=<regex>: render only the matching images (a quick look while designing; art-png is still emptied first)
const only = process.env.ART_ONLY ? new RegExp(process.env.ART_ONLY) : null;
const browser = await chromium.launch();
const contexts = new Map();
for (const j of jobs.filter((x) => !only || only.test(x.name))) {
  if (!contexts.has(j.scale)) {
    const ctx = await browser.newContext({ deviceScaleFactor: j.scale });
    contexts.set(j.scale, await ctx.newPage());
  }
  const tab = contexts.get(j.scale);
  const file = join(OUT, `${j.name}.html`);
  writeFileSync(file, j.html);
  await tab.setViewportSize({ width: j.w, height: j.h });
  await tab.goto(pathToFileURL(file).href);
  await tab.evaluate(() => document.fonts.ready);
  await tab.waitForLoadState('networkidle');
  await tab.evaluate(() => window.fit());
  await tab.screenshot({ path: join(OUT, `${j.name}.png`), omitBackground: j.transparent });
}
await browser.close();
writeFileSync(join(OUT, 'covers.json'), JSON.stringify(covered.map((m) => m.key)));
console.log(`art-html: ${jobs.length} images, ${covered.length} ad covers of ${models.length} models; templates used ${usage.join('/')}`);
