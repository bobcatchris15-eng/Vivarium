# S2 / S2b - living soil cube (substrate mesher + renderer + material)

Files:
- `src/Vivarium.Sim/Repr/Substrate/LayeredSubstrate.Sampling.cs`
- `src/Vivarium.Sim/Repr/Substrate/Mesh/SubstrateMesher.cs`
- `src/Vivarium.Sim/Repr/Substrate/Mesh/SubstrateMeshSelfTests.cs`
- `game/Repr/Substrate/SubstrateRenderer.cs`
- `game/Repr/Substrate/SoilCubeDemo.cs`
- `game/Shaders/repr_substrate.gdshader`

Captures/logs:
- Before (`S2`): `build/s2b/before/soil_cube/`
- After (`S2b`): `build/s2b/after/soil_cube/`
Run: `tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --repr-review E:\Vivarium\build\s2b\after --repr-demo soil_cube --repr-env autoexp=0`.
**Use `autoexp=0`**: with auto exposure the dark wet cube is re-exposed and looks identical to the dry one.

## Method & S2b Remediation
- **Renderer (`SubstrateRenderer.cs`)**: `0.5 m` tiles, 3 levels (`0.01 / 0.03 / 0.10 m`), coarsest level whose vertex step projects `<= 9 px`, `1.35x` hysteresis. Builds on `Task.Run`, arrays packed on the worker, 1 commit/frame, levels kept. Own `DirtyLog` cursor; `Geometry|SoilMaterial` rebuilds only intersecting tiles (old mesh stays until commit); `Water` records upload a `20x20` `RGBAH` texture (`sat`, `film mm`, `standing mm`) and never rebuild geometry; wall `CUSTOM0` refreshed via `RefreshWater` at most once / `2 s` / tile. Expanded `Parts` from `4` to `5` (`Ground`, `Cut`, `Rock`, `Stones`, `Litter`) with `water_enabled` active on `Ground`, `Rock`, `Stones`, and `Litter`, and shadow-casting disabled on the 1.5 mm surface `Litter` layer.
- **Matrix Sampling (`LayeredSubstrate.Sampling.cs`)**: Added `SampleMatrix(Vec3 p, bool surfaceSoilOnly = false)` so terrain and wall meshing sample the continuous soil/rock matrix without baking buried stone spheres (`InStone`) as pale grey bedrock halos into the surrounding soil mesh. When `surfaceSoilOnly` is true (un-excavated ground surface right up to rock outcrop contacts), keeps the sample in the organic topsoil horizons (`Litter`/`Humus`) with runoff moisture enrichment and smooth `1.2 cm` cross-horizon attribute blending so rock contacts and thin wedges never bleach into a white/grey sub-soil collar.
- **Mesher (`SubstrateMesher.cs`)**:
  - Added `SubstrateMeshSet.Litter` (`BuildLitter`) emitting deterministic 3D curled decaying leaf blades (9 vertices, 8 triangles, ovate silhouette with midrib arch and curled tip/margins) and 3D woody twigs (6 vertices, 4 triangles) clustered in natural drifts on undisturbed organic soil at fine/medium LOD (`_step <= 0.035`).
  - Added mesoscale clod and root-hummock relief (`0.14 m` and `0.28 m` octaves) to `GroundRelief` and crag relief to `TopY` on `Rock`, contour-aligned quad diagonal splitting in `BuildTops`, 2-scale central-difference normals in `SurfaceNormalAt`, and 9-point local rim beveling in `HeightAt` near excavations (`r = 1.4 * _step`).
  - Fixed `DigStrip` so sub-surface excavation horizon strips are clamped strictly below `Math.Min(ya, yb) - 0.002` (eliminating vertical triangle spikes poking above the excavated bowl rim) while preserving full horizon-boundary wall strips inside the dig bounds.
  - Upgraded `BuildStones` to `Icosphere(2)` at `_step <= 0.015`, sampling surrounding soil matrix moisture (`SampleMatrix(c).Saturation`) and per-stone mineralogy variation.
- **Shader (`repr_substrate.gdshader`)**:
  - Added `kind == 4` (`is_litter3d`) shading for 3D curled fallen leaves (midrib, organic leaf-tissue mottle, weathered margin darkening, contact AO) and woody twigs.
  - Replaced connected Worley cell-border (`wa.y - wa.x`) darkening and polygonal desiccation webs on organic topsoil with convex rounded granule distances (`wa.x`, `wb.x`) and discrete triple-junction inter-crumb pore pockets.
  - Replaced Voronoi `edge`-driven `hollow` wet pooling with rotated quintic multi-scale micro-depressions (`microDepr`) and domain-warped `water_tex` UV lookups (`P.xz + wwarp`), eliminating both the backwards dark Voronoi polygon web on wet macro shots and the 5 cm x 5 cm rectangular column puddle artifact.
  - Replaced closed-loop `vnoise3 == 0.5` cartoon black fracture rings on bedrock with continuous world-space rock base color (`is_rock && !is_stone`) and directional bedding-parting / conjugate joint crevices.
  - Replaced `dFdx`/`dFdy` screen-space `bump_normal` (which caused 2x2 pixel quad striping) with anti-tiled rotated normal maps (`set_nrm`) + smooth rotated clod slopes, and added a 1.8 cm orthographic shadow-pass depth offset on `Ground`/`Litter` to eliminate 6 mm directional shadow-map texel acne in macro views.

