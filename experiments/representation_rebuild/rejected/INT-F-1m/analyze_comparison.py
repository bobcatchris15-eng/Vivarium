import json
import sys
from datetime import datetime
from pathlib import Path

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parent
metrics = ['fps_mean', 'frame_ms_p50', 'frame_ms_p95', 'frame_ms_p99',
           'frame_ms_worst', 'gpu_frame_ms_mean', 'gpu_frame_ms_max',
           'allocated_bytes_per_frame', 'draw_calls', 'primitives',
           'video_mem_bytes', 'node_count', 'gc_gen0', 'gc_gen1', 'gc_gen2']
out = {'notes': ['One paired 180-second observation, warmup 120 seconds, cap 30; no repeated/interleaved acceptance claim.',
                 'Frozen control game DLL vs working candidate game DLL; both load the same current Sim DLL.',
                 'Candidate also includes the independently checked coverage snapshot fix; this is a stack comparison.',
                 'Per-second startup p95 maxima below are not pooled frame percentiles.']}
for side in ('candidate', 'control'):
    directory = root / f'paced-mature-{side}'
    perf = json.loads((directory / 'diagnostics/perf_summary.json').read_text(encoding='utf-8-sig'))
    samples_path = next((directory / 'diagnostics').glob('*/samples.jsonl'))
    rows = [json.loads(s) for s in samples_path.read_text(encoding='utf-8-sig').splitlines() if s]
    metadata = json.loads((samples_path.parent / 'metadata.json').read_text(encoding='utf-8-sig'))
    launch = json.loads((directory / 'launch.json').read_text(encoding='utf-8-sig'))
    iso = lambda s: datetime.fromisoformat(s.replace('Z', '+00:00'))
    offset = (iso(launch['started_utc']) - iso(metadata['started_utc'])).total_seconds()
    selected = [(row, row['elapsed_seconds'] - (rows[i-1]['elapsed_seconds'] if i else 0))
                for i, row in enumerate(rows)
                if offset + 25 <= row['elapsed_seconds'] <= offset + 75]
    def scopes(names):
        return {name: perf['scopes_cpu_ms'].get(name) for name in names}
    out[side] = {key: perf.get(key) for key in metrics}
    out[side].update({
        'flora_cache_start': perf.get('flora_cache_start'),
        'flora_cache_end': perf.get('flora_cache_end'),
        'flora_not_ready_frames': perf.get('flora_not_ready_frames'),
        'scopes': scopes(['Flora', 'Flora.RefreshSteps', 'Flora.LayerBuildStep',
                          'Flora.RecipeWorker', 'Coverage', 'Guardian.Vegetation', 'Session.Advance']),
        'raw_final_sample': {k: rows[-1][k] for k in ('tick','flora','fauna','backlog_sim_seconds','primitives')},
        'startup_25_to_75_seconds': {
            'sample_count': len(selected),
            'fps_weighted': sum(r['frame_count'] for r, dt in selected) / sum(dt for r, dt in selected),
            'worst_per_second_p95_ms': max(r['frame_ms_p95'] for r, dt in selected),
            'worst_frame_ms': max(r['frame_ms_worst'] for r, dt in selected),
            'note': 'Approximate boundaries from launch/diagnostic timestamps, one-second samples.'}})
out['percent_change'] = {key: 100 * (out['candidate'][key] / out['control'][key] - 1)
                         for key in metrics if out['control'][key] not in (None, 0)}
(root / 'paced-comparison.json').write_text(json.dumps(out, indent=2) + '\n', encoding='utf-8')
print(json.dumps(out, indent=2))
