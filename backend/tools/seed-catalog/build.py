"""Build the sample catalogue's data files from models.py.

    python backend/tools/seed-catalog/build.py   (needs Pillow: pip install pillow)

- downloads every photo of each model from dummyjson.com (free demo product photos), puts it on white, pads it square,
  resizes to 800 px and saves WebP to Seed/Data/ProductImages/p<id>-<n>.webp;
- does the same for the OPEN_MODELS (key 195 on): openly licensed photos (Wikimedia Commons / Openverse: CC0, public
  domain, CC BY, CC BY-SA) listed in photos.json by find-photos.py, optionally cropped, near-white background cleaned;
  writes CREDITS.md (source, creator, licence, changes of every such photo);
- an existing WebP is never rewritten (only missing ones are made) and the marketing covers pNNN-m.webp of
  art-png-to-webp.py are kept, as the first image of their model;
- writes Seed/Data/product-models.json (what ProductGenerator reads), with "brand" when the model has one;
- adds the leaves / mid categories / top categories the models need to Seed/Data/catalog-seed.json (existing ones are
  kept: tests and links use them), adds the models' brands to its brand list and drops the legacy product list
  (products now come from the models). The file keeps its line endings.
Run it again after editing models.py / photos.json; it only downloads photos it does not have yet.
"""
import importlib.util
import io
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data'))
IMAGES = os.path.join(DATA, 'ProductImages')
CACHE = os.path.join(HERE, '.cache')
UA = {'User-Agent': 'shophub-seed/1.0 (sample catalogue; https://github.com/minhhung19872002/Marketplace)'}

spec = importlib.util.spec_from_file_location('models', os.path.join(HERE, 'models.py'))
models = importlib.util.module_from_spec(spec)
spec.loader.exec_module(models)


def fetch(url: str) -> bytes:
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, ''.join(c if c.isalnum() or c in '.-' else '_' for c in url.replace('https://', ''))[:200])
    if os.path.exists(path):
        return open(path, 'rb').read()
    last = None
    for attempt in range(5):
        try:
            data = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60).read()
            open(path, 'wb').write(data)
            return data
        except urllib.error.HTTPError as e:
            last = e
            if e.code != 429:
                break
            time.sleep(int(e.headers.get('Retry-After') or 0) or 20 * (attempt + 1))  # Commons rate limit: wait, retry
        except Exception as e:  # network hiccup: retry
            last = e
    raise RuntimeError(f'cannot download {url}: {last}')


def square_webp(data: bytes, crop=None, photo=False) -> bytes:
    im = Image.open(io.BytesIO(data))
    if photo:
        im = ImageOps.exif_transpose(im)
    im = im.convert('RGBA')
    if crop:  # fractions of the photo: left, top, right, bottom
        w, h = im.size
        im = im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)))
    bg = Image.new('RGBA', im.size, (255, 255, 255, 255))
    bg.alpha_composite(im)
    im = bg.convert('RGB')
    if photo:
        im = clean_white(im)
    # Trim the white margin, then pad to a square with 6 % air around the item
    box = Image.eval(im, lambda v: 255 - v).getbbox() or (0, 0, im.width, im.height)
    im = im.crop(box)
    side = int(max(im.size) * 1.12)
    canvas = Image.new('RGB', (side, side), (255, 255, 255))
    canvas.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
    canvas = canvas.resize((800, 800), Image.LANCZOS)
    out = io.BytesIO()
    # a real photo (not a transparent cut-out) has more texture: a little more compression keeps it small
    canvas.save(out, 'WEBP', quality=72 if photo else 80, method=6)
    return out.getvalue()


def clean_white(im: Image.Image) -> Image.Image:
    """A studio photo's paper / light-box background is rarely pure white: when the photo's border is mostly light,
    lift near-white pixels (luminance >= 232) to white so the trim and the white padding meet without a visible edge.
    A photo on a coloured / dark background is left as it is (it is padded with white around)."""
    mask = Image.eval(im.convert('L'), lambda v: 255 if v >= 232 else 0)
    w, h = im.size
    border = list(mask.crop((0, 0, w, 3)).get_flattened_data()) + list(mask.crop((0, h - 3, w, h)).get_flattened_data()) \
        + list(mask.crop((0, 0, 3, h)).get_flattened_data()) + list(mask.crop((w - 3, 0, w, h)).get_flattened_data())
    if sum(1 for v in border if v) < 0.6 * len(border):
        return im
    return Image.composite(Image.new('RGB', im.size, (255, 255, 255)), im, mask)


