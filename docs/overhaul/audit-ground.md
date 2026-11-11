# Audit: ground, soil, litter, rocks (Gr0)

Method: static read of shaders/renderers plus baseline2 captures. No Godot run. Line numbers are as of HEAD 44def99. Items already in audit-flora / audit-performance-architecture are not repeated.

## 1. Inventory

| Layer | Drawn by | Material / textures | Notes |
|---|---|---|---|
| Terrain top | IslandRenderer `_terrainMat` (IslandRenderer.cs:131-158), TerrainMesh.cs:27 (sub=3 over hf.Step) | terrain.gdshader; Ground048 soil, Ground037 damp, Moss002, ScatteredLeaves007, Gravel022 (Bridge.cs:463). All CC0 ambientCG, 1K and 4K sets | tile_metres 0.82 (terrain.gdshader:67). Field tex (moisture/substrate/light) at ~25 cm cells drives blends |
| Blend rules | terrain.gdshader:155-218 | soil->damp by moisture smoothstep(.35,.78) (:190); leaf by `stored_litter*fbm` through `hblend` (:161,:201); moss by cover_tex (:162); gravel by sub.r (:164); silt by groundwater (:132); green "ambient cover" tint (:344-355) | Slope: only tri_blend (y vs x/z) for soil; no slope-driven material change (no scree, no exposed subsoil on slopes) |
| Anti-tiling | `sample_untiled` surface.gdshaderinc:272 | 2 taps, 1.1 rad rotation, vnoise(uv*0.23) mask; plus `drift`/`macro_shift` warp (:138-146) | Soil/damp only. Leaf layer samples `displaced_uv*1.5` plain (:201-206), moss `*2.4` plain (:267), gravel plain triplanar (:282) - these tile visibly |
| Relief | POM 24 steps within 3-5 m (:174-183); aggregate crumb normals fade 1.8-4.5 m (:246-264) | soil_hgt | Beyond 5 m ground is a flat albedo+normal only |
| Litter (3D) | LoamLitterBuilder (game/App): leaf quads, twigs, duff, 120/m2 nominal, caps 5000/6000 (:18-20), 0.072 spacing | loam_litter.gdshader, ForestFloorLitter_4K atlas, `vertex_tint` = COLOR | Leaf = alpha-cut quad of one atlas; NORMAL_MAP_DEPTH 0.15-0.30 |
| Soil crumbs / filler | SoilDetailRenderer.cs:91-96 (clods 0.011, blades, stalks), cull 6 m | fruit.gdshader / ground_cover.gdshader | Mostly flora-adjacent |
| Cut face (island side) | TerrainMesh.BuildWalls (:155, rows=8, step=hf.Step*0.25); strata.gdshader | soil/gravel/rock sets blended per layer, layer palette tint, banding_strength 0.22, POM 20 steps | Perimeter UV phase-locked (perimeter_metres) |
| Rocks | PropRenderer.RefreshProps (:104-125) one MeshInstance per rock; PropMeshes.Rock (7 ring x 6-8 sided fracture hull), RockSurfaceProfile.cs:25-45 (3 families x 6 palettes) | rock.gdshader, Rock058 only; `sample_stochastic_pbr` + 16-step POM <5 m; world-XZ projected UV (rock.gdshader:53) | Only terrain contact cue is `foot` stain (:163-166, obj_pos.y) and terrain `contact_mask` (field A) |
| Pebbles | PropRenderer MultiMesh x32 variants, `pebble_mode` (rock.gdshader:71-96) | same shader, tile 0.35 | No shadows, fade 9.5 m |
| Moss/lichen on ground | CoverageRenderer (+MossGrowth) mesh layers + terrain `cover_tex` | coverage_mat.gdshader | Owned by flora audit; only the terrain-side blend is relevant here |

## 2. Visible defects (evidence)

