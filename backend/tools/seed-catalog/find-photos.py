"""Find openly licensed product photos for a model and show them on a numbered contact sheet.

    python backend/tools/seed-catalog/find-photos.py commons "incategory:Baby_bottles"
    python backend/tools/seed-catalog/find-photos.py openverse "baby bottle"
    python backend/tools/seed-catalog/find-photos.py search "baby bottle" "feeding bottle"   (both sources, several queries)
    python backend/tools/seed-catalog/find-photos.py pick <key> <sheet-name> <n> [<n>...] [--crop n=l,t,r,b]

- `commons` / `openverse` search Wikimedia Commons (any free licence it hosts) / the Openverse API (license_type=commercial)
  and keep only licences a commercial demo may reuse: CC0, public domain, CC BY, CC BY-SA. The hits are sorted with
  plain light backgrounds first (product-photo style), drawn on .cache/sheets/<name>.png and listed in
  .cache/sheets/<name>.json.
- `pick` copies the chosen hits (with their source page, creator, licence) into photos.json under the model key;
  build.py downloads them from there and writes CREDITS.md. `--crop 2=0.1,0,0.9,1` crops hit 2 to that box (fractions).
"""
import html
import io
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SHEETS = os.path.join(HERE, '.cache', 'sheets')
PHOTOS = os.path.join(HERE, 'photos.json')
UA = {'User-Agent': 'shophub-seed/1.0 (sample catalogue; https://github.com/minhhung19872002/Marketplace)'}

ALLOWED_RE = re.compile(r'^(cc0|cc[ -]?zero|public domain|pdm(-owner)?|cc[ -]by([ -]sa)?)([ -]\d(\.\d)?)?$', re.I)


def allowed(lic: str) -> bool:
    """CC0 / public domain / CC BY / CC BY-SA only: never NC (non-commercial) or ND (no derivatives — we crop / pad)."""
    return bool(ALLOWED_RE.match(lic.strip()))


def get(url: str, timeout=40) -> bytes:
    last = None
    for attempt in range(5):
        try:
            return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout).read()
        except urllib.error.HTTPError as e:
            last = e
            if e.code != 429:  # 401/403/404: asking again will not help
                break
            time.sleep(int(e.headers.get('Retry-After') or 0) or 15 * (attempt + 1))  # Commons / Openverse rate limit
        except Exception as e:  # network hiccup: retry
            last = e
    raise RuntimeError(f'cannot download {url}: {last}')


def strip(s: str) -> str:
    return html.unescape(re.sub(r'<[^>]+>', '', s or '')).strip()


def commons(query: str, limit=50):
    q = urllib.parse.urlencode({
        'action': 'query', 'format': 'json', 'generator': 'search', 'gsrnamespace': 6, 'gsrlimit': limit,
        'gsrsearch': f'{query} filetype:bitmap', 'prop': 'imageinfo', 'iiprop': 'url|extmetadata|size|mime', 'iiurlwidth': 400})
    pages = json.loads(get(f'https://commons.wikimedia.org/w/api.php?{q}')).get('query', {}).get('pages', {})
    out = []
    for p in sorted(pages.values(), key=lambda p: p.get('index', 0)):
        info = p['imageinfo'][0]
        meta = {k: v.get('value', '') for k, v in info.get('extmetadata', {}).items()}
        lic = strip(meta.get('LicenseShortName', ''))
        if not allowed(lic) or info['width'] < 500:
            continue
        out.append({
            'source': 'Wikimedia Commons', 'title': p['title'], 'page': info['descriptionurl'], 'url': info['url'],
            'thumb': info['thumburl'], 'license': lic,
            # Artist is empty on some museum uploads: the photographer is then in Credit ("… / Photo by X")
            'creator': strip(meta.get('Artist', '')) or strip(meta.get('Attribution', '')) or strip(meta.get('Credit', '')) or 'Không rõ',
            'licenseUrl': meta.get('LicenseUrl', '') or ('https://creativecommons.org/publicdomain/mark/1.0/' if 'public' in lic.lower() else ''),
            'restrictions': strip(meta.get('Restrictions', '')), 'size': [info['width'], info['height']],
        })
    return out


