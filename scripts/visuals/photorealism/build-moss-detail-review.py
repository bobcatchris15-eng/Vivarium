"""Validate and present the organic mat/structure refinement and weaker bokeh."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[3]/'build/photorealism'
out=root/'moss-detail-review-20261024';out.mkdir(exist_ok=True)
old_world='moss-rebuild-world-03';new_world='moss-detail-world-07';new_isolation='moss-detail-isolated-04'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
for name,count in [(old_world,53),(new_world,53),(new_isolation,144),('moss-detail-ruffle-06',18)]:
    r=load(root/name/'run.json')
    assert r['source_stable'] and r['engine_errors']==0 and r['exit_code']==0 and r['png_count']==count,(name,r)
a,b=[load(root/n/'captures/world_review.json') for n in [old_world,new_world]]
assert a['digest_before']==b['digest_before']==b['digest_after']
assert a['scenes'][0]['actual_eye']==b['scenes'][0]['actual_eye']
assert a['scenes'][0]['target']==b['scenes'][0]['target']
x,y=[load(root/n/'captures/log_coverage_far_reverse_dof_probe.json')['controls'][0] for n in [old_world,new_world]]
assert abs(y['amount']/x['amount']-.3)<.00001
for key in ['near_distance','near_transition','far_distance','far_transition','msaa']:
    assert (x[key]==y[key] if key=='msaa' else abs(x[key]-y[key])<1e-5),key
camera_frames=load(root/new_world/'captures/log_coverage_far_reverse_camera_frames.json')
assert len(camera_frames)==52 and min(f['alignment'] for f in camera_frames)>.9999
proof=dict(world_before=old_world,world_after=new_world,isolation_after=new_isolation,world_digest=b['digest_before'],bokeh_before=x['amount'],bokeh_after=y['amount'],bokeh_ratio=y['amount']/x['amount'],performance_acceptance=False)
world={}
for mode,count in [('baseline',12),('off',12),('restored',12),('motion_baseline',8),('motion_off',8)]:
    world[mode]=[[f'../{n}/captures/log_coverage_far_reverse_dof_{mode}_{i:02}.png' for i in range(count)] for n in [old_world,new_world]]
species={}
for p in sorted((root/new_isolation/'captures').glob('*/specimen.json')):
    name=load(p)['species']
    if name=='ruffle_lichen': p=root/'moss-detail-ruffle-06/captures/ruffle_lichen/specimen.json'
    now=load(p)
    old='sundew-clump-isolated-02' if name=='blue_sundew' else 'isolated-flora-flicker-02'
    dirs=[root/old/'captures'/name,root/'moss-rebuild-isolated-02/captures'/name,p.parent]
    refs=[load(d/'specimen.json') for d in dirs]
    species[name]=dict(paths=['../'+d.relative_to(root).as_posix() for d in dirs],triangles=[r['geometry']['Stats']['triangles_built'] for r in refs],digests=[r['geometry']['Stats']['world_digest'] for r in refs])
assert len(species)==8
proof['species']=species
proof['camera_frames_checked']=len(camera_frames)
proof['minimum_camera_alignment']=min(f['alignment'] for f in camera_frames)
(out/'proof.json').write_text(json.dumps(proof,indent=2),encoding='utf-8')
html="""<!doctype html><meta charset="utf-8"><title>Organic moss refinement</title>
<style>body{background:#15201d;color:#e8efe6;font:16px system-ui;margin:24px}p{max-width:1100px;line-height:1.5}a{color:#a7d7b7}.row{display:flex;gap:14px}.panel{flex:1;min-width:0}img{width:100%;display:block}button,select{font:inherit;padding:6px;margin:5px}h2{font-size:19px}</style>
<h1>Moss: organic mat, detailed structures, quieter bokeh</h1>
<p>The raised structures use their older species-specific geometry and a separate texture-free material. The mat has a warped, feathered perimeter, scanned color/normal/roughness, organic tuft cavities, and continuous height/health interpolation. Bokeh strength is 30% of its previous amount; focus distances are unchanged.</p>
<h2>Isolated species</h2><p>Original detail is included as a visual reference. The middle panel is the simplified rebuild you rejected; the right is the current refinement. See the proof for fixture digests and actual triangle counts.</p>
<select id="species"></select><select id="distance"><option value="whole">Whole patch</option><option value="macro">Close view</option></select><p id="stats"></p>
<div class="row"><div class="panel"><h2>Original species detail</h2><img id="s0"></div><div class="panel"><h2>Simplified rebuild</h2><img id="s1"></div><div class="panel"><h2>Current refinement</h2><img id="s2"></div></div>
<h2>Matched fallen-log habitat</h2><p>These views share the same world state and camera. Current combines the moss refinement with the requested 70% bokeh reduction. Bokeh-off comparisons help judge surface detail without blur.</p>
<button id="play">Play</button><button id="prev">Previous</button><button id="next">Next</button><select id="mode"><option value="baseline">Bokeh on</option><option value="off">Bokeh off</option><option value="restored">Bokeh restored</option><option value="motion_baseline">Moving camera, bokeh on</option><option value="motion_off">Moving camera, bokeh off</option></select><span id="frame"></span>
<div class="row"><div class="panel"><h2>Previous rebuild / full bokeh</h2><img id="w0"></div><div class="panel"><h2>Current refinement / 30% bokeh</h2><img id="w1"></div></div>
<p><a href="proof.json">Matched captures, bokeh ratio, fixture digests and triangle counts</a> · <a href="../moss-lichen-rebuild-review-20261024/index.html">Earlier numerical fleck-fix controls</a></p>
<script>const specimens=SPECIMENS,world=WORLDS,$=id=>document.getElementById(id);for(const n of Object.keys(specimens))$('species').add(new Option(n.replaceAll('_',' '),n));function showSpecies(){const s=specimens[$('species').value];for(let i=0;i<3;i++)$('s'+i).src=s.paths[i]+'/'+$('distance').value+'_baseline_00.png';$('stats').textContent='Triangles: '+s.triangles.join(' / ')+'; original and current fixture digest '+(s.digests[0]===s.digests[2]?'matches':'differs; original is visual reference only')}let f=0,play=false;function showWorld(){const w=world[$('mode').value];for(let i=0;i<2;i++)$('w'+i).src=w[i][f%w[i].length];$('frame').textContent='Frame '+(f+1)}$('play').onclick=()=>{play=!play;$('play').textContent=play?'Pause':'Play'};for(const [id,step] of [['prev',-1],['next',1]])$(id).onclick=()=>{play=false;$('play').textContent='Play';f=(f+step+world[$('mode').value][0].length)%world[$('mode').value][0].length;showWorld()};$('mode').onchange=()=>{f=0;showWorld()};$('species').onchange=showSpecies;$('distance').onchange=showSpecies;setInterval(()=>{if(play){f=(f+1)%world[$('mode').value][0].length;showWorld()}},300);$('species').value='bogglass_moss';showSpecies();showWorld();</script>"""
(out/'index.html').write_text(html.replace('SPECIMENS',json.dumps(species)).replace('WORLDS',json.dumps(world)),encoding='utf-8')
print(json.dumps({k:v for k,v in proof.items() if k!='species'}))
