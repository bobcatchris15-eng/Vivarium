# INT-L — Production Cached Lighting Integration

## 1. Architecture & Integration Summary

The accepted L1b Candidate A2 cached sky-visibility lighting model (`LightCache`, `LightCacheWorker`, `SkyVisibilityField`, and `LightCycle`) is now integrated into the live production world as the default lighting mode (`UserSettings.LightingMode = "cached"`), while retaining `Stock` (`"stock"`) as a runtime-selectable control via settings UI (`Settings -> Lighting: Cached | Stock`) and CLI (`--lighting-mode stock|cached`, `--max-fps <int>`).

### Key Production Components
1. **Authoritative All-World Occluder Feed (`game/Repr/Light/WorldLightCache.cs`)**:
   - Feeds `LightCacheWorker` directly from `VivariumWorld` state (`Terrain`, `PilotTree`, `Rocks`, `Logs`, `Flora`, `DeadFlora`, and multi-segment climbers via `ClimberPath.Build`) rather than camera-culled `FloraRenderer` MultiMeshes, ensuring off-camera canopy and understory occluders are always represented.
   - Grid covers `Origin = (-5.0, -0.8, -5.0)`, `Dimensions = (50, 50, 50)` at `VoxelSize = 0.20 m` (10 m x 10 m x 10 m volume, 125,000 voxels, 500 KB `RG8` 3D texture), encompassing both the subterranean terrain profile and the full height of the ancient pilot tree.
   - Tracks source signatures (`SourceStamp`) per source ID and submits only changed or removed sources (`SubmitChanges` / `SubmitRemovals` up to 64 operations per poll tick), with bounded retry queues when the worker's 8192-source pending FIFO reaches capacity.
   - Observes `LightCacheWorker.Fault`: if an asynchronous worker fault occurs, `WorldLightCache` disposes the faulted worker non-blockingly and replays authoritative world state into a fresh worker.
   - World swaps (`BindWorld`) and terrain edits (`NotifyTerrainEdited` wired to `GameSession.SurfaceEdited`) cancel/dispose any prior worker non-blockingly and re-seed from authoritative state.
2. **Continuous Day/Night Rig & Static Sky Radiance (`game/Render/EnvironmentRig.cs`)**:
   - In `Cached` mode (`IsCachedLighting`), disables SDFGI (`SdfgiEnabled = false`), SSAO (`SsaoEnabled = false`), SSIL (`SsilEnabled = false`), TAA (`UseTaa = false`), and `ReflectionProbe`, and switches to `AmbientSource.Color`, `ReflectionSource.Disabled`, FXAA (`ScreenSpaceAA.Fxaa`), and fixed exposure (`AutoExposureEnabled = false`).
   - `ApplyDayNight()` continuously evaluates `LightCycle.Ambient(frac)`, `LightCycle.Bounce(frac)`, and `LightCycle.Exposure(frac)` across the 24-hour cycle, driving `Env.AmbientLightEnergy`, `CameraAttributes.ExposureMultiplier`, and `SkyVisibilityField.PushBounce(bounceGain * Env.AmbientLightEnergy * (0.40, 0.19, 0.08))`.
   - Sky shader parameters (`top_color`, `horizon_color`, `ground_color`, `sun_horizon_glow`, `cloud_density`, `star_visibility`) are gated behind a 2% change threshold in `Cached` mode so Godot does not re-convolve the sky radiance cubemap every frame.
3. **Shader Coverage (`game/Shaders/*.gdshader`, `game/Shaders/repr_skyvis.gdshaderinc`, `game/project.godot`)**:
   - Added global shader parameters `repr_skyvis_global_shape` (`vec4(0.5, 0.25, 1.0, 0.5)`) and `repr_skyvis_global_bounce` (`vec3`) alongside `repr_skyvis_tex`, `repr_skyvis_origin`, `repr_skyvis_inv_extent`, and `repr_skyvis_strength`.
   - `repr_skyvis.gdshaderinc` provides `repr_effective_shape()` and `repr_effective_bounce()`, falling back to the global shader uniforms when per-material uniforms are at default so dynamically generated flora/ground materials automatically receive A2 shaping and warm ground bounce while preserving per-material overrides in `LightProbeDemo`.
   - Connected `repr_skyvis.gdshaderinc` across all lit production shaders: `foliage.gdshader`, `bark.gdshader`, `repr_plant.gdshader`, `repr_substrate.gdshader`, `pilot_tree.gdshader`, `terrain.gdshader`, `loam_ground.gdshader`, `loam_litter.gdshader`, `rock.gdshader`, `log.gdshader`, `strata.gdshader`, `coverage_mat.gdshader`, and `ground_cover.gdshader`.

