import json, glob

presets = glob.glob('game/content/presets/*.json')
for p in sorted(presets):
    with open(p) as f:
        data = json.load(f)
    wt = data.get('water', {}).get('waterTable', 0)
    dia = data.get('diameter', 8)
    channels = [f for f in data.get('terrain', {}).get('features', []) if f.get('type') == 'channel']
    basins = [f for f in data.get('terrain', {}).get('features', []) if f.get('type') == 'basin']
    pid = data.get('id', p)
    bh = data.get('terrain', {}).get('baseHeight', 0)
    sx = data.get('terrain', {}).get('slopeX', 0)
    sz = data.get('terrain', {}).get('slopeZ', 0)
    print(f"{pid:22} dia={dia:4.1f} wt={wt:4.2f} baseH={bh:4.2f} slopeX={sx:6.3f} slopeZ={sz:6.3f} chs={len(channels)} basins={len(basins)}")
