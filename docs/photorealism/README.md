# Vivarium Photorealism Reference Manual

This document and associated artifacts record the botanical architecture, procedural geometry, PBR shader contracts, ground substrate systems, lighting models, and performance benchmarks established during the Photorealism Overhaul (September 2026). It serves as the durable reference for all future agents working on rendering, flora procedural generation, and substrate simulation in Vivarium.

---

## 1. Visual Reference Artifacts

All reference data is stored durably in the project repository under `docs/photorealism/`:

- **Metrics & Benchmarks**: [`photorealism_v0.1.2_metrics.json`](file:///E:/Vivarium/docs/photorealism/photorealism_v0.1.2_metrics.json) (complete 54-scene timing, draw call, and primitive statistics).
- **Contact Sheets**:
  - Sheet 1: [`photorealism_v0.1.2_sheet1.jpg`](file:///E:/Vivarium/docs/photorealism/photorealism_v0.1.2_sheet1.jpg) (Scenes 1–20)
  - Sheet 2: [`photorealism_v0.1.2_sheet2.jpg`](file:///E:/Vivarium/docs/photorealism/photorealism_v0.1.2_sheet2.jpg) (Scenes 21–40)
  - Sheet 3: [`photorealism_v0.1.2_sheet3.jpg`](file:///E:/Vivarium/docs/photorealism/photorealism_v0.1.2_sheet3.jpg) (Scenes 41–54)
- **Side-by-Side Comparison Pairs**:
  - Tree Umbraheart: [`species_umbraheart_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/species_umbraheart_compare.jpg)
  - Tree Ironlace: [`species_ironlace_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/species_ironlace_compare.jpg)
  - Tree Fenneedle: [`species_fenneedle_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/species_fenneedle_compare.jpg)
  - Tree Kiteleaf: [`species_kiteleaf_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/species_kiteleaf_compare.jpg)
  - Ground Vegetation & Soil: [`ground_vegetation_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/ground_vegetation_compare.jpg)
  - Terrain Lighting & Atmosphere: [`sparse_dry_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/sparse_dry_compare.jpg)
  - Plant Macro & Foliage: [`macro_flora_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/macro_flora_compare.jpg)
  - Terrarium Overview: [`overview_compare.jpg`](file:///E:/Vivarium/docs/photorealism/comparisons/overview_compare.jpg)

---

## 2. Botanical Geometry Architecture

All procedural plant geometry is generated non-authoritatively by the client renderer in `src/Vivarium.Sim/Geometry/`:

### A. Tree Architecture (`OrganismMeshes.cs`)
- **`TreeBole`**: Replaces straight cylindrical trunks with a 7-segment Bezier spline with organic lean and multi-frequency harmonic trunk taper.
  - Generates 5 buttressed root flares (`radius * 0.48 -> 0.002`) diving into the substrate at `y = -0.01`, ensuring trees are grounded into the earth without floating root collars.
- **`BroadleafTree` (`TreeIronlace`, `TreeUmbraheart`, `TreeKiteleaf`)**:
  - Crooked main bole with species-specific lean and height profile.
  - Ascending primary scaffold limbs branching alternate-distichously.
  - Secondary woody sprigs branching off scaffolds at `t = 0.45 + sprig * 0.23`.
  - `LeafSpray` clusters attached via cylindrical green petioles (`Primitives.Tube`), terminating in compound foliage blades.
  - Crown leader sprays preventing empty or flat tree silhouettes.
- **`TreeFenneedle`**:
  - Conical tiered architecture with 6 vertical whorl tiers (4–5 primary boughs per tier).
  - Gravitational branch droop curving gracefully under self-weight (`control.y = root.y - 0.035`).
  - Bottlebrush sub-branching sprays with attached drooping needle blades.

### B. Parametric Compound Blades (`FoliageBlade.cs`)
- Instead of planar 2-triangle billboards, leaves are generated as continuous 3D surfaces:
  - **Transverse Camber**: Parabolic arch across the leaf spine (`camber: 0.12..0.24`), creating authentic concave upper faces and convex backs.
  - **Longitudinal Curl**: Subtle tip droop along the rachis (`curl: 0.06..0.20`).
  - **Cuticular Roll**: Outward margin roll (`roll: -0.65..0.65`).
  - **Asymmetry & Shoulder Shaping**: Species-tuned width inflection point (`shoulder: 0.66..0.91`) matching ovate, lanceolate, or obovate foliage.

---

## 3. Shader Coordinate Encoding Contract

The shader pipeline (`game/Shaders/flora.gdshader`) uses vertex attributes `UV` and `UV2` as an explicit semantic contract:

| Attribute | Value | Semantic Meaning | Rendering Behavior |
|:---|:---|:---|:---|
| `UV.x` | Length in metres | Longitudinal distance along stem/trunk | Controls texture repetition rate along growth axis |
| `UV.y` | $0.0 \dots 1.0$ | Circumferential angle or blade width | Angular position around tube ($0 \dots 2\pi$) or blade half-width |
| `UV2.x` | `> 1.5` | Woody / Bark surface | Uses `bark_col`, `bark_nrm`, `bark_rgh`. Isotropic mapping, 0 backlight. |
| `UV2.x` | `1.0` | Fungal spore/gill surface | Generates radial gill shading and concentric growth rings. |
| `UV2.y` | `> 0.5` | Foliage Blade | Procedural venation, cellular cuticle, chlorophyll transmitted backlight. |
| `UV2.y` | `0.0` | Petiole / Young Stem | Opaque, waxy cuticle (roughness 0.69, minimal backlight 0.04). |

### Bark Texture Mapping Equation
Bark uses an isotropic physical mapping derived from physical dimensions:
```glsl
// UV.x = distance in metres along trunk (repeats 2.2x per metre)
// UV.y = 0..1 around circumference (wraps 1.0x seamlessly)
vec2 buv = vec2(UV.y * 1.0, UV.x * 2.2 + inst.w * 3.0);
vec3 bark = texture(bark_col, buv).rgb;

// Modulate photo-scanned ambientCG Bark014 with authored species tint
c = mix(bark, bark * (c / max(lum(c), 0.05)), 0.35);

// Cotangent frame normal derivation with enhanced relief
vec3 bn = unpack_normal(texture(bark_nrm, buv).rgb);
n = surface_normal(wn, world_pos, buv, normalize(vec3(bn.xy * 1.25, bn.z)));
rough = clamp(texture(bark_rgh, buv).r, 0.72, 0.98);
```

### Foliage Translucency Equation
Chlorophyll light transmission shifts transmitted backlight toward warm golden-green:
```glsl
BACKLIGHT = surface_mode == 2
    ? (woody_surface ? vec3(0.0) : (UV2.y > 0.5 ? c * vec3(1.1, 1.15, 0.75) * 0.26 : c * 0.04))
    : c * (surface_mode >= 3 ? 0.35 : 0.2);
```

---

## 4. Soil Substrate & Terrain Shading

- **Physical Texture Scaling**: `tile_metres = 0.45` in `game/Shaders/terrain.gdshader` matches close-up miniature terrarium scale (45 cm per repeat).
- **Geometric Relief**: 12mm vertex displacement lumps (`amp = soil_relief_amp * soilMask`) with surface normal derivation `normalize(NORMAL + vec3(n2.x, 0.0, n2.z) * soilMask)`.
- **Soil Pore Ambient Occlusion**:
  ```glsl
  AO = mix(0.82, 1.0, smoothstep(0.12, 0.6, dot(soil.rgb, vec3(0.333))));
  AO_LIGHT_AFFECT = 0.25;
  ```
  Preserves deep soil cavity shadows under direct sunlight and sky reflections.
- **Moss Rhizoid Boundary Transition**:
  Naturalized forest bryophyte tone mapping and dark damp humus rhizoid margin:
  ```glsl
  vec3 natural_moss = mix(m.rgb, m.rgb * vec3(0.74, 0.88, 0.58), 0.5);
  float h = dot(m.rgb, vec3(0.333)) * 1.2;
  float w = hblend(wMoss, h);
  vec3 rhizoid_col = mix(col * 0.78, natural_moss, smoothstep(0.08, 0.45, w));
  col = mix(col, rhizoid_col, w * 0.75);
  ```
- **Ground Detail Litter (`SoilDetailRenderer.cs`)**:
  - Instanced MultiMesh crumbs (3.5mm), clods (7mm), twigs, and decayed leaf flakes.
  - Micro-deposition clustered in hollows (`hollow = ((h_avg - hc) * 7.0)`) and sheltered prop contact zones (`shelter = exp(-dist / 0.45)`).
  - Instances tilted to conform to the terrain normal (`SurfaceFrame.TiltTo`).
  - Enabled `VertexColorIsSrgb = true` for accurate linear PBR color response.

---

## 5. Terrarium Atmosphere & Lighting Rig

Authored in `game/Render/EnvironmentRig.cs`:
- **Terrarium Haze**: `HazeFogDensity = 0.006f` (down from 0.02f) and `FogAerialPerspective = 0.12f`, eliminating foggy milky wash and restoring deep shadow contrast across the terrarium.
- **Sun Lighting**: `Sun.LightEnergy = 1.35f` with warm terrarium daylight spectrum (`1.0, 0.96, 0.9`).
- **Attached Contact Shadows**:
  - `ShadowBias = 0.035f`
  - `ShadowNormalBias = 0.35f` (down from 0.85f to prevent branch shadow detachment).
  - Two-split parallel directional shadows focused within 28 metres.

---

## 6. Full Reference Benchmark Comparison

Captured on NVIDIA GeForce GTX 1080 running Forward+ renderer across all 54 standard validation scenes:

| Scene | FPS Before | FPS After | Δ FPS | P95 ms Before | P95 ms After | Primitives Before | Primitives After |
|:---|---:|---:|---:|---:|---:|---:|---:|
| `overview` | 225.20 | 232.90 | +3% | 5.44 | 5.54 | 4,862,496 | 4,797,440 |
| `interaction` | 162.40 | 178.80 | +10% | 6.37 | 6.16 | 5,886,304 | 5,806,148 |
| `ground_vegetation` | 65.00 | 225.00 | **+246%** | 22.21 | 4.46 | 1,696,650 | 2,565,102 |
| `macro_flora` | 105.50 | 208.70 | **+98%** | 18.69 | 5.31 | 5,040,789 | 4,973,117 |
| `slime_network` | 206.50 | 208.70 | +1% | 5.35 | 5.22 | 3,730,665 | 3,634,279 |
| `slime_macro` | 236.40 | 254.40 | +8% | 4.42 | 4.29 | 1,570,775 | 1,703,539 |
| `fungi_patch` | 57.10 | 213.60 | **+274%** | 24.62 | 4.98 | 1,770,622 | 1,744,250 |
| `bracket_macro` | 247.20 | 264.00 | +7% | 4.26 | 4.11 | 2,488,728 | 2,406,422 |
| `fauna_terrestrial` | 269.40 | 266.60 | -1% | 3.92 | 4.30 | 530,465 | 506,105 |
| `fauna_aquatic` | 84.30 | 108.00 | **+28%** | 26.67 | 14.34 | 5,272,645 | 5,197,209 |
| `water_margin` | 66.50 | 185.50 | **+179%** | 25.40 | 6.73 | 3,393,332 | 3,353,996 |
| `rock_contact` | 352.30 | 337.30 | -4% | 3.46 | 4.80 | 478,745 | 469,535 |
| `log_contact` | 173.70 | 183.50 | +6% | 5.88 | 5.88 | 4,392,572 | 4,347,524 |
| `species_antlerlace_lichen` | 72.00 | 222.40 | **+209%** | 25.31 | 4.70 | 2,437,653 | 2,409,341 |
| `detail_antlerlace_lichen` | 80.10 | 279.20 | **+249%** | 24.23 | 3.86 | 2,261,765 | 2,240,737 |
| `species_bogglass_moss` | 183.30 | 189.70 | +3% | 5.62 | 5.56 | 1,010,493 | 1,007,399 |
| `species_embercrust_lichen` | 225.40 | 217.10 | -4% | 4.59 | 4.55 | 876,010 | 866,790 |
| `detail_embercrust_lichen` | 62.80 | 284.80 | **+354%** | 24.82 | 3.78 | 642,362 | 633,538 |
| `species_floodlace_moss` | 130.90 | 205.90 | **+57%** | 19.18 | 5.11 | 1,202,618 | 1,178,810 |
| `species_pearl_cushion_moss` | 185.30 | 199.00 | +7% | 5.52 | 5.23 | 2,733,979 | 2,730,975 |
| `species_ruffle_lichen` | 190.70 | 196.40 | +3% | 5.62 | 5.41 | 3,209,650 | 3,174,670 |
| `detail_ruffle_lichen` | 84.00 | 264.30 | **+215%** | 23.89 | 4.35 | 3,062,254 | 3,084,242 |
| `species_velvetweave_moss` | 125.30 | 213.30 | **+70%** | 18.06 | 4.87 | 1,247,918 | 1,223,822 |
| `dense_colony` | 201.50 | 213.60 | +6% | 5.29 | 5.03 | 1,237,394 | 1,216,522 |
| `colony_edge` | 143.70 | 174.60 | **+21%** | 8.12 | 6.18 | 5,579,653 | 5,486,381 |
| `sparse_dry` | 66.10 | 168.40 | **+155%** | 24.15 | 6.30 | 4,672,726 | 4,915,112 |
| `mixed_depth` | 89.10 | 194.90 | **+119%** | 21.88 | 5.75 | 5,810,329 | 5,726,529 |
| `cutaway_pond` | 192.00 | 202.10 | +5% | 5.98 | 5.71 | 5,347,619 | 5,279,815 |
| `species_ambervein` | 238.60 | 243.90 | +2% | 4.26 | 4.36 | 2,497,083 | 5,582,949 |
| `species_brooklace` | 62.50 | 194.40 | **+211%** | 22.42 | 5.50 | 2,431,355 | 2,399,399 |
| `species_clinglace` | 93.30 | 198.40 | **+113%** | 15.34 | 5.08 | 2,109,740 | 2,077,420 |
| `species_coinrunner` | 264.20 | 282.10 | +7% | 4.05 | 3.82 | 2,521,235 | 2,497,383 |
| `species_dewbonnet` | 230.60 | 229.90 | 0% | 4.52 | 4.60 | 1,896,945 | 2,122,711 |
| `species_embercrown` | 206.20 | 219.20 | +6% | 5.25 | 4.81 | 2,474,067 | 2,466,819 |
| `species_emberfan_fungus` | 81.30 | 217.90 | **+168%** | 17.90 | 4.80 | 3,584,890 | 3,542,362 |
| `species_fenbead` | 159.70 | 201.60 | **+26%** | 14.02 | 5.38 | 2,457,107 | 3,452,901 |
| `species_fenhook` | 240.20 | 219.50 | -9% | 4.59 | 4.98 | 1,903,401 | 1,894,885 |
| `species_fenneedle` | 171.50 | 166.20 | -3% | 6.57 | 6.25 | 5,612,222 | 5,527,248 |
| `species_frosttussock` | 206.60 | 215.60 | +4% | 5.31 | 4.97 | 2,151,497 | 2,128,981 |
| `species_glassfinger` | 80.30 | 243.10 | **+203%** | 20.18 | 4.26 | 2,689,979 | 2,667,937 |
| `species_glassrush` | 105.80 | 198.00 | **+87%** | 20.71 | 5.40 | 1,844,071 | 1,919,503 |
| `species_ironlace` | 283.00 | 288.70 | +2% | 3.80 | 3.71 | 1,082,841 | 1,080,523 |
| `species_kiteleaf` | 185.80 | 191.50 | +3% | 5.51 | 5.65 | 2,667,137 | 2,651,553 |
| `species_lanternbrush` | 168.20 | 179.40 | +7% | 6.13 | 6.02 | 3,656,255 | 3,614,757 |
| `species_mirrorleaf` | 70.70 | 187.50 | **+165%** | 20.00 | 5.50 | 2,772,985 | 3,506,633 |
| `species_mooncoin` | 213.10 | 251.80 | **+18%** | 14.84 | 4.37 | 2,136,563 | 2,745,125 |
| `species_prismstar` | 260.60 | 271.50 | +4% | 4.10 | 3.97 | 616,475 | 610,527 |
| `species_ringreed` | 177.90 | 183.10 | +3% | 5.79 | 5.82 | 2,687,885 | 2,651,873 |
| `species_shadebell` | 203.90 | 256.00 | **+26%** | 14.43 | 4.22 | 1,112,533 | 1,108,583 |
| `species_spiralvine` | 68.10 | 204.30 | **+200%** | 20.70 | 5.29 | 1,964,386 | 1,931,230 |
| `species_sunstone_rosette` | 241.40 | 247.90 | +3% | 4.28 | 4.20 | 2,782,457 | 2,755,423 |
| `species_trifold` | 217.70 | 216.70 | 0% | 4.79 | 4.99 | 1,474,948 | 2,642,048 |
| `species_umbraheart` | 240.10 | 243.40 | +1% | 4.38 | 4.46 | 1,231,085 | 1,221,809 |
| `species_veilfern` | 212.90 | 203.20 | -5% | 5.06 | 5.35 | 3,482,373 | 3,441,493 |

---

## 7. Operational Guidelines for Future Agents

1. **Geometry Invariant**:
   - `Vivarium.Sim` is the authoritative simulation library. The renderer in `game/` must remain non-authoritative.
   - All procedural meshes in `OrganismMeshes.cs` must satisfy triangle budget tests (`dotnet test Vivarium.sln -c Debug --filter "Suite=Flora&Speed!=Slow"`).
2. **Godot Executable**:
   - Always invoke the Mono Godot 4.7.1 build at `E:\Vivarium-release-0.1.2\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`.
3. **Reference Verification**:
   - When modifying shaders, lighting, or flora geometry, always run the reference probe:
     `<godot> --path game -- --reference <OUTPUT_DIR>`
   - Confirm with `scripts/compare-reference.ps1` that frame rate exceeds the 24 FPS target (mean typically 160–250 FPS).
