"""Idempotent densely layered starter presets; preserves unrelated dirty fields."""
import json,math,pathlib,re
root=pathlib.Path(__file__).resolve().parents[2]
folder=root/'game/content/presets'
manifest=pathlib.Path(__file__).with_name('dense_flora_targets.json')
extra={
'alpine_tundra':{'glowbriar':3,'dewbonnet':4},'arid_canyon':{'lanternbrush':3,'carrion_bell':3},
'bayou_marsh':{'shadebell':4,'moonfern':6,'rain_jelly':4},'cascading_falls':{'veilfern':6,'dewbonnet':5},
'creek':{'shadebell':4,'ringreed':6,'glass_antlers':4},'deep_inlet':{'shadebell':4,'glass_antlers':4},
'default':{},'estuary_delta':{'shadebell':4,'moonfern':5,'dewbonnet':4},
'isolated_pond':{'shadebell':4,'lanternbrush':3,'rain_jelly':4},
'misty_cloud_forest':{'hookthicket':4,'glass_antlers':4},
'oxbow_creek':{'glowbriar':3,'moonfern':5,'ghostbonnet':3},
'peat_fen':{'shadebell':3,'moonfern':5,'dewbonnet':4},
'rocky_rise':{'glowbriar':3},'shoreline':{'kinkcane':3,'dewbonnet':4},
'steep_rocky_slope':{'glowbriar':3,'embercrown':3,'ghostbonnet':3},
'woodland_vernal_pools':{'lanternbrush':3,'rain_jelly':5,'floodlace_moss':8}}
coverage={'velvetweave_moss','pearl_cushion_moss','floodlace_moss','embercrust_lichen',
'ruffle_lichen','antlerlace_lichen','bogglass_moss','starmoss'}
if not manifest.exists():
    plan={}
    for path in sorted(folder.glob('*.json')):
        data=json.loads(path.read_text(encoding='utf-8-sig'));name=data['id']
        assert name in extra, name
        counts={e['species']:e['count'] for e in data['starterFlora']}
        for sp,n in extra[name].items():counts[sp]=max(counts.get(sp,0),n)
        factor=1.3 if name=='default' else (1.3 if name=='rocky_rise' else 1.9)
        plan[name]={sp:(max(n,math.ceil(n*factor)) if sp not in coverage
                else max(n,math.ceil(n*2),10)) for sp,n in counts.items()}
        if not coverage.intersection(plan[name]):
            plan[name]['velvetweave_moss']=12
            plan[name]['embercrust_lichen']=10
    manifest.write_text(json.dumps(plan,indent=2)+'\n',encoding='utf-8')
else:plan=json.loads(manifest.read_text(encoding='utf-8'))
for path in sorted(folder.glob('*.json')):
    raw=path.read_text(encoding='utf-8-sig');data=json.loads(raw)
    m=re.search(r'("starterFlora"\s*:\s*)\[(.*?)\](\s*,\s*"starterFauna")',raw,re.S)
    if m is None:raise ValueError(f'No starterFlora: {path}')
    entries=data['starterFlora'];indexed={x['species']:x for x in entries}
    for sp,n in plan[data['id']].items():
        if sp in indexed:indexed[sp]['count']=max(indexed[sp]['count'],n)
        else:entries.append({'species':sp,'count':n})
    arr=json.dumps(entries,indent=2,ensure_ascii=False).replace('\n','\n  ')
    new=raw[:m.start()]+m.group(1)+arr+m.group(3)+raw[m.end():]
    assert json.loads(new)['starterFauna']==data['starterFauna']
    if new!=raw:path.write_text(new,encoding='utf-8')
    print(data['id'], 'named=',sum(e['count'] for e in entries if e['species'] not in coverage),
          'cover=',sum(e['count'] for e in entries if e['species'] in coverage),
          'species=',len(entries))
