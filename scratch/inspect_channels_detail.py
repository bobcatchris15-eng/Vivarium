import json, glob

for path in sorted(glob.glob('game/content/presets/*.json')):
    d = json.load(open(path))
    id_ = d['id']
    springs = d.get('water', {}).get('springs', [])
    chs = [f for f in d.get('terrain', {}).get('features', []) if f.get('type') == 'channel' and 'bedHeight' in f]
    basins = [f for f in d.get('terrain', {}).get('features', []) if f.get('type') == 'basin' and 'bedHeight' in f]
    base = d['terrain']['baseHeight']
    wt = d.get('water', {}).get('waterTable', 0)
    
    pts = []
    if chs:
        pts.append((chs[0]['x'], chs[0]['z'], chs[0]['bedHeight']))
        for c in chs:
            pts.append((c['toX'], c['toZ'], c['toBedHeight']))
    
    print(f"=== {id_} (D={d['diameter']}, BaseH={base}, WT={wt}) ===")
    print(f"  Springs ({len(springs)}): {springs}")
    print(f"  Pools ({len(basins)}): {[(b['x'], b['z'], b['radius'], b['bedHeight']) for b in basins]}")
    print(f"  Channels ({len(chs)}):")
    for i, c in enumerate(chs[:5]):
        print(f"    ch[{i}]: ({c['x']:.2f}, {c['z']:.2f}) -> ({c['toX']:.2f}, {c['toZ']:.2f}), W={c['width']}, bed={c['bedHeight']:.3f} -> {c['toBedHeight']:.3f}")
    if len(chs) > 5:
        print(f"    ... and {len(chs) - 5} more channels ...")
        c = chs[-1]
        print(f"    ch[{len(chs)-1}]: ({c['x']:.2f}, {c['z']:.2f}) -> ({c['toX']:.2f}, {c['toZ']:.2f}), W={c['width']}, bed={c['bedHeight']:.3f} -> {c['toBedHeight']:.3f}")
