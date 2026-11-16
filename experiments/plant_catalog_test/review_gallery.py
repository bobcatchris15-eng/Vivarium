"""Lay out unmodified native screenshots with authored species notes for local review.
Usage: python review_gallery.py CAPTURE_DIRECTORY VALIDATION_JSON
Pillow is needed only for the optional contact sheets.
"""
import html
import json
import sys
from pathlib import Path

directory = Path(sys.argv[1]).resolve()
report = json.loads(Path(sys.argv[2]).read_text())
cards = []
for item in sorted(report['species'], key=lambda item: item['id']):
    name = item['id']
    image = directory / 'plant_catalog' / (name + '_growth.png')
    if not image.exists():
        raise FileNotFoundError(image)
    cards.append(f'<article data-family="{html.escape(item["family"])}"><h2>{html.escape(name)}</h2>'
        f'<a href="plant_catalog/{name}_growth.png"><img loading="lazy" src="plant_catalog/{name}_growth.png"></a>'
        f'<p>{html.escape(item["silhouette"])}</p><p>{html.escape(item["growthHabit"])}</p></article>')
page = '''<!doctype html><meta charset="utf-8"><title>Vivarium graph flora catalog</title>
<style>body{background:#202629;color:#edf1e9;font:16px system-ui;margin:30px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(440px,1fr));gap:22px}article{background:#30383b;border-radius:8px;overflow:hidden;padding:16px}h2{font-size:20px}img{width:100%}p{line-height:1.5}button{margin:10px;padding:10px}</style>
<h1>Graph flora: 125 authored species</h1><p>Native Godot captures. Left to right: 20%, 55%, mature. Each species is normalized for framing; this is not a comparison of real size. Click a capture for full resolution.</p>
<nav>''' + ''.join(f'<button onclick="filter(\'{v}\')">{v}</button>' for v in ['all','woody','climber','grass','fern','forb','rosette','whorl','runner','pad','pitcher','trap','fungus','moss','lichen']) + '</nav><main>' + ''.join(cards) + '''</main>
<script>function filter(f){document.querySelectorAll('article').forEach(e=>e.hidden=f!='all'&&e.dataset.family!=f)}</script>'''
(directory/'index.html').write_text(page, encoding='utf-8')
try:
    from PIL import Image, ImageDraw
    files = sorted((directory/'plant_catalog').glob('*_growth.png'))
    for page in range((len(files)+19)//20):
        sheet = Image.new('RGB',(1600,1050),(30,34,36))
        draw = ImageDraw.Draw(sheet)
        for i,path in enumerate(files[page*20:(page+1)*20]):
            with Image.open(path) as source:
                image=source.crop((280,240,1480,790))
                image.thumbnail((390,180))
                x=(i%4)*400; y=(i//4)*210
                sheet.paste(image,(x+(400-image.width)//2,y))
                draw.text((x+10,y+187),path.stem.removesuffix('_growth'),fill='white')
        sheet.save(directory/f'contact-{page+1:02}.jpg')
except ImportError:
    print('Pillow unavailable; HTML gallery written without contact sheets.')
print(directory/'index.html')