---

## 2. Deterministic Self-Tests & Headless Boot

- **Isolated `ReprSelfTest` (`build/selftest-root/bin/ReprSelfTest/debug/ReprSelfTest.dll`)**:
  - `PASS light.cache (22 ms)`
  - `PASS light.cost voxels=108000 full_bake=30.8ms local_rebake=24.3ms(82500vox) big_world=1200000vox:full=400.3ms/local=58.5ms(155520vox) deposit=41.8ms cpu=4.9MB records=0.0MB tex=0.4MB (667 ms)`
  - `PASS light.cycle (0 ms)`
  - `PASS light.occlusion (248 ms)`
  - `PASS light.worker (243 ms)`
  - Full suite: `REPR_SELFTEST tests=63 failed=0`.
- **Headless Boot (`--boot-test`)**:
  - `VIVARIUM_BOOT version=0.1.4 schema=5 ticks=33 flora=152 fauna=77 save=True load=True roundtrip=True invariants=0`
  - `VIVARIUM_BOOT_OK` (exit code 0, 0 shader errors/warnings).

---

## 3. Real-World Interleaved Soak Benchmarks

Hardware: **NVIDIA GeForce GTX 1080** (Vulkan 1.4.312 Forward+), **AMD Ryzen 5 1600 Six-Core Processor**, Windows 10 (`1600x900` window, `render_scale = 0.85`, seed `804584441`, preset `Streamside Hollow`).

### 3.1 Uncapped Interleaved Pairs (`--max-fps 0`)

| Run | Mode | Warmup / Window | GPU Frame Mean (ms) | GPU Frame Max (ms) | Frame p50 (ms) | Mean FPS | Draws | Primitives | VRAM Used (MB) | Texture Mem (MB) | Light Uploads / Sources | LightCache CPU Mean (ms/frame) |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `cached_uncap_1` | `cached` | 25s / 20s | **7.391** | 8.612 | 10.9 | 57.46 | 647.2 | 6.16M | **2,070.9** | 688.4 | 17 / 194 | 0.0219 |
| `stock_uncap_1` | `stock` | 25s / 20s | **13.367** | 14.270 | 14.9 | 39.70 | 642.7 | 6.14M | **4,000.6** | 2,593.0 | 0 / 0 | — |
| `cached_uncap_2` | `cached` | 50s / 20s (all 62 sp loaded) | **7.601** | 8.699 | 14.8 | 58.01 | 694.5 | 6.69M | **2,086.8** | 697.8 | 27 / 197 | 0.0191 |
| `stock_uncap_2` | `stock` | 50s / 20s (all 62 sp loaded) | **13.773** | 14.595 | 16.9 | 49.57 | 693.6 | 6.69M | **4,089.4** | 2,603.9 | 0 / 0 | — |

- **Mean GPU Frame Time (Interleaved Uncapped)**: `7.496 ms` (`Cached`) vs `13.570 ms` (`Stock`) -> **-44.8% GPU frame time (`-6.074 ms/frame`)** at identical draw calls (`694.5` vs `693.6`) and primitives (`6.69M` vs `6.69M`).
- **VRAM Usage**: `2,086.8 MB` (`Cached`) vs `4,089.4 MB` (`Stock`) -> **-2,002.6 MB (-49.0%) VRAM reduction** by eliminating SDFGI voxel cascades and dynamic sky radiance cubemap churn.
- **Shutdown Cleanliness**: `Stock` leaks 7 `RenderingDevice` texture RIDs on exit (`WARNING: 7 RIDs of type "Texture" were leaked`); `Cached` exits with **0 texture RID leaks**.

