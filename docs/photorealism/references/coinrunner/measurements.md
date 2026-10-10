# Botanical and physical measurements — Coinrunner (*Hydrocotyle vulgaris* analogue)

**Dossier path:** `docs/photorealism/references/coinrunner/measurements.md`  
**Companion files:** `reference.md`, `source_manifest.json`  
**Specification:** Section 2.0 of `docs/photorealism/living_landscape_rendering_implementation_plan.md`  

All quantitative values are explicitly classified as **measured** (from calibrated high-res imagery/PBR scans), **documented** (from botanical taxonomy literature: Flora of the British Isles, Aquatic and Wetland Plants), **estimated** (inferred from biomechanical scaling laws), or **fictional** (specific Vivarium artistic calibration).

---

## 1. Overall physical scale and carpet architecture

| Dimension or physical property | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Carpet canopy height above terrain ($H$) | 0.03 – 0.08 (nom. 0.05) | m | R001, R002 | **documented** / **measured** | High |
| Individual stolon runner length ($L_{stolon}$) | 0.30 – 1.20 (nom. 0.65) | m | R001, R002 | **documented** | High |
| Leaf mosaic packing density | 0.72 – 0.92 | area ratio | R001, R003 | **measured** | High |
| Carpet lateral spread per organism | 0.40 – 1.50 | m | R001, R003 | **documented** | High |
| Stolon elevation above substrate | 0.0 – 5.0 (resting directly on ground) | mm | R002, R004 | **measured** | High |
| Juvenile runner leaf elevation | 0.01 – 0.03 | m | R006 | **measured** | High |

---

## 2. Stolon morphology, nodes, and adventitious rooting

| Dimension or physical property | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Stolon stem diameter | 1.0 – 2.2 (nom. 1.5) | mm | R002, R006 | **measured** | High |
| Internode length between nodes | 25 – 65 (nom. 42) | mm | R002, R006 | **measured** | High |
| Lateral stolon branching angle at node | 35° – 60° from parent axis | degrees | R002, R006 | **measured** | High |
| Lateral branching frequency | Every 2nd to 4th node | frequency | R002 | **measured** | High |
| Adventitious root count per node | 3 – 8 fibrous roots | count | R004 | **measured** | High |
| Adventitious root diameter | 0.3 – 0.8 | mm | R004 | **measured** | High |
| Root penetration depth into substrate | 15 – 50 | mm | R004, R005 | **measured** | High |
| Stolon flexural elasticity | High (bends up to 90° over 15 mm radius without buckling) | biomechanics | R002 | **estimated** | High |

---

## 3. Petiole architecture, angles, and phyllotaxis

| Organ / parameter | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Petioles per node | 1 – 3 (nom. 1 on young nodes, 2 on mature nodes) | count | R002, R006 | **measured** | High |
| Petiole length ($H_{petiole}$) | 25 – 70 (nom. 45) | mm | R001, R002 | **documented** / **measured** | High |
| Petiole diameter | 0.8 – 1.5 (nom. 1.1) | mm | R002, R006 | **measured** | High |
| Petiole emergence angle from stolon | 72° – 90° (near-vertical phototropism) | degrees | R001, R002 | **measured** | High |
| Petiole curvature / lean | 0° – 18° off-vertical towards canopy light gap | degrees | R003 | **measured** | High |
| Phyllotaxis arrangement along stolon | Solitary or paired alternate at consecutive nodes | morphology | R002 | **documented** | High |
| Node spacing uniformity | Poisson-disc with mean 38 mm ($\pm 8\text{ mm}$) | distribution | R003 | **measured** | High |

---

## 4. Lamina morphology, camber, and venation hierarchy