## Verification & Numbers (`S2` Before vs `S2b` After, fixed exposure `autoexp=0`)
- Self-tests (`experiments/representation_rebuild/selftest/ReprSelfTest.csproj`):
  - `mesh.*`: **10/10 PASS** (`mesh.border`, `mesh.cost` [step 0.01: `123,029` tris, `477 ms`, `88.0 B/vtx`; step 0.03: `15,833` tris, `80 ms`; step 0.10: `1,744` tris, `38 ms`], `mesh.determinism`, `mesh.dig`, `mesh.export`, `mesh.refreshwater`, `mesh.rockjoin`, `mesh.stones`, `mesh.valid`, `mesh.wallmatch`).
  - `substrate.*`: **18/18 PASS** (`substrate.causal`, `substrate.contact`, `substrate.continuity`, `substrate.determinism`, `substrate.dig`, `substrate.dig-bowl`, `substrate.dt-invariance`, `substrate.fractions`, `substrate.horizon-order`, `substrate.outcrop`, `substrate.performance`, `substrate.profile`, `substrate.rain24`, `substrate.roots`, `substrate.stones`, `substrate.water-budget`, `substrate.water-closed`, `substrate.wet-dry`).

| Shot (`soil_cube`, `autoexp=0`) | `S2` Before GPU (ms) | `S2b` After GPU (ms) | Delta GPU | Draws (`Before -> After`) | Prims (`Before -> After`) | Visual Outcome |
|---|---:|---:|---:|---:|---:|---|
| `whole_dry` | `13.60` | `8.52` | **-37.4%** | `42 -> 52` | `329k -> 359k` | Clean outcrop contact (no white collar), 3D litter drifts, rich organic humus/mineral/bedrock cut faces |
| `whole_wet` | `13.30` | `9.44` | **-29.0%** | `42 -> 52` | `329k -> 359k` | Natural organic depression puddle beside outcrop (no 5 cm x 5 cm square column artifact), darkened wet horizons |
| `top_dry` | `14.70` | `9.16` | **-37.7%** | `36 -> 52` | `327k -> 359k` | Material-led soil clods + 3D curled leaves & woody twigs; zero polygonal crack web |
| `top_wet` | `14.50` | `10.19` | **-29.7%** | `36 -> 52` | `327k -> 359k` | Glossy micro-depression water film and organic puddle; zero Voronoi grid lines |
| `top_macro_dry` | `12.40` | `8.34` | **-32.7%** | `18 -> 20` | `157k -> 161k` | 3D curled leaf blades & twigs over granular humus; zero 6 mm shadow-acne bands or 2x2 quad striping |
| `top_macro_wet` | `12.70` | `9.28` | **-26.9%** | `18 -> 20` | `157k -> 161k` | Wet sheen in micro-hollows and damp 3D leaves; zero backwards dark Voronoi cell borders |
| `wall_dry` | `14.20` | `9.22` | **-35.1%** | `39 -> 52` | `329k -> 359k` | Distinct litter/humus/mineral/weathered/bedrock horizons, fine wavy rootlets, no cartoon rock loops |
| `wall_macro_dry` | `13.30` | `8.92` | **-32.9%** | `39 -> 52` | `329k -> 359k` | Organic horizon fingering and cylindrical rootlets with contact AO |
| `outcrop_dry` | `14.80` | `9.08` | **-38.6%** | `38 -> 52` | `328k -> 359k` | Stratified metasedimentary rock outcrop with zero white collar, zero sawtooth base triangles, and 3D litter/cobble |
| `outcrop_wet` | `15.20` | `10.35` | **-31.9%** | `38 -> 52` | `328k -> 359k` | Wet specular rock face and runoff-moistened humus contact |
| `stone_dry` | `13.40` | `9.05` | **-32.5%** | `37 -> 34` | `324k -> 287k` | Smooth `Icosphere(2)` embedded cobble with damp soil-contact collar |
| `stone_wet` | `13.30` | `9.58` | **-28.0%** | `37 -> 34` | `324k -> 287k` | Glistening wet cobble matching surrounding soil matrix saturation |
| `seam_seam` | `12.30` | `7.77` | **-36.8%** | `42 -> 52` | `260k -> 285k` | Mixed LOD `[1 0 0 0]` (`97.6k` tris) with zero cracks across tile boundaries |
| `far_far` | `8.50` | `4.53` | **-46.7%** | `28 -> 36` | `28k -> 30k` | Coarse LOD `[1 1 1 1]` (`15.8k` tris) at `4.53 ms` GPU |
| `hole_dug` | `14.60` | `9.38` | **-35.8%** | `20 -> 22` | `158k -> 171k` | Beveled excavation rim (zero `DigStrip` spikes), warm moist earth walls, two exposed 3D cobbles, glossy bottom pool; only 1 of 4 tiles requested rebuild |
