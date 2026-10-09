"""Artwork of the sample data, drawn from the product photos (run after build.py; needs Pillow and the Be Vietnam Pro
font, downloaded once from Google Fonts into .cache — OFL licence).

    python backend/tools/seed-catalog/art.py
    python backend/tools/seed-catalog/art.py --logos     # only the 30 shop logos (no product photos needed)

Writes to Seed/Data/Art/:
- shop-<n>-logo.webp (256 px: a drawn mark + the shop name as a wordmark, each shop its own colour and type style; all
  of it inside the inscribed circle, so pages that crop logos round still show it) and shop-<n>-cover.webp (1200×300)
- hero-<n>.webp (1440×600, 2.4:1, empty left half for the HTML title) and side banners are drawn in CSS
- popup.webp (720×720) and event-1010.webp (1440×480, the campaign headline drawn in)
"""
import io
import json
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data'))
OUT = os.path.join(DATA, 'Art')
CACHE = os.path.join(HERE, '.cache')
FONT = os.path.join(CACHE, 'BeVietnamPro-ExtraBold.ttf')
FONT_BOLD = os.path.join(CACHE, 'BeVietnamPro-Bold.ttf')
# Wordmark faces of the shop logos (Google Fonts, OFL; variable weight except Be Vietnam Pro; all carry Vietnamese)
LOGO_FONTS = {
    'vietnam': ('bevietnampro/BeVietnamPro-ExtraBold.ttf', 'BeVietnamPro-ExtraBold.ttf'),
    'serif': ('playfairdisplay/PlayfairDisplay%5Bwght%5D.ttf', 'PlayfairDisplay.ttf'),
    'geometric': ('montserrat/Montserrat%5Bwght%5D.ttf', 'Montserrat.ttf'),
    'tech': ('lexend/Lexend%5Bwght%5D.ttf', 'Lexend.ttf'),
    'rounded': ('nunito/Nunito%5Bwght%5D.ttf', 'Nunito.ttf'),
    'condensed': ('oswald/Oswald%5Bwght%5D.ttf', 'Oswald.ttf'),
    'soft': ('baloo2/Baloo2%5Bwght%5D.ttf', 'Baloo2.ttf'),
}


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
        if key not in urls:  # an open-licence photo model (key 195 on): no transparent cut-out to draw with
            continue
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


def hex_rgb(h: str):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def lighter(c, f=0.35):
    return tuple(int(v + (255 - v) * f) for v in c)


def ensure_logo_fonts():
    import urllib.request
    for remote, local in LOGO_FONTS.values():
        path = os.path.join(CACHE, local)
        if not os.path.exists(path):
            url = f'https://github.com/google/fonts/raw/main/ofl/{remote}'
            open(path, 'wb').write(urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'shophub-seed'}), timeout=60).read())


# ---- shop logos (G4-A1): a drawn mark + the name as a wordmark, drawn 4× then scaled down for smooth edges ----
LOGO = 256
K = 4


def logo_font(style: str, size: float, weight: int):
    f = ImageFont.truetype(os.path.join(CACHE, LOGO_FONTS[style][1]), int(size * K))
    try:
        f.set_variation_by_axes([weight])
    except (OSError, ValueError):
        pass  # static face
    return f


def _poly(d, pts, cx, cy, s, fill):
    d.polygon([((cx + x * s) * K, (cy + y * s) * K) for x, y in pts], fill=fill)


def _ellipse(d, cx, cy, rx, ry, **kw):
    d.ellipse(((cx - rx) * K, (cy - ry) * K, (cx + rx) * K, (cy + ry) * K), **kw)


def _rrect(d, x0, y0, x1, y1, r, **kw):
    d.rounded_rectangle((x0 * K, y0 * K, x1 * K, y1 * K), r * K, **kw)


def _line(d, pts, w, fill):
    d.line([(x * K, y * K) for x, y in pts], fill=fill, width=int(w * K), joint='curve')
    for x, y in (pts[0], pts[-1]):
        _ellipse(d, x, y, w / 2, w / 2, fill=fill)


