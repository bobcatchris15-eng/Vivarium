"""Diagnostic plan views from MapReview JSON, separate from native game screenshots.

python scripts/map-review/render.py build/map-redesign/flow-final
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

root = Path(sys.argv[1])
panels = []
for path in sorted(root.glob("*.json")):
    d = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(d, dict) or "terrain" not in d:
        continue
    t = d["terrain"]
    size, radius = 450, d["Diameter"] / 2
    axis = np.linspace(-radius, radius, size)
    x, z = np.meshgrid(axis, axis)
    gx, gz = (x - t["OriginX"]) / t["Step"], (z - t["OriginZ"]) / t["Step"]
    ix, iz = np.clip(gx.astype(int), 0, t["Nx"] - 2), np.clip(gz.astype(int), 0, t["Nz"] - 2)
    fx, fz = gx - ix, gz - iz
    h = np.array(t["H"]).reshape(t["Nz"], t["Nx"])
    # Match the authoritative fixed triangle diagonal, rather than bilinear smoothing.
    a, b, c, e = h[iz, ix], h[iz, ix + 1], h[iz + 1, ix], h[iz + 1, ix + 1]
    y = np.where(fx >= fz, a + (b-a)*fx + (e-b)*fz, a + (e-c)*fx + (c-a)*fz)
    inside = np.ones_like(x, dtype=bool)
    for k in range(6):
        angle = math.pi / 6 + k * math.pi / 3
        inside &= x * math.cos(angle) + z * math.sin(angle) <= radius * math.sqrt(3) / 2
    dz, dx = np.gradient(y, 2 * radius / size)
    light = np.clip((1 + .4 * dx - .5 * dz) / np.sqrt(1 + dx*dx + dz*dz), .45, 1.2)
    rgb = np.zeros((size, size, 3)) + [125, 149, 102]
    rgb *= light[:, :, None]
    rgb[y < d["WaterTable"]] = [55, 118, 143]
    rgb[~inside] = [28, 34, 39]
    picture = Image.fromarray(np.uint8(np.clip(rgb, 0, 255)))
    draw = ImageDraw.Draw(picture)

    def pixel(a, b):
        return ((a / radius + 1) * (size - 1) / 2, (b / radius + 1) * (size - 1) / 2)

    frames = sorted(root.glob(d["id"] + "-*-water.json"))
    if frames:
        for cell in json.loads(frames[-1].read_text()):
            if cell["depth"] < .0003:
                continue
            px, pz = pixel(cell["x"], cell["z"])
            draw.ellipse((px-1, pz-1, px+1, pz+1), fill=(60, 183, 216))
    for spring in d["springs"]:
        px, pz = pixel(spring["X"], spring["Z"])
        draw.ellipse((px-5, pz-5, px+5, pz+5), fill=(255, 205, 70))
    panel = Image.new("RGB", (480, 490), (28, 34, 39))
    panel.paste(picture, (15, 30))
    ImageDraw.Draw(panel).text((20, 8), f'{d["name"]} | {d["seconds"]} s', fill="white")
    panels.append(panel)
canvas = Image.new("RGB", (1440, math.ceil(len(panels)/3)*490), (28, 34, 39))
for i, panel in enumerate(panels):
    canvas.paste(panel, ((i % 3)*480, (i // 3)*490))
canvas.save(root / "atlas.png")
