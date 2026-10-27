import json, glob

for p in sorted(glob.glob('game/content/presets/*.json')):
    d = json.load(open(p))
    id_ = d['id']
    springs = d.get('water', {}).get('springs', [])
    sp_parts = []
    for s in springs:
        sp_parts.append(f"({s['x']:.2f}, {s['z']:.2f})")
    sp_str = ', '.join(sp_parts)
    chs = [f for f in d.get('terrain', {}).get('features', []) if f.get('type') == 'channel' and 'bedHeight' in f]
    pools = [f for f in d.get('terrain', {}).get('features', []) if f.get('type') == 'basin' and 'bedHeight' in f]
    wt = d.get('water', {}).get('waterTable', 0)
    print(f"{id_:22}: springs=[{sp_str}] | {len(chs):2} chs | {len(pools):2} pools | WT={wt}")
