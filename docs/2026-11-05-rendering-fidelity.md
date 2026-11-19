# Rendering Fidelity & Performance Overhaul — November 2026

This overhaul improves the photorealism ("protorealism"), anatomical and optical fidelity, and CPU/GPU efficiency of **plants**, **ground**, and **animals** across the project. All implementation waves were planned centrally, executed in parallel via `opencode-go/deepseek-v4.1-flash` subagents across non-overlapping file domains, and validated with live Vulkan Forward+ captures (`--visual-review`) and simulation invariant checks (`--boot-test`).

---

## 1. Ground, Terrain, Loam & Geological Props

### Visual Fidelity
- **True Camera-Space Terrain Parallax (`game/Shaders/terrain.gdshader`):** Fixed `v_ts` in `vertex()`, which previously derived the tangent-space view ray from `normalize(-VERTEX)` (pointing toward the island origin `(0,0,0)` instead of `CAMERA_POSITION_WORLD`). Parallax occlusion mapping and micro-shadowing now track the actual camera viewpoint.
- **Multi-Scale Macro Soil Variation & Specular AA (`game/Shaders/terrain.gdshader`, `rock.gdshader`, `strata.gdshader`):** Added a 2–8 m low-frequency macro soil/mineral variation layer (shifting organic humus warmth vs. cool silt basins and macro roughness) to break up 0.82 m texture tiling, faded screen-derivative soil crumb normals over 1.8–4.5 m, and added normal-variance roughness filtering (Specular AA) to prevent grazing-angle shimmer.
- **Continuous Porous Wetness & Unified Shoreline Caustics (`game/Shaders/terrain.gdshader`):** Wired the previously unused `dampness` gradient (`smoothstep(0.30, 0.72, moisture)`) into a single continuous dry → damp → saturated pore-darkening and capillary sheen curve, and unified the water-surface elevation formula between `fragment()` and `light()` so shoreline wetness and submerged caustics align.
- **Weathered Creek Stone, Pebble Fast-Path & Log/Litter Contact (`game/Shaders/rock.gdshader`, `log.gdshader`, `loam_litter.gdshader`, `strata.gdshader`):**
  - Full rocks now exhibit elevation-aligned mineral stratification, curvature-based facet seam weathering, rainwater top patina, and capillary foot dampness.
  - Sub-centimetre pebbles (`pebble_mode > 0.5`) bypass 15-tap stochastic texture bombing and 28-tap POM raymarching in favor of a dedicated single-tap mineral shader with per-stone stratification, grain micro-normal, and basal cavity darkening.
  - Added `depth_prepass_alpha` to `loam_litter.gdshader` so curled fallen leaves cast leaf-shaped shadows rather than rectangular cards, plus thin-margin backlight translucency.
  - Added a noise-broken turf-overhang contact shadow along the top cut rim of `strata.gdshader`.
- **Refractive Forest-Bank Water Optics & Clear Overview Atmosphere (`game/Shaders/forest_canopy_radiance.gdshaderinc`, `mobile_water_optics.gdshaderinc`, `water_stream.gdshader`, `game/Render/EnvironmentRig.cs`, `game/Camera/CameraRig.cs`):**
  - Replaced the bright milky `sky_horizon_color` reflection at grazing pond angles (`y < 0.38`) with shaded overhanging bank earth and dappled understory foliage, restored true depth scaling in `water_stream.gdshader` (`depthf * 0.90 / cosine`), and deepened Beer–Lambert absorption so ponds read as clear, depth-absorbing forest water over visible beds.
  - Eliminated global void fog above water (`Env.FogEnabled = _underwater > 0.001f`, `Env.VolumetricFogDensity = 0.0f` above water) so atmosphere is strictly confined to the bounded `_habitatMist` cylinder over the island, keeping distant overview shots crisp.

### CPU & GPU Efficiency
- **Terrain Subdivision & Sculpt Decoupling (`src/Vivarium.Sim/Geometry/TerrainMesh.cs`, `game/Render/IslandRenderer.cs`):** Reduced `TerrainMesh.BuildTop` subdivision from `sub = 6` (~313k triangles for a ±0.5 mm clamped cosmetic offset) to `sub = 3` (~78k triangles), cutting ~1.17M primitives per frame across main + shadow passes and accelerating terrain sculpt rebuilds by 4×. Decoupled `UpdateFieldTexture` from the synchronous sculpt mesh rebuild frame, lowered the habitat mask refresh interval from 40 s to 2.0 s, and rewrote `RebuildPropCaches` from `O(16,384 × all_props)` to per-prop bounding-box stamping.
- **Loam Patch Stamp Caching, Visibility Ranges & Batched Pebble Uploads (`game/Render/LoamSurfaceDetailRenderer.cs`, `SoilDetailRenderer.cs`, `PropRenderer.cs`, `src/Vivarium.Sim/Water/SurfaceLiquid.cs`):**
  - Activated localized `PatchStamp` skipping in `LoamSurfaceDetailRenderer.cs` and configured hardware `VisibilityRangeEnd` / `Self` fade on duff (7.5 m, shadows off), peds (9.0 m), and leaves/twigs (13.0 m).
  - Replaced per-candidate linear scans of all rocks/logs/gravel in `SoilDetailRenderer.Place()` with a 1 m spatial bucket grid built once per refresh.
  - Reused the 32 gravel `MultiMeshInstance3D` nodes in `PropRenderer.cs` and replaced tens of thousands of per-pebble `SetInstanceTransform` calls with a single packed `mm.Buffer` upload per bucket, with hardware distance fade (`9.5 m`) and shadow casting off.
  - Made `SurfaceLiquid._samples` publish an immutable dictionary reference atomically so concurrent background coverage workers calling `SurfaceAt` cannot race with `Publish()`.

