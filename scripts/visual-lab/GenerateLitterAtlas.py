#!/usr/bin/env python3
"""
GenerateLitterAtlas.py - 4K isolated forest-floor litter atlas (WP-09).

Builds six 4096x4096 maps under ``game/Textures`` from procedural, alpha-masked
botanical elements.  Every atlas cell contains ONE isolated element so the
renderer can map a single leaf card onto a 1024 px UV cell without neighbouring
texels bleeding into the sample.

    4x4 grid  -> 16 cells of 1024x1024 px
    element   -> 800x950 px of texels centred in each cell

    cells  0..5   intact dried leaves (cordate, ovate, lanceolate, lobed,
                  elliptic, reniform) with raised venation and fungal spots
    cells  6..9   skeletonised / insect-chewed decomposing leaves with
                  transparent fenestrations between the veins
    cells 10..12  fragmented humus flakes and a curled leaf shard
    cells 13..15  rolled bark strips and a fallen bud scale

Maps written (each with a Godot .import sidecar):

    ForestFloorLitter_4K_Color.png          sRGB albedo
    ForestFloorLitter_4K_NormalGL.png       linear OpenGL +Y tangent normals
    ForestFloorLitter_4K_Roughness.png      linear roughness
    ForestFloorLitter_4K_Displacement.png   linear 16-bit height, 0.5 mean plane
    ForestFloorLitter_4K_Opacity.png        linear alpha silhouette
    ForestFloorLitter_4K_Transmittance.png  sRGB thin-tissue backlight colour

The maps are synthesised from analytic leaf silhouettes, a herringbone vein
field, a jittered-grid Voronoi areole network and value-noise micro-relief, with
baked fungal spots, necrosis and soil staining.  They are NOT crops of
ScatteredLeaves007.

Usage:
    python scripts/visual-lab/GenerateLitterAtlas.py
    python scripts/visual-lab/GenerateLitterAtlas.py --preview
"""

from __future__ import annotations

import argparse
import math
import time
from pathlib import Path

import numpy as np
from PIL import Image

ATLAS = 4096
CELL = 1024
GRID = 4
ELEMENT_W = 800
ELEMENT_H = 950
# Physical width assumed for the 800 px element.  Drives normal-map slope,
# displacement scale and the physical width of every vein.
ELEMENT_METRES = 0.055
TEXEL_METRES = ELEMENT_METRES / ELEMENT_W

OUT_DIR = Path(__file__).resolve().parents[2] / "game" / "Textures"
PREFIX = "ForestFloorLitter_4K"