def openverse(query: str, limit=20):
    q = urllib.parse.urlencode({'q': query, 'license_type': 'commercial', 'page_size': limit, 'mature': 'false'})
    out = []
    for r in json.loads(get(f'https://api.openverse.org/v1/images/?{q}'))['results']:
        lic = f"{'CC0' if r['license'] == 'cc0' else 'Public domain' if r['license'] == 'pdm' else 'CC ' + r['license'].upper()} {r['license_version'] or ''}".strip()
        if not allowed(lic):
            continue
        out.append({
            'source': f"Openverse ({r['source']})", 'title': r['title'] or '', 'page': r['foreign_landing_url'], 'url': r['url'],
            'thumb': r['thumbnail'], 'creator': r['creator'] or 'Không rõ', 'license': lic, 'licenseUrl': r['license_url'],
            'restrictions': '', 'size': [r.get('width'), r.get('height')],
        })
    return out


def plainness(im: Image.Image) -> float:
    """Share of the border that is light and flat: 1.0 = a product on a white sweep."""
    g = im.convert('L').resize((64, 64))
    px = g.load()
    border = [px[x, y] for x in range(64) for y in (0, 1, 62, 63)] + [px[x, y] for y in range(64) for x in (0, 1, 62, 63)]
    return sum(1 for v in border if v > 225) / len(border)


def search(kind: str, queries: list[str]) -> None:
    hits, seen = [], set()
    for query in queries:
        for source in ((commons, openverse) if kind == 'search' else (commons,) if kind == 'commons' else (openverse,)):
            try:
                found = source(query)
            except Exception as e:  # one source down / rate-limited: keep the other
                print('!', source.__name__, query, e)
                continue
            for h in found:
                if h['url'] not in seen:
                    seen.add(h['url'])
                    hits.append(h)
    query = queries[0]

    def thumb(h):
        try:
            im = Image.open(io.BytesIO(get(h['thumb'], 30))).convert('RGB')
            h['plain'] = round(plainness(im), 2)
            return h, im
        except Exception:
            return h, None

    with ThreadPoolExecutor(4) as ex:
        rows = [r for r in ex.map(thumb, hits) if r[1] is not None]
    rows.sort(key=lambda r: -r[0]['plain'])
    rows = rows[:24]
    name = re.sub(r'[^a-z0-9]+', '-', f'{kind}-{query}'.lower()).strip('-')[:60]
    os.makedirs(SHEETS, exist_ok=True)
    cols, tile = 6, 220
    sheet = Image.new('RGB', (cols * tile, ((len(rows) + cols - 1) // cols) * (tile + 20) or 1), (90, 90, 90))
    draw = ImageDraw.Draw(sheet)
    for i, (h, im) in enumerate(rows):
        im.thumbnail((tile - 8, tile - 8))
        x, y = (i % cols) * tile, (i // cols) * (tile + 20)
        sheet.paste(im, (x + 4, y + 4))
        draw.text((x + 6, y + tile - 2), f"{i} {h['license'][:12]} {h['plain']}", fill=(255, 255, 0))
    sheet.save(os.path.join(SHEETS, f'{name}.png'))
    json.dump([h for h, _ in rows], open(os.path.join(SHEETS, f'{name}.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(os.path.join(SHEETS, f'{name}.png'))
    for i, (h, _) in enumerate(rows):
        print(i, h['license'], h['plain'], h['title'][:70], '| R:' + h['restrictions'] if h['restrictions'] else '')


def pick(key: str, sheet: str, picks: list[str]) -> None:
    hits = json.load(open(os.path.join(SHEETS, f'{sheet}.json'), encoding='utf-8'))
    crops = {}
    numbers = []
    for p in picks:
        if '=' in p:
            n, box = p.split('=')
            crops[int(n)] = [float(v) for v in box.split(',')]
        else:
            numbers.append(int(p))
    photos = json.load(open(PHOTOS, encoding='utf-8')) if os.path.exists(PHOTOS) else {}
    photos[key] = []
    for n in numbers:
        h = {k: v for k, v in hits[n].items() if k not in ('thumb', 'plain', 'size')}
        if n in crops:
            h['crop'] = crops[n]
        photos[key].append(h)
    with open(PHOTOS, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(dict(sorted(photos.items(), key=lambda kv: int(kv[0]))), f, ensure_ascii=False, indent=1)
        f.write('\n')
    for h in photos[key]:
        print(key, h['license'], h['creator'][:40], h['page'])


if __name__ == '__main__':
    if sys.argv[1] == 'pick':
        args = [a for a in sys.argv[4:] if a != '--crop']
        pick(sys.argv[2], sys.argv[3], args)
    else:
        search(sys.argv[1], sys.argv[2:])