---

## 2. Plants, Foliage, Moss & Ground Cover

### Visual Fidelity
- **Smooth Single-Sheet Arched Leaf Laminae (`src/Vivarium.Sim/Geometry/Primitives.cs`, `OrganismMeshes.cs`, `OrganismMeshes.Lacemantle.cs`, `OrganismMeshes.Cragparasol.cs`, `OrganismMeshes.Splitcrown.cs`, `OrganismMeshes.Marrowbloom.cs`, `PilotTreeMeshes.cs`):**
  - Removed duplicate coincident backface sheets in `Primitives.CurvedLeaf` (eliminating z-fighting and halving leaf triangle counts under `cull_disabled`) and fixed base/tip `UV.y` pinching.
  - Upgraded `EmbercrownLeaflet`, `LacemantleLacyLeaf`, `CragparasolLeaf`, and `SplitcrownLeaflet` from coarse 2–3 segment polygonal boxes/diamonds to smooth 8–9 row arched single-sheet laminae with midrib V-gutters, marginal undulation, acuminate drip tips, and un-pinched UVs.
- **3-Tier Botanical Venation & Cuticle Relief (`game/Shaders/leaf.gdshader`, `flora.gdshader`, `living_leaf_optics.gdshaderinc`):**
  - Synthesized a tapering primary midrib, arched pinnate secondary lateral veins, and bullate inter-vein mesophyll pillows in `leaf.gdshader` and `flora.gdshader`, driving 3D cotangent normal slopes (channeled adaxial midrib vs. proud abaxial keel), vascular bundle pigmentation, basal petiole collar AO, and backlit optical thickness (`tissue_tau`).
- **Fungal Pileus/Stipe Realism & Natural Micro-Breeze (`game/Shaders/flora.gdshader`, `leaf.gdshader`, `ground_cover.gdshader`):**
  - Added radial pileus striation, hygrophanous moisture zoning, viscid cuticle micro-normals, silky longitudinal stipe fibrils, basal mycelium darkening, and fleshy subsurface scattering (`surface_mode == 3`) for mushrooms.
  - Wired per-vertex flexibility and attachment data into a subtle, deterministic-capture-safe micro-breeze in `leaf.gdshader`, `flora.gdshader`, and `ground_cover.gdshader`.
  - Enabled curated 3D moss gametophore shoot clusters on established moss cushions at default tier 1 (`CoverageRenderer.cs`, `CoverageRenderer.MossGrowth.cs`) and enhanced close-up moss optical porosity and velvet rim scattering in `coverage_mat.gdshader`.

### CPU & GPU Efficiency
- **Per-Plant Placement Cache & De-Quadratified Vein Pass (`game/Render/OrganismRenderers.cs`):** Cached per-plant `GroundHeight`, `SurfaceNormal`, `UprightBasis`, and species definitions across camera-driven refreshes, grouped vein plants by species in a single pass, and capped active night bioluminescent `OmniLight3D` nodes (`MaxActiveLuminousLights = 28`) with node pooling and hardware distance fade (`6.5–9.0 m`).
- **Species-Level LOD Calibration & Base-Cloned Fruit Meshes (`src/Vivarium.Sim/Geometry/OrganismMeshes.VisualBreakFlora.cs`, `OrganismMeshes.cs`, `FloraLeaf.cs`):**
  - Keyed `LodScaleCache` by species/tier rather than per-morph seed so the 9-step binary search runs once per species tier across all morphs.
  - Added `OrganismMeshes.FloraFruitingFromBase` so fruiting variants clone the already-built `fullMesh` and append fruit clusters directly instead of regenerating the entire plant mesh from scratch.
  - Enabled distance-based flora LOD (`_reducedDistantPlants = true`) and calibrated `FloraDetail` thresholds (`FullPixels = 210, MidPixels = 75, DistanceMid = 3.0 m, DistanceFar = 5.5 m`) to match the 8 m island scale.
- **Distance Shader LODs & Turf Shadow Culling (`game/Shaders/coverage_mat.gdshader`, `pilot_tree.gdshader`, `fruit.gdshader`, `game/Render/AmbientGroundCoverRenderer.cs`):** Gated expensive multi-octave Voronoi/FBM moss/lichen noise and cylindrical bark POM by camera distance, disabled shadow casting on 2 cm ambient turf filler blades (which now use basal contact AO), and added hardware `VisibilityRangeEnd` fades across coverage tiles, turf, and bank-fringe patches.

---

## 3. Animals (Fauna & Guardians)

