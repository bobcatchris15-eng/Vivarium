#!/usr/bin/env python3
"""Offline PROSPECT-informed optical & forb architectural authoring tool for Vivarium.

Generates per-species `visual.leaf` and `visual.form` blocks across all 62 herbaceous,
succulent, rosette, runner, fern, and graminoid species in `game/content/flora/*.json`,
using a simplified 3-band PROSPECT-D / Jacquemoud-Baret radiative slab transfer model
coupled with Bousquet cuticle reflectance ranges and HerbGraphSpecies morphology.
"""

from __future__ import annotations

import json
import math
import pathlib
import re
from typing import Any

ROOT = pathlib.Path(__file__).resolve().parent.parent
FLORA_DIR = ROOT / "game" / "content" / "flora"
HERB_CATALOG = ROOT / "src" / "Vivarium.Sim" / "Repr" / "Plant" / "Catalog" / "HerbGraphSpecies.cs"
PROFILES_CS = ROOT / "game" / "Render" / "FloraSurfaceProfiles.cs"

GOLDEN_ANGLE_DEG = 137.50776405003785

THICK_SUCCULENTS: dict[str, dict[str, float]] = {
    "sunstone_rosette": {"lamina": 5.0, "midrib": 8.0, "edge": 2.0, "farina": 0.60, "cab": 25.0, "car": 12.0, "n": 2.0, "front_r": 0.28, "back_r": 0.55, "sheen": 0.72, "f0": 0.042},
    "cragset":          {"lamina": 4.2, "midrib": 6.8, "edge": 1.6, "farina": 0.45, "cab": 28.0, "car": 11.0, "n": 2.1, "front_r": 0.32, "back_r": 0.58, "sheen": 0.65, "f0": 0.040},
    "brightknot":       {"lamina": 3.5, "midrib": 4.5, "edge": 1.4, "farina": 0.15, "cab": 34.0, "car": 9.5,  "n": 1.9, "front_r": 0.24, "back_r": 0.48, "sheen": 0.78, "f0": 0.042},
    "glassfinger":      {"lamina": 5.5, "midrib": 7.5, "edge": 2.2, "farina": 0.25, "cab": 26.0, "car": 8.0,  "n": 2.2, "front_r": 0.22, "back_r": 0.46, "sheen": 0.80, "f0": 0.044},
    "fenbead":          {"lamina": 2.8, "midrib": 4.0, "edge": 1.1, "farina": 0.10, "cab": 36.0, "car": 7.5,  "n": 1.8, "front_r": 0.26, "back_r": 0.50, "sheen": 0.70, "f0": 0.040},
    "tidebell":         {"lamina": 2.4, "midrib": 3.6, "edge": 0.9, "farina": 0.20, "cab": 30.0, "car": 6.5,  "n": 1.8, "front_r": 0.25, "back_r": 0.48, "sheen": 0.68, "f0": 0.040},
    "marrowbloom":      {"lamina": 2.2, "midrib": 3.2, "edge": 0.8, "farina": 0.35, "cab": 14.0, "car": 10.5, "n": 2.3, "front_r": 0.42, "back_r": 0.64, "sheen": 0.42, "f0": 0.036},
    "tankbloom":        {"lamina": 2.5, "midrib": 4.2, "edge": 0.9, "farina": 0.30, "cab": 38.0, "car": 9.0,  "n": 2.0, "front_r": 0.26, "back_r": 0.54, "sheen": 0.70, "f0": 0.042},
}

FARINA_THIN_OVERRIDES: dict[str, dict[str, float]] = {
    "mooncoin":     {"lamina": 0.28, "farina": 0.72, "cab": 24.0, "car": 6.5, "n": 1.85},
    "frosttussock": {"lamina": 0.22, "farina": 0.45, "cab": 26.0, "car": 6.0, "n": 1.75},
    "ashwire":      {"lamina": 0.18, "farina": 0.40, "cab": 22.0, "car": 7.5, "n": 1.80},
    "plumegrass":   {"lamina": 0.20, "farina": 0.25, "cab": 34.0, "car": 9.0, "n": 1.65},
    "starbulb":     {"lamina": 0.26, "farina": 0.22, "cab": 32.0, "car": 7.0, "n": 1.70},
    "silverweed":   {"lamina": 0.14, "farina": 0.18, "cab": 36.0, "car": 6.5, "n": 1.55},
    "blackneedle":  {"lamina": 0.28, "farina": 0.15, "cab": 46.0, "car": 8.0, "n": 1.85},
}

