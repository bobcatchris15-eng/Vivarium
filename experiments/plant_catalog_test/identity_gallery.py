import html, json, os
from pathlib import Path
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'build/plant-graph-tuning-20261115'
OUT = BASE / 'identity-reviewed'
notes_path = next(p for p in (BASE/'identity-release-validation.json', BASE/'identity-verified-validation.json') if p.exists())
notes = json.loads(notes_path.read_text(encoding='utf-8'))
scales = json.loads((BASE/'identity-scales.json').read_text(encoding='utf-8'))
overrides = {'greywatch':'identity-cavity', 'veilfern':'identity-cavity', 'fencomb':'identity-cavity', 'hollowcrown':'identity-taper', 'fenforge':'four-species-reviewed', 'embercrown':'four-species-reviewed', 'banksorrow':'four-species-reviewed', 'kinkcane':'four-species-reviewed'}
reports = {}
def provenance(path):
    report = path.parent / 'report.json'
    if report not in reports:
        reports[report] = json.loads(report.read_text(encoding='utf-8'))
    data = reports[report]
    shot = next((s for s in data['shots'] if s.get('png') == path.name), None)
    if not shot:
        raise ValueError(f'No matching report shot: {path}')
    return {'path': str(path.relative_to(BASE)).replace('\\','/'), 'source_directory': path.parent.parent.name,
            'report': str(report.relative_to(BASE)).replace('\\','/'), 'shot': shot['shot'],
            'game_module': data.get('game_module'), 'sim_module': data.get('sim_module')}
def url(path):
    return quote(os.path.relpath(path, OUT).replace('\\','/'), safe='/')
def esc(s): return html.escape(str(s), quote=True)
OUT.mkdir(parents=True, exist_ok=True)
records, cards = [], []
for item in sorted(notes['species'], key=lambda x:x['id']):
    name = item['id']
    graph = BASE / overrides.get(name, 'identity-final') / 'plant_catalog' / (name+'_growth.png')
    legacy = BASE / 'identity-legacy/plant_catalog' / (name+'_legacy.png')
    if name not in scales: raise ValueError(f'Missing shared scale: {name}')
    for p in (graph, legacy):
        if not p.is_file(): raise FileNotFoundError(p)
    macros = []
    # Select the newest actual report-backed macro capture per kind.
    for kind in ('foliage', 'cavity'):
        choices = list(BASE.glob(f'identity-*/plant_catalog/{name}_{kind}_growth.png')) + list(BASE.glob(f'four-species-reviewed/plant_catalog/{name}_{kind}_growth.png'))
        if choices:
            p = max(choices, key=lambda x:x.stat().st_mtime_ns)
            macros.append((kind,p))
    records.append({'species':name, 'shared_scale':scales[name], 'legacy':provenance(legacy), 'graph':provenance(graph),
                    'macros':[dict(kind=k, **provenance(p)) for k,p in macros]})
    links = ' · '.join(f'<a href="{url(p)}">{esc(k)} macro</a> <small>({esc(p.parent.parent.name)})</small>' for k,p in macros)
    cards.append(f'''<article data-family="{esc(item['family'])}"><h2>{esc(name)}</h2><p>{esc(item['family'])} · {esc(item['silhouette'])}</p><p>{esc(item['growthHabit'])}</p><div class="pair"><figure><figcaption>Legacy · identity-legacy</figcaption><a href="{url(legacy)}"><img loading="lazy" src="{url(legacy)}" alt="{esc(name)} legacy"></a></figure><figure><figcaption>Graph · {esc(graph.parent.parent.name)}</figcaption><a href="{url(graph)}"><img loading="lazy" src="{url(graph)}" alt="{esc(name)} graph"></a></figure></div><p>{links}</p></article>''')
if len(records) != 125: raise ValueError(f'Expected 125 species, got {len(records)}')
manifest = {'notes':str(notes_path.relative_to(BASE)), 'shared_scale_file':'identity-scales.json',
            'description':'Original capture files; each image references its own capture report and module IDs.', 'species':records}
(OUT/'capture_sources.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
families = sorted({x['family'] for x in notes['species']})
page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Vivarium plant identity review</title><style>body{background:#192021;color:#edf1e9;font:16px system-ui;margin:2rem}a{color:#a9d6c2}article{background:#293334;border-radius:8px;padding:1rem;margin:1rem 0}h2{margin:.2rem 0}p{line-height:1.45}.pair{display:grid;grid-template-columns:1fr 1fr;gap:1rem}figure{margin:0}img{display:block;width:100%}figcaption{padding:.4rem 0}button{padding:.5rem;margin:.2rem}small{color:#bbc7be}@media(max-width:700px){.pair{grid-template-columns:1fr}}</style><h1>Plant identity: legacy and graph</h1><p>Paired original Godot captures use the shared <a href="../identity-scales.json">identity scales</a>. Compare the mature plant at the right of each capture; juvenile stages are not age matched. Scales are per species, so do not compare absolute size between species. Click an image for the original.</p><p>Captures come from several runs and module versions. See <a href="capture_sources.json">capture provenance</a> and the <a href="../''' + notes_path.name + '''">authored validation notes</a>. Macro links identify their capture directory.</p><nav><button onclick="filter('all')">all</button>''' + ''.join(f'<button onclick="filter(\'{esc(f)}\')">{esc(f)}</button>' for f in families) + '</nav><main>' + ''.join(cards) + '''</main><script>function filter(f){document.querySelectorAll('article').forEach(e=>e.hidden=f!=='all'&&e.dataset.family!==f)}</script></html>'''
(OUT/'index.html').write_text(page, encoding='utf-8')
print(f'{len(records)} pairs; {sum(len(r["macros"]) for r in records)} macro links; {OUT}')
