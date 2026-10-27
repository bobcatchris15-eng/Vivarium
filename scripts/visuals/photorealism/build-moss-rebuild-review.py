"""Assemble the matched fleck regression and isolated coverage-form review."""
import json
from pathlib import Path
from PIL import Image

root=Path(__file__).resolve().parents[3]/'build/photorealism'
out=root/'moss-lichen-rebuild-review-20261024'
out.mkdir(exist_ok=True)
world_runs=['moss-rebuild-baseline-02','moss-srgb-clamp-only-03','moss-rebuild-world-03']
for name in world_runs:
    run=json.loads((root/name/'run.json').read_text(encoding='utf-8-sig'))
    assert run['source_stable'] and run['exit_code']==0 and run['engine_errors']==0 and run['png_count']==53,name
worlds=[json.loads((root/n/'captures/world_review.json').read_text(encoding='utf-8-sig')) for n in world_runs]
assert len({w['digest_before'] for w in worlds})==1
assert all(w['digest_before']==w['digest_after'] for w in worlds)
assert all(w['scenes'][0]['actual_eye']==worlds[0]['scenes'][0]['actual_eye'] for w in worlds)
assert all(w['scenes'][0]['target']==worlds[0]['scenes'][0]['target'] for w in worlds)
proof_path=out/'comparison-proof.json'
proof=json.loads(proof_path.read_text(encoding='utf-8')) if proof_path.exists() else {'runs':{}}
for name,w in zip(world_runs,worlds):
    proof['runs'][name]={'run':json.loads((root/name/'run.json').read_text(encoding='utf-8-sig')),'digest':w['digest_before'],'eye':w['scenes'][0]['actual_eye'],'target':w['scenes'][0]['target']}
proof['final_world_run']=world_runs[-1]
proof['final_isolation_run']='moss-rebuild-isolated-02'
proof['final_control_summary']='../moss-rebuild-world-03/fleck-control-summary.json'
proof['final_purple_scan']='../moss-rebuild-world-03/purple-candidates.json'
proof_path.write_text(json.dumps(proof,indent=2),encoding='utf-8')
world={}
for mode,count in [('baseline',12),('off',12),('restored',12),('motion_baseline',8),('motion_off',8)]:
    world[mode]=[]
    for name in world_runs:
        paths=[]
        for i in range(count):
            path=root/name/'captures'/f'log_coverage_far_reverse_dof_{mode}_{i:02}.png'
            assert path.exists(),path
            crop=out/'crops'/name/f'{mode}_{i:02}.png'
            crop.parent.mkdir(parents=True,exist_ok=True)
            Image.open(path).crop((590,240,690,340)).save(crop)
            paths.append(dict(full='../'+path.relative_to(root).as_posix(),crop=crop.relative_to(out).as_posix()))
        world[mode].append(paths)
isolation=root/'moss-rebuild-isolated-02'
run=json.loads((isolation/'run.json').read_text(encoding='utf-8-sig'))
assert run['source_stable'] and run['engine_errors']==0 and run['png_count']==144
species={}
for file in sorted((isolation/'captures').glob('*/specimen.json')):
    current=json.loads(file.read_text(encoding='utf-8'))
    name=current['species']
    previous=root/('sundew-clump-isolated-02' if name=='blue_sundew' else 'isolated-flora-flicker-02')/'captures'/name
    old=json.loads((previous/'specimen.json').read_text(encoding='utf-8'))
    def relative(p): return '../'+p.relative_to(root).as_posix()
    species[name]=dict(before=relative(previous),after=relative(file.parent),
        triangles_before=old['geometry']['Stats']['triangles_built'],triangles_after=current['geometry']['Stats']['triangles_built'],
        fixture_digest_matches=old['geometry']['Stats']['world_digest']==current['geometry']['Stats']['world_digest'])
