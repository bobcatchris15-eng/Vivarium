import json
import math
import os

PRESETS_DIR = "game/content/presets"

def update_preset(filename, modifier_fn):
    path = os.path.join(PRESETS_DIR, filename)
    with open(path) as f:
        d = json.load(f)
    d = modifier_fn(d)
    with open(path, "w") as f:
        json.dump(d, f, indent=2)
    print(f"Updated {filename}")

# 1. alpine_tundra
def fix_alpine_tundra(d):
    t = d["terrain"]
    t["slopeX"] = -0.05
    t["slopeZ"] = 0.03
    t["baseHeight"] = 0.85
    # Hills framing the valley
    t["features"] = [
        { "type": "hill", "x": -1.5, "z": -4.2, "radius": 3.4, "amount": 0.55 },
        { "type": "hill", "x": -4.0, "z": 2.5, "radius": 3.2, "amount": 0.50 },
        { "type": "hill", "x": 3.8, "z": -1.5, "radius": 3.5, "amount": 0.55 },
        # Channels running through the center valley from Northwest (-4.5, -2.5) to Southeast (4.8, 5.2)
        { "type": "channel", "x": -4.2, "z": -2.2, "toX": -2.4, "toZ": -0.8, "width": 0.38, "amount": 0.18, "sharp": True, "bedHeight": 0.88, "toBedHeight": 0.70 },
        { "type": "channel", "x": -2.4, "z": -0.8, "toX": -0.5, "toZ": 0.6, "width": 0.40, "amount": 0.18, "sharp": True, "bedHeight": 0.70, "toBedHeight": 0.52 },
        { "type": "channel", "x": -0.5, "z": 0.6, "toX": 1.4, "toZ": 2.0, "width": 0.42, "amount": 0.18, "sharp": True, "bedHeight": 0.52, "toBedHeight": 0.34 },
        { "type": "channel", "x": 1.4, "z": 2.0, "toX": 3.2, "toZ": 3.6, "width": 0.44, "amount": 0.18, "sharp": True, "bedHeight": 0.34, "toBedHeight": 0.16 },
        { "type": "channel", "x": 3.2, "z": 3.6, "toX": 4.8, "toZ": 5.2, "width": 0.46, "amount": 0.18, "sharp": True, "bedHeight": 0.16, "toBedHeight": -0.05 }
    ]
    d["water"]["springs"] = [{ "x": -4.2, "z": -2.2, "dischargePerHour": 0.28 }]
    return d

update_preset("alpine_tundra.json", fix_alpine_tundra)