def mark(d, kind: str, cx: float, cy: float, s: float, fg, bg):
    """A simple pictogram about 2·s wide centred at (cx, cy), in fg; bg cuts the holes."""
    w = s * 0.2
    if kind == 'bag':
        _rrect(d, cx - s * 0.8, cy - s * 0.45, cx + s * 0.8, cy + s * 0.95, s * 0.18, fill=fg)
        d.arc(((cx - s * 0.42) * K, (cy - s * 1.05) * K, (cx + s * 0.42) * K, (cy + s * 0.15) * K), 180, 360, fill=fg, width=int(w * K))
        _ellipse(d, cx - s * 0.38, cy - s * 0.1, s * 0.1, s * 0.1, fill=bg)
        _ellipse(d, cx + s * 0.38, cy - s * 0.1, s * 0.1, s * 0.1, fill=bg)
    elif kind == 'bolt':  # hexagon with a lightning bolt
        _poly(d, [(math.cos(math.radians(a)), math.sin(math.radians(a))) for a in range(30, 390, 60)], cx, cy, s, fg)
        _poly(d, [(0.14, -0.72), (-0.42, 0.1), (-0.02, 0.1), (-0.14, 0.72), (0.42, -0.12), (0.02, -0.12)], cx, cy, s, bg)
    elif kind == 'phone':
        _rrect(d, cx - s * 0.55, cy - s, cx + s * 0.55, cy + s, s * 0.2, fill=fg)
        _rrect(d, cx - s * 0.4, cy - s * 0.78, cx + s * 0.4, cy + s * 0.58, s * 0.06, fill=bg)
        _ellipse(d, cx, cy + s * 0.8, s * 0.09, s * 0.09, fill=bg)
    elif kind == 'diamond':
        _poly(d, [(-1, -0.3), (-0.55, -0.85), (0.55, -0.85), (1, -0.3), (0, 0.95)], cx, cy, s, fg)
        _line(d, [(cx - s * 0.9, cy - s * 0.3), (cx + s * 0.9, cy - s * 0.3)], s * 0.08, bg)
        _line(d, [(cx - s * 0.25, cy - s * 0.82), (cx, cy - s * 0.3), (cx + s * 0.25, cy - s * 0.82)], s * 0.08, bg)
        _line(d, [(cx, cy - s * 0.3), (cx, cy + s * 0.75)], s * 0.08, bg)
    elif kind == 'shirt':
        _poly(d, [(-0.35, -0.85), (-1, -0.45), (-0.75, 0.05), (-0.5, -0.1), (-0.5, 0.9), (0.5, 0.9), (0.5, -0.1), (0.75, 0.05),
                  (1, -0.45), (0.35, -0.85), (0, -0.55)], cx, cy, s, fg)
    elif kind == 'house':
        _poly(d, [(-1, -0.05), (0, -0.95), (1, -0.05), (0.75, -0.05), (0.75, 0.9), (-0.75, 0.9), (-0.75, -0.05)], cx, cy, s, fg)
        _rrect(d, cx - s * 0.2, cy + s * 0.3, cx + s * 0.2, cy + s * 0.9, s * 0.05, fill=bg)
    elif kind == 'sparkle':
        _poly(d, [(0, -1), (0.2, -0.2), (1, 0), (0.2, 0.2), (0, 1), (-0.2, 0.2), (-1, 0), (-0.2, -0.2)], cx - s * 0.15, cy + s * 0.1, s * 0.85, fg)
        _poly(d, [(0, -1), (0.25, -0.25), (1, 0), (0.25, 0.25), (0, 1), (-0.25, 0.25), (-1, 0), (-0.25, -0.25)], cx + s * 0.68, cy - s * 0.62, s * 0.32, fg)
    elif kind == 'crown':
        _poly(d, [(-1, -0.55), (-0.5, 0.05), (0, -0.8), (0.5, 0.05), (1, -0.55), (0.8, 0.55), (-0.8, 0.55)], cx, cy, s, fg)
        _rrect(d, cx - s * 0.8, cy + s * 0.68, cx + s * 0.8, cy + s * 0.88, s * 0.06, fill=fg)
        for x, y in ((-1, -0.68), (0, -0.93), (1, -0.68)):
            _ellipse(d, cx + x * s, cy + y * s, s * 0.13, s * 0.13, fill=fg)
    elif kind == 'heart':
        _ellipse(d, cx - s * 0.45, cy - s * 0.3, s * 0.52, s * 0.52, fill=fg)
        _ellipse(d, cx + s * 0.45, cy - s * 0.3, s * 0.52, s * 0.52, fill=fg)
        _poly(d, [(-0.93, -0.1), (0.93, -0.1), (0, 0.92)], cx, cy, s, fg)
        _ellipse(d, cx - s * 0.5, cy - s * 0.42, s * 0.14, s * 0.14, fill=bg)
    elif kind == 'pot':
        _rrect(d, cx - s * 0.8, cy - s * 0.3, cx + s * 0.8, cy + s * 0.75, s * 0.25, fill=fg)
        _rrect(d, cx - s * 1.0, cy - s * 0.5, cx + s * 1.0, cy - s * 0.3, s * 0.08, fill=fg)
        _rrect(d, cx - s * 0.2, cy - s * 0.75, cx + s * 0.2, cy - s * 0.55, s * 0.08, fill=fg)
    elif kind == 'dumbbell':
        _rrect(d, cx - s * 0.6, cy - s * 0.1, cx + s * 0.6, cy + s * 0.1, s * 0.05, fill=fg)
        for sx in (-1, 1):
            _rrect(d, cx + sx * s * 0.5 - s * 0.14, cy - s * 0.6, cx + sx * s * 0.5 + s * 0.14, cy + s * 0.6, s * 0.08, fill=fg)
            _rrect(d, cx + sx * s * 0.82 - s * 0.12, cy - s * 0.38, cx + sx * s * 0.82 + s * 0.12, cy + s * 0.38, s * 0.08, fill=fg)
    elif kind == 'basket':
        _poly(d, [(-1, -0.15), (1, -0.15), (0.75, 0.85), (-0.75, 0.85)], cx, cy, s, fg)
        d.arc(((cx - s * 0.6) * K, (cy - s * 0.95) * K, (cx + s * 0.6) * K, (cy + s * 0.25) * K), 190, 350, fill=fg, width=int(w * K))
        for x in (-0.4, 0, 0.4):
            _line(d, [(cx + x * s, cy + s * 0.12), (cx + x * s * 0.85, cy + s * 0.6)], s * 0.1, bg)
    elif kind == 'paw':
        _ellipse(d, cx, cy + s * 0.35, s * 0.5, s * 0.42, fill=fg)
        for x, y in ((-0.72, -0.15), (-0.28, -0.6), (0.28, -0.6), (0.72, -0.15)):
            _ellipse(d, cx + x * s, cy + y * s, s * 0.2, s * 0.26, fill=fg)
    elif kind == 'wheel':
        for sx in (-1, 1):
            _ellipse(d, cx + sx * s * 0.6, cy + s * 0.35, s * 0.38, s * 0.38, outline=fg, width=int(w * 0.9 * K))
        _line(d, [(cx - s * 0.6, cy + s * 0.35), (cx - s * 0.1, cy - s * 0.25), (cx + s * 0.35, cy - s * 0.25), (cx + s * 0.6, cy + s * 0.35)], s * 0.16, fg)
        _line(d, [(cx + s * 0.2, cy - s * 0.25), (cx + s * 0.35, cy - s * 0.7)], s * 0.16, fg)
    elif kind == 'leaf':  # a pointed leaf, tilted, with its vein
        a = math.radians(35)
        rot = lambda x, y: (x * math.cos(a) + y * math.sin(a), -x * math.sin(a) + y * math.cos(a))
        side = [(0.55 * math.sin(math.pi * t / 20), -1 + 2 * t / 20) for t in range(21)]
        _poly(d, [rot(x, y) for x, y in side] + [rot(-x, y) for x, y in reversed(side)], cx, cy, s, fg)
        _line(d, [(cx + rot(0, -0.6)[0] * s, cy + rot(0, -0.6)[1] * s), (cx + rot(0, 1.05)[0] * s, cy + rot(0, 1.05)[1] * s)], s * 0.1, bg)
    elif kind == 'cup':
        _rrect(d, cx - s * 0.75, cy - s * 0.3, cx + s * 0.45, cy + s * 0.8, s * 0.25, fill=fg)
        _ellipse(d, cx + s * 0.6, cy + s * 0.2, s * 0.3, s * 0.3, outline=fg, width=int(w * K))
        for x in (-0.35, 0.05):
            _line(d, [(cx + x * s, cy - s * 0.95), (cx + x * s + s * 0.1, cy - s * 0.6)], s * 0.1, fg)
    elif kind == 'watch':
        _rrect(d, cx - s * 0.35, cy - s, cx + s * 0.35, cy + s, s * 0.1, fill=fg)
        _ellipse(d, cx, cy, s * 0.62, s * 0.62, fill=fg)
        _ellipse(d, cx, cy, s * 0.45, s * 0.45, fill=bg)
        _line(d, [(cx, cy - s * 0.3), (cx, cy), (cx + s * 0.22, cy + s * 0.1)], s * 0.09, fg)
    else:  # store front
        _rrect(d, cx - s * 0.85, cy - s * 0.2, cx + s * 0.85, cy + s * 0.9, s * 0.08, fill=fg)
        _poly(d, [(-1, -0.2), (-0.8, -0.85), (0.8, -0.85), (1, -0.2)], cx, cy, s, fg)
        _rrect(d, cx - s * 0.25, cy + s * 0.3, cx + s * 0.25, cy + s * 0.9, s * 0.05, fill=bg)


