# Geological and botanical measurements — Ground Materials (Substrate, Moss, Litter, Riparian Mud)

**Dossier path:** `docs/photorealism/references/ground_materials/measurements.md`  
**Companion files:** `reference.md`, `source_manifest.json`  
**Specification:** Section 2.0 and Section 5.0 of `docs/photorealism/living_landscape_rendering_implementation_plan.md`  

All quantitative values are explicitly classified as **measured** (from calibrated high-res imagery/PBR photogrammetry scans), **documented** (from soil science taxonomy: USDA Soil Survey Manual, FAO Guidelines for Soil Description), **estimated** (inferred from hydraulic and ecological dynamics), or **fictional** (specific Vivarium artistic calibration).

---

## 1. Overall terrain strata scales and horizon thickness

| Horizon / stratum layer | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Fresh leaf litter layer (Oi horizon) | 10 – 35 (nom. 20) | mm | R001, R008 | **documented** / **measured** | High |
| Semi-decomposed organic humus (Oe/Oa horizon) | 15 – 50 (nom. 30) | mm | R001, R007 | **documented** / **measured** | High |
| Compacted mineral soil loam (A horizon) | 50 – 200 (nom. 120) | mm | R001, R004 | **documented** | High |
| Sub-surface gravelly sandy loam (B/C horizon) | 150 – 600 | mm | R004 | **documented** | High |
| Living moss cushion height above substrate | 8 – 30 (nom. 16) | mm | R003, R006 | **measured** | High |
| Exposed bedrock outcrop protrusion | 0.15 – 1.40 | m | R004, R005 | **fictional** / **documented** | High |
| Riparian shoreline transition band width | 0.15 – 0.40 | m | R009, R010 | **documented** | High |

---

## 2. Microtopography, substrate relief, and aggregate scales

| Substrate class | Physical relief amplitude | Feature wavelength | Feature description | Category | Evidence ID |
|---|---|---|---|---|---|
| Organic humus (loam) | 1.5 – 8.0 mm | 4 – 15 mm | Granular crumb aggregates, dark organic pellets | **measured** | R001, R007 |
| Mineral soil (pathways) | 1.5 – 6.0 mm | 5 – 20 mm | Compacted sand/silt matrix with sub-cm pebble inclusions | **measured** | R001, R005 |
| Mineral pebble inclusions | 2.0 – 15.0 mm | Isolated clasts | Angular to sub-rounded quartzite and chert clasts | **measured** | R005 |
| Leaf litter layer | 3.0 – 25.0 mm | 20 – 80 mm | Curled, overlapping broadleaf fragments and shed twigs | **measured** | R001, R008 |
| Moss cushion carpet | 4.0 – 18.0 mm | 15 – 50 mm | Feathery mounded cushions with sunken inter-clump sinuses | **measured** | R003, R006 |
| Riparian alluvial silt | 0.5 – 2.0 mm | 30 – 120 mm | Sub-millimeter silt particles with gentle wave/flow micro-ripples | **measured** | R009, R010 |
| Weathered fieldstone rock | 5.0 – 45.0 mm | 50 – 300 mm | Fractured mineral planes, pitted erosion hollows | **measured** | R004, R005 |
| Coarse deadwood bark | 5.0 – 22.0 mm | 25 – 65 mm | Exfoliating fibrous bark strips, splintered heartwood fissures | **measured** | R002 |

---

## 3. Moss shoot architecture, branching, and phyllotaxis

| Organ / parameter | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Primary moss stem length | 10 – 25 (nom. 18) | mm | R003, R006 | **documented** / **measured** | High |
| Stem diameter | 0.25 – 0.50 | mm | R006 | **measured** | High |
| Branching architecture | Bi-pinnate to tri-pinnate planar sprays (*Thuidium* habit) | morphology | R006 | **documented** | High |
| Secondary branch insertion angle | 42° – 65° from primary stem | degrees | R006 | **measured** | High |
| Secondary branch count per cm of stem | 8 – 18 branches | count/cm | R006 | **measured** | High |
| Tertiary branchlet insertion angle | 38° – 55° from secondary branch | degrees | R006 | **measured** | High |
| Micro-leaf length (stem leaves) | 0.8 – 1.4 | mm | R006 | **measured** | High |
| Micro-leaf width | 0.4 – 0.7 | mm | R006 | **measured** | High |
| Micro-leaf phyllotaxis | Dense spiral imbricate (overlapping shingle arrangement) | morphology | R006 | **documented** | High |
| Micro-leaf camber / costa | Concave boat-shaped (plicae furrows), single slender nerve | morphology | R006 | **documented** | High |
| Colony growing margin profile | Irregular ragged scalloped lobing (lobe radius 15–40 mm) | geometry | R003, R004 | **measured** | High |

