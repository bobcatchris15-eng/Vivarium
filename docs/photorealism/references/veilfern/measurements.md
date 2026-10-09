# Botanical and physical measurements — Veilfern (*Adiantum pedatum* analogue)

**Dossier path:** `docs/photorealism/references/veilfern/measurements.md`  
**Companion files:** `reference.md`, `source_manifest.json`  
**Specification:** Section 2.0 of `docs/photorealism/living_landscape_rendering_implementation_plan.md`  

All quantitative values are explicitly classified as **measured** (from calibrated high-res imagery/PBR scans), **documented** (from botanical taxonomy literature: Flora of North America, Ferns of the World), **estimated** (inferred from biomechanical scaling laws), or **fictional** (specific Vivarium artistic calibration).

---

## 1. Overall physical scale and clump architecture

| Dimension or physical property | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Mature clump height ($H$) | 0.45 – 0.75 (nom. 0.58) | m | R001, R002 | **documented** / **measured** | High |
| Rosette clump spread ($D_{spread}$) | 0.60 – 1.10 (nom. 0.82) | m | R001, R003 | **documented** / **measured** | High |
| Active living fronds per mature clump | 8 – 18 (nom. 12) | count | R001, R003 | **measured** | High |
| Senescent/prostrate fronds at base | 2 – 5 | count | R001, R004 | **measured** | High |
| Juvenile crozier emergence height | 0.05 – 0.25 | m | R009 | **documented** | High |
| Inter-clump colony spacing | 0.40 – 0.90 | m | R003 | **documented** | High |

---

## 2. Basal rhizome, crown, and stipe emergence

| Dimension or physical property | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Basal rhizome diameter | 15 – 35 | mm | R004 | **documented** | High |
| Rhizome growth habit | Short-creeping to erect compact crown | morphology | R004 | **documented** | High |
| Chaffy scale length (ramenta on rhizome/stipe base) | 2.5 – 6.0 | mm | R004, R009 | **measured** | High |
| Chaffy scale width | 0.8 – 1.8 | mm | R004 | **measured** | High |
| Chaffy scale color / thickness | Golden to chestnut brown / papery thin (0.05 mm) | optical | R004, R009 | **measured** | High |
| Crown anchorage depth in soil/humus | 30 – 70 | mm | R004, R005 | **estimated** | High |
| Basal stipe emergence cluster diameter | 35 – 80 | mm | R004 | **measured** | High |

---

## 3. Stipe, rachis branching hierarchy, and phyllotaxis

| Branch order | Organ description | Insertion angle | Diameter range | Length range | Taper ratio | Evidence ID |
|---|---|---|---|---|---|---|
| Stipe (Order 0) | Wiry main stalk from crown | 35° – 65° from horizontal | 1.8 – 3.2 mm -> 0.8 – 1.2 mm | 0.25 – 0.45 m | 0.40 | R001, R002 |
| Primary fork (Order 1) | Pseudodichotomous split | Included angle 45° – 75° | 1.0 – 1.4 mm -> 0.6 – 0.9 mm | 0.08 – 0.16 m | 0.62 | R002 |
| Rachis branches (Order 2) | Recurved arching branches (2 per frond) | 40° – 60° curve | 0.7 – 1.1 mm -> 0.4 – 0.6 mm | 0.15 – 0.32 m | 0.55 | R002 |
| Pinnae (Order 3) | Lateral pinna stalks (4–8 per branch) | 45° – 65° from rachis | 0.4 – 0.7 mm -> 0.25 – 0.35 mm | 0.08 – 0.22 m | 0.50 | R002, R006 |
| Petiolules (Order 4) | Capillary pinnule stalks | 35° – 55° from pinna rachis | 0.15 – 0.30 mm (uniform) | 1.5 – 4.5 mm | 1.00 | R006, R007 |

### Phyllotaxis and architecture:
- **Crown phyllotaxis:** Spiral rosette arrangement (Fibonacci 2/5 or 3/8 phyllotaxis, ~137.5° divergence) around the compact rhizome apex.
- **Pinnae arrangement along rachis:** Alternating distichously on the convex outer side of each recurved rachis arm.
- **Pinnule arrangement along pinna:** Alternate, distichous, spaced 4–9 mm apart along pinna axis.
- **Flexibility / stiffness:** Stipes exhibit high elastic flex under wind; natural oscillation frequency 1.2–2.0 Hz (**fictional / estimated**).

---

## 4. Lamina morphology, camber, and venation hierarchy

