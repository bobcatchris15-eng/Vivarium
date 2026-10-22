import json
import os

PRESETS_DIR = "game/content/presets"

def load(pid):
    with open(os.path.join(PRESETS_DIR, f"{pid}.json")) as f:
        return json.load(f)

def save(pid, d):
    with open(os.path.join(PRESETS_DIR, f"{pid}.json"), "w") as f:
        json.dump(d, f, indent=2)
    print(f"Saved {pid}.json")

# 1. cascading_falls
# In cascading_falls, Bank drops:
# ch[0]: (-4.5, -2.5)->(-1.8, -1.2) Bank: 1.830 -> 0.961
# ch[1]: (-1.8, -1.2)->( 0.8,  0.5) Bank: 0.961 -> 0.581
# ch[2]: ( 0.8,  0.5)->( 3.5,  2.2) Bank: 0.581 -> 0.461
# Basin 1 is at (-1.8, -1.2), Basin 2 is at (0.8, 0.5), Basin 3 is at (3.5, 2.2)
cf = load("cascading_falls")
# Plunge pool basins:
for feat in cf["terrain"]["features"]:
    if feat.get("type") == "basin":
        if feat["x"] == -1.8: feat["bedHeight"] = 0.78; feat["amount"] = 0.18
        elif feat["x"] == 0.8: feat["bedHeight"] = 0.40; feat["amount"] = 0.18
        elif feat["x"] == 3.5: feat["bedHeight"] = 0.28; feat["amount"] = 0.18
    elif feat.get("type") == "channel" and "bedHeight" in feat:
        if feat["x"] == -4.5:
            feat["bedHeight"] = 1.66
            feat["toBedHeight"] = 0.78
            feat["amount"] = 0.18
        elif feat["x"] == -1.8:
            feat["bedHeight"] = 0.78
            feat["toBedHeight"] = 0.40
            feat["amount"] = 0.18
        elif feat["x"] == 0.8:
            feat["bedHeight"] = 0.40
            feat["toBedHeight"] = 0.28
            feat["amount"] = 0.18
save("cascading_falls", cf)

# 2. estuary_delta
# Bank drops:
# ch[0]: 0.778 -> 0.391
# ch[1]: 0.391 -> 0.290
# ch[2]: 0.290 -> 0.166
# ch[3]: 0.166 -> 0.023
ed = load("estuary_delta")
ed_beds = [
    (0.618, 0.231),
    (0.231, 0.130),
    (0.130, 0.006),
    (0.006, -0.140)
]
ci = 0
for feat in ed["terrain"]["features"]:
    if feat.get("type") == "channel" and "bedHeight" in feat:
        feat["bedHeight"] = ed_beds[ci][0]
        feat["toBedHeight"] = ed_beds[ci][1]
        feat["amount"] = 0.18
        ci += 1
save("estuary_delta", ed)

# 3. misty_cloud_forest
# Bank drops:
# ch[0]: 1.689 -> 1.228
# ch[1]: 1.228 -> 0.773
# ch[2]: 0.773 -> 0.683
# ch[3]: 0.683 -> 0.539 (basin at (1.5, 4.2))
mcf = load("misty_cloud_forest")
mcf_beds = [
    (1.520, 1.060),
    (1.060, 0.610),
    (0.610, 0.510),
    (0.510, 0.360)
]
for feat in mcf["terrain"]["features"]:
    if feat.get("type") == "basin" and feat["x"] == 1.5:
        feat["bedHeight"] = 0.34
        feat["amount"] = 0.20
ci = 0
for feat in mcf["terrain"]["features"]:
    if feat.get("type") == "channel" and "bedHeight" in feat:
        feat["bedHeight"] = mcf_beds[ci][0]
        feat["toBedHeight"] = mcf_beds[ci][1]
        feat["amount"] = 0.18
        ci += 1
save("misty_cloud_forest", mcf)

# 4. shoreline
# Bank drops:
# ch[0]: 0.392 -> 0.318
# ch[1]: 0.318 -> 0.314
# ch[2]: 0.314 -> 0.301
# ch[3]: 0.301 -> 0.245
# ch[4]: 0.245 -> 0.153
sh = load("shoreline")
sh_beds = [
    (0.232, 0.158),
    (0.158, 0.154),
    (0.154, 0.141),
    (0.141, 0.085),
    (0.085, -0.010)
]
ci = 0
for feat in sh["terrain"]["features"]:
    if feat.get("type") == "channel" and "bedHeight" in feat:
        feat["bedHeight"] = sh_beds[ci][0]
        feat["toBedHeight"] = sh_beds[ci][1]
        feat["amount"] = 0.16
        ci += 1
save("shoreline", sh)

print("Batch 1 completed")
