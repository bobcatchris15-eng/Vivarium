import json, glob, math

presets = glob.glob('game/content/presets/*.json')
for p in sorted(presets):
    with open(p) as f:
        data = json.load(f)
    pid = data.get('id', p)
    wt = data.get('water', {}).get('waterTable', 0)
    dia = data.get('diameter', 8)
    radius = dia / 2.0
    # Let's inspect where the lowest channel end goes, and whether edge points go below wt
    channels = [f for f in data.get('terrain', {}).get('features', []) if f.get('type') == 'channel' and 'bedHeight' in f]
    lowest_bed = min([f['toBedHeight'] for f in channels if 'toBedHeight' in f] + [100])
    
    # check channel endpoints
    ends = [(f['toX'], f['toZ'], f['toBedHeight']) for f in channels if 'toBedHeight' in f]
    print(f"{pid:22} dia={dia:2.0f} wt={wt:4.2f} minChannelEnd={lowest_bed:6.3f} ends={len(ends)}")