def open_url(photo: dict) -> str:
    """Download URL of an open photo: Commons originals can be 20 MB, so ask Commons for a 1,280 px rendition."""
    m = re.match(r'https://upload\.wikimedia\.org/wikipedia/commons/[0-9a-f]/[0-9a-f]{2}/([^?]+)', photo['url'])
    if m:
        return f'https://commons.wikimedia.org/wiki/Special:FilePath/{m.group(1)}?width=1280'
    return photo['url']


# Same rule as SeedNameTests (ShopHub.UnitTests): no "Loại 1", "- 2"… at the end of a name
NUMBERED = re.compile(r'(\b(Loại|Loai|Số|So|No\.?|Mẫu|Kiểu)\s*\d+|#\s*\d+|\s[-–]\s*\d+)\s*$', re.I)


def check(model: dict, seed: dict) -> None:
    """The rules the seeder applies: attribute names / options / numbers of the industry set, origin, name."""
    top = model['leaf'].split('/')[0]
    tops = {t['name']: t for t in seed['categories']}
    attribute_set = tops[top]['attributeSet'] if top in tops else models.NEW_TOPS[top][0]
    defs = {d['name']: d for d in seed['attributeSets'][attribute_set]}
    problems = []
    if NUMBERED.search(model['name']):
        problems.append('numbered name')
    if len(model['name']) > 120:
        problems.append('name > 120')
    if model['origin'] not in seed['originOptions']:
        problems.append(f"origin {model['origin']}")
    if model['variant'] and model['variant'] not in models.VARIANTS:
        problems.append(f"variant {model['variant']}")
    for name, values in model['attrs'].items():
        d = defs.get(name)
        if d is None:
            problems.append(f'unknown attribute {name}')
        elif d['type'] in ('SingleSelect', 'MultiSelect'):
            if any(v not in d['options'] for v in values) or (d['type'] == 'SingleSelect' and len(values) != 1):
                problems.append(f'{name}={values}')
        elif d['type'] == 'Number':
            try:
                float(values[0])
            except ValueError:
                problems.append(f'{name}={values}')
    for name, d in defs.items():
        if d['required'] and name not in model['attrs']:
            problems.append(f'missing required {name}')
    if problems:
        raise SystemExit(f"model {model['id']} {model['name']}: {', '.join(problems)}")