| Dimension or morphological trait | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Pinnule length ($L$) | 8 – 18 (nom. 14) | mm | R006, R007 | **measured** | High |
| Pinnule width ($W$) | 5 – 12 (nom. 8.5) | mm | R006, R007 | **measured** | High |
| Aspect ratio ($L / W$) | 1.4 – 1.8 : 1 | ratio | R007 | **measured** | High |
| Pinnule outline shape | Fan-shaped to cuneate-deltoid (asymmetric base) | geometry | R006, R007 | **documented** | High |
| Basal attachment | Petiolulate; inner margin straight, parallel to rachis | morphology | R006 | **documented** | High |
| Outer margin | Incised / lobed into 3–6 shallow rounded lobes | morphology | R007 | **documented** | High |
| Margin lobing depth | 1.0 – 2.8 | mm | R007 | **measured** | High |
| Transverse camber (fan cup / curvature) | 0.5 – 1.4 | mm | R006, R007 | **measured** | High |
| Edge recurvature / droop | 0.3 – 0.8 | mm | R007 | **measured** | High |
| Lamina interveinal thickness | 0.12 – 0.18 | mm | R008 | **measured** | High |
| Stipe / rachis surface relief | 0.02 – 0.08 (highly polished / cylindrical) | mm | R002, R006 | **measured** | High |
| Venation pattern | Free dichotomous flabellate (forking 2–4 times) | morphology | R008 | **documented** | High |
| Vein divergence angle at forks | 16° – 26° | degrees | R008 | **measured** | High |
| Abaxial vein relief | 0.15 – 0.30 | mm | R008 | **measured** | High |
| False indusia (sori flaps on underside margin) | Length 1.2 – 2.4 mm, width 0.6 – 1.1 mm | mm | R008 | **documented** | High |

---

## 5. Photometric, material, and optical values

| Channel / physical response | Calibrated value / range | Colorspace / Units | Evidence ID(s) | Notes |
|---|---|---|---|---|
| Adaxial pinnule albedo (upper) | sRGB `(48, 112, 58)` to `(62, 138, 72)` (emerald) | sRGB / Linear | R007, R010 | Vivid saturated emerald green |
| Abaxial pinnule albedo (underside) | sRGB `(78, 148, 92)` to `(92, 168, 108)` (jade) | sRGB / Linear | R008 | Slightly paler, reveals translucent veins |
| Adaxial roughness | 0.30 – 0.42 (dry satin), 0.10 – 0.18 (dew/wet) | Linear [0..1] | R007, R010 | Hydrophobic wax cuticle |
| Abaxial roughness | 0.62 – 0.76 (matte) | Linear [0..1] | R008 | Diffuse cellular surface |
| Lamina transmission / backlight fraction | 0.42 – 0.58 (interveinal), 0.12 (veins) | Linear [0..1] | R008 | High subsurface transmission; glowing green |
| Stipe albedo | sRGB `(28, 20, 18)` to `(38, 26, 22)` (ebony-chestnut) | sRGB / Linear | R002, R006 | Dark near-black with deep warm undertone |
| Stipe roughness | 0.18 – 0.28 (highly lustrous / polished) | Linear [0..1] | R002, R006 | Polished cuticular specular highlights |
| Stipe transmission | 0.00 (completely opaque) | Linear [0..1] | R002, R008 | Solid fibrous bundle casts crisp shadow |
| Chaffy scale albedo | sRGB `(142, 98, 52)` to `(168, 122, 68)` (golden amber) | sRGB / Linear | R004, R009 | Papery dry ramenta |
| Chaffy scale transmission | 0.25 – 0.40 | Linear [0..1] | R009 | Thin translucent scales |

---

## 6. Phenological and life-stage progressions

- **Spring crozier stage ($H = 0.05–0.25\text{ m}$):**
  - Stipes emerge vertically with tightly circinate (involute coiled) fiddleheads.
  - Clothed in dense, overlapping golden-brown papery scales (ramenta).
  - Rapid elongation: stipes extend before pinnules unfurl.
- **Mature frond stage ($H = 0.45–0.75\text{ m}$):**
  - Fully unfurled pedate canopy forming tiered arching umbrellas.
  - Ebony stipes harden and polish; pinnules reach peak chlorophyll saturation and hydrophobicity.
  - Sori (marginal false indusia) mature along recurved outer lobes.
- **Senescent / Autumn stage:**
  - Chlorophyll reabsorption leaves pinnules translucent golden-straw color (sRGB `(175, 148, 82)`).
  - Stipes become brittle, collapsing onto the basal litter layer where they form durable dark fibrous mulch.