| Dimension or morphological trait | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Leaf blade diameter ($D_{leaf}$) | 14 – 32 (nom. 22) | mm | R007, R008 | **documented** / **measured** | High |
| Aspect ratio (Major / Minor axis) | 1.0 – 1.12 : 1 (circular to slightly oval) | ratio | R007 | **measured** | High |
| Blade outline shape | Orbicular peltate (shield-like disc) | geometry | R007 | **documented** | High |
| Petiole attachment position | Eccentric by 0.5–2.0 mm from geometric center | mm | R007, R008 | **measured** | High |
| Margin crenation lobes | 8 – 14 shallow rounded crenations | count | R007 | **documented** / **measured** | High |
| Crenation notch depth | 0.6 – 1.8 | mm | R007 | **measured** | High |
| Saucer camber (concave dish depth) | 1.0 – 2.5 | mm | R007 | **measured** | High |
| Undulating margin curl | 0.5 – 1.2 | mm | R007 | **measured** | High |
| Lamina thickness (sub-succulent) | 0.28 – 0.45 | mm | R008 | **measured** | High |
| Venation pattern | Actinodromous (radial from central petiole node) | morphology | R007, R008 | **documented** | High |
| Primary radial vein rays | 7 – 11 primary rays | count | R007, R008 | **measured** | High |
| Vein branching near margin | Dichotomous fork 3–6 mm inside outer edge | morphology | R007 | **measured** | High |
| Abaxial primary vein rib relief | 0.25 – 0.55 | mm | R008 | **measured** | High |
| Adaxial vein furrow depth | 0.10 – 0.20 | mm | R007 | **measured** | High |
| Concentric lunar ring radius | 4.0 – 8.0 mm from petiole insertion | mm | R007 | **fictional** | High |

---

## 5. Photometric, material, and optical values

| Channel / physical response | Calibrated value / range | Colorspace / Units | Evidence ID(s) | Notes |
|---|---|---|---|---|
| Adaxial leaf albedo (disc center) | sRGB `(38, 96, 44)` to `(48, 118, 56)` (emerald) | sRGB / Linear | R007 | Rich deep emerald green |
| Adaxial concentric ring albedo | sRGB `(92, 148, 108)` to `(110, 168, 122)` (silvery green) | sRGB / Linear | R007 | Pale reflective ring band |
| Abaxial leaf albedo (underside) | sRGB `(72, 138, 80)` to `(88, 156, 96)` (lime-jade) | sRGB / Linear | R008 | Paler green, reveals radial ribs |
| Adaxial roughness | 0.20 – 0.32 (glossy), 0.08 – 0.14 (wet) | Linear [0..1] | R007, R010 | Leathery waxy cuticular sheen |
| Abaxial roughness | 0.58 – 0.72 (matte) | Linear [0..1] | R008 | Fine cellular texture |
| Lamina transmission / backlight fraction | 0.24 – 0.36 (interveinal), 0.06 (veins & node) | Linear [0..1] | R008 | Fleshy sub-succulent light diffusion |
| Stolon & petiole albedo | sRGB `(82, 115, 58)` to `(105, 135, 68)` (amber-lime) | sRGB / Linear | R002, R006 | Translucent herbaceous stem |
| Stolon & petiole roughness | 0.32 – 0.44 | Linear [0..1] | R002, R006 | Smooth cylindrical surface |
| Stolon transmission | 0.15 – 0.25 | Linear [0..1] | R002 | Translucent green light scatter |
| Nodal root cluster albedo | sRGB `(142, 128, 108)` (pale buff-brown) | sRGB / Linear | R004 | Soil-stained fibrous roots |

---

## 6. Phenological and life-stage progressions

- **Juvenile stolon runner stage:**
  - Fast-growing runner tips (10–30 mm/day); node leaves are tiny cupped discs (4–10 mm diameter).
  - Amber-green stolons snake across terrain voids, seeking soil moisture.
  - Roots initiate as tiny white knobs at nodes before penetrating soil.
- **Mature carpet stage:**
  - Leaves reach full diameter (18–32 mm); petioles adjust heights (35–70 mm) to form continuous interlocking canopy.
  - Adventitious roots firmly anchored in substrate (up to 50 mm deep).
  - Glandular hydathodes active at leaf crenations, exuding micro-beads of moisture.
- **Senescent / Decaying stage:**
  - Senescent leaves turn pale yellow-ochre (sRGB `(182, 160, 68)`) with brown necrotic margins.
  - Lamina thins and decomposes into organic film within 2–3 weeks.
  - Fibrous stolon mesh remains intact on the ground, binding topsoil and forming organic humus scaffolding.