FELTED_IDS = {"burlmantle", "mooncoin", "hallowsting", "duskbristle", "ashwire", "quillsedge", "blackneedle"}
WAXY_PAD_IDS = {"mirrorleaf", "glasslily", "marshkettle", "butterglint", "pitcher_plant", "mirrorstrap_fern", "barbarrow", "paddlerib"}


def prospect_transmission(cab: float, car: float, n_layers: float, lamina_mm: float, base_col: list[float], is_thick: bool) -> tuple[list[float], float]:
    """Compute linear RGB transmission colour and strength from PROSPECT pigment & slab thickness."""
    # PROSPECT-D specific absorption peaks: chlorophyll hits B (~440nm) and R (~660nm) strongly,
    # while carotenoids attenuate B; mesophyll cell layers N increase internal multiple scattering.
    if is_thick:
        eff_depth = 0.22 * lamina_mm * (0.65 + 0.22 * n_layers)
        k_rgb = (
            0.032 * cab + 0.004 * car + 0.12 * eff_depth,
            0.008 * cab + 0.002 * car + 0.07 * eff_depth,
            0.044 * cab + 0.036 * car + 0.15 * eff_depth,
        )
        t_raw = [math.exp(-k) for k in k_rgb]
        # Volumetric water parenchyma scatters milky yellow-green toward desaturated sage
        t_mix = [
            max(0.08, min(0.95, round(0.55 * t_raw[i] + 0.45 * (base_col[i] * 0.7 + 0.15), 3)))
            for i in range(3)
        ]
        peak = max(t_mix)
        strength = max(0.14, min(0.42, round(0.46 / (1.0 + 0.18 * lamina_mm), 3)))
        if peak > 0.01:
            t_col = [round(min(0.85, max(0.12, c / peak * 0.62)), 3) for c in t_mix]
        else:
            t_col = [0.35, 0.55, 0.25]
        return t_col, strength

    # Thin slab: Beer-Lambert across N elementary layers
    c_scale = max(0.15, lamina_mm / 0.20)
    tau_r = (0.016 * cab + 0.003 * car) * (0.75 + 0.20 * n_layers) * (0.6 + 0.4 * c_scale)
    tau_g = (0.004 * cab + 0.001 * car) * (0.75 + 0.20 * n_layers) * (0.6 + 0.4 * c_scale)
    tau_b = (0.022 * cab + 0.025 * car) * (0.75 + 0.20 * n_layers) * (0.6 + 0.4 * c_scale)
    tr, tg, tb = math.exp(-tau_r), math.exp(-tau_g), math.exp(-tau_b)
    mx = max(tr, tg, tb, 1e-4)
    # Retain a trace of species hue so silver/aqua leaves don't shift pure chartreuse
    t_col = [
        round(min(0.95, max(0.08, (tr / mx) * 0.58 + base_col[0] * 0.25)), 3),
        round(min(0.95, max(0.18, (tg / mx) * 0.68 + base_col[1] * 0.18)), 3),
        round(min(0.95, max(0.05, (tb / mx) * 0.34 + base_col[2] * 0.30)), 3),
    ]
    strength = round(min(0.80, max(0.18, 0.52 * tg * math.pow(0.20 / max(lamina_mm, 0.06), 0.22))), 3)
    return t_col, strength


def parse_herb_table() -> dict[str, dict[str, Any]]:
    text = HERB_CATALOG.read_text(encoding="utf-8")
    pattern = re.compile(
        r'new\("(?P<id>[^"]+)",\s*"[^"]*",\s*"(?P<form>[^"]+)",\s*'
        r'(?P<shoots>\d+),\s*(?P<leaves>\d+),\s*(?P<spread>[0-9.]+),\s*(?P<width>[0-9.]+),\s*(?P<bloom>\d+)'
    )
    rows: dict[str, dict[str, Any]] = {}
    for m in pattern.finditer(text):
        rows[m.group("id")] = {
            "form": m.group("form"),
            "shoots": int(m.group("shoots")),
            "leaves": int(m.group("leaves")),
            "spread": float(m.group("spread")),
            "width": float(m.group("width")),
            "bloom": int(m.group("bloom")),
        }
    return rows


