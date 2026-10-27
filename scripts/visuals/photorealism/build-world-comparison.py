"""Compare two successful initial-world captures with matching recorded state/cameras."""
import argparse
import html
import json
import os
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    reports = []
    for run in (args.before, args.after):
        gate = read(run / 'run.json')
        if gate['exit_code'] or gate['engine_errors'] or not gate['source_stable']:
            raise ValueError(f'Capture integrity failed: {run}')
        report = read(run / 'captures/world_review.json')
        if report['digest_before'] != report['digest_after']:
            raise ValueError(f'Render changed world state: {run}')
        reports.append(report)
    before, after = reports
    for field in ('seed', 'tick', 'digest_before', 'resolution', 'quality', 'light_seconds'):
        if before[field] != after[field]:
            raise ValueError(f'Incompatible recorded world/settings: {field}')
    previous = {scene['name']: scene for scene in before['scenes']}
    matched, excluded, cards = [], [], []

    def link(path):
        return html.escape(os.path.relpath(path.resolve(), output.parent).replace('\\', '/'), quote=True)

    for scene in after['scenes']:
        name = scene['name']
        old = previous.get(name)
        if old is None or any(old.get(k) != scene.get(k) for k in ('actual_eye', 'target')):
            excluded.append(name)
            continue
        paths = [run / 'captures' / (name + '.png') for run in (args.before, args.after)]
        if not all(path.is_file() for path in paths):
            excluded.append(name)
            continue
        matched.append(name)
        pair = ''.join(f'<figure><a href="{link(path)}"><img loading="lazy" src="{link(path)}" alt="{html.escape(name)} {label}"></a><figcaption>{label}: {html.escape(run.name)}</figcaption></figure>' for path, run, label in zip(paths, (args.before, args.after), ('Earlier integration', 'Final integration')))
        cards.append(f'<section><h2>{html.escape(name)}</h2><div class="pair">{pair}</div></section>')
    proof = {'scope': 'local integration cleanup comparison', 'before': str(args.before.resolve()), 'after': str(args.after.resolve()), 'world_digest': before['digest_before'], 'matched_scenes': matched, 'excluded_scenes': excluded, 'performance_comparison': False, 'raw_shader_time_controlled': False}
    output.with_suffix('.json').write_text(json.dumps(proof, indent=2) + '\n', encoding='utf-8')
    output.write_text('''<!doctype html><html lang="en"><meta charset="utf-8"><title>Vivarium local integration comparison</title>
<style>body{margin:2rem;background:#151b19;color:#e2e9e5;font:16px system-ui}a{color:#a9dec5}p{max-width:85ch;line-height:1.5}.pair{display:grid;grid-template-columns:1fr 1fr;gap:1rem}figure{margin:0}img{width:100%}figcaption{padding:.5rem}section{margin:2rem 0}@media(max-width:800px){.pair{grid-template-columns:1fr}}</style>
<h1>Local integration comparison</h1><p>Earlier successful integration versus final integration. The recorded world digest, quality, lighting time and camera positions match. Some raw shader TIME users and initial CPU animal poses remain uncontrolled. This comparison covers integration cleanup; the historical clean baseline failed native rendering and is excluded. Performance is not evaluated.</p>
''' + f'<p>{len(matched)} matching scenes. {len(excluded)} scenes excluded.</p>' + ''.join(cards) + '</html>', encoding='utf-8')
    print(f'{output}: {len(matched)} matched scenes, {len(excluded)} excluded')


if __name__ == '__main__':
    main()