### Visual Fidelity
- **Eliminated Fauna Pattern Flicker (`game/Shaders/fauna.gdshader`):** Derived procedural skin/marking seed from the stable packed individual identity in `INSTANCE_CUSTOM.x` across all 17 species and body styles instead of falling back to `float(INSTANCE_ID)` when MultiMesh slots compact during frustum culling.
- **Organic Body Cross-Sections & Smooth Limb Sockets (`src/Vivarium.Sim/Geometry/OrganismMeshes.cs`):** Flattened ventral belly contours (`×0.84`), widened lower-lateral flanks (`+6%`), and smoothed proximal shoulder/hip socket tapers across the first three appendage rings.
- **Active Amphibian/Mollusk Mucus, Corneal Eye Domes & Thin-Tissue SSS (`game/Render/FaunaBodyProfiles.cs`, `game/Shaders/fauna.gdshader`):**
  - Activated `tissue_mucus` (`0.80–0.90`) for amphibians, mollusks, and annelids (`FaunaBodyProfiles.cs`), enabling the dual-layer hydrated skin + clearcoat mucus film in `fauna.gdshader`.
  - Re-enabled and upgraded vertebrate/amphibian and arthropod eye optics with a convex corneal normal dome, species-specific pigmented iris rings (golden-amber flecked toad, bronze/copper salamander) with radial filaments and pupils, `CLEARCOAT = 1.0`, and an analytic sky/canopy catchlight.
  - Added thin-tissue `BACKLIGHT` subsurface scattering for toes, fins, wings, and lateral body margins.
- **Guardian Fur, Keratin, Capillary Wetness & Eye Catchlights (`game/Shaders/guardian_surface.gdshader`, `game/Render/GuardianRenderer.cs`):**
  - Bound imported `AOTexture` and `EmissionTexture` maps, rendered `EyeHighlight` meshes as unshaded additive corneal catchlights (plus an analytic sky-dome catchlight in `guardian_surface.gdshader` mode 4), added inter-strand root AO (`mix(0.42, 1.0, strand_height)`) and grazing velvet scattering to fur/plumage, added growth-ring striae and edge translucency to keratin scales, and replaced the flat waterline with noise-modulated capillary wicking and ventral/paw mud darkening.

### CPU & GPU Efficiency
- **Analytic Vertex Spine Derivative & Distance Noise LOD (`game/Shaders/fauna.gdshader`):** Replaced 3 separate `spine_wave` vertex evaluations with an analytic `spine_wave_slope()` returning `(wave, dWave/dx)` in one `sin`/`cos` pair, and faded high-frequency skin/pore/grain noise between 2.5–4.5 m with Specular AA.
- **Frustum-Gated Guardian Modifiers, Cached Contact Index & Zero-Allocation Loops (`game/Render/GuardianRenderer.cs`, `GuardianAppendageModifier.cs`, `GuardianTerrainLegModifier.cs`, `GuardianVegetationContact.cs`, `GuardianSkinContact.cs`, `OrganismRenderers.cs`):**
  - Disabled all `SkeletonModifier3D` solvers when the guardian is outside the camera frustum, cached per-frame guardian shader uniforms, and hoisted `LimbContactBones`.
  - Removed the unconditional `finally { _contactIndexDirty = true; }` in `GuardianVegetationContact.cs` so the spatial index only rebuilds when plants move or change.
  - Pooled scratch arrays/collections in `GuardianSkinContact.Evaluate`, `GuardianAppendageModifier`, `GuardianTerrainLegModifier`, and `FaunaRenderer._Process`, and removed inner-loop `Stopwatch.GetTimestamp()` overhead.

---

## 4. Measured Validation (`--visual-review` & `--boot-test`)

Validated on Vulkan 1.4.312 Forward+ (NVIDIA GeForce GTX 1080, `1600×900`, `Quality = 1`, dynamic shadows + SDFGI + SSAO enabled, `seed = 804584441`, `Streamside Hollow`):

| Capture View | Metric | Baseline (Pass 1) | Final | Improvement |
|---|---|---:|---:|---:|
| **`overview`** | Median frame time / FPS | 9.14 ms (109.5 FPS) | **7.27 ms (137.6 FPS)** | **-20.5% ms (+25.7% FPS)** |
| | Draw calls / Primitives | 373 / 2.60M | **278 / 1.60M** | **-25.5% draws / -38.6% tris** |
| **`foliage_terrain`** | Median frame time / FPS | 21.43 ms (46.7 FPS) | **17.21 ms (58.1 FPS)** | **-19.7% ms (+24.5% FPS)** |
| | Draw calls / Primitives | 1,050 / 15.31M | **814 / 8.87M** | **-22.5% draws / -42.1% tris** |
| **`grazing_transition`** | Median frame time / FPS | 28.11 ms (35.6 FPS) | **22.14 ms (45.2 FPS)** | **-21.2% ms (+27.0% FPS)** |
| | Draw calls / Primitives | 1,142 / 15.41M | **958 / 10.30M** | **-16.1% draws / -33.2% tris** |

- `VIVARIUM_BOOT_OK`: save/load roundtrip passed with `0` invariant violations.