INDUSTRY_MARK = {
    'Điện Thoại & Phụ Kiện': 'phone', 'Máy Tính & Laptop': 'bolt', 'Thiết Bị Điện Tử': 'bolt', 'Đồng Hồ': 'watch',
    'Thời Trang Nam': 'shirt', 'Thời Trang Nữ': 'shirt', 'Giày Dép Nam': 'dumbbell', 'Giày Dép Nữ': 'diamond', 'Túi Ví Nữ': 'bag',
    'Nhà Cửa & Đời Sống': 'house', 'Sắc Đẹp': 'sparkle', 'Sức Khỏe': 'heart', 'Thể Thao & Du Lịch': 'dumbbell',
    'Ô Tô & Xe Máy': 'wheel', 'Bách Hóa Online': 'basket', 'Thú Cưng': 'paw',
}
# A mark that says more than the industry does
NAME_MARK = {'Cà Phê Phố Núi': 'cup', 'Nông Sản Sạch Việt': 'leaf', 'Mỹ Phẩm Thiên Nhiên': 'leaf', 'Gốm Bát Tràng Xinh': 'pot', 'Bếp Nhà Lá': 'pot'}
# The Mall brands get a hand-picked look; the other shops cycle through CYCLE by index (mark from their first industry)
LOGO_LOOKS = {
    'ShopHub Official Store': dict(mark='bag', style='vietnam', weight=800, tagline='OFFICIAL STORE', name='ShopHub'),
    'Mall TechZone': dict(mark='bolt', style='tech', weight=700, split=('Tech', 'Zone'), light=300),
    'Mall Fashionista': dict(mark='diamond', style='serif', weight=700, italic=True),
    'Mall HomeLux': dict(mark='house', style='geometric', weight=800, upper=True, track=0.1),
    'Mall BeautyPro': dict(mark='sparkle', style='rounded', weight=900, split=('Beauty', 'Pro'), light=400),
    'Mall SportKing': dict(mark='crown', style='condensed', weight=700, upper=True, italic=True, track=0.04),
    'Mall BabyCare': dict(mark='heart', style='soft', weight=800, split=('Baby', 'Care'), light=500),
}
CYCLE = [
    dict(style='geometric', weight=800), dict(style='serif', weight=700), dict(style='tech', weight=600),
    dict(style='rounded', weight=900), dict(style='condensed', weight=600, upper=True, track=0.05), dict(style='soft', weight=700),
]


