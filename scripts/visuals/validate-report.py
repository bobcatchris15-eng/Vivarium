#!/usr/bin/env python3
"""Validate living-landscape v1 raw reports. Standard library only; never trusts summaries."""
import argparse
import hashlib
import html
import json
import math
import sys
import re
import struct
from datetime import datetime
from pathlib import Path

REQUIRED = ('invariants', 'schema_version', 'executing_code', 'provenance', 'world', 'scenario', 'render_settings', 'cpu_timing', 'gpu_timing', 'frame_times', 'counters', 'digest_before', 'digest_after', 'render_only_digest_before', 'render_only_digest_after', 'artifacts', 'warnings')
PROVENANCE = ('run_id', 'source_revision', 'source_tree_sha256', 'engine_version', 'executable_sha256', 'gpu', 'driver', 'cpu', 'ram_bytes', 'os', 'display_scaling', 'isolation_confirmed', 'started_utc', 'preset', 'startup_kind')

def number(value, positive=False):
    return type(value) in (int, float) and math.isfinite(value) and (value > 0 if positive else value >= 0)

def percentile(samples, quantile):
    values = sorted(samples)
    return values[min(len(values)-1, int(len(values)*quantile))]

def validate(report, directory=None):
    errors = []
    def require(ok, message):
        if not ok: errors.append(message)
    require(type(report) is dict, 'report must be an object')
    if type(report) is not dict: return errors
    for key in REQUIRED: require(key in report, 'missing section: ' + key)
    if errors: return errors
    require(report['schema_version'] == 1, 'unsupported schema_version')
    p = report['provenance']
    if type(p) is not dict: return errors + ['provenance must be object']
    for key in PROVENANCE: require(key in p and p[key] is not None and p[key] != '', 'missing provenance: ' + key)
    for key in ('run_id', 'source_revision', 'source_tree_sha256', 'engine_version', 'executable_sha256', 'gpu', 'driver', 'cpu', 'os', 'preset', 'startup_kind'):
        require(type(p.get(key)) is str and bool(p[key]), 'invalid provenance type: ' + key)
    require(number(p.get('ram_bytes'), True), 'invalid RAM')
    require(number(p.get('display_scaling'), True), 'invalid display scaling')
    require(type(p.get('source_revision')) is str and bool(re.fullmatch(r'[0-9a-fA-F]{40}', p['source_revision'])), 'source revision must be full SHA')
    try: datetime.fromisoformat(p.get('started_utc','').replace('Z','+00:00'))
    except (ValueError, AttributeError): errors.append('invalid provenance timestamp')
    code=report['executing_code']
    require(type(code) is dict and code.get('game_assembly_mvid_matches') is True and code.get('sim_assembly_mvid_matches') is True, 'executing module identity was not verified')
    require(type(code) is dict and all(type(code.get(k)) is str and bool(re.fullmatch(r'[0-9a-fA-F]{64}',code[k])) for k in ('game_assembly_sha256','sim_assembly_sha256')), 'executing assembly hashes missing/invalid')
    if p.get('build_game_sha256') != 'portable-runtime-attested':
        require(p.get('build_game_sha256') == code.get('game_assembly_sha256'), 'executing game assembly differs from launcher build')
    require(p.get('startup_kind') in ('cold','warm'), 'invalid startup kind')
    require(p.get('isolation_confirmed') is True, 'GPU isolation not confirmed')
    for key in ('source_tree_sha256', 'executable_sha256'):
        require(isinstance(p.get(key), str) and bool(re.fullmatch(r'[0-9a-fA-F]{64}', p[key])), 'invalid digest provenance: ' + key)
    settings = report['render_settings']
    # Production quality 1 explicitly supports the measured 95% presentation.
    # Larger diagnostic resolution reductions still cannot pass the visual gate.
    production_scale = settings.get('scale') == 1 or (settings.get('quality') == 1 and settings.get('scale') == .95)
    require(settings.get('width') == 1600 and settings.get('height') == 900 and production_scale, 'visual target resolution/scale mismatch')
    require(settings.get('quality') in (1, 2) and settings.get('renderer') == 'forward_plus', 'visual target quality/renderer mismatch')
    for key in ('width','height','scale','quality','renderer','display','vsync','msaa','screen_aa','flora_tiers','flags','adapter'):
        require(key in settings, 'missing render setting: ' + key)
    require(isinstance(settings.get('display'), str) and settings['display'].lower() in ('windows','x11','wayland','macos'), 'headless report has no visual/GPU acceptance')
    require(settings.get('vsync') == 'Disabled', 'VSync must be disabled')
    scenario = report['scenario']
    require(type(scenario.get('evolves')) is bool and type(scenario.get('sculpts')) is bool, 'scenario modes must be boolean')
    require(scenario.get('evolves') == (scenario.get('id') in ('live', 'stationary')) and scenario.get('sculpts') == (scenario.get('id') == 'sculpt'), 'scenario ID/mode mismatch')
    require(scenario.get('id') in ('frozen','live','understory','colony','traversal','sculpt','wet','stationary','repeat_traversal'), 'unsupported scenario')
    for key in ('digest_before','digest_after','render_only_digest_before','render_only_digest_after'):
        require(type(report[key]) is str and bool(re.fullmatch(r'[0-9a-fA-F]{64}', report[key])), 'invalid world digest: ' + key)
    for key in ('seed','tick_before','tick_after','flora','fauna'):
        require(type(report['world'].get(key)) is int and report['world'][key]>=0, 'invalid world counter: ' + key)
    require(number(scenario.get('duration_seconds'), True) and scenario['duration_seconds'] >= 60, 'less than 60 seconds measured')
    require(number(scenario.get('warmup_seconds')) and scenario['warmup_seconds'] >= 10, 'warmup missing')
    require(scenario.get('library_version') in (1, 2), 'scenario library incompatible')
    if scenario.get('library_version') == 2:
        require(scenario.get('flora_population_ready') is True, 'flora population incomplete at measurement')
    frame = report['frame_times']; samples = frame.get('samples_ms', [])
    require(isinstance(samples, list) and len(samples) >= 30 and all(number(x, True) for x in samples), 'malformed frame timings')
    if samples and all(number(x, True) for x in samples):
        require(abs(sum(samples)/1000 - scenario.get('duration_seconds', 0)) < .5, 'raw frames do not cover measurement (dropped samples)')
        for key, q in [('p50_ms', .5), ('p95_ms', .95), ('p99_ms', .99), ('p999_ms', .999), ('worst_ms', 1)]:
            require(number(frame.get(key)) and abs(frame[key] - percentile(samples, q)) < .01, 'incorrect summary: ' + key)
        require(percentile(samples, .99) <= 33.3, 'p99 exceeds 33.3 ms')
        require(not any(x > 50 for x in samples), 'hitch above 50 ms requires disclosed retest; no automatic pass')
    windows = frame.get('windows', [])
    # Windows close on a real frame boundary, so valid 1.0--1.2 second
    # intervals can number fewer than 59. Validate their actual coverage below.
    require(bool(windows), 'insufficient 1-second windows')
    end = 0; total_frames = 0
    for w in windows:
        if not all(number(w.get(k), k in ('duration_s', 'frames', 'fps')) for k in ('start_s','duration_s','frames','fps')):
            errors.append('malformed window'); continue
        require(abs(w['start_s'] - end) < .01, 'non-contiguous FPS windows')
        require(w['duration_s'] >= 1 and w['duration_s'] < 1.2, 'window stalled/skipped')
        require(abs(w['fps'] - w['frames']/w['duration_s']) < .01, 'incorrect FPS window')
        require(w['fps'] >= 30, '1-second window below 30 FPS')
        require(type(w['frames']) is int, 'window frames must be integer')
        if samples and all(number(x, True) for x in samples) and type(w['frames']) is int:
            covered_ms = sum(samples[total_frames:total_frames + w['frames']])
            require(abs(covered_ms/1000 - w['duration_s']) < .02, 'window does not match raw frame durations/count')
        total_frames += w['frames']; end = w['start_s'] + w['duration_s']
    require(total_frames <= len(samples) and sum(samples[total_frames:])/1000 < 1.000001 if all(number(x,True) for x in samples) else False, 'FPS windows/frame samples inconsistent')
    require(abs(scenario.get('duration_seconds', 0) - end) < 1.2, 'windows do not cover measurement')
    invariants = report['invariants']
    require(type(invariants) is dict and invariants.get('ticks') == 3, 'same-tick replay missing')
    if type(invariants) is dict:
        require(invariants.get('reload_digest') == report['render_only_digest_after'], 'reload digest invariant failed')
        require(type(invariants.get('replay_expected_digest')) is str and bool(re.fullmatch(r'[0-9a-fA-F]{64}', invariants['replay_expected_digest'])) and invariants.get('replay_expected_digest') == invariants.get('replay_actual_digest'), 'same-tick replay invariant failed')
    require(report['render_only_digest_before'] == report['render_only_digest_after'], 'render-only simulation digest changed')
    if not scenario.get('evolves') and not scenario.get('sculpts'):
        require(report['digest_before'] == report['digest_after'], 'frozen simulation digest changed')
    memory = report['counters'].get('memory', [])
    require(len(memory) == len(windows) and bool(memory), 'memory series incomplete')
    for i, sample in enumerate(memory):
        require(all(number(sample.get(k), k == 'working_set_bytes') for k in ('elapsed_s', 'working_set_bytes', 'vram_bytes')), 'invalid memory sample')
        if i < len(windows) and number(sample.get('elapsed_s')):
            require(abs(sample['elapsed_s'] - windows[i].get('start_s', 0) - windows[i].get('duration_s', 0)) < .05, 'memory temporal coverage mismatches frames')
    if memory and all(number(x.get('working_set_bytes'), True) and number(x.get('vram_bytes')) for x in memory):
        require(max(x['vram_bytes'] for x in memory) > 0, 'VRAM monitor unavailable')
        require(max(x['vram_bytes'] for x in memory) < 6 * 1024**3, 'VRAM exceeds 6 GiB budget')
        for key in ('working_set_bytes', 'vram_bytes'):
            first = sorted(x[key] for x in memory[:10])[5]; last = sorted(x[key] for x in memory[-10:])[5]
            require(last - first <= 64 * 1024**2, key + ' growth exceeds 64 MiB screening threshold (20 minute soak still required)')
    require(report['gpu_timing'] is None, 'GPU timing v1 is unsupported and must be null')
    viewport_gpu = report.get('viewport_gpu_timing')
    if viewport_gpu is not None:
        require(type(viewport_gpu) is dict, 'viewport GPU timing must be an object')
        if type(viewport_gpu) is dict:
            gpu_samples = viewport_gpu.get('samples_ms')
            require(type(gpu_samples) is list and all(number(x, True) for x in gpu_samples), 'invalid viewport GPU samples')
            if type(gpu_samples) is list:
                require(viewport_gpu.get('available') is bool(gpu_samples), 'viewport GPU availability mismatches samples')
                if gpu_samples and all(number(x, True) for x in gpu_samples):
                    mean = viewport_gpu.get('mean_ms')
                    require(number(mean, True) and abs(mean - sum(gpu_samples)/len(gpu_samples)) < .001, 'invalid viewport GPU mean')
                else:
                    require(viewport_gpu.get('mean_ms') is None, 'unavailable viewport GPU must have null mean')
    require(type(report['cpu_timing']) is list and len(report['cpu_timing']) == len(windows), 'CPU series incomplete')
    if type(report['cpu_timing']) is list:
        for i, sample in enumerate(report['cpu_timing']):
            require(type(sample) is dict, 'malformed CPU sample')
            if type(sample) is not dict: continue
            require(all(number(sample.get(k)) for k in ('elapsed_s','process_peak_ms','simulation_last_ms')), 'invalid CPU timing')
            if i < len(windows) and number(sample.get('elapsed_s')):
                require(abs(sample['elapsed_s'] - windows[i].get('start_s',0) - windows[i].get('duration_s',0)) < .05, 'CPU temporal coverage mismatch')
            scopes = sample.get('scope_max_ms')
            require(type(scopes) is dict and all(type(k) is str and number(x) for k,x in scopes.items()), 'malformed CPU scope timing')
    artifacts = report['artifacts']
    require(len(artifacts) >= 14, 'reference/motion evidence absent')
    names = [x.get('name') for x in artifacts]; paths = [x.get('path') for x in artifacts]
    require(len(set(names)) == len(names) and len(set(paths)) == len(paths), 'duplicate capture names/paths')
    require({'overview','interaction','ground_vegetation','macro_flora','dense_colony','water_margin','sparse_dry','mixed_depth'}.issubset(names), 'required reference regimes missing')
    require(all('motion_' + str(i).zfill(2) in names for i in range(12)), '12 second motion sequence incomplete')
    require(scenario.get('scenes') == artifacts, 'scenario capture manifest mismatches artifacts')
    if directory is not None:
        root = Path(directory).resolve()
        for artifact in artifacts:
            file = (root / artifact.get('path', '')).resolve()
            require(file.is_relative_to(root) and file.is_file(), 'missing/outside capture: ' + str(artifact.get('path')))
            if file.is_relative_to(root) and file.is_file():
                header=file.read_bytes()[:24]
                require(len(header)==24 and header[:8]==b'\x89PNG\r\n\x1a\n' and header[12:16]==b'IHDR' and struct.unpack('>II',header[16:24])==(1600,900), 'invalid PNG/size: '+str(artifact.get('path')))
    return errors

