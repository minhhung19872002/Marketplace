"""Build the sample catalogue's data files from models.py.

    python backend/tools/seed-catalog/build.py   (needs Pillow: pip install pillow)

- downloads every photo of each model from dummyjson.com (free demo product photos), puts it on white, pads it square,
  resizes to 800 px and saves WebP to Seed/Data/ProductImages/p<id>-<n>.webp (old images are removed);
- writes Seed/Data/product-models.json (what ProductGenerator reads);
- adds the leaves / mid categories / top categories the models need to Seed/Data/catalog-seed.json (existing ones are
  kept: tests and links use them) and drops the legacy product list (products now come from the models).
Run it again after editing models.py; it only downloads photos it does not have yet.
"""
import importlib.util
import io
import json
import os
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data'))
IMAGES = os.path.join(DATA, 'ProductImages')
CACHE = os.path.join(HERE, '.cache')
UA = {'User-Agent': 'Mozilla/5.0 shophub-seed/1.0'}

spec = importlib.util.spec_from_file_location('models', os.path.join(HERE, 'models.py'))
models = importlib.util.module_from_spec(spec)
spec.loader.exec_module(models)


def fetch(url: str) -> bytes:
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, ''.join(c if c.isalnum() or c in '.-' else '_' for c in url.replace('https://', '')))
    if os.path.exists(path):
        return open(path, 'rb').read()
    last = None
    for _ in range(3):
        try:
            data = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60).read()
            open(path, 'wb').write(data)
            return data
        except Exception as e:  # network hiccup: retry
            last = e
    raise RuntimeError(f'cannot download {url}: {last}')


def square_webp(data: bytes) -> bytes:
    im = Image.open(io.BytesIO(data)).convert('RGBA')
    bg = Image.new('RGBA', im.size, (255, 255, 255, 255))
    bg.alpha_composite(im)
    im = bg.convert('RGB')
    # Trim the white margin, then pad to a square with 6 % air around the item
    box = Image.eval(im, lambda v: 255 - v).getbbox() or (0, 0, im.width, im.height)
    im = im.crop(box)
    side = int(max(im.size) * 1.12)
    canvas = Image.new('RGB', (side, side), (255, 255, 255))
    canvas.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
    canvas = canvas.resize((800, 800), Image.LANCZOS)
    out = io.BytesIO()
    canvas.save(out, 'WEBP', quality=80, method=6)
    return out.getvalue()


def main() -> None:
    catalogue = {p['id']: p for p in json.loads(fetch('https://dummyjson.com/products?limit=0&select=title,images'))['products']}
    os.makedirs(IMAGES, exist_ok=True)
    wanted = {}
    for model in models.MODELS:
        for n, url in enumerate(catalogue[model['id']]['images'][:5]):
            wanted[f"p{model['id']:03d}-{n}.webp"] = url

    def build(item):
        name, url = item
        path = os.path.join(IMAGES, name)
        if not os.path.exists(path):
            open(path, 'wb').write(square_webp(fetch(url)))
        return name

    with ThreadPoolExecutor(6) as ex:
        list(ex.map(build, wanted.items()))
    for name in os.listdir(IMAGES):
        if name not in wanted:
            os.remove(os.path.join(IMAGES, name))
    old_manifest = os.path.join(DATA, 'product-images.manifest.json')
    if os.path.exists(old_manifest):
        os.remove(old_manifest)

    out = []
    for model in models.MODELS:
        variant = None
        if model['variant']:
            tier, options = models.VARIANTS[model['variant']]
            variant = {'tier': tier, 'options': [{'name': o, 'delta': d} for o, d in options]}
        out.append({
            'key': model['id'], 'category': model['leaf'], 'name': model['name'], 'price': model['price'], 'variant': variant,
            'attributes': model['attrs'], 'origin': model['origin'], 'weightG': model['weight'],
            'images': sorted(n for n in wanted if n.startswith(f"p{model['id']:03d}-")),
        })
    with open(os.path.join(DATA, 'product-models.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
        f.write('\n')

    seed_path = os.path.join(DATA, 'catalog-seed.json')
    seed = json.load(open(seed_path, encoding='utf-8'))
    tops = {t['name']: t for t in seed['categories']}
    for model in models.MODELS:
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
    seed['products'] = []
    with open(seed_path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(seed, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print(f'{len(out)} models, {len(wanted)} photos')


if __name__ == '__main__':
    sys.exit(main())