def _text_w(d, text, font, track=0.0):
    return d.textlength(text, font=font) + track * font.size * max(len(text) - 1, 0)


def _draw_text(d, x, y, text, font, fill, track=0.0):
    if not track:
        d.text((x, y), text, font=font, fill=fill)
        return
    for ch in text:
        d.text((x, y), ch, font=font, fill=fill)
        x += d.textlength(ch, font=font) + track * font.size


def _lines(name: str, look: dict):
    """Wordmark lines; each line is a list of parts (a 'split' name draws its two halves in two weights)."""
    if 'split' in look:
        return [list(look['split'])]
    text = name.upper() if look.get('upper') else name
    words = text.split()
    if len(text) <= 11 or len(words) < 2:
        return [[text]]
    best = min(range(1, len(words)), key=lambda k: abs(len(' '.join(words[:k])) - len(' '.join(words[k:]))))
    return [[' '.join(words[:best])], [' '.join(words[best:])]]


def _part(im, d, x, y, part, font, fill, track, italic):
    if not italic:
        _draw_text(d, x, y, part, font, fill, track)
        return
    # Slanted: draw on a mask, shear it, paste in the colour
    pad = font.size // 3
    mw, mh = int(_text_w(d, part, font, track)) + 2 * pad, int(font.size * 1.5)
    m = Image.new('L', (mw, mh), 0)
    _draw_text(ImageDraw.Draw(m), pad, 0, part, font, 255, track)
    m = m.transform(m.size, Image.AFFINE, (1, 0.22, -0.11 * mh, 0, 1, 0), Image.BICUBIC)
    im.paste(Image.new('RGB', m.size, fill), (int(x - pad), int(y)), m)


