"""Product photo framing of the sample catalogue (G-VIS): the object fills the square like on a real marketplace grid.

Used by build.py (every product photo goes through `product_square`). Three kinds of source photo:
- cut-out: a transparent studio photo (dummyjson) — the alpha channel is the object;
- isolated / plain: an object on a plain background (paper / light box / a solid colour) — the background is found as
  the plain-coloured region connected to the border; a near-white one is lifted to pure white; it is extended where
  the square goes past the photo;
- scene: anything else (a real-scene photo, many Wikimedia ones) — cover-cropped to a filled square, never shrunk into
  a frame.
The object is trimmed to its bounding box and scaled so it fills FILL of the 800 px square, centred: a cut-out on a
very light pastel of its top-level industry (PASTEL; white where a pastel looks wrong, e.g. clothing), an isolated photo
on its own (white / plain) background — its edge-to-edge background removal is not clean enough to lay it on a colour.
The trimmed cut-out (RGBA) is also returned, for the ad covers / banners drawn by art-html.mjs (an isolated photo of a
mostly white object gets none: it would melt into the template; the template then shows the photo in a card).
"""
import io

import numpy as np
from PIL import Image, ImageFilter, ImageOps
from scipy import ndimage

SIZE = 800
FILL = 0.88          # longest side of the object / side of the square
MAX_BYTES = 80_000   # per product WebP

# Eight very light pastels, one per top-level industry (deterministic); None = white
SKY, LAVENDER, BUTTER, MINT, ROSE, PEACH, AQUA, SAND = (
    '#e8f2ff', '#f1ecfd', '#fdf5dc', '#e7f5ea', '#fdedf2', '#feefe4', '#e3f5f5', '#f4f0e8')
PASTEL = {
    'Điện Thoại & Phụ Kiện': SKY, 'Máy Tính & Laptop': LAVENDER, 'Thiết Bị Điện Tử': AQUA, 'Máy Ảnh & Quay Phim': SAND,
    'Đồng Hồ': BUTTER, 'Giày Dép Nam': SAND, 'Giày Dép Nữ': ROSE, 'Túi Ví Nữ': PEACH, 'Mẹ & Bé': AQUA,
    'Nhà Cửa & Đời Sống': MINT, 'Sắc Đẹp': ROSE, 'Sức Khỏe': AQUA, 'Thể Thao & Du Lịch': PEACH, 'Ô Tô & Xe Máy': SKY,
    'Đồ Chơi': BUTTER, 'Bách Hóa Online': MINT, 'Thú Cưng': BUTTER,
    # clothing: on-hanger / on-model photos read as studio shots on white
    'Thời Trang Nam': None, 'Thời Trang Nữ': None,
}


def hex_rgb(h: str):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def background(top: str):
    c = PASTEL.get(top)
    return hex_rgb(c) if c else (255, 255, 255)


def _border(a: np.ndarray, w: int = 4) -> np.ndarray:
    return np.concatenate([a[:w].reshape(-1, a.shape[-1]) if a.ndim == 3 else a[:w].ravel(),
                           a[-w:].reshape(-1, a.shape[-1]) if a.ndim == 3 else a[-w:].ravel(),
                           a[:, :w].reshape(-1, a.shape[-1]) if a.ndim == 3 else a[:, :w].ravel(),
                           a[:, -w:].reshape(-1, a.shape[-1]) if a.ndim == 3 else a[:, -w:].ravel()])


def _touching_border(mask: np.ndarray) -> np.ndarray:
    """The parts of a boolean mask connected to the image border."""
    labels, _ = ndimage.label(mask)
    edge = np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))
    edge = edge[edge != 0]
    return np.isin(labels, edge)


def _main_parts(obj: np.ndarray) -> np.ndarray:
    """Drop specks: keep the connected parts of the object of at least 4 % of its largest part."""
    labels, n = ndimage.label(ndimage.binary_opening(obj, iterations=2))
    if n == 0:
        return obj
    sizes = ndimage.sum(np.ones_like(labels), labels, range(1, n + 1))
    keep = [i + 1 for i, v in enumerate(sizes) if v >= 0.04 * sizes.max()]
    return np.isin(labels, keep)


