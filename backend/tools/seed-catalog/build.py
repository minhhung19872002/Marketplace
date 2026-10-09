"""Build the sample catalogue's data files from models.py.

    python backend/tools/seed-catalog/build.py   (needs Pillow: pip install pillow)

- downloads every photo of each model from dummyjson.com (free demo product photos) and frames it with photo.py: the
  object trimmed and scaled to fill 88 % of an 800 px square, on white or its industry's pastel (a real-scene photo is
  cover-cropped instead), WebP to Seed/Data/ProductImages/p<id>-<n>.webp; the trimmed cut-outs go to .cache/cutouts/
  (with kinds.json) for the ad covers and banners of art-html.mjs;
- does the same for the OPEN_MODELS (key 195 on): openly licensed photos (Wikimedia Commons / Openverse: CC0, public
  domain, CC BY, CC BY-SA) listed in photos.json by find-photos.py, optionally cropped, near-white background cleaned;
  writes CREDITS.md (source, creator, licence, changes of every such photo);
- an existing WebP is never rewritten (only missing ones are made) unless `--reprocess` is given: then every photo is
  framed again from its original in .cache/ (downloaded again when missing — never from an already processed WebP);
  the ad covers pNNN-ad.webp of art-png-to-webp.py are kept, as the first image of their model;
- writes Seed/Data/product-models.json (what ProductGenerator reads), with "brand" when the model has one;
- adds the leaves / mid categories / top categories the models need to Seed/Data/catalog-seed.json (existing ones are
  kept: tests and links use them), adds the models' brands to its brand list and drops the legacy product list
  (products now come from the models). The file keeps its line endings.
Run it again after editing models.py / photos.json; it only downloads photos it does not have yet.
"""
import importlib.util
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor


sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import photo  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data'))
IMAGES = os.path.join(DATA, 'ProductImages')
CACHE = os.path.join(HERE, '.cache')
CUTOUTS = os.path.join(CACHE, 'cutouts')
REPROCESS = '--reprocess' in sys.argv
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
            wanted[f"p{model['id']:03d}-{n}.webp"] = (url, model['leaf'].split('/')[0])
    for model in models.OPEN_MODELS:
        for n, item in enumerate(photos[str(model['id'])]):
            wanted[f"p{model['id']:03d}-{n}.webp"] = (item, model['leaf'].split('/')[0])

    os.makedirs(CUTOUTS, exist_ok=True)
    if REPROCESS:  # cut-outs of the earlier framing would be stale
        for name in os.listdir(CUTOUTS):
            os.remove(os.path.join(CUTOUTS, name))
    kinds_path = os.path.join(CUTOUTS, 'kinds.json')
    kinds = json.load(open(kinds_path, encoding='utf-8')) if os.path.exists(kinds_path) else {}

    def build(item):
        name, (source, top) = item
        path = os.path.join(IMAGES, name)
        stem = name[:-len('.webp')]
        if REPROCESS or not os.path.exists(path) or stem not in kinds:  # never rewrite an existing image unless asked
            if isinstance(source, str):
                data, kind, cut = photo.product_square(fetch(source), top)
            else:
                data, kind, cut = photo.product_square(fetch(open_url(source)), top, source.get('crop'), photo=True)
            if REPROCESS or not os.path.exists(path):
                open(path, 'wb').write(data)
            if cut is not None:
                cut.save(os.path.join(CUTOUTS, f'{stem}.png'))
            kinds[stem] = kind
        return name

    with ThreadPoolExecutor(4) as ex:
        list(ex.map(build, wanted.items()))
    with open(kinds_path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(dict(sorted(kinds.items())), f, indent=0)
    for name in os.listdir(IMAGES):
        # pNNN-ad.webp: the ad cover art-png-to-webp.py lays in front of the model's photos — keep it (the older
        # marketing covers pNNN-m.webp are replaced by the ad covers)
        if name not in wanted and not name.endswith('-ad.webp'):
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
        cover = f"p{model['id']:03d}-ad.webp"
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
    category_photos(seed)
    print(f'{len(out)} models, {len(wanted)} photos')


# The product photo of each top-level category (its cut-out): the home category grid shows it in a grey circle
CATEGORY_PHOTO = {
    'Điện Thoại & Phụ Kiện': 123, 'Máy Tính & Laptop': 78, 'Thiết Bị Điện Tử': 100, 'Máy Ảnh & Quay Phim': 216, 'Đồng Hồ': 106,
    'Thời Trang Nam': 83, 'Thời Trang Nữ': 162, 'Giày Dép Nam': 88, 'Giày Dép Nữ': 189, 'Túi Ví Nữ': 172, 'Mẹ & Bé': 196,
    'Nhà Cửa & Đời Sống': 71, 'Sắc Đẹp': 2, 'Sức Khỏe': 253, 'Thể Thao & Du Lịch': 147, 'Ô Tô & Xe Máy': 113, 'Đồ Chơi': 210,
    'Bách Hóa Online': 16, 'Thú Cưng': 22,
}


def slug(text: str) -> str:
    """Same as ShopHub.Application.Common.Slug.From."""
    import unicodedata
    out, dash = [], False
    for c in unicodedata.normalize('NFD', text.replace('đ', 'd').replace('Đ', 'D')):
        if unicodedata.category(c) == 'Mn':
            continue
        if c.isascii() and c.isalnum():
            out.append(c.lower())
            dash = False
        elif not dash and out:
            out.append('-')
            dash = True
    return ''.join(out).strip('-')[:80].rstrip('-')


def category_photos(seed: dict) -> None:
    """Art/cat-<slug>.webp: 168 px (2× of the 84 px circle), transparent, the cut-out filling ~88 % of it."""
    from PIL import Image
    art = os.path.join(DATA, 'Art')
    for top in seed['categories']:
        key = CATEGORY_PHOTO.get(top['name'])
        src = os.path.join(CUTOUTS, f'p{key:03d}-0.png') if key else None
        if not src or not os.path.exists(src):
            print(f"no category photo for {top['name']}")
            continue
        im = Image.open(src).convert('RGBA')
        im.thumbnail((148, 148), Image.LANCZOS)
        canvas = Image.new('RGBA', (168, 168), (0, 0, 0, 0))
        canvas.alpha_composite(im, ((168 - im.width) // 2, (168 - im.height) // 2))
        canvas.save(os.path.join(art, f"cat-{slug(top['name'])}.webp"), 'WEBP', quality=88, method=6)


def write_credits(photos: dict) -> None:
    names = {m['id']: m['name'] for m in models.OPEN_MODELS}
    lines = [
        '# Nguồn ảnh mẫu có giấy phép mở',
        '',
        'Ảnh của các mẫu sản phẩm từ mã 195 trở đi (`OPEN_MODELS` trong `models.py`) lấy từ Wikimedia Commons / Openverse,',
        'chỉ nhận giấy phép cho phép dùng thương mại: CC0, phạm vi công cộng (public domain), CC BY, CC BY-SA',
        '(không nhận NC / ND). Tệp này do `build.py` sinh từ `photos.json` — sửa `photos.json` rồi chạy lại, đừng sửa tay.',
        '',
        'Thay đổi chung cho mọi ảnh: thu nhỏ, cắt thành ảnh vuông 800 px, lưu WebP. Ảnh vật trên nền trơn: cắt sát vật',
        '(nền gần trắng được tách bỏ, thay bằng nền trắng hoặc màu nhạt của ngành hàng; nền màu khác được giữ và nới rộng)',
        'để vật chiếm khoảng 88 % khung; ảnh chụp cảnh thật: cắt vuông lấp đầy khung (`photo.py`). Ảnh có trong ảnh bìa',
        'quảng cáo `pNNN-ad.webp` (vật được tách nền, đặt trên mẫu đồ hoạ) cũng là bản sửa đổi. Ảnh CC BY-SA sau khi sửa vẫn theo',
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