def draw_logo(shop: dict, index: int) -> Image.Image:
    name = shop['name'][len('Mall '):] if shop['name'].startswith('Mall ') else shop['name']
    look = dict(LOGO_LOOKS.get(shop['name']) or CYCLE[index % len(CYCLE)])
    look.setdefault('mark', NAME_MARK.get(shop['name']) or INDUSTRY_MARK.get(shop['sells'][0], 'store'))
    colour = hex_rgb(shop['color'])
    deep = tuple(int(v * 0.7) for v in colour)
    white, tint = (255, 255, 255), lighter(colour, 0.9)
    im = Image.new('RGB', (LOGO * K, LOGO * K), white)
    d = ImageDraw.Draw(im)
    _ellipse(d, 128, 128, 126, 126, fill=tint)  # the safe circle, lightly tinted
    lines = _lines(look.get('name', name), look)
    two = len(lines) == 2
    badge = look.get('tagline') or ('MALL' if shop.get('mall') else None)
    # Vertical rhythm: mark, wordmark, badge — the block is centred in the circle
    size = 40 if not two else 31
    track = look.get('track', 0)
    max_w = 200 if not two else 210
    while True:
        fonts = [logo_font(look['style'], size, look['weight'])]
        fonts.append(logo_font(look['style'], size, look['light']) if 'light' in look else fonts[0])
        widths = [sum(_text_w(d, p, fonts[min(n, 1)], track) for n, p in enumerate(line)) / K for line in lines]
        if max(widths) <= max_w or size <= 16:
            break
        size -= 1
    line_h = size * 1.15
    mark_s = 32 if not two else 27
    block = mark_s * 2 + 12 + line_h * len(lines) + (26 if badge else 0)
    top = 128 - block / 2
    mark(d, look['mark'], 128, top + mark_s, mark_s, colour, tint)
    y = top + mark_s * 2 + 6
    for li, line in enumerate(lines):
        x = (LOGO - widths[li]) / 2 * K
        for n, p in enumerate(line):
            font = fonts[min(n, 1)]
            _part(im, d, x, (y + li * line_h) * K, p, font, deep if n == 0 else colour, track, look.get('italic'))
            x += _text_w(d, p, font, track)
    if badge:
        fb = logo_font('geometric', 11, 800)
        wb = _text_w(d, badge, fb, 0.2) / K
        by = y + line_h * len(lines) + 6
        _rrect(d, 128 - wb / 2 - 9, by, 128 + wb / 2 + 9, by + 19, 9.5, fill=colour)
        _draw_text(d, (128 - wb / 2) * K, (by + 2.5) * K, badge, fb, white, 0.2)
    return im.resize((LOGO, LOGO), Image.LANCZOS)


def main():
    ensure_fonts()
    ensure_logo_fonts()
    cut, models = load_cutouts()
    seed = json.load(open(os.path.join(DATA, 'catalog-seed.json'), encoding='utf-8'))

    # ---- shops: logo + cover ----
    by_top = {}
    for m in models.values():
        by_top.setdefault(m['category'].split('/')[0], []).append(m['key'])
    for i, s in enumerate(seed['shops']):
        colour = hex_rgb(s['color'])
        save(draw_logo(s, i), f'shop-{i:02d}-logo.webp', quality=90)

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
    import sys
    if '--logos' in sys.argv:
        ensure_logo_fonts()
        for n, shop in enumerate(json.load(open(os.path.join(DATA, 'catalog-seed.json'), encoding='utf-8'))['shops']):
            save(draw_logo(shop, n), f'shop-{n:02d}-logo.webp', quality=90)
        print('logos written')
    else:
        main()
