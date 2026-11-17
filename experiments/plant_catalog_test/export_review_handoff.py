"""Publish the frozen review queue without machine-local paths or ignored build assets."""
import html
import json
import shutil
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'build/plant-graph-tuning-20261115'
SOURCE = BASE / 'review-contact-sheets'
OUT = ROOT / 'docs/representation-rebuild/species-review-2026-11-17'

def main():
    source = json.loads((SOURCE/'review-order.json').read_text(encoding='utf-8'))
    OUT.mkdir(parents=True, exist_ok=True)
    records = [{k: item[k] for k in ('sheet', 'row', 'column', 'id', 'name')}
               for item in source['species']]
    assert len(records) == len({item['id'] for item in records}) == 125
    sections = []
    for n in range(1, 22):
        for mode in ('current', 'previous'):
            filename = f'sheet-{n:02d}-{mode}.jpg'
            shutil.copy2(SOURCE/filename, OUT/filename)
        draft_link = ''
        if n <= 6:
            draft = Image.new('RGB', (1800,1870), '#202829')
            draw = ImageDraw.Draw(draft)
            font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',27)
            draw.text((24,14),f'Sheet {n:02d} | REVISION DRAFT - OWNER REVIEW PENDING',font=font,fill='#f0eee4')
            for i, item in enumerate(x for x in records if x['sheet']==n):
                species=item['id']
                candidates=[BASE/f'{run}/plant_catalog/{species}_growth.png' for run in
                            ('publication-revisions','sheets01-04-revisions','review-sheets-current')]
                path=next(p for p in candidates if p.exists())
                x,y=(i%2)*900,70+(i//2)*600
                with Image.open(path) as image:
                    draft.paste(image.crop((200,80,1400,800)).resize((900,540),Image.Resampling.LANCZOS),(x,y+60))
                draw.text((x+18,y+12),f'{i+1}. {item["name"]}',font=font,fill='#f0eee4')
                item['draft_capture_run']=path.parents[1].name
            draft.save(OUT/f'sheet-{n:02d}-draft.jpg',quality=96,subsampling=0)
            draft_link=f'<p><a href="sheet-{n:02d}-draft.jpg">Open latest revision draft (not accepted)</a></p>'
        names = ', '.join(html.escape(x['name']) for x in records if x['sheet'] == n)
        sections.append(f'<section id="sheet-{n:02d}"><h2>Sheet {n:02d}</h2><p>{names}</p>{draft_link}'
            f'<div class="pair"><figure><figcaption>Current at start of review</figcaption>'
            f'<a href="sheet-{n:02d}-current.jpg"><img loading="lazy" src="sheet-{n:02d}-current.jpg"></a></figure>'
            f'<figure><figcaption>Previous-form reference</figcaption>'
            f'<a href="sheet-{n:02d}-previous.jpg"><img loading="lazy" src="sheet-{n:02d}-previous.jpg"></a></figure></div></section>')
    (OUT/'review-order.json').write_text(json.dumps({'species_count':125, 'columns':2, 'rows':3,
        'next_review_sheet':7, 'description':'Frozen initial review images, before corrections from sheets 1 through 6. See README.md for acceptance and pending work.',
        'species':records}, indent=2)+'\n', encoding='utf-8')
    for mode in ('current', 'previous'):
        shutil.copy2(BASE/f'review-sheets-{mode}/plant_catalog/report.json', OUT/f'capture-report-{mode}.json')
    nav = ' '.join(f'<a href="#sheet-{n:02d}">{n:02d}</a>' for n in range(1,22))
    (OUT/'index.html').write_text('''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Vivarium review handoff</title>
<style>body{background:#202829;color:#f0eee4;font:17px system-ui;margin:24px}a{color:#b4dbc6}nav{position:sticky;top:0;background:#202829;padding:12px;line-height:2}nav a{padding:8px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:18px}figure{margin:0}img{width:100%;display:block}section{scroll-margin-top:100px;margin:40px 0}@media(max-width:900px){.pair{grid-template-columns:1fr}}</style>
<h1>125 species · 21 paired review sheets</h1><p>Resume owner review at <a href="#sheet-07">sheet 7</a>. These are frozen initial-review images; corrections from sheets 1–6 are recorded in <a href="README.md">the handoff</a> and may already be implemented in source. Sizes are normalized per species. Click images for full resolution.</p><nav>'''+nav+'</nav>'+''.join(sections)+'</html>', encoding='utf-8')
    print(f'Exported {len(records)} species, 42 sheets to {OUT}')

if __name__ == '__main__':
    main()