assert len(species)==8
(out/'species-proof.json').write_text(json.dumps(species,indent=2),encoding='utf-8')
html='''<!doctype html><meta charset="utf-8"><title>Moss and lichen rebuild review</title>
<style>body{background:#131b21;color:#e6edf2;font:16px system-ui;margin:24px}p{max-width:1150px;line-height:1.5}button,select{font:inherit;padding:7px;margin:5px}a{color:#8bdcff}.columns{display:flex;gap:14px}.panel{flex:1;min-width:0;overflow:auto}img{display:block;width:100%}.crop img{image-rendering:pixelated;width:300px;max-width:100%}.zoom img{width:auto}h2{font-size:19px}table{border-collapse:collapse}td,th{padding:8px;border:1px solid #52606b}</style>
<h1>Moss and lichen: defect fix and visual rebuild</h1>
<p>The matched old-renderer test reproduces the purple block. The clamp-only test changes one shared shader include and retains the old geometry. The rebuilt version uses opaque contours, new species clumps and separate bounded tissue materials. MSAA 4x, TAA and FXAA remain enabled in these world comparisons.</p>
<p>The default crop magnifies the confirmed defect location without changing its colors. Use Full scene for context. Motion frames move the camera, so the fixed crop may no longer follow the defect.</p>
<button id="play">Pause</button><button id="prev">Previous frame</button><button id="next">Next frame</button><span id="frame"></span>
<select id="mode"><option value="baseline">Bokeh on</option><option value="off">Bokeh off</option><option value="restored">Restored bokeh on</option><option value="motion_baseline">Moving camera / bokeh on</option><option value="motion_off">Moving camera / bokeh off</option></select>
<select id="view"><option value="crop">Fleck close inspection</option><option value="full">Full scene</option></select>
<div class="columns crop" id="world"><div class="panel"><h2>Old renderer</h2><img id="old"></div><div class="panel"><h2>Input clamp only</h2><img id="clamp"></div><div class="panel"><h2>Rebuilt + numeric guards</h2><img id="new"></div></div>
<p><a href="comparison-proof.json">Matched world/camera proof</a> · <a href="species-proof.json">Isolated fixture and triangle counts</a></p>
<h2>Species form review</h2><p>All eight production coverage paths are isolated here, including the previously rebuilt sundew for regression review. These are first-pass procedural forms; artistic and performance acceptance remain separate from the numerical defect fix. Patch boundaries still show the coverage grid; macro organs remain visibly faceted.</p>
<select id="species"></select><select id="distance"><option value="whole">Whole patch</option><option value="macro">Close view</option></select><span id="stats"></span>
<div class="columns"><div class="panel"><h2>Previous appearance</h2><img id="speciesOld"></div><div class="panel"><h2>Rebuilt appearance</h2><img id="speciesNew"></div></div>
<script>const worldData=WORLD,speciesData=SPECIES;const $=id=>document.getElementById(id);let f=2,playing=true;
function draw(){const groups=worldData[$('mode').value];for(const [i,id] of ['old','clamp','new'].entries())$(id).src=groups[i][f%groups[i].length][$('view').value];$('world').classList.toggle('crop',$('view').value==='crop');$('frame').textContent='Frame '+(f+1)}
$('play').onclick=()=>{playing=!playing;$('play').textContent=playing?'Pause':'Play'};for(const [id,step] of [['prev',-1],['next',1]])$(id).onclick=()=>{playing=false;$('play').textContent='Play';const n=worldData[$('mode').value][0].length;f=(f+step+n)%n;draw()};$('mode').onchange=()=>{f=0;draw()};$('view').onchange=draw;setInterval(()=>{if(playing){f=(f+1)%worldData[$('mode').value][0].length;draw()}},250);draw();
for(const name of Object.keys(speciesData))$('species').add(new Option(name.replaceAll('_',' '),name));
function specimen(){const s=speciesData[$('species').value],v=$('distance').value;$('speciesOld').src=s.before+'/'+v+'_baseline_00.png';$('speciesNew').src=s.after+'/'+v+'_baseline_00.png';$('stats').textContent='Triangles: '+s.triangles_before+' → '+s.triangles_after+'; ecological fixture digest '+(s.fixture_digest_matches?'matches':'differs (visual reference only)')}
$('species').onchange=specimen;$('distance').onchange=specimen;specimen();</script>'''
(out/'index.html').write_text(html.replace('WORLD',json.dumps(world)).replace('SPECIES',json.dumps(species)),encoding='utf-8')
print('Validated three matched world runs and eight isolated species; review gallery generated.')
