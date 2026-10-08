"""Artwork of the sample data, drawn from the product photos (run after build.py; needs Pillow and the Be Vietnam Pro
font, downloaded once from Google Fonts into .cache — OFL licence).

    python backend/tools/seed-catalog/art.py

Writes to Seed/Data/Art/:
- shop-<n>-logo.webp (256 px, the shop's initials on its colour) and shop-<n>-cover.webp (1200×300, its products)
- hero-<n>.webp (1440×600, 2.4:1, empty left half for the HTML title) and side banners are drawn in CSS
- popup.webp (720×720) and event-1010.webp (1440×480, the campaign headline drawn in)
"""
import io
import json
import os
import random
import unicodedata

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data'))
OUT = os.path.join(DATA, 'Art')
CACHE = os.path.join(HERE, '.cache')
FONT = os.path.join(CACHE, 'BeVietnamPro-ExtraBold.ttf')
FONT_BOLD = os.path.join(CACHE, 'BeVietnamPro-Bold.ttf')


def ensure_fonts():
    import urllib.request
    for path in (FONT, FONT_BOLD):
        if not os.path.exists(path):
            name = os.path.basename(path)
            url = f'https://github.com/google/fonts/raw/main/ofl/bevietnampro/{name}'
            open(path, 'wb').write(urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'shophub-seed'}), timeout=60).read())


def load_cutouts() -> dict:
    models = {m['key']: m for m in json.load(open(os.path.join(DATA, 'product-models.json'), encoding='utf-8'))}
    catalogue = json.loads(open(os.path.join(CACHE, next(f for f in os.listdir(CACHE) if f.startswith('dummyjson.com_products'))), 'rb').read())
    urls = {p['id']: p['images'] for p in catalogue['products']}
    out = {}
    for key in models:
        url = urls[key][0]
        path = os.path.join(CACHE, ''.join(c if c.isalnum() or c in '.-' else '_' for c in url.replace('https://', '')))
        im = Image.open(path).convert('RGBA')
        box = im.getchannel('A').getbbox()
        out[key] = im.crop(box) if box else im
    return out, models


def gradient(size, top, bottom, angle=True):
    w, h = size
    base = Image.new('RGB', size, top)
    over = Image.new('RGB', size, bottom)
    mask = Image.linear_gradient('L').resize((w, h)) if not angle else Image.linear_gradient('L').rotate(-35, expand=True).resize((w, h))
    return Image.composite(over, base, mask)


def place(canvas: Image.Image, item: Image.Image, box, shadow=True):
    """Fit an item into box (x, y, w, h), centred, with a soft floor shadow."""
    x, y, w, h = box
    it = item.copy()
    it.thumbnail((w, h), Image.LANCZOS)
    px, py = x + (w - it.width) // 2, y + (h - it.height) // 2
    if shadow:
        sh = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(sh)
        d.ellipse((px + it.width * 0.1, py + it.height - 10, px + it.width * 0.9, py + it.height + 14), fill=(0, 0, 0, 70))
        canvas.alpha_composite(sh.filter(ImageFilter.GaussianBlur(10)))
    canvas.alpha_composite(it, (px, py))


def blobs(canvas: Image.Image, colour, seed):
    rnd = random.Random(seed)
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    w, h = canvas.size
    for _ in range(5):
        r = rnd.randint(h // 4, h // 2)
        cx, cy = rnd.randint(w // 3, w), rnd.randint(-r // 2, h + r // 2)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=colour + (38,))
    canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(6)))


def save(im: Image.Image, name: str, quality=82):
    os.makedirs(OUT, exist_ok=True)
    im.convert('RGB').save(os.path.join(OUT, name), 'WEBP', quality=quality, method=6)


def initials(name: str) -> str:
    """'ShopHub Official Store' → SH, 'Mall TechZone' → TZ, 'Gia Dụng Thông Minh' → GD."""
    words = [w for w in name.replace('&', ' ').split() if w[0].isalnum()]
    if words and words[0] == 'Mall':
        words = words[1:]
    if not words:
        return 'SH'
    capitals = [c for c in words[0] if c.isupper()]
    pick = ''.join(capitals[:2]) if len(capitals) >= 2 else words[0][0] + (words[1][0] if len(words) > 1 else words[0][1])
    return unicodedata.normalize('NFC', pick).upper()


def hex_rgb(h: str):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def lighter(c, f=0.35):
    return tuple(int(v + (255 - v) * f) for v in c)