- Terrain smeared and stretched at close range: `guardians/armored_forest_feet.png` shows the leaf layer as vertically-stretched, rubbery, wet-looking blobs near the camera with obvious repeating oval leaf shapes. Roughly 1.5x tiled leaf atlas, with NO anti-tiling.
- One flat, saturated orange leaf quad (same image, foreground) is textureless and unlit-looking against the neighbouring tile: litter-quad albedo is much more saturated than the terrain leaf layer. It looks like a sticker.
- Litter reads as a repeating orange leaf tile from above: `visual/grazing_transition.png`, `visual/understory_eye_height.png`.
- Bare meadow/grass patches are a flat, blurry, uniform green plane with soft cloud-like blotches and no texture: `visual/rocky_outcrop.png` (left foreground), `visual/shoreline.png`, `visual/foliage_terrain.png` (bottom-left). This is the `ambient_col` tint (terrain.gdshader:344-355) at weight up to 0.72 over soil, which also suppresses soil normal detail and leaves no blades behind it at those spots.
- Cut face: broad, smooth, near-horizontal bands with a pale pasty grey-white bottom layer and a dark soot-like column on the right: `visual/overview.png` area, `visual/foliage_terrain.png` (top-left), `visual/woodland_trunks.png` area. Layers are blended with long smoothsteps (TerrainMesh.cs:225-226) and tinted from a palette, so there are no crisp strata, roots, or pebbles in the section. The rock layer shows a visibly repeating glossy cobble tile.
- Rocks: few and small in all captures. Gravel patch in `shoreline.png` right has a hard rectangular-ish border against litter. Boulders show no moss/litter/soil drift accumulation at their base other than a dark stain; no contact mesh blending.
- Flat lighting: heavy overall darkness, grey backdrop, ground mostly uniformly lit; sun direction in terrain.gdshader:106 (0.35,0.75,0.45) and rock.gdshader:37 (0.35,0.85,0.25) differ and are hard-coded rather than read from the real light.
- Far ground (> 5 m) has no relief and no macro colour breakup other than the `macro` warm/cool tint (:239-243, +-5%), which is too subtle to read.

## 3. Ranked items

### GR1 Leaf layer on terrain tiles and smears (high value)
- Defect: stretched, repeating leaf atlas on terrain (`armored_forest_feet.png`, `grazing_transition.png`).
- Cause: terrain.gdshader:201-211 samples ScatteredLeaves007 with plain `displaced_uv*1.5` (no `sample_untiled`), also driven by POM-displaced UV so it shears at grazing view; slope projections (:215-233) do not include the leaf layer at all, so it stretches on slopes.
- Fix: sample leaf with `sample_untiled` at a different scale/rotation than soil (e.g. 0.9 m, rotated); use a separate undisplaced UV (leaf is a flat layer over the soil, POM offset not needed); add a world-noise colour jitter (hue/value +-12%) per ~0.4 m cell; fade leaf weight out on slopes with tri_blend.y so it does not smear on steep faces.
- Files: game/Shaders/terrain.gdshader (+ optionally surface.gdshaderinc helper).
- GPU risk: low-med (+2 texture taps per litter pixel only where w_leaf_h > 0.01).
- Validate: `--visual-review` grazing_transition + understory_eye_height; `--guardian-review` feet shots.

