# L1 - cached geometry occlusion + cheap live sun (light_probe)

## What was built
- `src/Vivarium.Sim/Repr/Light/OcclusionVolume.cs` (`ILightTransportField`): dense voxel grid, fixed-point extinction (Add/Remove exactly reversible), `AddTriangles(sourceId, positions, indices, 3x4, opacityPerArea)`, `AddHeightfield`, `Remove(sourceId)`, `Bake/BakeAll/Invalidate` (9 rays: zenith, 4 at 55 deg, 4 at 20 deg, 3 m trace, early-out, Parallel.For, deterministic), `Sample` (trilinear), `PackRgba8` / `PackOpenRgba8` / `PackRegion`. Open0..3 = transmittance at 55 deg elevation toward +X,-X,+Z,-Z; Open4 = zenith; Open5 = mean of the 20 deg ring. `Invalidate` merges overlapping dirty boxes so shared voxels bake once.
- Self tests: `dotnet run --project experiments/representation_rebuild/selftest -- light` -> `light.occlusion` PASS (open field, slab at 0.05/0.10/0.20 m voxels, leaf-cloud monotonic, bent normal, locality bit-identical beyond trace+1 voxel, Remove restores bit-exactly, determinism, heightfield, pack layout) and `light.cost` PASS (always passes; numbers below).
- GPU: `game/Repr/Light/SkyVisibilityField.cs` (owns the volume, uploads an RGBA8 `ImageTexture3D` only after a bake, binds globals `repr_skyvis_tex/origin/inv_size/strength` + `repr_skyvis_voxel`), `game/Shaders/repr_skyvis.gdshaderinc` (`repr_sky_visibility`, `repr_sky_directional`). Hook (4 lines, guarded by `repr_skyvis_strength > 0.0`) in `flora.gdshader` and `leaf.gdshader` after their existing `AO`/`AO_LIGHT_AFFECT`: `AO *= visibility`, `AO_LIGHT_AFFECT = min(existing, 0.15)`. With strength 0 (default) nothing changes. `game/project.godot` got a `[shader_globals]` section with the five globals (required: shaders reference them at compile time).
- light_probe demo: feeds ground (heightfield), 4 box rocks (opaque) and the 12 legacy plants (497,679 triangles from the MultiMesh meshes; UV2 convention used: UV2.x > 1.5 = wood = opaque, everything else = leaf, opacity 1.0). Ground/rocks use a small shader material (same albedo/roughness as the StandardMaterial it replaces) carrying the hook, only when `skyvis=1`. Shots added: `dusk` (DayFraction 0.72), `under_close` (eye 0.25,0.15,0.25 looking toward the nearest plant at 1.55,0.55,0.9; no plant stands 0.5 m from the eye, so "0.5 m" was interpreted as a short look-up through one plant's canopy), plus `dyn` before/after variants and `sunshots` on demand.
- `ReprEnv` keys added: `skyvis, skyvis_voxel, skyvis_strength, skyvis_leaf, exposure=fixed|x, ambient_scale=x|autoa|autoc, ambient_a/ambient_c (table overrides), sky_static, reflprobe_static, reflprobe_opt, aa=taa|fxaa|msaa2|none, candidate=A|C, frames, shots, dyn, sunshots`, diagnostics `rig_freeze, refl_src, amb_src, sky_bg`. Nothing in `EnvironmentRig.cs` was touched; a `ReprEnvDriver` node (process priority 100000, child of the rig) post-adjusts what the rig wrote each frame.
- Runner/scripts: `build/overhaul/L1/run_l1.py` (L0 protocol), `compare.py` (sheets + pixel diff), `make_table.py`, `dyn_sheet.py`; raw `results_*.json`, `table_m1m2.md`, `pixdiff.txt`, sheets `sheet_*.jpg`, PNGs in `png/`.

## Method
Same machine/settings as L0 (Radeon 860M, 1600x900, scale 0.85, tier 1, vsync off, 180 frames/shot, GPU ms = `ViewportGetMeasuredRenderTimeGpu`). Throwaway warm launch, then control -> config -> control ..., every config its own launch. Delta = config minus mean of its two adjacent controls. Two full sets (m1, m2); A, A msaa2, A fog-off, C, all_off were run twice, the single-key probes once. 15 controls over both sets. Editor process was left alone; many python.exe processes pre-existed (as in L0), so control spread includes that. Calibration runs (frames=20) used for exposure/ambient/strength matching; they are not used for timing.

## Control spread (GPU ms, 15 controls)
| shot | min | max | mean |
|---|---|---|---|
| mid | 18.78 | 21.57 | 19.58 |
| under | 16.82 | 18.54 | 17.23 |
| night | 19.17 | 20.21 | 19.55 |
| dusk | 18.90 | 21.25 | 19.58 |
| under_close | 15.72 | 17.39 | 16.13 |
Spread ~2.5 ms, so single deltas under ~1 ms are noise (sky_static and skyvis_only are in that range).

## GPU table (absolute ms, delta vs adjacent controls in brackets; VRAM delta from mid)
| config | n | mid | under | night | dusk | under_close | dVRAM mid MB |
|---|---|---|---|---|---|---|---|
| control | 15 | 19.58 | 17.23 | 19.55 | 19.58 | 16.13 | 0 (2688-2720 abs) |
| A: cached, fxaa, fog on | 2 | 8.32 (-10.89) | 7.08 (-9.94) | 8.19 (-11.19) | 8.34 (-11.08) | 6.03 (-9.94) | -353 |
| A msaa2 | 2 | 10.03 (-9.46) | 8.56 (-8.47) | 10.30 (-9.11) | 10.00 (-9.43) | 7.14 (-8.83) | -311 |
| A fog off | 2 | 7.74 (-11.83) | 6.59 (-10.51) | 7.58 (-12.02) | 7.68 (-11.63) | 5.59 (-10.41) | -367 |
| C: stock lowered (sdfgi off, ssao on, fxaa) | 2 | 11.72 (-7.64) | 10.17 (-6.96) | 11.41 (-8.17) | 11.46 (-8.34) | 8.94 (-7.11) | -332 |
| all_off=1 (L0 definition) | 2 | 7.51 (-12.35) | 6.56 (-10.85) | 7.47 (-12.10) | 7.50 (-12.46) | 5.33 (-10.75) | -368 |
| sky_static=1 alone | 1 | 19.24 (-1.58) | 17.21 (-1.17) | 19.86 (+0.02) | 19.83 (+0.18) | 16.18 (-0.10) | -32 |
| reflprobe_static=1 alone | 1 | 17.48 (-2.57) | 15.04 (-2.69) | 16.97 (-2.88) | 17.21 (-2.55) | 14.25 (-2.67) | +16 |
| skyvis=1 alone (field cost) | 1 | 19.72 (+0.17) | 17.38 (+0.33) | 20.01 (+0.24) | 19.74 (+0.05) | 16.15 (-0.47) | +33 |
Candidate A mid shot is 8.3 ms vs 19.6 (-56 %) and within ~0.8 ms of all_off. The field itself is not measurable (+0.2 ms, inside noise). Voxel size (single 60-frame runs, mid): 0.20 m 8.70, 0.10 m 8.4-8.6, 0.05 m 9.34 (~+0.9 ms: 5.3 MB texture, more cache misses). Flat-colour backdrop (`sky_bg=color`) 8.33 vs 8.4-8.6: the sky backdrop pass is not a cost once SDFGI is gone.
Candidate A includes: sdfgi off, ssao off, fxaa, fog on (default), sky_static, probe off, fixed exposure, ambient source = flat colour, skyvis.

## Findings that changed the plan
1. **The reflection probe is the real ambient/reflection source of the control image, not SDFGI.** With the probe off, the sky ambient (very bright HDR sky radiance) takes over: image luminance rises to ~208 % of control whatever `AmbientLightEnergy` is (scaling it 1.0 -> 0.01 changed nothing: with source Sky the radiance part is not scaled by energy). `sdfgi=0` alone with fixed exposure stays at 96 % of control. This is what made L0's all_off "washed out". Candidates therefore set the ambient source to flat Color (energy then works) and use a per-DayFraction ambient table.
2. **Auto exposure in the control sits at a near constant multiplier ~1.9-1.95** (measured by running the control with exposure fixed at 0.8/1.0/1.25 and solving per time of day). `exposure=fixed` uses table {0.05: 1.95, 0.25: 1.94, 0.50: 1.94, 0.72: 1.93, 0.90: 1.85}; control config with it reaches 96.6/94.6/89.6 % of control luminance (mid/dusk/night) so the table is slightly low, never above.
3. Ambient tables (energy multiplier applied after the rig, x100 DayFraction): A {5: 0.15, 25: 0.15, 50: 0.15, 72: 0.225, 90: 1.0}, C {5: 0.074, 25: 0.074, 50: 0.083, 72: 0.135, 90: 0.61}. Calibrated by luminance only (matched to <= 101 %, never above), 3 iterations.

## What sky and the reflection probe were costing
- **Sky**: the rig calls `SetShaderParameter` on the sky material 8 times every frame; the Godot sky treats every material uniform write as "uniform set updated" and marks the radiance cubemap dirty, so with `ProcessMode.Incremental` the filtering restarts every frame. Fix (`sky_static=1`): the Sky gets a clone of the material, the rig keeps writing the original (which nothing renders), and the driver copies the 8 rig-driven params to the clone only when one moves by >2 % (relative). Result: -1.2..-1.6 ms on day shots, ~0 at dusk/night (single run, within noise). The earlier "sky costs 3 ms" of L0 (`sky=0`) was mostly SDFGI sky-light and sky ambient work removed together; the camera-pass backdrop is not expensive (flat colour: no change). A real day cycle still pushes every ~1-2 s (not quantified).
- **Reflection probe**: not a per-frame re-render. Evidence: UpdateMode Always raises frame p50 from 21 to 32 ms while the viewport GPU timer stays flat (probe renders are outside that timer), so Once is not re-rendering; moving the probe 1000 m away removes the whole 2.4 ms; disabling shadows, box projection, environment ambient or interior mode does nothing; freezing the rig (`rig_freeze`) does nothing; `sky_static` does nothing to it. So the cost is per-pixel probe sampling in the opaque forward pass on heavy-overdraw foliage. There is no node-API way to keep the cached result and drop that cost, so `reflprobe_static=1` hides the probe (cache dropped, honest) and the missing probe is replaced by the flat ambient + sky reflections (finding 1).

## Cached volume costs (light_probe: 7.2 x 3.2 x 7.2 m, 497,679 triangles)
| voxel | voxels | deposit | full bake | upload | texture | CPU grids | source records |
|---|---|---|---|---|---|---|---|
| 0.20 | 20,736 | 87 ms | 3 ms | 5 ms | 81 KB | 0.9 MB | 0.06 MB |
| 0.10 | 165,888 | 117 ms | 19-21 ms | 7-8 ms | 648 KB (663,552 B) | 7.6 MB | 0.3 MB |
| 0.05 | 1,327,104 | 227 ms | 222 ms | 28 ms | 5.1 MB | 60.8 MB | 1.6 MB |
Headless self test (`light.cost`, 6x3x6 m at 0.10, 108,000 voxels): full bake 23 ms, local re-bake 9.5 ms (82,500 voxels: the 3 m trace reach covers most of a 6 m grid), deposit 21 ms, 4.9 MB CPU, 0.4 MB texture. In a 20x3x20 m world (1.2 M voxels) a full bake is 133 ms but removing one plant re-bakes 155,520 voxels in 21 ms, i.e. local cost is independent of grid size. The upload is always the whole volume: `ImageTexture3D.Update` takes every layer, so there is no partial GPU upload (`PackRegion` exists for when that API allows it). Nothing is uploaded per frame.

Dynamic check (variant `dyn`, plant 3 removed and re-added at a new position, `sheet_dyn.jpg`): deposit/remove 5 ms, local re-bake 25-27 ms (161,280 voxels; the demo grid is small so the box is ~the whole grid), upload 2.2 ms, 663,552 bytes. Sky visibility at the old spot (0, 0.15, 2) 0.434 -> 0.616 (brightens), at the new spot (-1.3, 0.15, -1.0) 0.432 -> 0.317 (darkens); moving back restores both exactly. No full re-bake was run (`BakedVoxelCounter` delta shows only the local boxes).

Sun motion: the volume stores geometry only; `skyvis: ...` is logged once per launch and the shots at DayFraction 0.05 / 0.25 / 0.50 / 0.72 / 0.90 reuse the same texture with no re-bake. Shading still follows the sun (cast-shadow direction changes between `sheet_sun_sun50.jpg`, `sheet_vis1_mid.jpg`, `sheet_final_dusk.jpg`): the sun is the live directional light with its shadow map, only ambient/occlusion is cached.

## Pixel difference vs control (mean abs sRGB difference 0-255; luminance in % of control)
Noise floor control vs control: 0.04-0.13.
| shot | A fxaa | A msaa2 | A fog off | C | all_off (L0 def.) |
|---|---|---|---|---|---|
| mid | 9.69 (99.4 %) | 9.70 (99.9 %) | 9.70 (99.4 %) | 8.93 (100.8 %) | 31.39 (122 %) |
| under | 7.46 (96.8 %) | 7.44 (97.0 %) | 7.47 (96.7 %) | 8.48 (101.0 %) | 36.72 (103 %) |
| night | 6.81 (99.4 %) | 6.83 (100.6 %) | 6.81 (99.5 %) | 6.61 (100.0 %) | 36.41 (365 %) |
| dusk | 7.03 (99.9 %) | 7.06 (101.4 %) | 7.04 (99.7 %) | 6.69 (100.8 %) | 37.66 (303 %) |
| under_close | 8.80 (94.6 %) | 8.78 (94.9 %) | 8.83 (94.6 %) | 12.13 (104.9 %) | 44.50 (128 %) |
Sun shots (A vs control, frames=20 runs): sun25 9.82, sun50 10.14. Strength tuning (A, MAD mid/under_close): 0.6 -> 10.8/11.2, 1.0 -> 9.7/9.0, 1.5 -> 8.9/7.9 but luminance falls to 96/90 %; re-matching luminance with more ambient (strength 1.5/2.0) gave no consistent MAD gain, so strength stays 1.0 (`CandAStrength`). Voxel 0.05/0.20 at the mid shot are within 0.2 MAD of 0.10 (no visible banding difference in this scene). The all_off column is the L0 config (autoexp off, no ambient fix), so it is not an equal-exposure comparison; it is shown for continuity.

Exaggeration test (A, strength 3, `sheet_exag_mid.jpg`, `sheet_exag_under_close.jpg`): foliage interiors, trunks and the ground in the plants' footprint go dark while open ground and the outer foliage stay bright; luminance falls to 90.6 % (mid) and 85.2 % (under_close). AO written from the spatial shader does attenuate ambient in Forward+ here (the field was visible with no other change), so no alternative ambient path was needed.

## Visual verdict (sheets: `sheet_final_*.jpg` = control | A | C | all_off)
- mid: A and C both read as the control at sheet scale: same canopy shape, same sun-shadow shapes, same overall brightness. A is slightly better than C in canopy interiors and ground darkening under plants; C keeps crisper small-scale contact darkening from SSAO. Both lose the tan lit colour of the hanging bell flowers (control: tan, A/C: near black) and the ground is a little paler and less saturated than control (flat colour ambient instead of probe).
- under / under_close: A is 3-5 % darker than control and a little flatter on trunks; C is 1-5 % brighter and noticeably worse in under_close (MAD 12.1 vs 8.8: foliage flat grey-green). A wins here.
- night / dusk: both match control luminance (calibrated); dark foliage reads similar, bells again darker, the glowing teal bells match. A is as good as C.
- Compared with control, neither candidate is better in any shot; they are equal or slightly worse. Against all_off (washed out, pale flowers, 2-4x too bright) both are far better.
- Remaining artefacts: bell-flower tint loss (probe reflections), pale ground, light leaking under thin ground edges not seen, voxel banding not visible at 0.10 m on foliage, no self-shadowing acne seen with the 1.5-voxel normal offset; the shader's own height-based AO is multiplied by the field (double darkening, absorbed by the ambient table). Not verified: other species and the full game scene, animated/windy plants (field is static), larger worlds visually.

## Recommendation
Adopt candidate A without the fog (fog off saves another 0.6 ms with no visible change in this scene): about 7.7 ms mid vs 19.6 (-12 ms, -370 MB VRAM), image within noise-of-taste of control. Candidate C (no new system) reaches 11.7 ms and is visually as close except in canopy-underside close-ups, but SSAO is still ~3 ms of that; A's field costs nothing measurable and re-bakes locally. Before shipping: (1) replace the harness exposure/ambient tables by a per-time-of-day curve inside the rig (the tables are calibration data, not design), (2) handle the bell-flower tint (small ambient/emission tweak per species), (3) wire `DirtyRecord`s from the real plant compiler into `Invalidate`, (4) keep voxel 0.10-0.20 m (0.05 m buys nothing visible and costs ~0.9 ms and 5 MB), (5) frame-rate cap remains the main lever for an always-on display (L0).
