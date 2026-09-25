"""Contact sheets + metrics from a reference capture: python scripts/reference-sheet.py <capture_dir> <out_dir> <label>"""
import json, os, sys
from PIL import Image, ImageDraw
src, out, label = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out, exist_ok=True)
facts = json.load(open(os.path.join(src, "reference_report.json")))["facts"]
sc = facts["scenes"]; W, H, C = 480, 270, 4
for part in range(0, len(sc), 20):
    chunk = sc[part:part + 20]; rows = (len(chunk) + C - 1) // C
    sheet = Image.new("RGB", (W * C, (H + 18) * rows), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
    for i, s in enumerate(chunk):
        im = Image.open(os.path.join(src, s["name"] + ".png")).convert("RGB").resize((W, H), Image.LANCZOS)
        x, y = (i % C) * W, (i // C) * (H + 18)
        sheet.paste(im, (x, y + 18)); dr.text((x + 4, y + 3), f"{s['name']}  {s['fps_mean']}fps", fill=(230, 230, 230))
    sheet.save(os.path.join(out, f"{label}_sheet{part // 20 + 1}.jpg"), quality=82)
json.dump({k: v for k, v in facts.items() if not k.startswith("screenshot_")}, open(os.path.join(out, f"{label}_metrics.json"), "w"), indent=1)