---

## 4. Leaf litter fragment geometry, camber, and venation

| Organ / parameter | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Intact fallen leaf length | 40 – 85 (nom. 60) | mm | R001, R008 | **measured** | High |
| Fragmented leaf piece size | 5 – 35 | mm | R001, R008 | **measured** | High |
| Fallen leaf 3D camber / curl depth | 5 – 22 (warped arch above soil) | mm | R001, R008 | **measured** | High |
| Curled leaf edge radius | 3 – 10 | mm | R008 | **measured** | High |
| Senescent leaf lamina thickness | 0.10 – 0.18 (shriveled dry tissue) | mm | R008 | **measured** | High |
| Exposed vein skeleton relief | 0.4 – 0.9 (weathered raised fibrous skeleton) | mm | R008 | **measured** | High |
| Twig detritus diameter | 2.5 – 12.0 | mm | R001, R008 | **measured** | High |
| Twig detritus length | 30 – 180 | mm | R001, R008 | **measured** | High |
| Bark flake chip size | 10 – 45 mm length, 2 – 6 mm thickness | mm | R001, R002 | **measured** | High |

---

## 5. Photometric, material, and optical values across moisture states

| Substrate & condition | Albedo color (sRGB / linear) | Roughness range | Specular F0 | Evidence ID(s) | Notes |
|---|---|---|---|---|---|
| Dry Organic Humus | sRGB `(58, 48, 40)` to `(72, 60, 50)` | 0.84 – 0.94 | 0.04 | R001, R007 | Diffuse, porous crumb aggregates |
| Saturated Wet Humus | sRGB `(28, 22, 18)` to `(36, 28, 24)` | 0.22 – 0.38 | 0.04 | R001, R007 | Darkens by 52%; damp sheen |
| Dry Mineral Soil / Loam | sRGB `(115, 98, 80)` to `(138, 118, 98)` | 0.82 – 0.92 | 0.04 | R005 | Pale buff sandy loam |
| Saturated Mineral Soil | sRGB `(54, 44, 34)` to `(68, 54, 42)` | 0.18 – 0.32 | 0.04 | R005 | Darkens by 54% |
| Living Moss Carpet (dry) | sRGB `(64, 108, 42)` to `(78, 126, 52)` | 0.68 – 0.82 | 0.04 | R003, R006 | Feathery velvety green |
| Living Moss Carpet (wet) | sRGB `(42, 82, 28)` to `(54, 98, 36)` | 0.24 – 0.40 | 0.04 | R003, R006 | Saturated deep emerald, droplet highlights |
| Dry Broadleaf Litter | sRGB `(132, 92, 54)` to `(158, 112, 68)` | 0.72 – 0.86 | 0.04 | R001, R008 | Warm tan to russet brown |
| Wet Decomposed Litter | sRGB `(52, 36, 24)` to `(68, 46, 32)` | 0.28 – 0.44 | 0.04 | R001, R008 | Darkens significantly; slimy organic film |
| Riparian Silt (dry bank) | sRGB `(128, 114, 96)` to `(148, 132, 112)` | 0.78 – 0.88 | 0.04 | R009, R010 | Pale dry alluvial sand |
| Saturated Shoreline Mud | sRGB `(44, 38, 32)` to `(58, 50, 42)` | 0.10 – 0.22 | 0.04 | R009, R010 | Darkens by 60%; water sheen puddle reflection |
| Standing water puddle films | Water clear tint over dark mud | 0.02 – 0.06 | 0.02 (Fresnel) | R010 | Mirror-like specular reflection |

---

## 6. Heightfield blending and vegetation contact rules

- **Heightfield displacement blend formula:**
  $$\text{BlendWeight}_A = \text{clamp}\left(\frac{(H_A + \text{Mask}_A) - \max(H_B + \text{Mask}_B) + \text{Softness}}{\text{Softness}}, 0.0, 1.0\right)$$
  - Prevents muddy linear alpha blending across substrate boundaries.
  - Leaves, twigs, and pebbles stand proud while sand/loam settles into low cavities.
- **Vegetation contact and soil embedding tolerances:**
  - Tree root collar flutes: embed 40–120 mm below nominal heightfield surface; organic humus and moss accumulate 15–40 mm high against buttress ribs.
  - Fern rhizome crowns: sit centered on terrain normal with 20–40 mm basal embedding; no floating stipe bases.
  - Stolon runners: follow terrain heightfield within 2.0 mm tolerance; nodal root spurs penetrate 15–30 mm into soil.
  - Nurse logs: sink 20–60 mm into soil; deadwood underside receives contact darkening mask (AO multiplier 0.15–0.30).