def compatibility(before, after):
    errors = []
    if before["provenance"].get("accessibility_mode", "auto") != after["provenance"].get("accessibility_mode", "auto"): errors.append("incompatible provenance: accessibility_mode")
    for key in ('engine_version', 'gpu', 'driver', 'cpu', 'ram_bytes', 'os', 'display_scaling', 'preset', 'startup_kind'):
        if before['provenance'].get(key) != after['provenance'].get(key): errors.append('incompatible provenance: ' + key)
    for section in ('render_settings',):
        if before[section] != after[section]: errors.append('incompatible ' + section)
    for key in ('id', 'library_version', 'preparation', 'selected_scene', 'warmup_seconds', 'evolves', 'sculpts'):
        if before['scenario'].get(key) != after['scenario'].get(key): errors.append('incompatible scenario: ' + key)
    for key in ('seed', 'tick_before', 'tick_after', 'flora', 'fauna', 'flora_after', 'fauna_after', 'preparation_bio_days'):
        if before['world'].get(key) != after['world'].get(key): errors.append('incompatible world: ' + key)
    if before['digest_after'] != after['digest_after']: errors.append('incompatible final world digest/tick outcome')
    if before['digest_before'] != after['digest_before']: errors.append('incompatible initial world digest')
    def poses(r): return [{k:a.get(k) for k in ('name','eye','target','light_seconds')} for a in r['scenario']['scenes']]
    if poses(before) != poses(after): errors.append('incompatible captures/camera/light')
    return errors

