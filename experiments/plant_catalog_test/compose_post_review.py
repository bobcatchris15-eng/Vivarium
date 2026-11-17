"""Compose fresh post-review sheets from one complete native catalog capture."""
import argparse
import html
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/representation-rebuild/post-review-2026-11-17'
FONT = Path('C:/Windows/Fonts/segoeui.ttf')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('capture', type=Path, help='batch capture root')
    args = parser.parse_args()
    capture = args.capture.resolve()
    manifest = json.loads((OUT / 'manifest.json').read_text(encoding='utf-8'))
    species = manifest['species']
    batches = json.loads((capture / 'batches.json').read_text(encoding='utf-8'))['batches']
    expected = {s['id'] for s in species}
    by_id = {}
    reports = []
    signatures = set()
    for batch in batches:
        report_path = capture / batch['directory'] / 'plant_catalog' / 'report.json'
        report = json.loads(report_path.read_text(encoding='utf-8'))
        if report['demo'] != 'plant_catalog' or not report.get('game_module') or not report.get('sim_module'):
            raise ValueError('Capture report lacks catalog/module provenance')
        signatures.add((report['game_module'], report['sim_module'], report['seed'],
                        json.dumps(report['machine'], sort_keys=True), json.dumps(report['env_overrides'], sort_keys=True),
                        report.get('plant_config'), report.get('camera_reference')))
        if len(batch['ids']) > 12 or {s['name'] for s in report['shots']} != set(batch['ids']):
            raise ValueError(f"Unexpected shot roster in {batch['directory']}")
        for shot in report['shots']:
            if shot['name'] in by_id:
                raise ValueError(f"Duplicate shot for {shot['name']}")
            by_id[shot['name']] = (shot, report_path.parent)
        reports.append((batch, report, report_path))
    if len(species) != 79 or len(expected) != 79 or set(by_id) != expected or len(signatures) != 1:
        raise ValueError('Report must contain exactly one shot for each of the 79 manifest species')
    for ident, (shot, folder) in by_id.items():
        if shot.get('shot') != ident + '_growth' or shot.get('png') != ident + '_growth.png':
            raise ValueError(f'Unexpected shot association for {ident}')
        if not all(k in shot for k in ('eye', 'target', 'fov_deg')):
            raise ValueError(f'Missing camera provenance for {ident}')
        with Image.open(folder / shot['png']) as im:
            if im.size != (1600, 900):
                raise ValueError(f'Unexpected capture dimensions for {ident}: {im.size}')
            im.verify()
    label_font = ImageFont.truetype(str(FONT), 27)
    detail_font = ImageFont.truetype(str(FONT), 17)
    header_font = ImageFont.truetype(str(FONT), 31)
    sections = []
    for start in range(0, len(species), 6):
        batch = species[start:start + 6]
        page = start // 6 + 1
        sheet = Image.new('RGB', (1800, 1870), '#202829')
        draw = ImageDraw.Draw(sheet)
        draw.text((24, 14), f'Post-review species | Sheet {page:02d} / 14', font=header_font, fill='#f0eee4')
        for index, item in enumerate(batch):
            x, y = (index % 2) * 900, 70 + (index // 2) * 600
            shot, folder = by_id[item['id']]
            with Image.open(folder / shot['png']) as im:
                image = im.crop((200, 80, 1400, 800)).resize((900, 540), Image.Resampling.LANCZOS)
            sheet.paste(image, (x, y + 60))
            draw.text((x + 18, y + 5), f"{item['number']:02d}. {item['name']}", font=label_font, fill='#f0eee4')
            draw.text((x + 18, y + 36), f"{item['id']} | original sheet {item['original_sheet']:02d}",
                      font=detail_font, fill='#bac7bf')
        filename = f'sheet-{page:02d}.jpg'
        sheet.save(OUT / filename, quality=95, subsampling=0)
        names = ', '.join(html.escape(s['name']) for s in batch)
        sections.append(f'<section id="sheet-{page:02d}"><h2>Sheet {page:02d}</h2><p>{names}</p>'
                        f'<a href="{filename}"><img loading="lazy" src="{filename}" alt="Sheet {page:02d}"></a></section>')
    reports_dir = OUT / 'reports'
    reports_dir.mkdir(exist_ok=True)
    for batch, report, report_path in reports:
        shutil.copyfile(report_path, reports_dir / (batch['directory'] + '.json'))
        batch['report'] = 'reports/' + batch['directory'] + '.json'
        batch['report_modified_utc'] = datetime.fromtimestamp(report_path.stat().st_mtime, timezone.utc).isoformat()
    game_module, sim_module, seed, *_ = next(iter(signatures))
    metadata = {'seed': seed, 'game_module': game_module, 'sim_module': sim_module,
                'manifest': 'manifest.json', 'batches': batches}
    (OUT / 'capture-provenance.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    nav = ' '.join(f'<a href="#sheet-{n:02d}">{n:02d}</a>' for n in range(1, 15))
    report_links = ' · '.join(f'<a href="{b["report"]}">{b["directory"]} report</a>' for b, _, _ in reports)
    (OUT / 'index.html').write_text('''<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width"><title>Post-review species</title>
<style>body{background:#202829;color:#f0eee4;font:17px system-ui;margin:24px}a{color:#b4dbc6}
nav{position:sticky;top:0;background:#202829;padding:12px;line-height:2}nav a{padding:8px}
img{width:min(100%,900px);display:block}section{scroll-margin-top:100px;margin:40px 0}</style>
<h1>79 revised species · 14 sheets</h1><p>Fresh mature native captures; species sizes are normalized individually.
<a href="manifest.json">Manifest</a> ·
<a href="capture-provenance.json">Provenance</a>. Click a sheet for full resolution.</p><p>'''
        + report_links + '</p><nav>' + nav + '</nav>' + ''.join(sections) + '</html>', encoding='utf-8')
    print(f'Validated 79 native shots; wrote 14 sheets to {OUT}')


if __name__ == '__main__':
    main()