def main():
    ensure_fonts()
    cut, models = load_cutouts()
    seed = json.load(open(os.path.join(DATA, 'catalog-seed.json'), encoding='utf-8'))
    big = ImageFont.truetype(FONT, 108)

    # ---- shops: logo + cover ----
    by_top = {}
    for m in models.values():
        by_top.setdefault(m['category'].split('/')[0], []).append(m['key'])
    for i, s in enumerate(seed['shops']):
        colour = hex_rgb(s['color'])
        logo = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
        d = ImageDraw.Draw(logo)
        d.ellipse((0, 0, 255, 255), fill=colour)
        d.ellipse((14, 14, 241, 241), outline=(255, 255, 255, 150), width=4)
        text = initials(s['name'])
        tw = d.textlength(text, font=big)
        d.text(((256 - tw) / 2, 58), text, font=big, fill='white')
        if s['mall']:
            d.rounded_rectangle((78, 196, 178, 232), 8, fill='white')
            small = ImageFont.truetype(FONT_BOLD, 24)
            d.text((128 - d.textlength('MALL', font=small) / 2, 199), 'MALL', font=small, fill=colour)
        save(logo, f'shop-{i:02d}-logo.webp')

        cover = gradient((1200, 300), lighter(colour, 0.15), colour).convert('RGBA')
        blobs(cover, (255, 255, 255), i)
        rnd = random.Random(100 + i)
        keys = [k for t in s['sells'] for k in by_top.get(t, [])]
        rnd.shuffle(keys)
        for n, k in enumerate(keys[:4]):
            place(cover, cut[k], (560 + n * 160, 40, 150, 220))
        save(cover, f'shop-{i:02d}-cover.webp')

    # ---- home heroes (right half: products; left half stays calm for the HTML title + CTA) ----
    heroes = [
        ((201, 61, 25), (238, 120, 60), [123, 100, 106, 105]),   # tech: iPhone, AirPods, Watch, MagSafe
        ((74, 20, 140), (173, 20, 87), [8, 7, 4, 2]),            # beauty
        ((15, 123, 74), (46, 125, 50), [71, 51, 47, 12]),        # home
    ]
    for n, (a, b, keys) in enumerate(heroes):
        hero = gradient((1440, 600), a, b).convert('RGBA')
        blobs(hero, (255, 255, 255), 50 + n)
        slots = [(700, 70, 330, 460), (990, 120, 260, 360), (1180, 300, 220, 260), (900, 330, 220, 230)]
        for k, box in zip(keys, slots):
            place(hero, cut[k], box)
        save(hero, f'hero-{n + 1}.webp')

    # ---- popup + campaign banner (headline drawn in) ----
    pop = gradient((720, 720), (201, 61, 25), (230, 81, 0)).convert('RGBA')
    blobs(pop, (255, 236, 179), 7)
    d = ImageDraw.Draw(pop)
    f1, f2 = ImageFont.truetype(FONT, 96), ImageFont.truetype(FONT_BOLD, 40)
    d.text((360 - d.textlength('SIÊU SALE', font=f1) / 2, 60), 'SIÊU SALE', font=f1, fill='white')
    d.text((360 - d.textlength('10.10', font=f1) / 2, 160), '10.10', font=f1, fill=(255, 236, 179))
    for k, box in zip([124, 88, 186, 49], [(70, 300, 220, 330), (270, 360, 200, 240), (450, 330, 200, 300), (300, 520, 150, 150)]):
        place(pop, cut[k], box)
    d = ImageDraw.Draw(pop)
    d.rounded_rectangle((180, 630, 540, 690), 30, fill='white')
    d.text((360 - d.textlength('Mã giảm đến ₫100.000', font=ImageFont.truetype(FONT_BOLD, 26)) / 2, 644), 'Mã giảm đến ₫100.000',
           font=ImageFont.truetype(FONT_BOLD, 26), fill=(201, 61, 25))
    save(pop, 'popup.webp')

    ev = gradient((1440, 480), (183, 28, 28), (230, 81, 0)).convert('RGBA')
    blobs(ev, (255, 236, 179), 9)
    d = ImageDraw.Draw(ev)
    f1, f2, f3 = ImageFont.truetype(FONT, 104), ImageFont.truetype(FONT_BOLD, 38), ImageFont.truetype(FONT_BOLD, 34)
    d.text((80, 90), 'SIÊU SALE 10.10', font=f1, fill='white')
    d.text((84, 240), 'Voucher đến ₫100.000 · Freeship mọi đơn', font=f2, fill=(255, 236, 179))
    label = 'Săn deal ngay'
    lw = d.textlength(label, font=f3)
    d.rounded_rectangle((84, 330, 84 + lw + 72, 400), 35, fill='white')
    d.text((84 + 36, 343), label, font=f3, fill=(183, 28, 28))
    for k, box in zip([78, 123, 172, 6], [(1000, 50, 270, 240), (1250, 80, 160, 330), (960, 260, 210, 200), (1170, 300, 140, 160)]):
        place(ev, cut[k], box)
    save(ev, 'event-1010.webp')
    print('art:', len(os.listdir(OUT)), 'files')


if __name__ == '__main__':
    main()