def comparison(before, after, before_path, after_path, output):
    rows = []
    for key in ('p50_ms','p95_ms','p99_ms','p999_ms','worst_ms'):
        b, a = before['frame_times'][key], after['frame_times'][key]
        rows.append(f'<tr><td>{key}</td><td>{b:.3f}</td><td>{a:.3f}</td><td>{(a/b-1)*100:+.2f}%</td></tr>')
    cards = []
    amap = {x['name']:x for x in after['artifacts']}
    for item in before['artifacts']:
        if item['name'] not in amap: continue
        b = (before_path.parent / item['path']).resolve().as_uri(); a = (after_path.parent / amap[item['name']]['path']).resolve().as_uri()
        cards.append(f'<h2>{html.escape(item["name"])}</h2><a href="{b}"><img width="48%" src="{b}"></a> <a href="{a}"><img width="48%" src="{a}"></a>')
    output.write_text('<!doctype html><meta charset="utf-8"><title>Landscape comparison</title><h1>Matched landscape comparison</h1><table><tr><th>Wall frame time</th><th>Before</th><th>After</th><th>Delta</th></tr>' + ''.join(rows) + '</table>' + ''.join(cards), encoding='utf-8')

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('report', type=Path); parser.add_argument('--compare', type=Path); parser.add_argument('--html', type=Path); parser.add_argument('--result', type=Path)
    args = parser.parse_args()
    try:
        report = json.loads(args.report.read_text(encoding='utf-8-sig')); errors = validate(report, args.report.parent)
        if args.compare:
            other = json.loads(args.compare.read_text(encoding='utf-8-sig')); errors += validate(other, args.compare.parent); errors += compatibility(report, other)
            if not errors:
                if other['frame_times']['p99_ms'] > report['frame_times']['p99_ms'] * 1.05: errors.append('matched p99 regression above 5%; image-quality gain requires explicit review')
                if args.html: comparison(report, other, args.report, args.compare, args.html)
        result = {'passed':not errors, 'errors':errors, 'report':str(args.report), 'gate':'60 second screening only; G0 still requires all scenarios and 20 minute soak'}
    except (OSError, ValueError, KeyError, TypeError, IndexError) as ex:
        result = {'passed':False, 'errors':['malformed/unreadable report: ' + str(ex)]}
    if args.result: args.result.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2)); return 0 if result['passed'] else 1

if __name__ == '__main__': sys.exit(main())