def parse_leaf_profiles() -> dict[str, dict[str, float]]:
    text = PROFILES_CS.read_text(encoding="utf-8")
    pat = re.compile(
        r'"(?P<id>[^"]+)"\s*=>\s*new\(new\([^)]+\),\s*new\([^)]+\),\s*'
        r'(?P<type>\d+),\s*(?P<veins>[0-9.]+)f?,\s*(?P<sheen>[0-9.]+)f?,\s*(?P<trans>[0-9.]+)f?\)'
    )
    out: dict[str, dict[str, float]] = {}
    for m in pat.finditer(text):
        out[m.group("id")] = {
            "type": int(m.group("type")),
            "veins": float(m.group("veins")),
            "sheen": float(m.group("sheen")),
            "trans": float(m.group("trans")),
        }
    return out


def build_leaf_and_form(sp_id: str, doc: dict[str, Any], row: dict[str, Any], prof: dict[str, float] | None) -> tuple[dict[str, Any], dict[str, Any]]:
    visual = doc["visual"]
    growth = doc["growth"]
    col = visual.get("color", [0.3, 0.5, 0.25])
    height = float(visual.get("height", 0.2))
    r_max = float(growth.get("radiusAtMax", 0.15))

    form_kind = row["form"]
    raw_leaves = max(4, min(220, row["shoots"] * max(1, row["leaves"])))

    if form_kind == "rosette":
        archetype = "rosette"
    elif form_kind in ("runner", "pad"):
        archetype = "mat"
    elif form_kind in ("grass", "whorl"):
        archetype = "clump"
    else:
        archetype = "alternate_stem"

    if sp_id in THICK_SUCCULENTS:
        spec = THICK_SUCCULENTS[sp_id]
        lamina = spec["lamina"]
        midrib = spec["midrib"]
        edge = spec["edge"]
        cab = spec["cab"]
        car = spec["car"]
        n_layers = spec["n"]
        farina = spec["farina"]
        front_r = spec["front_r"]
        back_r = spec["back_r"]
        vein_r = round(min(0.85, front_r + 0.17), 3)
        wax_r = round(max(0.14, front_r - 0.10), 3)
        sheen = spec["sheen"]
        f0 = spec["f0"]
        t_col, t_str = prospect_transmission(cab, car, n_layers, lamina, col, is_thick=True)
        edge_deg = 8.0 if form_kind == "rosette" else 12.0
        cup = 0.62 if sp_id in ("sunstone_rosette", "cragset", "tankbloom") else 0.45
        droop = 0.10
        leaf_block = {
            "class": "thick",
            "cab": cab,
            "car": car,
            "n": n_layers,
            "thickness_mm": {"lamina": lamina, "midrib": midrib, "edge": edge},
            "roughness": {"front": front_r, "back": back_r, "vein": vein_r, "wax": wax_r},
            "specular": {"f0": f0, "cuticle_sheen": sheen},
            "transmission": {"color": t_col, "strength": t_str},
            "curl": {"edge_deg": edge_deg, "cup": cup, "droop": droop},
            "farina": farina,
            "turgor": 1.0,
        }
    else:
        ov = FARINA_THIN_OVERRIDES.get(sp_id, {})
        g_norm = max(0.1, min(1.0, col[1]))
        cab = ov.get("cab", round(min(68.0, max(18.0, 24.0 + 42.0 * (1.0 - g_norm * 0.65))), 1))
        car = ov.get("car", round(min(16.0, max(4.5, 6.0 + 8.0 * col[0])), 1))
        if form_kind == "fern":
            lamina_default = 0.14
        elif form_kind == "grass":
            lamina_default = 0.18
        elif sp_id in WAXY_PAD_IDS:
            lamina_default = 0.28
        else:
            lamina_default = 0.20
        lamina = ov.get("lamina", lamina_default)
        midrib = round(min(2.4, lamina * (4.2 if form_kind != "grass" else 2.8)), 2)
        edge = round(max(0.05, lamina * 0.48), 2)
        n_layers = ov.get("n", 1.35 if form_kind == "fern" else 1.55 if form_kind != "grass" else 1.45)
        farina = ov.get("farina", 0.0)
        base_sheen = prof["sheen"] if prof else 0.30
        if sp_id in WAXY_PAD_IDS:
            base_sheen = max(base_sheen, 0.48)
        elif sp_id in FELTED_IDS:
            base_sheen = min(base_sheen, 0.18)
        front_r = round(min(0.84, max(0.24, 0.62 - base_sheen * 0.42 + (0.14 if sp_id in FELTED_IDS else 0.0))), 3)
        back_r = round(min(0.92, max(0.54, front_r + 0.28)), 3)
        vein_r = round(min(0.88, max(0.40, front_r + 0.18)), 3)
        wax_r = round(min(0.55, max(0.18, front_r - 0.12)), 3)
        f0 = 0.038 if sp_id in WAXY_PAD_IDS else 0.030 if sp_id in FELTED_IDS else 0.034
        t_col, t_str = prospect_transmission(cab, car, n_layers, lamina, col, is_thick=False)
        if prof:
            t_str = round(min(0.80, max(0.14, 0.5 * t_str + 0.5 * min(0.75, prof["trans"] * 1.4))), 3)
        edge_deg = 22.0 if form_kind == "fern" else 14.0 if form_kind == "grass" else 20.0
        cup = 0.22 if form_kind == "grass" else 0.32 if sp_id in WAXY_PAD_IDS else 0.30
        droop = 0.35 if form_kind in ("grass", "fern") else 0.24 if form_kind == "forb" else 0.16
        leaf_block = {
            "class": "thin",
            "cab": cab,
            "car": car,
            "n": round(n_layers, 2),
            "thickness_mm": {"lamina": lamina, "midrib": midrib, "edge": edge},
            "roughness": {"front": front_r, "back": back_r, "vein": vein_r, "wax": wax_r},
            "specular": {"f0": f0, "cuticle_sheen": round(base_sheen, 3)},
            "transmission": {"color": t_col, "strength": t_str},
            "curl": {"edge_deg": edge_deg, "cup": cup, "droop": droop},
            "farina": farina,
            "turgor": 1.0,
        }

    # Physical per-leaf dimensions bounded cleanly
    if archetype == "clump":
        leaf_len = max(0.03, min(1.5, round(height * 0.65, 3)))
        leaf_w = max(0.004, min(0.25, round(r_max * row["width"] * 0.65, 4)))
        internode = max(0.004, min(0.25, round(height / max(4, row["leaves"] * 2), 4)))
    elif archetype == "rosette":
        leaf_len = max(0.02, min(0.8, round(r_max * 0.55, 3)))
        leaf_w = max(0.008, min(0.35, round(r_max * row["width"] * 1.15, 4)))
        internode = max(0.001, min(0.05, round(height * 0.04, 4)))
    elif archetype == "mat":
        leaf_len = max(0.015, min(0.5, round(r_max * 0.24, 3)))
        leaf_w = max(0.006, min(0.3, round(r_max * row["width"] * 0.85, 4)))
        internode = max(0.004, min(0.18, round(r_max * 0.16, 4)))
    else:
        leaf_len = max(0.02, min(0.7, round(max(r_max * 0.42, height * 0.26), 3)))
        leaf_w = max(0.008, min(0.35, round(r_max * row["width"] * 0.9, 4)))
        internode = max(0.008, min(0.25, round(height / max(3, row["leaves"] + 1), 4)))

    form_block = {
        "archetype": archetype,
        "phyllotaxis_deg": round(GOLDEN_ANGLE_DEG, 4),
        "leaf_count": raw_leaves,
        "leaf_length": leaf_len,
        "leaf_width": leaf_w,
        "internode_length": internode,
        "curl": round(leaf_block["curl"]["edge_deg"] / 45.0, 3),
        "cup": round(leaf_block["curl"]["cup"], 3),
        "droop": round(leaf_block["curl"]["droop"], 3),
    }
    return leaf_block, form_block


def main() -> None:
    herb_table = parse_herb_table()
    if len(herb_table) != 61:
        raise RuntimeError(f"Expected 61 species in HerbGraphSpecies.Table, parsed {len(herb_table)}")
    profiles = parse_leaf_profiles()
    updated = 0
    thick_count = 0
    for sp_id, row in sorted(herb_table.items()):
        path = FLORA_DIR / f"{sp_id}.json"
        if not path.exists():
            raise FileNotFoundError(f"Missing flora JSON: {path}")
        doc = json.loads(path.read_text(encoding="utf-8"))
        leaf_block, form_block = build_leaf_and_form(sp_id, doc, row, profiles.get(sp_id))
        doc["visual"]["leaf"] = leaf_block
        doc["visual"]["form"] = form_block
        path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
        updated += 1
        if leaf_block["class"] == "thick":
            thick_count += 1
    print(f"PROSPECT_AUTHOR_OK updated={updated} thick_succulent={thick_count} thin_herb={updated - thick_count}")


if __name__ == "__main__":
    main()