### 3.2 30 FPS Cap Steady-State Pair (`--max-fps 30`, 50s warmup / 20s window)

| Run | Mode | Frame p50 (ms) | GPU Frame Mean (ms) | GPU Busy Fraction | Draws | Primitives | VRAM Used (MB) | Light Uploads / Sources |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `cached_cap30_1` | `cached` | **33.2** | **8.711** | **0.2516 (25.2%)** | 693.8 | 6.69M | **2,119.7** | 27 / 198 |
| `stock_cap30_1` | `stock` | **33.1** | **15.604** | **0.4423 (44.2%)** | 695.0 | 6.69M | **4,105.7** | 0 / 0 |

---

## 4. Visual Review & Acceptance Demo Captures

### 4.1 Real-Island Visual Review (`build/int_l/visual_cached` vs `build/int_l/visual_stock`)

All 9 fixed-camera shots captured on seed `804584441` (`169` flora across `60` species, `77` fauna) after full flora population readiness and initial light-cache upload (`light_cache_uploads = 1`, `light_cache_sources = 178`):

| Shot | Primitives | Draws | Stock GPU Median (ms) | Cached GPU Median (ms) | GPU Time Delta | Stock Frame Median (ms) | Cached Frame Median (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `overview` | 7.21M | 744 | 18.284 | **10.396** | **-43.1%** | 18.809 | **10.973** |
| `foliage_terrain` | 9.25M | 907 | 21.510 | **11.499** | **-46.5%** | 22.050 | **12.007** |
| `grazing_transition` | 7.58M | 1138 | 21.935 | **11.972** | **-45.4%** | 22.478 | **12.506** |
| `shoreline` | 7.75M | 814 | 18.614 | **10.409** | **-44.1%** | 19.189 | **10.969** |
| `understory_eye_height` | 9.56M | 1025 | 19.568 | **10.795** | **-44.8%** | 20.098 | **11.354** |
| `woodland_trunks` | 9.69M | 1156 | 18.312 | **10.009** | **-45.3%** | 18.862 | **10.518** |
| `rocky_outcrop` | 9.82M | 1108 | 21.074 | **12.493** | **-40.7%** | 21.650 | **13.006** |
| `meadow_observation` | 1.63M | 755 | 6.397 | **2.911** | **-54.5%** | 7.064 | **6.103** |
| `meadow_observation_dusk` | 4.89M | 1288 | 7.403 | **3.815** | **-48.5%** | 8.044 | **7.355** |

- **Visual Comparison Notes**:
  - In `overview`, `foliage_terrain`, `understory_eye_height`, and `woodland_trunks`, `Cached` preserves understory depth, trunk/canopy darkening, warm ground bounce, and thin-leaf backlit warmth without SDFGI blotches or SSAO haloing.
  - In `meadow_observation_dusk`, `Stock` exhibits a harsh horizontal lighting seam mid-way up the ancient pilot tree where the 5 m tall `ReflectionProbe` box terminates (`y = 5.0 m`), leaving the upper canopy artificially bright green at dusk. `Cached` covers the full 10 m vertical volume smoothly with no probe boundary seam.

### 4.2 `light_probe` Acceptance Demo (`build/int_l/light_probe`)

- Captured `mid`, `under`, `under_close`, `dusk`, `night`, `dyn_before`, and `dyn_after` (`candidate=a2,dyn=1`):
  - `mid`: `gpu = 5.116 ms`, `vram = 914.4 MB`
  - `under`: `gpu = 3.792 ms`
  - `under_close`: `gpu = 7.535 ms`
  - `dusk`: `gpu = 5.025 ms`
  - `night`: `gpu = 5.032 ms`
  - Dynamic plant move (`dyn_before` -> `dyn_after` -> restore): `deposit/remove = 10.1 ms`, `local_rebake = 45.2 ms`, `upload = 3.8 ms`; sky visibility at original spot updates `0.434 -> 0.616` and at new spot `0.432 -> 0.317` (and restores `0.616 -> 0.434` / `0.317 -> 0.432`).
