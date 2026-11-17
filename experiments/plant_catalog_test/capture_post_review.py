"""Capture the selected roster in bounded, serial native Godot batches.

Run only after the current build and GPU owner explicitly releases its lock.
"""
import argparse
import json
import os
import subprocess
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'docs/representation-rebuild/post-review-2026-11-17/manifest.json'
OUT = ROOT / 'build/post-review-species-20261117'
GODOT = ROOT / 'tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
SEED = '804584441'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-module', required=True)
    parser.add_argument('--sim-module', required=True)
    args = parser.parse_args()
    ids = [s['id'] for s in json.loads(MANIFEST.read_text(encoding='utf-8'))['species']]
    if len(ids) != 79 or len(set(ids)) != 79:
        raise ValueError('Manifest must have exactly 79 unique IDs')
    OUT.mkdir(parents=True, exist_ok=False)
    env = os.environ.copy()
    env['APPDATA'] = str(OUT / 'isolated-appdata')
    env['LOCALAPPDATA'] = str(OUT / 'isolated-localappdata')
    Path(env['APPDATA']).mkdir()
    Path(env['LOCALAPPDATA']).mkdir()
    batches = []
    for start in range(0, len(ids), 12):
        selected = ids[start:start + 12]
        directory = f'batch-{start // 12 + 1:02d}'
        command = [str(GODOT), '--path', str(ROOT / 'game'), '--', '--repr-review', str(OUT / directory),
                   '--repr-demo', 'plant_catalog', '--seed', SEED, '--plant-catalog-mature-only',
                   '--plant-catalog-species', ','.join(selected)]
        started = datetime.now(timezone.utc).isoformat()
        print(f'Starting {directory}: {len(selected)} species', flush=True)
        with (OUT / (directory + '.log')).open('w', encoding='utf-8') as log:
            result = subprocess.run(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
        if result.returncode:
            raise RuntimeError(f'{directory} failed with exit {result.returncode}; see its log')
        report = json.loads((OUT / directory / 'plant_catalog/report.json').read_text(encoding='utf-8'))
        if (str(report['game_module']).lower() != args.game_module.lower()
                or str(report['sim_module']).lower() != args.sim_module.lower() or str(report['seed']) != SEED):
            raise ValueError(f'{directory} did not use the expected final modules and seed')
        if len(report['shots']) != len(selected) or {s['name'] for s in report['shots']} != set(selected):
            raise ValueError(f'{directory} report does not match its selected species')
        batches.append({'directory': directory, 'ids': selected, 'command': command, 'started_utc': started,
                        'completed_utc': datetime.now(timezone.utc).isoformat()})
        (OUT / 'batches.json').write_text(json.dumps({'batches': batches}, indent=2) + '\n', encoding='utf-8')
        print(f'Completed {directory}', flush=True)
    print(f'Captured {len(ids)} species in {len(batches)} batches at {OUT}', flush=True)


if __name__ == '__main__':
    main()