def main() -> None:
    catalogue = {p['id']: p for p in json.loads(fetch('https://dummyjson.com/products?limit=0&select=title,images'))['products']}
    photos = json.load(open(os.path.join(HERE, 'photos.json'), encoding='utf-8'))
    seed_path = os.path.join(DATA, 'catalog-seed.json')
    seed_raw = open(seed_path, encoding='utf-8', newline='').read()
    seed = json.loads(seed_raw)
    all_models = models.MODELS + models.OPEN_MODELS
    keys = [m['id'] for m in all_models]
    assert len(keys) == len(set(keys)), 'duplicate model key'
    assert min(m['id'] for m in models.OPEN_MODELS) > max(m['id'] for m in models.MODELS), 'open models continue after dummyjson ids'
    for model in models.OPEN_MODELS:
        check(model, seed)
        assert 1 <= len(photos.get(str(model['id']), [])) <= 3, f"model {model['id']}: 1-3 photos in photos.json"

    os.makedirs(IMAGES, exist_ok=True)
    wanted = {}
    for model in models.MODELS:
        for n, url in enumerate(catalogue[model['id']]['images'][:5]):
            wanted[f"p{model['id']:03d}-{n}.webp"] = url
    for model in models.OPEN_MODELS:
        for n, photo in enumerate(photos[str(model['id'])]):
            wanted[f"p{model['id']:03d}-{n}.webp"] = photo

    def build(item):
        name, source = item
        path = os.path.join(IMAGES, name)
        if not os.path.exists(path):  # never rewrite an existing image
            if isinstance(source, str):
                data = square_webp(fetch(source))
            else:
                data = square_webp(fetch(open_url(source)), source.get('crop'), photo=True)
            open(path, 'wb').write(data)
        return name

    with ThreadPoolExecutor(4) as ex:
        list(ex.map(build, wanted.items()))
    for name in os.listdir(IMAGES):
        # pNNN-m.webp: the marketing cover art-png-to-webp.py lays in front of the model's photos — keep it
        if name not in wanted and not name.endswith('-m.webp'):
            os.remove(os.path.join(IMAGES, name))
    old_manifest = os.path.join(DATA, 'product-images.manifest.json')
    if os.path.exists(old_manifest):
        os.remove(old_manifest)

    out = []
    for model in all_models:
        variant = None
        if model['variant']:
            tier, options = models.VARIANTS[model['variant']]
            variant = {'tier': tier, 'options': [{'name': o, 'delta': d} for o, d in options]}
        cover = f"p{model['id']:03d}-m.webp"
        entry = {
            'key': model['id'], 'category': model['leaf'], 'name': model['name'], 'price': model['price'], 'variant': variant,
            'attributes': model['attrs'], 'origin': model['origin'], 'weightG': model['weight'],
            'images': ([cover] if os.path.exists(os.path.join(IMAGES, cover)) else [])
            + sorted(n for n in wanted if n.startswith(f"p{model['id']:03d}-")),
        }
        brand = model['brand'] or models.BRANDS.get(model['id'])
        if brand:
            entry['brand'] = brand
        out.append(entry)
    with open(os.path.join(DATA, 'product-models.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
        f.write('\n')

    tops = {t['name']: t for t in seed['categories']}
    for model in all_models:
        top_name, mid_name, leaf_name = model['leaf'].split('/')
        if top_name not in tops:
            attribute_set, commission, icon = models.NEW_TOPS[top_name]
            tops[top_name] = {'name': top_name, 'icon': icon, 'commissionBp': commission, 'attributeSet': attribute_set, 'children': []}
            seed['categories'].append(tops[top_name])
        mid = next((c for c in tops[top_name]['children'] if c['name'] == mid_name), None)
        if mid is None:
            mid = {'name': mid_name, 'children': []}
            tops[top_name]['children'].append(mid)
        if not any(l['name'] == leaf_name for l in mid['children']):
            mid['children'].append({'name': leaf_name})
    for entry in out:
        if entry.get('brand') and entry['brand'] not in seed['brands']:
            seed['brands'].append(entry['brand'])
    seed['products'] = []
    text = json.dumps(seed, ensure_ascii=False, indent=1) + '\n'
    with open(seed_path, 'w', encoding='utf-8', newline='') as f:
        f.write(text.replace('\n', '\r\n') if '\r\n' in seed_raw else text)
    write_credits(photos)
    print(f'{len(out)} models, {len(wanted)} photos')


def write_credits(photos: dict) -> None:
    names = {m['id']: m['name'] for m in models.OPEN_MODELS}
    lines = [
        '# Nguồn ảnh mẫu có giấy phép mở',
        '',
        'Ảnh của các mẫu sản phẩm từ mã 195 trở đi (`OPEN_MODELS` trong `models.py`) lấy từ Wikimedia Commons / Openverse,',
        'chỉ nhận giấy phép cho phép dùng thương mại: CC0, phạm vi công cộng (public domain), CC BY, CC BY-SA',
        '(không nhận NC / ND). Tệp này do `build.py` sinh từ `photos.json` — sửa `photos.json` rồi chạy lại, đừng sửa tay.',
        '',
        'Thay đổi chung cho mọi ảnh: thu nhỏ, đặt lên nền trắng, cắt bớt viền trắng, thêm lề trắng thành ảnh vuông',
        '800 px, lưu WebP; ảnh có nền sáng thì phần gần trắng được làm trắng hẳn. Ảnh CC BY-SA sau khi sửa vẫn theo',
        'CC BY-SA cùng phiên bản. Tên / logo thương hiệu (nếu thấy trong ảnh) chỉ để dữ liệu mẫu trông thật,',
        'không phải hàng thật được bán và không có liên kết nào với chủ thương hiệu.',
        '',
        '| Tệp | Mẫu | Nguồn | Tác giả | Giấy phép | Thay đổi thêm |',
        '|---|---|---|---|---|---|',
    ]

    def cell(v: str) -> str:
        return ' '.join((v or '').replace('|', '/').split())[:120]

    by = {}
    for key in sorted(photos, key=int):
        for n, p in enumerate(photos[key]):
            by[p['license']] = by.get(p['license'], 0) + 1
            lic = f"[{cell(p['license'])}]({p['licenseUrl']})" if p.get('licenseUrl') else cell(p['license'])
            extra = 'cắt khung (trái, trên, phải, dưới) ' + ', '.join(f'{v:.2f}' for v in p['crop']) if p.get('crop') else ''
            lines.append(f"| `p{int(key):03d}-{n}.webp` | {cell(names.get(int(key), ''))} | [{cell(p['source'])}]({p['page']}) | "
                         f"{cell(p['creator'])} | {lic} | {extra} |")
    lines += ['', 'Tổng theo giấy phép: ' + ', '.join(f'{k}: {v}' for k, v in sorted(by.items(), key=lambda kv: -kv[1])) + '.', '']
    with open(os.path.join(HERE, 'CREDITS.md'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines))


if __name__ == '__main__':
    sys.exit(main())