def analyse(im: Image.Image):
    """(kind, rgba cut-out | None, (background colour, object mask, flattened RGB photo) | None) for an RGBA photo."""
    a = np.asarray(im)
    alpha = a[..., 3]
    if (_border(alpha[..., None]) < 16).mean() > 0.5:          # transparent studio photo
        return 'cutout', im, None
    flat = Image.new('RGB', im.size, (255, 255, 255))
    flat.paste(im, mask=im.getchannel('A'))
    rgb = np.asarray(flat).astype(np.int16)
    border = _border(rgb)
    median = np.median(border, axis=0)
    near = np.abs(border - median).max(axis=1) <= 22
    if near.mean() < 0.72:                                    # busy border: a real scene
        return 'scene', None, (None, None, flat)
    bg = _touching_border(np.abs(rgb - median).max(axis=2) <= 26)
    if bg.mean() > 0.97 or bg.mean() < 0.12:                  # nothing left / background hardly visible: treat as a scene
        return 'scene', None, (None, None, flat)
    obj = _main_parts(~bg)
    colour = tuple(int(v) for v in median)
    if not (min(colour) >= 228 and max(colour) - min(colour) <= 20):
        return 'plain', None, (colour, obj, flat)
    # paper / light box: lift the background to pure white (so the white margin meets it without an edge) and, unless
    # the object itself is mostly white (it would melt into any background), cut it out for the ad covers
    soft = Image.fromarray((bg * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5))
    flat = Image.composite(Image.new('RGB', im.size, (255, 255, 255)), flat, soft)
    tight = _main_parts(~_touching_border(np.abs(rgb - median).max(axis=2) <= 14))
    whiteness = (rgb[tight].min(axis=1) >= 232).mean() if tight.any() else 1.0
    cut = None
    if whiteness < 0.3:
        mask = Image.fromarray((tight * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1.1))
        cut = im.copy()
        cut.putalpha(mask)
    return 'isolated', cut, ((255, 255, 255), obj, flat)


def _encode(canvas: Image.Image, quality: int) -> bytes:
    for q in range(quality, 40, -6):
        out = io.BytesIO()
        canvas.save(out, 'WEBP', quality=q, method=6)
        if out.tell() <= MAX_BYTES:
            break
    return out.getvalue()


def trim(cut: Image.Image) -> Image.Image:
    box = cut.getchannel('A').point(lambda v: 255 if v > 24 else 0).getbbox()
    return cut.crop(box) if box else cut


def product_square(data: bytes, top: str, crop=None, photo=False):
    """(webp bytes, kind, trimmed RGBA cut-out or None)."""
    im = Image.open(io.BytesIO(data))
    if photo:
        im = ImageOps.exif_transpose(im)
    im = im.convert('RGBA')
    if crop:  # fractions of the photo: left, top, right, bottom
        w, h = im.size
        im = im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)))
    kind, cut, plain = analyse(im)
    if kind == 'cutout':
        cut = trim(cut)
        canvas = Image.new('RGBA', (SIZE, SIZE), background(top) + (255,))
        scale = SIZE * FILL / max(cut.size)
        it = cut.resize((max(1, round(cut.width * scale)), max(1, round(cut.height * scale))), Image.LANCZOS)
        canvas.alpha_composite(it, ((SIZE - it.width) // 2, (SIZE - it.height) // 2))
        return _encode(canvas.convert('RGB'), 82), kind, cut
    colour, obj, flat = plain
    if kind != 'scene':
        # object on a plain background: a square around the object, the background extended where the photo ends
        ys, xs = np.nonzero(obj)
        x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
        side = int(max(x1 - x0, y1 - y0) / FILL)
        cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
        canvas = Image.new('RGB', (side, side), colour)
        canvas.paste(flat, (side // 2 - cx, side // 2 - cy))
        return _encode(canvas.resize((SIZE, SIZE), Image.LANCZOS), 80), kind, trim(cut) if cut is not None else None
    # scene: cover crop to a square (a little above the centre on portrait photos: subjects sit high)
    w, h = flat.size
    side = min(w, h)
    left = (w - side) // 2
    top_px = int((h - side) * 0.4)
    sq = flat.crop((left, top_px, left + side, top_px + side)).resize((SIZE, SIZE), Image.LANCZOS)
    return _encode(sq, 74), 'scene', None
