"""Turn the PNGs rendered by art-html.mjs into the seed's WebP files.

    python backend/tools/seed-catalog/art-png-to-webp.py

- hero / side / strip / Mall banners, event banner, popup → Seed/Data/Art/<name>.webp (opaque); banners are kept
  ≤ 150 KB (the quality steps down until they fit), hero / side / strip at their 2× size (sharp on retina screens)
- frame-1010 → Seed/Data/Art/frame-1010.png (kept PNG: the transparent band is laid over product photos)
- cover-pNNN → Seed/Data/ProductImages/pNNN-ad.webp (≤ 90 KB, an ad cover in front of the model's photos), and the
  model's entry in product-models.json gets it as its first image; ad covers of models no longer covered are removed
- shop logos are never written here (art.py --logos draws them): a stray shop-*-logo.png is skipped
"""
import io
import json
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'ShopHub.Infrastructure', 'Seed', 'Data'))
PNG = os.path.join(HERE, '.cache', 'art-png')
ART = os.path.join(DATA, 'Art')
IMAGES = os.path.join(DATA, 'ProductImages')


def webp(im: Image.Image, limit: int, quality: int = 86) -> bytes:
    for q in range(quality, 40, -4):
        out = io.BytesIO()
        im.save(out, 'WEBP', quality=q, method=6)
        if out.tell() <= limit:
            break
    return out.getvalue()


os.makedirs(ART, exist_ok=True)
covers = []
written = 0
for name in sorted(os.listdir(PNG)):
    if not name.endswith('.png'):
        continue
    stem = name[:-4]
    im = Image.open(os.path.join(PNG, name))
    if stem.startswith('shop-') and stem.endswith('-logo'):
        continue
    if stem.startswith('frame-'):
        im.save(os.path.join(ART, f'{stem}.png'), optimize=True)
    elif stem.startswith('cover-p'):
        key = int(stem[len('cover-p'):])
        open(os.path.join(IMAGES, f'p{key:03d}-ad.webp'), 'wb').write(webp(im.convert('RGB'), 90_000, 84))
        covers.append(key)
    else:
        open(os.path.join(ART, f'{stem}.webp'), 'wb').write(webp(im.convert('RGB'), 150_000))
    written += 1

for name in os.listdir(IMAGES):
    if name.endswith('-ad.webp') and int(name[1:4]) not in covers:
        os.remove(os.path.join(IMAGES, name))

path = os.path.join(DATA, 'product-models.json')
models = json.load(open(path, encoding='utf-8'))
for m in models:
    cover = f"p{m['key']:03d}-ad.webp"
    m['images'] = [i for i in m['images'] if i != cover and not i.endswith('-m.webp')]
    if m['key'] in covers:
        m['images'].insert(0, cover)
with open(path, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(models, f, ensure_ascii=False, indent=1)
    f.write('\n')
print(f'{written} files, {len(covers)} ad covers, art in {ART}')
