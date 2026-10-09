# Botanical and physical measurements — Ironlace (*Carpinus betulus* analogue)

**Dossier path:** `docs/photorealism/references/ironlace/measurements.md`  
**Companion files:** `reference.md`, `source_manifest.json`  
**Specification:** Section 2.0 of `docs/photorealism/living_landscape_rendering_implementation_plan.md`  

All quantitative values are explicitly classified as **measured** (from calibrated high-res imagery/PBR scans), **documented** (from botanical taxonomy literature: Kew POWO, Flora Europaea), **estimated** (inferred from biomechanical scaling laws), or **fictional** (specific Vivarium artistic calibration).

---

## 1. Overall physical scale and architecture

| Dimension or physical property | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Mature tree height ($H$) | 6.0 – 11.0 (nom. 8.5) | m | R001, R002 | **fictional** / **documented** | High (scaled for 16 m island) |
| Canopy crown spread ($D_{crown}$) | 5.0 – 9.0 (nom. 7.2) | m | R001, R003 | **fictional** / **documented** | High |
| Crown depth ratio ($H_{crown} / H$) | 0.60 – 0.78 | ratio | R001, R002 | **documented** | High |
| Bole height to first major limb | 1.8 – 3.2 | m | R001, R004 | **documented** | High |
| Trunk DBH (diameter at 1.3 m) | 0.35 – 0.75 | m | R001, R004 | **documented** | High |
| Juvenile tree height | 0.8 – 2.5 | m | R001 | **documented** | High |
| Veteran snag height | 5.5 – 9.5 | m | R009 | **documented** | High |

---

## 2. Root collar, basal fluting, and ground anchoring

| Dimension or physical property | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Basal root collar diameter ($D_{base}$) | 1.2 – 2.6 | m | R004, R005 | **measured** | High |
| Buttress flare angle to horizontal | 35° – 52° | degrees | R004 | **measured** | High |
| Fluting rib count (primary vertical bosses) | 4 – 8 (nom. 5) | count | R004, R009 | **measured** | High |
| Fluting rib radial amplitude (crest to sinus) | 40 – 180 | mm | R004, R005 | **measured** | High |
| Sinus hollow curvature radius | 25 – 80 | mm | R004 | **measured** | High |
| Ground penetration depth before flare termination | 150 – 350 | mm | R004, R005 | **estimated** | High |
| Soil mound accumulation at trunk base | 30 – 110 | mm | R004 | **estimated** | Medium |

---

## 3. Branching hierarchy, angles, and phyllotaxis

| Branch order | Organ description | Insertion angle | Diameter range | Length range | Taper ratio | Evidence ID |
|---|---|---|---|---|---|---|
| Order 0 | Trunk bole | Vertical (0°–12° tilt) | 750 -> 420 mm | 1.8 – 3.2 m | 0.56 | R001, R004 |
| Order 1 | Primary scaffold limbs (3–5 limbs) | 42° – 65° from vertical | 420 -> 210 mm | 3.0 – 5.5 m | 0.50 | R002 |
| Order 2 | Secondary boughs (4–8 per scaffold) | 35° – 55° from parent | 210 -> 80 mm | 1.8 – 3.2 m | 0.38 | R002 |
| Order 3 | Tertiary branchlets (alternate planar) | 28° – 46° from parent | 80 -> 25 mm | 0.8 – 1.6 m | 0.31 | R002, R006 |
| Order 4 | Quaternary twigs (distichous zig-zag) | 32° – 52° from parent | 25 -> 6 mm | 0.25 – 0.65 m | 0.24 | R006 |
| Order 5 | Terminal filigree shoots | 35° – 58° from parent | 6 -> 1.8 mm | 0.08 – 0.22 m | 0.30 | R006 |

### Phyllotaxis and internode rhythm:
- **Phyllotaxis rule:** 1/2 alternate distichous (planar arrangement, angular divergence = 180°).
- **Internode length on terminal twigs:** 12 – 28 mm (shortening towards shoot tips: 28 mm -> 14 mm).
- **Shoot node zig-zag deviation:** 8° – 16° inflection at each node along the shoot axis (**measured** from R006).
- **Petiole insertion:** Alternate, solitary, angled 25°–45° forward from twig axis.
- **Petiole dimensions:** Length 6 – 12 mm, diameter 0.8 – 1.4 mm, adaxially channeled/grooved (**measured** from R007).

---

## 4. Lamina morphology, camber, and venation hierarchy

