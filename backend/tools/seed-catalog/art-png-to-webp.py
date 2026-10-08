"""Turn the PNGs rendered by art-html.mjs into the seed's WebP files.

    python backend/tools/seed-catalog/art-png-to-webp.py

- shop logos, Mall slides, heroes, event banner, popup → Seed/Data/Art/<name>.webp (opaque)
- frame-1010 → Seed/Data/Art/frame-1010.png (kept PNG: the transparent band is laid over product photos)
- cover-pNNN → Seed/Data/ProductImages/pNNN-m.webp (a marketing cover in front of the model's studio photos), and the
  model's entry in product-models.json gets it as its first image
"""
import json
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data'))
PNG = os.path.join(HERE, '.cache', 'art-png')
ART = os.path.join(DATA, 'Art')
IMAGES = os.path.join(DATA, 'ProductImages')

os.makedirs(ART, exist_ok=True)
covers = []
for name in sorted(os.listdir(PNG)):
    if not name.endswith('.png'):
        continue
    stem = name[:-4]
    im = Image.open(os.path.join(PNG, name))
    if stem.startswith('frame-'):
        im.save(os.path.join(ART, f'{stem}.png'), optimize=True)
    elif stem.startswith('cover-p'):
        key = int(stem[len('cover-p'):])
        im.convert('RGB').save(os.path.join(IMAGES, f'p{key:03d}-m.webp'), 'WEBP', quality=82, method=6)
        covers.append(key)
    else:
        im.convert('RGB').save(os.path.join(ART, f'{stem}.webp'), 'WEBP', quality=84, method=6)

path = os.path.join(DATA, 'product-models.json')
models = json.load(open(path, encoding='utf-8'))
for m in models:
    cover = f"p{m['key']:03d}-m.webp"
    m['images'] = [i for i in m['images'] if i != cover]
    if m['key'] in covers:
        m['images'].insert(0, cover)
with open(path, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(models, f, ensure_ascii=False, indent=1)
    f.write('\n')
print(f'{len(covers)} covers, art in {ART}')
