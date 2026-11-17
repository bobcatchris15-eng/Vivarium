"""Compose paired six-species review sheets from native mature catalog captures."""
import html
import json
import os
from pathlib import Path
from urllib.parse import quote
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'build/plant-graph-tuning-20261115'
OUT = BASE / 'review-contact-sheets'
FIRST = ['plumegrass', 'bannerreed', 'coalspike', 'banksorrow', 'hollowcrown', 'witchberry']
FONT = Path('C:/Windows/Fonts/segoeui.ttf')

def font(size):
    return ImageFont.truetype(str(FONT), size)

def relative(path):
    return quote(os.path.relpath(path, OUT).replace('\\', '/'), safe='/')

def main():
    current = BASE / 'review-sheets-current/plant_catalog'
    previous = BASE / 'review-sheets-previous/plant_catalog'
    ids = sorted(p.name.removesuffix('_growth.png') for p in current.glob('*_growth.png'))
    if len(ids) != 125:
        raise ValueError(f'Expected 125 current captures, found {len(ids)}')
    order = FIRST + [x for x in ids if x not in FIRST]
    reports = {mode: json.loads((folder/'report.json').read_text(encoding='utf-8'))
               for mode, folder in [('current', current), ('previous', previous)]}
    OUT.mkdir(parents=True, exist_ok=True)
    records, pages = [], []
    for start in range(0, len(order), 6):
        number = start//6+1
        batch = order[start:start+6]
        canvases = {}
        for mode in reports:
            sheet = Image.new('RGB', (1800, 1870), '#202829')
            draw = ImageDraw.Draw(sheet)
            label = 'CURRENT FORMS' if mode == 'current' else 'PREVIOUS FORMS - REFERENCE'
            draw.text((24, 14), f'Sheet {number:02d} / 21   |   {label}', font=font(31), fill='#f0eee4')
            canvases[mode] = sheet
        detail = []
        for i, species in enumerate(batch):
            name = json.loads((ROOT/f'game/content/flora/{species}.json').read_text(encoding='utf-8-sig'))['name']
            row, col = divmod(i, 2)
            x, y = col*900, 70+row*600
            record = {'sheet': number, 'row': row+1, 'column': col+1, 'id': species, 'name': name}
            links = []
            for mode, folder, suffix in [('current', current, 'growth'), ('previous', previous, 'legacy')]:
                source = folder/f'{species}_{suffix}.png'
                with Image.open(source) as image:
                    # Preserve a broad, identical native-camera crop for both forms.
                    crop = image.crop((200, 80, 1400, 800)).resize((900, 540), Image.Resampling.LANCZOS)
                canvases[mode].paste(crop, (x, y+60))
                draw = ImageDraw.Draw(canvases[mode])
                draw.text((x+18, y+5), f'{i+1}. {name}', font=font(27), fill='#f0eee4')
                draw.text((x+18, y+36), species, font=font(17), fill='#bac7bf')
                shot = next(s for s in reports[mode]['shots'] if s.get('png') == source.name)
                record[mode] = {'image': str(source), 'report': str(folder/'report.json'),
                                'shot': shot['shot'], 'game_module': reports[mode].get('game_module'),
                                'sim_module': reports[mode].get('sim_module')}
                links.append(f'<a href="{relative(source)}">{mode} original</a>')
            records.append(record)
            detail.append(f'<li>{html.escape(name)}: {" / ".join(links)}</li>')
        for mode, sheet in canvases.items():
            sheet.save(OUT/f'sheet-{number:02d}-{mode}.jpg', quality=96, subsampling=0)
        pages.append(f'''<section id="sheet-{number:02d}"><h2>Sheet {number:02d}</h2>
<div class="pair"><figure><figcaption>Current</figcaption><a href="sheet-{number:02d}-current.jpg"><img loading="lazy" src="sheet-{number:02d}-current.jpg"></a></figure>
<figure><figcaption>Previous reference</figcaption><a href="sheet-{number:02d}-previous.jpg"><img loading="lazy" src="sheet-{number:02d}-previous.jpg"></a></figure></div>
<ol>{''.join(detail)}</ol></section>''')
    (OUT/'review-order.json').write_text(json.dumps({'species_count': len(records), 'columns': 2, 'rows': 3,
        'description': 'Fully mature native Godot captures. Each species uses matching camera and normalization in both modes; sizes are normalized per species.',
        'species': records}, indent=2)+'\n', encoding='utf-8')
    nav = ' '.join(f'<a href="#sheet-{n:02d}">{n:02d}</a>' for n in range(1, 22))
    page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Vivarium species review sheets</title>
<style>body{background:#202829;color:#f0eee4;font:17px system-ui;margin:24px}a{color:#b4dbc6}nav{position:sticky;top:0;background:#202829;padding:12px;line-height:2}nav a{padding:8px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:18px}figure{margin:0}img{width:100%;display:block}section{scroll-margin-top:100px;margin:40px 0}li{line-height:1.8}@media(max-width:900px){.pair{grid-template-columns:1fr}}</style>
<h1>125 mature species · 21 review sheets</h1><p>Two columns, three rows. Current and previous forms have matching positions. Click any sheet or original image for detail. Sizes are normalized per species.</p><nav>''' + nav + '</nav>' + ''.join(pages) + '</html>'
    (OUT/'index.html').write_text(page, encoding='utf-8')
    print(f'{len(records)} species; {len(pages)} paired sheets; {OUT}')

if __name__ == '__main__':
    main()
