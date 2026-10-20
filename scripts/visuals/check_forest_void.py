"""Verify the forest review's deliberately empty void frames, with capture provenance."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image

repo = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('runs', nargs='+', type=Path)
parser.add_argument('--output', type=Path, default=repo / 'build/forest-reference-20261015/void-verification.json')
args = parser.parse_args()
rows = []
for run in args.runs:
    launch = json.loads((run / 'launch.json').read_text(encoding='utf-8-sig'))
    assert launch['success'] and launch['source_stable'] and launch['forced_recompile'], run
    review = json.loads((run / 'world_review.json').read_text(encoding='utf-8-sig'))
    for name in ('forest_void', 'forest_void_underwater'):
        camera = next(s for s in review['scenes'] if s['name'] == name)
        path = run / f'{name}.png'
        with Image.open(path) as source:
            frame = source.convert('RGB')
            extrema = frame.getextrema()
            passed = extrema == ((0, 0), (0, 0), (0, 0))
            rows.append(dict(run=run.name, name=name, quality=review['quality'], light_seconds=review['light_seconds'],
                sha256=hashlib.sha256(path.read_bytes()).hexdigest(), resolution=frame.size,
                pixels=frame.width * frame.height, channel_extrema=extrema, passed=passed,
                source_tree_sha256=launch['source_tree_sha256'], binaries=launch['binaries'], camera=camera))
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(dict(passed=all(r['passed'] for r in rows),
    scope='Full deliberately empty void frames; forced underwater effect is diagnostic, not an aquatic scene; no universal boundary-camera claim.',
    frames=rows), indent=2), encoding='utf-8')
print(f"Exact RGB(0,0,0): {sum(r['passed'] for r in rows)}/{len(rows)} full frames")
raise SystemExit(0 if all(r['passed'] for r in rows) else 1)
