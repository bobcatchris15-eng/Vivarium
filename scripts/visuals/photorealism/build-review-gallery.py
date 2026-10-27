"""Build a local, read-only gallery from completed native review runs."""
import argparse
import html
import json
import os
import re
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', action='append', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--motion-stride', type=int, default=30)
    args = parser.parse_args()
    if args.motion_stride < 1:
        parser.error('--motion-stride must be positive')
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)

    def link(path):
        return html.escape(os.path.relpath(path.resolve(), output.parent).replace('\\', '/'), quote=True)

    sections = []
    navigation = []
    for run in args.run:
        summary = json.loads((run / 'run.json').read_text(encoding='utf-8-sig'))
        if summary['exit_code'] != 0 or not summary['source_stable'] or summary['engine_errors']:
            raise ValueError(f'Capture integrity failed: {run}')
        cards = []
        for image in sorted((run / 'captures').rglob('*.png')):
            motion = re.fullmatch(r'(?:live-|forest_motion_)(\d+)', image.stem)
            if motion and int(motion[1]) % args.motion_stride and int(motion[1]) not in (31, 239):
                continue
            name = image.relative_to(run / 'captures').as_posix()
            cards.append(f'<figure data-name="{html.escape(name, quote=True)}"><a href="{link(image)}"><img loading="lazy" src="{link(image)}" alt="{html.escape(name, quote=True)}"></a><figcaption>{html.escape(name)}</figcaption></figure>')
        section_id = f'run-{len(sections)}'
        navigation.append(f'<li><a href="#{section_id}">{html.escape(run.name)}</a></li>')
        sections.append(f'<section id="{section_id}"><h2>{html.escape(run.name)}</h2><p>{summary["png_count"]} images, {len(cards)} shown (motion sampled) · source stable during capture · {summary["engine_errors"]} engine errors · <a href="{link(run / "run.json")}">capture manifest</a></p><div class="grid">{"".join(cards)}</div></section>')
    comparison = output.parent / 'comparison.html'
    comparison_link = f'<p><a href="{link(comparison)}">Matched late-integration comparison (not a historical overhaul baseline)</a></p>' if comparison.exists() else ''
    output.write_text('''<!doctype html><html lang="en"><meta charset="utf-8"><title>Vivarium rendering review</title>
<style>body{margin:2rem;background:#151b19;color:#e2e9e5;font:16px system-ui}a{color:#a9dec5}h1{font-size:1.8rem}p{max-width:80ch;line-height:1.55}input{padding:.7rem;width:min(32rem,90%);background:#27312d;color:white;border:1px solid #758b7d;border-radius:.3rem}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:1rem}figure{margin:0;background:#222d27;padding:.5rem;border-radius:.4rem}img{width:100%;aspect-ratio:16/10;object-fit:contain;background:#101512}figcaption{padding:.5rem;overflow-wrap:anywhere}section{margin:2rem 0}figure[hidden]{display:none}</style>
<h1>Vivarium rendering review</h1><p>Native captures for human inspection. Each run records stable sources during its own capture; different runs may represent different revisions of the local edits. Capture integrity does not establish photographic quality or a performance target. Click an image for its original resolution. Refer to the completion report for exclusions, known defects, and remaining work.</p>
<label>Filter image names <input id="filter" type="search" placeholder="rock, face, coat, water, species…"></label>
''' + comparison_link + '<nav><ul>' + ''.join(navigation) + '</ul></nav>' + ''.join(sections) + '''<script>document.querySelector('#filter').addEventListener('input',e=>{const q=e.target.value.toLowerCase();document.querySelectorAll('figure').forEach(f=>f.hidden=!f.dataset.name.toLowerCase().includes(q));});</script></html>''', encoding='utf-8')
    print(output)


if __name__ == '__main__':
    main()