# --------------------------------------------------------------------------- #
# numeric helpers
# --------------------------------------------------------------------------- #
def smoothstep(edge0: float, edge1: float, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def value_noise(shape, cells_x: int, cells_y: int, rng):
    """Bilinear value noise on a lattice of ``cells_x`` x ``cells_y``."""
    h, w = shape
    base = rng.random((cells_y + 1, cells_x + 1)).astype(np.float32)
    gy = np.linspace(0.0, cells_y, h, endpoint=False, dtype=np.float32)
    gx = np.linspace(0.0, cells_x, w, endpoint=False, dtype=np.float32)
    iy = np.floor(gy).astype(np.int32)
    ix = np.floor(gx).astype(np.int32)
    fy = (gy - iy)[:, None]
    fx = (gx - ix)[None, :]
    fy = fy * fy * (3.0 - 2.0 * fy)
    fx = fx * fx * (3.0 - 2.0 * fx)
    iy0 = np.clip(iy, 0, cells_y - 1)
    ix0 = np.clip(ix, 0, cells_x - 1)
    top = base[iy0][:, ix0] * (1.0 - fx) + base[iy0][:, ix0 + 1] * fx
    bot = base[iy0 + 1][:, ix0] * (1.0 - fx) + base[iy0 + 1][:, ix0 + 1] * fx
    return (top * (1.0 - fy) + bot * fy).astype(np.float32)


def fbm(shape, cx: int, cy: int, octaves: int, rng, gain: float = 0.5):
    total = np.zeros(shape, np.float32)
    amp = 1.0
    norm = 0.0
    for o in range(octaves):
        total += amp * value_noise(shape, max(1, cx << o), max(1, cy << o), rng)
        norm += amp
        amp *= gain
    return total / norm


def normalised_noise(shape, cx, cy, octaves, rng):
    n = fbm(shape, cx, cy, octaves, rng)
    return (n - n.mean()) / max(1e-5, n.std())


def segment_distance(px, py, ax, ay, bx, by):
    vx = bx - ax
    vy = by - ay
    wx = px - ax
    wy = py - ay
    t = np.clip((wx * vx + wy * vy) / (vx * vx + vy * vy + 1e-9), 0.0, 1.0)
    dx = wx - t * vx
    dy = wy - t * vy
    return np.sqrt(dx * dx + dy * dy)


def ridge(distance_px, width_px):
    return np.exp(-(distance_px / max(1e-3, width_px)) ** 2)


def bezier(p0, p1, p2, steps: int):
    t = np.linspace(0.0, 1.0, steps, dtype=np.float32)[:, None]
    pts = (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t ** 2 * np.array(p2)
    return pts


def cellular(shape, spacing_x: float, spacing_y: float, phase: float):
    """Jittered-grid Voronoi.  Returns (nearest distance, second distance, cell hash)."""
    h, w = shape
    xs = np.arange(w, dtype=np.float32) + 0.5
    ys = np.arange(h, dtype=np.float32) + 0.5
    ix = np.floor(xs / spacing_x).astype(np.int32)[None, :]
    iy = np.floor(ys / spacing_y).astype(np.int32)[:, None]
    d1 = np.full(shape, 1e9, np.float32)
    d2 = np.full(shape, 1e9, np.float32)
    winner = np.zeros(shape, np.float32)
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            cx = ix + ox
            cy = iy + oy
            jx = np.sin(cx * 127.1 + cy * 311.7 + phase) * 43758.5453
            jy = np.sin(cx * 269.5 + cy * 183.3 + phase) * 24634.6345
            hx = jx - np.floor(jx)
            hy = jy - np.floor(jy)
            sx = (cx + 0.10 + 0.80 * hx) * spacing_x
            sy = (cy + 0.10 + 0.80 * hy) * spacing_y
            dx = xs[None, :] - sx
            dy = ys[:, None] - sy
            dd = dx * dx + dy * dy
            closer = dd < d1
            d2 = np.where(closer, d1, np.minimum(d2, dd))
            d1 = np.where(closer, dd, d1)
            winner = np.where(closer, hx, winner)
    return np.sqrt(d1), np.sqrt(d2), winner


# --------------------------------------------------------------------------- #
# element definitions
# --------------------------------------------------------------------------- #
# a, b are the beta-profile exponents that shape each botanical silhouette and
# wmax is the largest half-width in element units.
FORMS = (
    dict(kind="intact", shape="cordate", a=0.22, b=0.62, wmax=0.96, teeth=24, tooth_amp=0.070,
         notch=0.17, secondary=9, rise=0.150, palette=(0.500, 0.278, 0.126), decay=0.10, spots=9),
    dict(kind="intact", shape="ovate", a=0.42, b=0.66, wmax=0.90, teeth=28, tooth_amp=0.060,
         notch=0.0, secondary=9, rise=0.140, palette=(0.600, 0.462, 0.222), decay=0.18, spots=12),
    dict(kind="intact", shape="lanceolate", a=0.55, b=0.95, wmax=0.64, teeth=34, tooth_amp=0.044,
         notch=0.0, secondary=11, rise=0.090, palette=(0.412, 0.338, 0.176), decay=0.22, spots=7),
    dict(kind="intact", shape="lobed", a=0.35, b=0.58, wmax=0.96, teeth=0, tooth_amp=0.0,
         notch=0.0, secondary=8, rise=0.150, palette=(0.560, 0.352, 0.148), decay=0.16, spots=10),
    dict(kind="intact", shape="elliptic", a=0.46, b=0.52, wmax=0.72, teeth=0, tooth_amp=0.0,
         notch=0.0, secondary=10, rise=0.110, palette=(0.436, 0.368, 0.262), decay=0.26, spots=14),
    dict(kind="intact", shape="reniform", a=0.20, b=0.58, wmax=0.98, teeth=20, tooth_amp=0.066,
         notch=0.26, secondary=9, rise=0.170, palette=(0.468, 0.232, 0.108), decay=0.14, spots=8),
    dict(kind="skeleton", shape="ovate", a=0.42, b=0.66, wmax=0.90, teeth=28, tooth_amp=0.060,
         notch=0.0, secondary=10, rise=0.140, palette=(0.628, 0.542, 0.360), decay=0.55, spots=5, holes=0.42),
    dict(kind="skeleton", shape="cordate", a=0.22, b=0.62, wmax=0.96, teeth=24, tooth_amp=0.070,
         notch=0.17, secondary=9, rise=0.155, palette=(0.500, 0.382, 0.222), decay=0.58, spots=7, holes=0.36),
    dict(kind="skeleton", shape="lobed", a=0.35, b=0.58, wmax=0.96, teeth=0, tooth_amp=0.0,
         notch=0.0, secondary=10, rise=0.150, palette=(0.548, 0.408, 0.196), decay=0.64, spots=4, holes=0.52),
    dict(kind="skeleton", shape="elliptic", a=0.46, b=0.52, wmax=0.72, teeth=0, tooth_amp=0.0,
         notch=0.0, secondary=12, rise=0.110, palette=(0.396, 0.330, 0.208), decay=0.72, spots=6, holes=0.60),
    dict(kind="fragment", shape="flake", palette=(0.330, 0.198, 0.094), decay=0.86, spots=2),
    dict(kind="fragment", shape="flake", palette=(0.268, 0.158, 0.078), decay=0.90, spots=3),
    dict(kind="fragment", shape="shard", palette=(0.404, 0.262, 0.120), decay=0.72, spots=4),
    dict(kind="bark", shape="strip", palette=(0.352, 0.238, 0.140), decay=0.55, spots=3),
    dict(kind="bark", shape="strip", palette=(0.288, 0.192, 0.118), decay=0.68, spots=2),
    dict(kind="scale", shape="bud", a=0.36, b=1.05, wmax=0.56, palette=(0.404, 0.176, 0.104), decay=0.40, spots=3),
)


def beta_width(v, a, b, wmax):
    vv = np.clip(v, 1e-4, 1.0 - 1e-4)
    peak = (a / (a + b)) ** a * (b / (a + b)) ** b
    return wmax * (vv ** a) * ((1.0 - vv) ** b) / peak


def leaf_width(form, v):
    w = beta_width(v, form["a"], form["b"], form["wmax"])
    if form["shape"] == "lobed":
        lobes = 0.80 + 0.20 * np.cos((v - 0.5) * math.pi * 6.4)
        w = w * np.clip(lobes, 0.0, None)
    if form["teeth"] > 0 and form["tooth_amp"] > 0:
        phase = (v * form["teeth"]) % 1.0
        w = w * (1.0 + form["tooth_amp"] * (0.72 - 1.35 * phase))
    return w


# --------------------------------------------------------------------------- #
# per-cell synthesis
# --------------------------------------------------------------------------- #
def render_element(form: dict, rng, verbose: bool = True):
    H, W = ELEMENT_H, ELEMENT_W
    t0 = time.perf_counter()

    xs = (np.arange(W, dtype=np.float32) + 0.5) - W * 0.5       # -400..400 px
    ys = np.arange(H, dtype=np.float32) + 0.5                    # 0..950 px, top down
    U = (xs / (W * 0.5))[None, :]                               # -1..1
    V = (1.0 - ys / H)[:, None]                                 # 0 base .. 1 tip
    UPX = np.broadcast_to(xs[None, :], (H, W))
    VPX = np.broadcast_to(ys[:, None], (H, W))

    kind, shape = form["kind"], form["shape"]
    has_lamina = kind in ("intact", "skeleton")

    # ------------------------------------------------------------------ mask
    petiole = np.full((H, W), -1e5, np.float32)
    if has_lamina:
        w = leaf_width(form, V)
        asym_l = 1.0 + 0.050 * np.sin(V * 5.1 + rng.uniform(0, 6.28))
        asym_r = 1.0 + 0.048 * np.sin(V * 4.3 + rng.uniform(0, 6.28))
        dist = np.minimum((w * asym_r - U), (U + w * asym_l)) * (W * 0.5)

        if form["notch"] > 0:
            half = 0.34 * float(w.max())
            depth = form["notch"] * H
            cut = ys[:, None] - (H - depth * np.clip(1.0 - np.abs(U) / half, 0.0, 1.0))
            dist = np.minimum(dist, -cut)

        # 3 mm woody petiole: a short tapered stalk below the lamina base
        stem_len = 0.045 * H
        stem_half = 0.013
        stem = np.minimum((stem_half * (1.0 - 0.55 * (ys[:, None] - (H - stem_len)) / stem_len) - np.abs(U)) * (W * 0.5),
                          ys[:, None] - (H - stem_len))
        petiole = stem
        dist = np.maximum(dist, stem)

        if form.get("holes", 0.0) > 0:
            # insect margin bites
            for _ in range(6):
                cx = rng.uniform(-0.92, 0.92) * W * 0.5
                cy = rng.uniform(0.12, 0.94) * H
                rr = rng.uniform(10, 52)
                d = np.sqrt((xs[None, :] - cx) ** 2 + (ys[:, None] - cy) ** 2) - rr
                if abs(cx) > 0.26 * W * 0.5:
                    dist = np.minimum(dist, -d)
    elif kind == "fragment" and shape == "flake":
        ang = np.arctan2((V - 0.5) * 1.12, U)
        harm = (1.0 + 0.15 * np.sin(ang * 3.0 + rng.uniform(0, 6.3))
                + 0.10 * np.sin(ang * 5.0 + rng.uniform(0, 6.3))
                + 0.06 * np.sin(ang * 9.0 + rng.uniform(0, 6.3))
                + 0.04 * np.sin(ang * 17.0 + rng.uniform(0, 6.3)))
        radius = np.sqrt((U * 0.98) ** 2 + ((V - 0.5) * 0.86) ** 2)
        dist = (0.70 * harm - radius) * (W * 0.5)
        cut_ang = rng.uniform(-2.4, 2.4)
        cut = (np.abs(np.arctan2(np.sin(ang - cut_ang), np.cos(ang - cut_ang))) - rng.uniform(0.45, 0.95)) * 90.0
        dist = np.minimum(dist, cut)
    elif kind == "fragment" and shape == "shard":
        ang = np.arctan2((V - 0.5) * 1.05, U)
        radius = np.sqrt(U ** 2 + ((V - 0.5) * 1.05) ** 2)
        outer = 0.76 + 0.030 * np.sin(ang * 3.0 + 0.7)
        inner = 0.26 + 0.05 * np.sin(ang * 2.0 + 1.9)
        theta0 = rng.uniform(0.15, 0.55)
        theta1 = theta0 + rng.uniform(1.5, 2.2)
        window = np.minimum(ang - theta0, theta1 - ang) * 190.0
        dist = np.minimum(np.minimum(outer - radius, radius - inner), window) * (W * 0.5)
    elif kind == "bark" and shape == "strip":
        wobble = rng.uniform(0, 6.3)
        centre = 0.055 * np.sin(V * 3.4 + wobble)
        half = 0.30 * (0.62 + 0.38 * np.sin(np.clip(V, 0.02, 1.0) * math.pi) ** 0.55)
        dist = (half - np.abs(U - centre)) * (W * 0.5)
        ragged = 7.0 * np.sin(ys[:, None] * 0.75 + wobble) + 4.0 * np.sin(ys[:, None] * 2.1 + wobble * 1.7)
        dist = np.minimum(dist, (0.985 * H + ragged) - ys[:, None])
        dist = np.minimum(dist, ys[:, None] - (0.015 * H + ragged * 0.5))
    else:  # bud scale
        w = beta_width((V - 0.04) / 0.96, form["a"], form["b"], form["wmax"])
        dist = (w - np.abs(U)) * (W * 0.5)
        dist = np.minimum(dist, (0.98 - V) * H)
        dist = np.minimum(dist, V * H)

    dist = np.maximum(dist, -4.0)
    alpha = np.clip(dist * 0.72 + 0.5, 0.0, 1.0)
    inside = dist > 0.0

    if verbose:
        print(f"    silhouette      {time.perf_counter() - t0:6.2f}s")

    # --------------------------------------------------------- vein network
    mid_d = np.full((H, W), 1e4, np.float32)
    sec_d = np.full((H, W), 1e4, np.float32)
    vein_pale = np.zeros((H, W), np.float32)
    mu = None
    if has_lamina:
        mu = 0.018 * np.sin(V * 3.1 + rng.uniform(0, 6.3)) - 0.03 * (V - 0.5) + 0.012 * np.sin(V * 9.0)
        mid_d = np.abs(U - mu) * (W * 0.5)

        n_sec = form.get("secondary", 8)
        rise = form.get("rise", 0.14)
        for i in range(n_sec):
            v0 = 0.06 + (i + 0.35) * (0.86 / n_sec)
            v1 = min(0.985, v0 + rise)
            wm = float(leaf_width(form, v1))
            side = 1.0 if i % 2 == 0 else -1.0
            row = int(np.clip((1.0 - v0) * (H - 1), 0, H - 1))
            u0 = float(mu[row][0])
            p0 = (u0 * W * 0.5, (1.0 - v0) * H)
            p2 = (side * wm * 0.94 * W * 0.5, (1.0 - v1) * H)
            p1 = (p0[0] + 0.45 * (p2[0] - p0[0]), p0[1] + 0.78 * (p2[1] - p0[1]))
            pts = bezier(p0, p1, p2, 6)
            for k in range(len(pts) - 1):
                d = segment_distance(UPX, VPX, pts[k][0], pts[k][1], pts[k + 1][0], pts[k + 1][1])
                sec_d = np.minimum(sec_d, d)
        # short veinlets running between neighbouring secondaries
        for i in range(0, n_sec, 2):
            v0 = 0.10 + (i + 0.3) * (0.86 / n_sec)
            side = 1.0 if i % 2 == 0 else -1.0
            d = segment_distance(UPX, VPX, 0.12 * side * W * 0.5, (1.0 - v0) * H,
                                 0.46 * side * W * 0.5, (1.0 - v0 - 0.075) * H)
            sec_d = np.minimum(sec_d, d * 1.25)

        vein_pale = np.clip(0.72 * ridge(mid_d, 4.6) + 0.60 * ridge(sec_d, 2.4), 0.0, 1.0)

    if verbose:
        print(f"    venation        {time.perf_counter() - t0:6.2f}s")

    # ------------------------------------------------------ areole network
    ar_spacing = 13.0 if kind != "skeleton" else 15.0
    d1, d2, cell_hash = cellular((H, W), ar_spacing, ar_spacing * 1.45, rng.uniform(0, 6.3))
    areole = (d2 - d1) * 0.5                     # distance to the areole wall, px
    areole = np.where(inside, areole, 0.0)

    if verbose:
        print(f"    areoles         {time.perf_counter() - t0:6.2f}s")

    # ---------------------------------------------------------------- height
    micro = normalised_noise((H, W), 190, 226, 2, rng)
    meso = fbm((H, W), 8, 10, 4, rng)
    fibre_y = value_noise((H, W), 3, 46, rng)

    height = (
        0.00032 * ridge(mid_d, 4.2)
        + 0.000140 * ridge(sec_d, 2.3)
        + 0.000050 * ridge(areole, 1.25)
        + 0.000030 * smoothstep(1.0, 8.0, areole)
        + 0.000030 * (meso - 0.5)
        + 0.0000075 * micro
    )
    # lamina arch: dried leaves cup across the midrib
    if mu is not None:
        height += 0.00030 * (1.0 - np.clip(np.abs(U - mu) / 0.95, 0.0, 1.0) ** 1.7)

    if kind == "bark":
        fissure = value_noise((H, W), 22, 3, rng)
        height = (0.00034 * np.abs(fissure - 0.5) + 0.00022 * (fibre_y - 0.5)
                  + 0.00095 * smoothstep(0.22, 0.34, np.abs(U)) * (kind == "bark"))
    elif kind == "fragment":
        height = height * 0.55 + 0.00055 * (value_noise((H, W), 6, 8, rng) - 0.5)
        height += 0.00035 * smoothstep(0.2, 0.9, np.abs(U)) * (shape == "shard")
    elif kind == "scale":
        ring = np.sin(np.sqrt((U / 0.95) ** 2 + ((V - 0.5) / 0.86) ** 2) * 24.0 + 1.4)
        height = height + 0.000075 * ring + 0.00012 * smoothstep(0.1, 0.45, np.abs(U))

    height = np.where(inside, height, 0.0).astype(np.float32)

    if verbose:
        print(f"    relief          {time.perf_counter() - t0:6.2f}s")

    # ---------------------------------------------------------------- colour
    base = np.clip(np.array(form["palette"], np.float32) * rng.uniform(0.94, 1.06, 3).astype(np.float32), 0.02, 0.95)
    grain = normalised_noise((H, W), 22, 28, 4, rng)
    blotch = fbm((H, W), 4, 5, 4, rng)
    fine = normalised_noise((H, W), 260, 300, 2, rng)

    colour = np.empty((H, W, 3), np.float32)
    colour[:] = base[None, None, :] * np.array([1.04, 0.985, 0.88], np.float32)[None, None, :]

    radial = np.clip(np.abs(U), 0.0, 1.0)
    colour *= (0.86 + 0.14 * (1.0 - radial ** 1.7))[..., None]
    colour *= (0.86 + 0.28 * blotch)[..., None]
    colour *= (1.0 + 0.10 * grain)[..., None]
    colour *= (1.0 + 0.035 * fine)[..., None]
    colour *= (0.96 + 0.08 * cell_hash)[..., None]

    # lignified veins: raised, paler, with a slight dark halo
    halo = np.clip(0.55 * ridge(mid_d, 7.0) + 0.40 * ridge(sec_d, 4.2), 0.0, 1.0)
    colour *= (1.0 - 0.16 * halo[..., None])
    colour = colour * (1.0 - 0.30 * vein_pale[..., None]) + np.array([0.66, 0.58, 0.42], np.float32) * (
        0.34 * vein_pale[..., None])
    tert = ridge(areole, 1.35)
    colour = colour * (1.0 - 0.14 * tert[..., None]) + np.array([0.58, 0.50, 0.36], np.float32) * (
        0.16 * tert[..., None])

    if kind == "bark":
        plate = value_noise((H, W), 18, 2, rng)
        colour *= (0.55 + 0.85 * plate)[..., None]
        colour *= (0.84 + 0.36 * fibre_y)[..., None]
        rolled = smoothstep(0.20, 0.32, np.abs(U))
        colour *= (1.0 - 0.45 * rolled)[..., None]
        colour = colour * (1.0 - 0.20 * rolled[..., None]) + np.array([0.56, 0.44, 0.30], np.float32) * (
            0.26 * rolled[..., None])
    if kind == "scale":
        band = 0.5 + 0.5 * np.sin(np.sqrt((U / 0.95) ** 2 + ((V - 0.5) / 0.86) ** 2) * 24.0)
        colour *= (0.82 + 0.30 * band)[..., None]
        colour *= (1.0 - 0.30 * smoothstep(0.30, 0.55, np.abs(U)))[..., None]

    # fungal leaf spots: dark necrotic rings around a bleached centre
    spots = int(form.get("spots", 6))
    spot_mask = np.zeros((H, W), np.float32)
    ring_mask = np.zeros((H, W), np.float32)
    for _ in range(spots):
        cx = rng.uniform(-0.88, 0.88) * W * 0.5
        cy = rng.uniform(0.10, 0.94) * H
        rx = rng.uniform(9, 32)
        ry = rx * rng.uniform(0.72, 1.4)
        ang = rng.uniform(0, math.pi)
        dx = xs[None, :] - cx
        dy = ys[:, None] - cy
        rxx = dx * math.cos(ang) + dy * math.sin(ang)
        ryy = -dx * math.sin(ang) + dy * math.cos(ang)
        r = np.sqrt((rxx / rx) ** 2 + (ryy / ry) ** 2)
        spot_mask = np.maximum(spot_mask, np.exp(-((r - 0.15) / 1.15) ** 2))
        ring_mask = np.maximum(ring_mask, np.exp(-((r - 1.0) / 0.13) ** 2) * (r > 0.5))
    colour *= (1.0 - 0.42 * spot_mask[..., None])
    colour *= (1.0 - 0.34 * ring_mask[..., None])
    colour += np.array([0.055, 0.020, 0.006], np.float32) * ring_mask[..., None]

    decay = float(form.get("decay", 0.2))
    if decay > 0.35:
        soil = np.array([0.150, 0.100, 0.052], np.float32)
        colour = colour * (1.0 - 0.34 * decay) + soil * (0.34 * decay)
    if kind == "fragment":
        colour *= (0.72 + 0.36 * grain[..., None])

    # edge necrosis: a dark, brittle rim just inside the margin
    edge_band = smoothstep(0.0, 9.0, dist)
    colour *= (0.66 + 0.34 * edge_band)[..., None]

    # micro-cavity occlusion where areole walls meet
    cavity = np.clip(1.0 - 0.16 * tert - 0.10 * ridge(sec_d, 5.0), 0.70, 1.0)
    colour *= cavity[..., None]
    colour = np.clip(colour, 0.0, 0.90)

    # ------------------------------------------------------------- roughness
    rough = (0.795 + 0.060 * (grain * 0.5 + 0.5) + 0.040 * (fine * 0.5 + 0.5)
             - 0.105 * ridge(mid_d, 3.0) - 0.075 * ridge(sec_d, 2.2)
             + 0.10 * spot_mask + 0.07 * ring_mask + 0.10 * decay)
    if kind == "bark":
        rough = 0.865 + 0.075 * (grain * 0.5 + 0.5) - 0.10 * smoothstep(0.20, 0.32, np.abs(U))
    if kind == "scale":
        rough = 0.640 + 0.070 * (grain * 0.5 + 0.5)
    rough = np.clip(rough, 0.34, 0.99)

    # -------------------------------------------------------- fenestration
    hole_soft = np.zeros((H, W), np.float32)
    hole_edge = np.zeros((H, W), np.float32)
    if form.get("holes", 0.0) > 0:
        holes = float(form["holes"])
        region = fbm((H, W), 3, 4, 3, rng)
        keep = smoothstep(0.40, 0.56, region) > 0.5
        thresh = 0.93 - 0.58 * holes
        missing = (cell_hash > thresh) & keep
        hole_soft = missing.astype(np.float32) * smoothstep(1.2, 3.6, areole)
        hole_soft = np.maximum(hole_soft, smoothstep(0.80, 0.86, fbm((H, W), 9, 11, 3, rng)) * (dist > 14.0))
        hole_soft = np.clip(hole_soft, 0.0, 1.0)
        alpha = alpha * (1.0 - hole_soft)
        hole_edge = smoothstep(0.0, 5.0, dist) * (1.0 - hole_soft)
        inside = alpha > 0.5
        colour *= (1.0 - 0.62 * hole_soft[..., None])
        # torn edges around each fenestration darken and thicken
        hole_rim = np.clip(hole_soft - np.roll(hole_soft, 2, axis=0) * 0.5, 0.0, 1.0)
        colour *= (1.0 - 0.35 * hole_rim[..., None])
        rough = rough + 0.10 * hole_soft

    # -------------------------------------------------------- transmittance
    thin = np.clip(1.0 - height / 0.00038, 0.0, 1.0)
    trans = np.empty((H, W, 3), np.float32)
    trans[:] = np.array([0.86, 0.48, 0.21], np.float32)
    trans *= (0.42 + 0.58 * thin)[..., None]
    trans = trans * (1.0 - 0.70 * vein_pale[..., None])
    trans *= (1.0 - 0.60 * decay)
    if kind == "bark":
        trans *= 0.10
    if kind == "scale":
        trans *= 0.50
    trans *= (1.0 - hole_soft[..., None])
    trans = np.clip(trans, 0.0, 1.0)

    # ---------------------------------------------------------- displacement
    disp = np.clip(0.5 + height / 0.0055, 0.0, 1.0)
    disp = np.where(alpha > 0.01, disp, 0.5)

    if verbose:
        print(f"    shading         {time.perf_counter() - t0:6.2f}s")

    return dict(
        alpha=np.clip(alpha, 0.0, 1.0).astype(np.float32),
        height=height.astype(np.float32),
        colour=colour.astype(np.float32),
        rough=rough.astype(np.float32),
        trans=trans.astype(np.float32),
        disp=disp.astype(np.float32),
    )


def normal_from_height(height: np.ndarray, metres_per_texel: float) -> np.ndarray:
    gy, gx = np.gradient(height.astype(np.float32))
    nx = -gx / metres_per_texel
    ny = gy / metres_per_texel
    nz = np.ones_like(nx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    nx *= inv
    ny *= inv
    nz *= inv
    rgb = np.empty(height.shape + (3,), np.float32)
    rgb[..., 0] = nx * 0.5 + 0.5
    rgb[..., 1] = ny * 0.5 + 0.5
    rgb[..., 2] = nz * 0.5 + 0.5
    return np.clip(rgb, 0.0, 1.0)


def write_import(path: Path, normal: bool = False, roughness_src: str = "") -> None:
    """Writes a Godot 4 import sidecar matching the existing game/Textures format."""
    res = f"res://Textures/{path.name}"
    lossy = "0.7" if path.name.endswith("Opacity.png") else "1.0"
    params = [
        "compress/mode=2",
        "compress/high_quality=true",
        f"compress/lossy_quality={lossy}",
        "compress/uastc_level=0",
        "compress/rdo_quality_loss=0.0",
        "compress/hdr_compression=1",
        f"compress/normal_map={1 if normal else 0}",
        "compress/channel_pack=0",
        "mipmaps/generate=true",
        "mipmaps/limit=-1",
        f"roughness/mode={1 if roughness_src else 0}",
        f'roughness/src_normal="{roughness_src}"',
        "process/channel_remap/red=0",
        "process/channel_remap/green=1",
        "process/channel_remap/blue=2",
        "process/channel_remap/alpha=3",
        "process/fix_alpha_border=true",
        "process/premult_alpha=false",
        "process/normal_map_invert_y=false",
        "process/hdr_as_srgb=false",
        "process/hdr_clamp_exposure=false",
        "process/size_limit=0",
        "detect_3d/compress_to=0",
    ]
    content = (
        "[remap]\n\n"
        'importer="texture"\n'
        'type="CompressedTexture2D"\n\n'
        "[deps]\n\n"
        f'source_file="{res}"\n'
        "dest_files=[]\n\n"
        "[params]\n\n" + "\n".join(params) + "\n"
    )
    path.with_name(path.name + ".import").write_text(content, encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description="Generate the 4K isolated litter atlas")
    ap.add_argument("--preview", action="store_true", help="also write a 2048 composite preview")
    ap.add_argument("--cells", type=str, default="", help="debug: only render these cell indices, e.g. 0,6,13")
    args = ap.parse_args()

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    colour = np.zeros((ATLAS, ATLAS, 3), np.uint8)
    normal = np.zeros((ATLAS, ATLAS, 3), np.uint8)
    normal[...] = (128, 128, 255)
    rough = np.full((ATLAS, ATLAS), 230, np.uint8)
    disp = np.full((ATLAS, ATLAS), 32768, np.uint16)
    opacity = np.zeros((ATLAS, ATLAS), np.uint8)
    trans = np.zeros((ATLAS, ATLAS, 3), np.uint8)

    only = {int(s) for s in args.cells.split(",") if s.strip()} if args.cells else None
    started = time.perf_counter()
    for index, form in enumerate(FORMS):
        if only is not None and index not in only:
            continue
        print(f"[{index:2d}] {form['kind']:8s} {form['shape']}")
        rng = np.random.default_rng(0x5EED_0000 + index * 7919)
        el = render_element(form, rng)
        ox = (CELL - ELEMENT_W) // 2 + (index % GRID) * CELL
        oy = (CELL - ELEMENT_H) // 2 + (index // GRID) * CELL
        sl = (slice(oy, oy + ELEMENT_H), slice(ox, ox + ELEMENT_W))

        colour[sl] = np.clip(el["colour"] * 255.0 + 0.5, 0, 255).astype(np.uint8)
        normal[sl] = np.clip(normal_from_height(el["height"], TEXEL_METRES) * 255.0 + 0.5, 0, 255).astype(np.uint8)
        rough[sl] = np.clip(el["rough"] * 255.0 + 0.5, 0, 255).astype(np.uint8)
        disp[sl] = np.clip(el["disp"] * 65535.0 + 0.5, 0, 65535).astype(np.uint16)
        opacity[sl] = np.clip(el["alpha"] * 255.0 + 0.5, 0, 255).astype(np.uint8)
        trans[sl] = np.clip(el["trans"] * 255.0 + 0.5, 0, 255).astype(np.uint8)

    outputs = (
        ("Color", colour, "RGB"),
        ("NormalGL", normal, "RGB"),
        ("Roughness", rough, "L"),
        ("Displacement", disp, "I;16"),
        ("Opacity", opacity, "L"),
        ("Transmittance", trans, "RGB"),
    )
    for map_name, array, mode in outputs:
        path = OUT_DIR / f"{PREFIX}_{map_name}.png"
        Image.fromarray(array, mode=mode).save(path, optimize=False, compress_level=6)
        normal_flag = map_name == "NormalGL"
        rough_src = f"res://Textures/{PREFIX}_NormalGL.png" if map_name == "Roughness" else ""
        write_import(path, normal=normal_flag, roughness_src=rough_src)
        print(f"  wrote {path.name:44s} {array.shape} {path.stat().st_size / 1e6:7.2f} MB")

    if args.preview:
        rgba = np.dstack((colour, opacity)).astype(np.uint8)
        prev = Image.fromarray(rgba, "RGBA").resize((2048, 2048), Image.LANCZOS)
        bg = Image.new("RGBA", prev.size, (12, 10, 8, 255))
        bg.alpha_composite(prev)
        bg.convert("RGB").save(Path(__file__).with_name("litter_atlas_preview.png"))

    print(f"done in {time.perf_counter() - started:.1f}s")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
