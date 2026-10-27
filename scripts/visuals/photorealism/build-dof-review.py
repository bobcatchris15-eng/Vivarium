"""Build fixed-camera DOF comparisons and temporal triage, without asserting a root cause."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
from importlib.util import spec_from_file_location, module_from_spec


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    args = parser.parse_args()
    spec = spec_from_file_location('triage', Path(__file__).with_name('analyze-isolated-species.py'))
    triage = module_from_spec(spec)
    spec.loader.exec_module(triage)
    groups = {}
    for path in sorted((args.run / 'captures').rglob('*_00.png')):
        if '_dof_' not in path.name and not path.name.startswith('dof_'):
            continue
        prefix = path.stem[:-3]
        paths = sorted(path.parent.glob(prefix + '_[0-9][0-9].png'))
        key = str(path.parent.relative_to(args.run / 'captures') / prefix).replace('\\', '/')
        groups[key] = [p.relative_to(args.run).as_posix() for p in paths]
    results = []
    for key, paths in groups.items():
        if '_motion_' in key:
            results.append(dict(group=key, frames=len(paths), screening='Skipped: camera movement invalidates fixed-pixel temporal triage.'))
            continue
        frames = np.stack([np.asarray(Image.open(args.run / p).convert('RGB')) for p in paths]).astype(np.int16)
        median = np.median(frames, axis=0)
        delta = np.abs(frames - median)
        peaks = delta.max(axis=(0, 3))
        thresholds = {}
        for level in (16, 32, 64):
            candidates = triage.components(peaks > level)
            thresholds[str(level)] = dict(pixels=int((peaks > level).sum()), small_components=len(candidates), candidates=candidates[:20])
        results.append(dict(group=key, frames=len(paths), mean_absolute_display_delta=float(delta.mean()), thresholds=thresholds))
    (args.run / 'temporal-analysis.json').write_text(json.dumps(dict(
        scope='Candidate screening only: changes may include sampling, blur, animals and uncontrolled shader TIME. Neither a candidate nor its absence proves a rendering defect or a root cause.',
        rows=results), indent=2), encoding='utf-8')
    html = '''<!doctype html><meta charset="utf-8"><title>Fixed-camera bokeh controls</title>
<style>body{background:#141a20;color:#e3edf3;font:16px system-ui;margin:24px}a{color:#8bd8ff}select,button{font:inherit;margin:6px;padding:6px;max-width:95%}.pair{display:flex;gap:12px}.view{width:50%;overflow:auto}img{width:100%;display:block}body.zoom img{width:auto}p{max-width:1100px}</style>
<h1>Fixed-camera bokeh controls</h1><p>Compare the same scene across controls. Ordinary softness is expected with defocus; look for the reported black or cyan/magenta flecks. These captures alone do not establish a cause. Frames loop at 6 fps. Zoom shows native pixels; use scroll bars to inspect.</p>
<button id="play">Pause</button><button onclick="document.body.classList.toggle('zoom')">Native pixel zoom</button><span id="frame"></span>
<div class="pair"><div class="view"><select id="left"></select><img id="a"></div><div class="view"><select id="right"></select><img id="b"></div></div>
<p><a href="run.json">Run integrity</a> · <a href="temporal-analysis.json">Temporal candidate screening</a></p>
<script>const groups=GROUPS;const keys=Object.keys(groups);let f=0,playing=true;
for(const id of ['left','right']){const s=document.getElementById(id);for(const k of keys)s.add(new Option(k,k));s.onchange=()=>{f=0;draw()}}
right.selectedIndex=Math.min(1,keys.length-1);const baseline=keys.find(k=>k.endsWith('_dof_baseline'));if(baseline){left.value=baseline;right.value=baseline.replace(/baseline$/,'off')}
function draw(){for(const [s,i] of [[left,a],[right,b]]){const list=groups[s.value];i.src=list[f%list.length]}frame.textContent='Frame '+(f+1)}
play.onclick=()=>{playing=!playing;play.textContent=playing?'Pause':'Play'};setInterval(()=>{if(playing){f=(f+1)%groups[left.value].length;draw()}},167);draw();</script>'''
    (args.run / 'index.html').write_text(html.replace('GROUPS', json.dumps(groups)), encoding='utf-8')
    print(json.dumps([dict(group=r['group'], components64=r.get('thresholds', {}).get('64', {}).get('small_components'), delta=round(r.get('mean_absolute_display_delta', 0), 4)) for r in results]))


if __name__ == '__main__':
    main()
