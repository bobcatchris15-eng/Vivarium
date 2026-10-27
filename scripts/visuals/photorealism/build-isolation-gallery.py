"""Build an isolated-species gallery with fixed-frame playback and MSAA controls."""
import argparse
import html
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    args = parser.parse_args()
    gate = json.loads((args.run/'run.json').read_text(encoding='utf-8-sig'))
    report = json.loads((args.run/'captures/isolation_report.json').read_text())
    if gate['exit_code'] or gate['engine_errors'] or not gate['source_stable'] or report['failures']:
        raise ValueError('Native capture integrity failed')
    triage = json.loads((args.run/'temporal-analysis.json').read_text())
    scores = {r['species']:r['triage_score'] for r in triage['rows']}
    entries = [dict(species=r['species'], coverage=r['coverage'], score=scores[r['species']]) for r in report['results']]
    cards = ''.join(f'<button class="card" data-species="{html.escape(r["species"],quote=True)}"><img loading="lazy" src="captures/{r["species"]}/whole_baseline_00.png"><span>{html.escape(r["species"])} · temporal candidates {r["score"]}</span></button>' for r in entries)
    template = '''<!doctype html><html lang="en"><meta charset="utf-8"><title>Vivarium isolated flora review</title>
<style>body{background:#172019;color:#e7ede8;font:16px system-ui;margin:24px}a{color:#b9eac9}p{max-width:100ch;line-height:1.5}button,select,input{font:inherit}button,select{padding:.5rem;background:#2c3d30;color:white;border:1px solid #718873;border-radius:5px;cursor:pointer}nav{display:flex;gap:1rem;flex-wrap:wrap;align-items:center}.pair{display:grid;grid-template-columns:1fr 1fr;gap:1rem}.pane{overflow:auto;background:#111815}.pane img{display:block;width:100%}.native .pane img{width:1024px;max-width:none;image-rendering:pixelated}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:12px;margin-top:2rem}.card{padding:4px;text-align:left}.card img{width:100%;aspect-ratio:1}.card span{display:block;overflow-wrap:anywhere;font-size:13px}h2{font-size:1.3rem}@media(max-width:800px){.pair{grid-template-columns:1fr}}</style>
<h1>All flora species, isolated</h1><p>__COUNT__ species, each alone. Production coverage patches for moss/lichen/sundew; game-tier specimen geometry and materials for other flora. Whole and close views, six fixed-camera baseline frames and three MSAA-off controls at each scale. One authored fixture/variant per species; this is not exhaustive habitat, variant or performance acceptance.</p>
<p><a href="run.json">Capture integrity</a> · <a href="captures/isolation_report.json">Species manifest</a> · <a href="temporal-analysis.json">Temporal triage</a>. Candidate counts rank small changing pixel regions; legitimate edges and shading can trigger them. They are not confirmed defect counts. The two columns have different frame counts and are not synchronized gameplay frames.</p>
<nav><select id="species"></select><select id="view"><option value="whole">Whole specimen</option><option value="macro">Close view</option></select><button id="play">Play fixed-camera frames</button><label>Frame <input id="frame" type="range" min="0" max="5" value="0"></label><label><input id="native" type="checkbox">Native pixel size</label></nav>
<h2 id="title"></h2><p id="detail"></p><div class="pair" id="pair"><div><h3>Baseline: MSAA 4× + TAA + FXAA</h3><div class="pane"><a id="baseLink"><img id="base"></a></div></div><div><h3>MSAA off: TAA + FXAA retained</h3><div class="pane"><a id="controlLink"><img id="control"></a></div></div></div>
<div class="grid">__CARDS__</div><script>
const entries=__DATA__, select=document.querySelector('#species'), view=document.querySelector('#view'), slider=document.querySelector('#frame');
for(const e of entries){const o=document.createElement('option');o.value=e.species;o.textContent=e.species+' (candidates '+e.score+')';select.append(o);}
function update(){const e=entries.find(x=>x.species===select.value),i=+slider.value,p='captures/'+e.species+'/'+view.value;
document.querySelector('#title').textContent=e.species;document.querySelector('#detail').textContent=(e.coverage?'Production coverage patch':'Game-tier individual specimen')+' · baseline frame '+i+' · control frame '+(i%3)+' · '+e.score+' temporal candidates across both baseline views';
document.querySelector('#base').src=p+'_baseline_'+String(i).padStart(2,'0')+'.png';document.querySelector('#baseLink').href=document.querySelector('#base').src;
document.querySelector('#control').src=p+'_msaa_off_'+String(i%3).padStart(2,'0')+'.png';document.querySelector('#controlLink').href=document.querySelector('#control').src;}
select.onchange=view.onchange=slider.oninput=update;let timer=null;
document.querySelector('#play').onclick=()=>{if(timer){clearInterval(timer);timer=null;document.querySelector('#play').textContent='Play fixed-camera frames';}else{timer=setInterval(()=>{slider.value=(+slider.value+1)%6;update()},300);document.querySelector('#play').textContent='Pause';}};
document.querySelector('#native').onchange=e=>document.querySelector('#pair').classList.toggle('native',e.target.checked);
document.querySelectorAll('.card').forEach(b=>b.onclick=()=>{select.value=b.dataset.species;slider.value=0;update();window.scrollTo({top:0,behavior:'smooth'})});update();
</script></html>'''
    output = template.replace('__COUNT__',str(len(entries))).replace('__CARDS__',cards).replace('__DATA__',json.dumps(entries))
    (args.run/'index.html').write_text(output,encoding='utf-8')
    print(args.run/'index.html')


if __name__ == '__main__':
    main()