| Dimension or morphological trait | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Leaf blade length ($L$) | 45 – 85 (nom. 65) | mm | R007, R008 | **documented** / **measured** | High |
| Leaf blade width ($W$) | 25 – 45 (nom. 35) | mm | R007, R008 | **documented** / **measured** | High |
| Aspect ratio ($L / W$) | 1.7 – 2.1 : 1 | ratio | R007 | **measured** | High |
| Blade outline shape | Ovate-oblong to elliptic | geometry | R007 | **documented** | High |
| Leaf apex / tip | Acuminate (sharp pointed tip) | geometry | R007 | **documented** | High |
| Leaf base | Rounded to subcordate, slightly asymmetric | geometry | R007 | **documented** | High |
| Margin serration | Sharply biserrate (doubly serrate) | morphology | R007 | **documented** | High |
| Primary tooth pitch / count | 12 – 16 major teeth per side | count | R007 | **measured** | High |
| Secondary tooth depth | 0.4 – 0.9 | mm | R007 | **measured** | High |
| Transverse lamina camber (V-profile fold) | 1.5 – 3.5 (fold angle 145°–165°) | mm | R007, R008 | **measured** | High |
| Longitudinal curvature (droop/curl) | 0.8 – 2.2 | mm | R006, R007 | **measured** | High |
| Lamina interveinal thickness | 0.18 – 0.24 | mm | R008 | **measured** | High |
| Primary midrib width at base | 1.2 – 1.8 | mm | R007, R008 | **measured** | High |
| Primary midrib abaxial projection relief | 0.8 – 1.4 | mm | R008 | **measured** | High |
| Secondary vein pairs | 11 – 15 pairs | count | R007, R008 | **documented** / **measured** | High |
| Secondary vein divergence angle from midrib | 40° – 52° | degrees | R007 | **measured** | High |
| Secondary vein craspedodromous path | Direct, unbranched straight to tooth apex | morphology | R007 | **documented** | High |
| Tertiary venation | Reticulate, scalariform (ladder-like bars) | morphology | R008 | **measured** | High |

---

## 5. Bark and substrate relief measurements

| Dimension or physical property | Permitted range / nominal | Units | Evidence ID(s) | Category | Confidence |
|---|---|---|---|---|---|
| Mature trunk bark ridge wavelength | 35 – 85 | mm | R004, R005, R010 | **measured** | High |
| Bark fissure / furrow depth | 8 – 25 | mm | R004, R005, R010 | **measured** | High |
| Furrow cross-section profile | U-shaped to narrow V-groove | geometry | R005 | **measured** | High |
| Fluting macro-wavelength (trunk circumference) | 220 – 480 | mm | R004 | **measured** | High |
| Young bough bark surface relief | 0.8 – 2.5 (nearly smooth with lenticels) | mm | R002, R006 | **documented** | High |
| Terminal twig surface relief | 0.1 – 0.4 (smooth cuticle) | mm | R006 | **measured** | High |

---

## 6. Photometric, material, and optical values

| Channel / physical response | Calibrated value / range | Colorspace / Units | Evidence ID(s) | Notes |
|---|---|---|---|---|
| Adaxial leaf albedo (upper) | sRGB `(58, 72, 42)` to `(74, 91, 48)` (bronze-olive) | sRGB / Linear | R007 | Dark rich olive, subtle bronze shift |
| Abaxial leaf albedo (underside) | sRGB `(118, 142, 98)` to `(132, 155, 110)` (pale celadon) | sRGB / Linear | R008 | ~40% higher luminance than adaxial |
| Adaxial roughness | 0.28 – 0.42 (dry), 0.14 – 0.22 (wet) | Linear [0..1] | R007, R010 | Smooth waxy cuticle with specular sheen |
| Abaxial roughness | 0.68 – 0.82 (matte) | Linear [0..1] | R008 | Microscopic stomata / trichomes diffuse light |
| Lamina transmission / backlight fraction | 0.22 – 0.34 (interveinal), 0.04 (midrib) | Linear [0..1] | R008 | Midrib and secondary veins cast dark shadow |
| Bark dry albedo | sRGB `(85, 78, 72)` to `(112, 102, 94)` (neutral gray-brown) | sRGB / Linear | R005, R010 | Sinuous fibrous grain |
| Bark wet albedo | sRGB `(42, 38, 35)` to `(58, 52, 48)` | sRGB / Linear | R005, R010 | Darkens by ~50% in rain |
| Bark roughness | 0.78 – 0.92 (dry), 0.24 – 0.38 (wet hollows) | Linear [0..1] | R005, R010 | Highly porous dry; water gathers in crevices |

---

## 7. Phenological and life-stage progressions

- **Juvenile stage ($H = 0.8–2.5\text{ m}$):**
  - Upright excurrent growth; apical dominance maintained.
  - Bark is pale olive-gray and smooth (relief < 1.0 mm); fluting is absent or faintly indicated at root collar.
  - Leaves are slightly larger (up to 95 mm) with thinner cuticle (adaxial roughness 0.48).
- **Mature stage ($H = 6.0–11.0\text{ m}$):**
  - Deliquescent habit; trunk divides into 3–5 scaffold limbs forming broad filigree dome.
  - Pronounced muscular fluting (bosses 80–180 mm amplitude); bark develops fibrous longitudinal furrows.
  - High leaf density with planar distichous sprays casting 65–85% canopy light extinction.
- **Veteran / Senescent stage ($H = 8.0–14.0\text{ m}$):**
  - Scaffold limb dieback; hollows form in sinus crevices of fluted trunk.
  - Epiphytic moss carpets occupy north/shaded trunk flutes.
  - Snag retains dense, fibrous heartwood skeleton even after twig shedding.
