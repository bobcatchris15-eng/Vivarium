from pathlib import Path
from PIL import Image, ImageDraw
import shutil, json, hashlib

root=Path('game/build/flora-system/review')
species=['shadebell','ironlace','fenneedle','lanternbrush','embercrown']
notes={
'shadebell':'Leathery emerald tops, quieter veins, matte undersides; pagoda shelves and hanging bells retained.',
'ironlace':'Matte forest-green tissue, branching veins and paler undersides; sinewy wood and papery seeds retained.',
'fenneedle':'Broad, long sweeping needles with wax and longitudinal grain. Fictional beauty takes priority over Earth proportions.',
'lanternbrush':'Satin teal compound leaflets, individual tissue variants and fine anatomy; curved wine-brown wood retained.',
'embercrown':'Warm green tissue with continuous leaflet coordinates and quieter compound anatomy; red fruit spikes retained.'}
audit=root/'woody-audit'
for s in species:
    dst=root/'woody-final'/s/'front'/'before'
    dst.mkdir(parents=True,exist_ok=True)
    shutil.copy2(audit/s/'whole'/f'{s}-mature.png',dst/f'{s}-mature.png')

style='body{font:17px system-ui;background:#17201a;color:#e2ebe4;max-width:1400px;margin:30px auto;padding:0 24px}a{color:#a8dfbf}section{margin:40px 0;border-top:1px solid #49624e}img{width:100%}.pair{display:grid;grid-template-columns:1fr 1fr;gap:14px}button{font:inherit;padding:7px 14px;margin:4px;background:#314b3b;color:#e2ebe4;border:1px solid #718f76;cursor:pointer}h2{margin-bottom:8px}@media(max-width:750px){.pair{grid-template-columns:1fr}}'
html=[f'<!doctype html><meta charset="utf-8"><title>Woody flora material review</title><style>{style}</style><h1>Five trees and shrubs — integrated material pass</h1><p>Four generated tissue variants per species, separate undersides, authored physical maps, baked anatomy and stable leaf motion. These finishes await your review.</p><p><a href="index.html">Original three pilots</a> · <a href="woody-audit.html">Earlier audit</a></p><img src="woody-final/gallery.png" alt="Five updated species">']
for s in species:
    prefix=f'woody-final/{s}'
    html.append(f'<section id="{s}"><h2>{s.title()}</h2><p>{notes[s]}</p>')
    for light in ['front','back','shade']:
        html.append(f'<button onclick="light(\'{s}\',\'{light}\')">{light.title()} light</button>')
    html.append(f'<div class="pair"><div><h3>Before</h3><img id="{s}-before" src="{prefix}/front/before/{s}-mature.png"></div><div><h3>After</h3><img id="{s}-after" src="{prefix}/front/after/{s}-mature.png"></div></div>')
    html.append(f'<p><a href="{prefix}/detail/{s}-mature.png">Close detail</a> · <a href="{prefix}/underside/{s}-underside.png">Underside</a> · <a href="{prefix}/world/species_{s}_close.png">In the world</a></p><details><summary>Wind: two phases, both sides</summary>')
    for reverse in ['', 'reverse-']:
        html.append('<div class="pair">')
        for phase in ['0','1.5']:
            html.append(f'<div><p>{"Reverse" if reverse else "Front"}, {phase} seconds</p><img src="{prefix}/wind-{reverse}{phase}/{s}-mature.png"></div>')
        html.append('</div>')
    html.append('</details></section>')
html.append('<script>function light(s,l){for(const t of ["before","after"])document.getElementById(s+"-"+t).src="woody-final/"+s+"/"+l+"/"+t+"/"+s+"-mature.png"}</script>')
if not (root/'woody-audit.html').exists(): shutil.copy2(root/'woody.html',root/'woody-audit.html')
(root/'woody.html').write_text(''.join(html),encoding='utf-8')
canvas=Image.new('RGB',(1800,760),'#17201a'); draw=ImageDraw.Draw(canvas)
for i,s in enumerate(species):
    for row,kind in enumerate(['front/after','detail']):
        src=root/'woody-final'/s/kind/f'{s}-mature.png'
        im=Image.open(src).convert('RGB').resize((360,360))
        canvas.paste(im,(i*360,row*380+20))
    draw.text((i*360+10,4),s.title(),fill='#e2ebe4')
canvas.save(root/'woody-final/gallery.png')
print('Wrote woody review gallery')