### GR2 Litter quads vs terrain-layer colour/lighting mismatch
- Defect: flat, saturated orange quads on top of a muted terrain layer.
- Cause: loam_litter.gdshader:72 `ALBEDO = scanned_leaf * vertex_tint` with vertex_tint from builder COLOR (linear conversion at :27), NORMAL_MAP_DEPTH 0.30 (:75), no AO/contact darkening on top face (only back faces, :84), no fresnel/backlight match. Leaf atlas gives only ~1 shape set (LoamLitterBuilder.cs:27-31).
- Fix: desaturate/hue-shift vertex tint toward terrain leaf mean (cap saturation), add per-leaf random value/age variation, darken near the lower edge/contact using UV or per-vertex AO set by builder (leaf lifted at one edge), raise NORMAL_MAP_DEPTH to ~0.6, and add a 15-20% share of dark, decayed (duff) leaves. Needs builder tint change (outside this audit's file list: game/App/LoamLitterBuilder.cs).
- Files: game/Shaders/loam_litter.gdshader, game/App/LoamLitterBuilder.cs.
- GPU risk: none/low.
- Validate: feet shot + understory_eye_height. Owner decision: palette (autumn orange vs mixed brown/olive).

### GR3 Meadow/ambient green tint reads as flat plastic
- Defect: featureless blurred green planes.
- Cause: terrain.gdshader:344-355 mixes `ambient_col` with weight up to 0.72, reducing soil albedo variation and using only 7.5 Hz fbm; no normal or roughness contribution except `rough` to 0.9 (:355). Blades come from SoilDetailRenderer only within 6 m and at density capped 5-8/cell (SoilDetailRenderer.cs:324).
- Fix: replace flat tint with a grass/clover micro-texture (reuse Moss002 hblend at ~0.12 m, or a small procedural blade-noise normal), add dry/green variation by macro noise (0.37 and 1.9 Hz), cap weight at ~0.5 so soil crumbs show through, and fade the tint's brightness by AO. Alternative: far grass impostor (see GR7).
- Files: terrain.gdshader.
- GPU risk: low.
- Validate: rocky_outcrop, shoreline, foliage_terrain.

### GR4 Rock-ground contact (boulders sit on the ground rather than in it)
- Defect: boulders look placed; base stain only.
- Cause: PropRenderer.cs:104-125 places each rock at sim Y with scale; rock.gdshader:163-166 foot stain uses obj_pos.y only; terrain contact mask is a 25 cm field cell with PropRockContactMargin 0.06 (IslandRenderer.cs:321). No soil/litter/moss skirt, no AO on the terrain side beyond contact_mask*0.42.
- Fix: (a) sink rock origin by ~8-12% of height (check PropMeshes bottom ring at -0.38 vs placement); (b) in rock.gdshader fade into a terrain-matched colour (sample soil average color + moisture, i.e. pass `soil_tint` uniform) over the lowest 10-15 cm with noise-broken edge so there is no clean line; (c) add a small ring of crumb/pebble/leaf spawn (SoilDetailRenderer/GravelRefresh) around each rock base; (d) in terrain.gdshader boost litter/moss weight near contact (:161 already gives +45%) and add a stronger AO radius from a rock distance mask.
- Files: game/Render/PropRenderer.cs, game/Shaders/rock.gdshader, game/Shaders/terrain.gdshader (+ IslandRenderer.cs margin).
- GPU risk: low.
- Validate: rocky_outcrop.png and specimen-flora-slope shots; add a ground close-up (section 4).

### GR5 Cut face banding, tint and tile
- Defect: smooth pasty bands, pale grey bottom, repeating glossy cobble tile, dark soot column.
- Cause: TerrainMesh.cs:225-226 blend widths; strata.gdshader `banding_strength` 0.22; palette tint multiplies scan (strata.gdshader:geologic_tint); rock layer uses Rock058 at 1.1 m plain (no anti-tiling). Dark column on the right of foliage_terrain.png looks like a shadow-map or normal issue on the wall at that segment (needs a close look, possibly the tree trunk shadow).
- Fix: shorten blend to 1-3 cm with noise-warped boundary (1D noise along perimeter) so layer edges are wavy and crisp; add thin dark/light micro-bands and occasional embedded pebbles/roots via a gravel_hgt threshold; apply `sample_untiled` to the rock/gravel layer; reduce pale bottom value and add subtle moisture darkening toward water table; AO towards the top lip. 
- Files: src/Vivarium.Sim/Geometry/TerrainMesh.cs, game/Shaders/strata.gdshader.
- GPU risk: low.
- Validate: overview + foliage_terrain; add side-on cut-face shot.

### GR6 Rocks: single material, only one scan, faceted hull
- Defect: all rocks share Rock058 and a low-poly 7-ring hull with a plaster-like top; sizes small.
- Cause: rock.gdshader single texture set, `tint` (:117) nearly neutral grey, 6 palettes in RockSurfaceProfile.cs:23 only vary seeds; moss/lichen done via `lichen` and cover consumers.
- Fix: per-rock tint palette from instance seed (warm sandstone, cool slate, rust-stained) via COLOR or hashed inst_origin; add triplanar stochastic sampling (already `sample_stochastic_pbr`) with a second scale for large boulders; add rounded bevels and a soft top moss/soil lip by moving vertices by 3D noise (PropMeshes.Rock detail param) and stronger ridge wear; place more small rocks/cobbles in clusters (needs sim/placement owner decision).
- Files: game/Shaders/rock.gdshader, src/Vivarium.Sim/Geometry/PropMeshes.cs, RockSurfaceProfile.cs.
- GPU risk: low (mesh) / med if second texture scale is added (more taps).
- Owner decision: more rocks per island is a sim-layer change.

### GR7 Distance ground: no far detail, no macro variation
- Defect: ground beyond ~5 m flat (POM, crumbs and leaf quads gone).
- Cause: terrain.gdshader:174 pom_fade 3-5 m, :253 agg_fade 1.8-4.5 m; macro tint +-5% (:242); wLeaf/ambient driven by 25 cm field.
- Fix: stronger macro variation (value +-12%, hue shift, wetness patches) using 2 octaves at 0.37 and 0.11 Hz; add a cheap distance-fade detail normal at 0.3 m to keep close-feel; add roughness variation; keep litter albedo at distance by blending leaf layer weight up to ~0.5 beyond 6 m to hide where quads vanish.
- Files: terrain.gdshader.
- GPU risk: low.
- Validate: overview, meadow_observation.

### GR8 Lighting: unify sun and add ground AO
- Defect: hard-coded sun directions (terrain.gdshader:106, rock.gdshader:37 differ); `light()` custom overrides (terrain :418, rock :203) bypass shadow-softness; flat read.
- Fix: read the real DirectionalLight direction via a global shader param (set from the scene); remove duplicated custom `light()` or at least unify; add per-pixel micro AO from heights (soil_ao already used); add slight subsurface warm bounce under canopy.
- Files: terrain.gdshader, rock.gdshader, strata.gdshader, one renderer binding site (IslandRenderer.cs).
- GPU risk: none/low.
- Owner decision: touches lighting contract; may interact with the dusk/night presets.

## 4. Capture recommendation
Existing harness shots are too wide for ground work. Add a dedicated `--ground-review <ABS_DIR>` set (or reuse `--visual-review` camera presets): (1) top-down 1 m x 1 m at litter/soil/moss/gravel/meadow cells, (2) eye-level 0.3 m grazing, (3) rock base close-up at 0.5 m with and without moss, (4) cut face side-on at 1.5 m, (5) the same view at 5 m and 10 m to evidence the LOD fade. Fixed seed, fixed time of day, 1920x1080, same noon and dusk presets. Contact sheet script used for this audit: build/overhaul/Gr0/ (a.png, b.png).

## 5. Recommended dispatch order (<=8 packets)
1. Capture harness: ground close-up set (section 4). No GPU risk.
2. GR1 terrain leaf layer anti-tiling and slope fade (terrain.gdshader).
3. GR2 litter quad colour and normal (loam_litter.gdshader + LoamLitterBuilder.cs; needs owner palette decision).
4. GR3 meadow tint replacement (terrain.gdshader; after 2 to avoid merge conflicts in same file).
5. GR4 rock-ground contact (rock.gdshader, PropRenderer.cs, terrain.gdshader contact term).
6. GR5 cut face (TerrainMesh.cs + strata.gdshader).
7. GR7 far-ground macro variation (terrain.gdshader), fold GR8 sun unification if owner agrees.
8. GR6 rock variety (rock.gdshader + PropMeshes.cs); more rocks needs a sim-layer owner decision.

Serialize packets touching terrain.gdshader (2, 4, 7). Packets 5 and 6 are file-disjoint and can run in parallel with 3.

## Added by orchestrator after GR1 (2026-11-10)
**GR9. Hard-edged, stair-stepped material patches.** Grey gravel/rock patches, green moss patches and dark spots on the
terrain have polygonal, cell-aligned stepped outlines with no transition (visible in every `--ground-review` bare shot,
e.g. `litter_flat_mid_bare`, `slope_close_bare`). These dominate the ground's look more than texture tiling. Likely a
per-cell splat/coverage mask sampled without filtering or edge noise. Fix: soft, noise-warped boundaries; bilinear or
smoothed mask sampling. Ranked first among remaining GR items.
