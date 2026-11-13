# L0 - fixed per-frame render cost (light_probe)

## Machine and method
- AMD Radeon 860M iGPU, Godot 4.7.1 .NET Forward+, window 1600x900, render scale 0.85, Quality tier 1 (review default), vsync disabled by the runner, maxfps 0 unless stated.
- Demo `light_probe` (6x6 m ground, 12 legacy plants, 4 box rocks; ~1.1-1.5 M prims, 54-83 draws), 180 measured frames per shot. Metric: GPU ms (`ViewportGetMeasuredRenderTimeGpu` mean).
- Protocol: throwaway warm run discarded, then control -> config -> control ... (19 controls, 18 configs), each a separate Godot launch via `build/overhaul/L0/run_matrix.py`. Delta = config minus mean of its two adjacent controls. Raw: `build/overhaul/L0/results.json`, PNGs in `build/overhaul/L0/png/`, sheets `sheet_mid.jpg`, `sheet_under.jpg`.
- Overrides implemented in `game/Repr/Harness/ReprEnv.cs` (`--repr-env`), recorded in report.json (`env_overrides`). Nothing was skipped as non-toggleable; all keys applied. Approximations: `sky=0` = BackgroundMode Color + no Sky + ambient source Color + reflections Disabled (the rig still drives ambient colour/energy per frame, so energy follows the day curve); `fog` toggles VolumetricMistEnabled (rig re-derives the flag each frame). Keys msaa, fill, halfres_ssao, sdfgi_cascades, sdfgi_cell are implemented but were not in the matrix.
- An unrelated Godot editor process (PID 17740, `--editor`) was running throughout; other python.exe processes pre-existed. Variation from the editor is included in the control spread.

## Control spread (GPU ms, 19 controls)
- mid: min 19.32, max 21.42, mean 20.13
- under: min 16.97, max 19.37, mean 17.65
- night: min 19.32, max 21.03, mean 19.94
Control spread is ~2 ms, so deltas under ~1 ms are noise (glow, autoexp, atlas, splits, ssao-under).

## Delta table (GPU ms; absolute config vs control mean in brackets; dVRAM from mid shot)
|---|---|---|---|---|---|---|
| sdfgi=0 | -3.19 (16.99 vs 20.18) | -2.78 (14.91 vs 17.70) | -2.94 (17.27 vs 20.21) | -322.5 | 18.25 |  |
| ssao=0 | -1.20 (19.16 vs 20.36) | -0.76 (16.75 vs 17.50) | -0.56 (19.13 vs 19.69) | +0.0 | 20.56 |  |
| fog=0 | -1.23 (19.00 vs 20.23) | -0.55 (16.77 vs 17.32) | -0.38 (18.98 vs 19.36) | +18.5 | 20.34 |  |
| glow=0 | +0.25 (19.65 vs 19.40) | -0.98 (16.60 vs 17.58) | -0.82 (19.00 vs 19.82) | +16.0 | 20.84 |  |
| taa=0 | -2.52 (17.02 vs 19.55) | -2.30 (15.41 vs 17.71) | -2.72 (17.35 vs 20.07) | -25.5 | 18.55 |  |
| reflprobe=0 | -2.12 (17.59 vs 19.71) | -1.27 (16.30 vs 17.57) | -2.41 (17.63 vs 20.04) | +0.0 | 19.17 |  |
| autoexp=0 | +0.41 (20.49 vs 20.08) | -0.04 (17.39 vs 17.43) | +0.26 (20.24 vs 19.98) | +16.0 | 21.99 |  |
| shadows=0 | -2.30 (17.65 vs 19.95) | -1.00 (16.12 vs 17.12) | -2.12 (17.56 vs 19.68) | +32.0 | 19.00 |  |
| atlas=2048 | -0.08 (19.30 vs 19.38) | -0.29 (16.82 vs 17.11) | -0.33 (19.24 vs 19.57) | -24.7 | 20.54 |  |
| splits=1 | -0.11 (19.51 vs 19.62) | -0.27 (16.97 vs 17.24) | -1.27 (18.48 vs 19.75) | +16.0 | 20.58 |  |
| scale=0.7 | -4.38 (15.94 vs 20.32) | -4.08 (13.96 vs 18.04) | -4.04 (16.08 vs 20.13) | -92.5 | 17.21 |  |
| sky=0 | -2.95 (17.39 vs 20.35) | -2.64 (15.63 vs 18.27) | -2.79 (17.59 vs 20.38) | -70.9 | 18.78 |  |
| all_off=1 | -12.73 (7.54 vs 20.27) | -11.07 (6.53 vs 17.60) | -12.41 (7.61 vs 20.01) | -407.7 | 8.70 |  |
| all_off=1,shadows=0 | -14.13 (5.97 vs 20.09) | -11.95 (5.42 vs 17.37) | -13.90 (5.81 vs 19.71) | -375.7 | 7.14 |  |
| all_off=1,scale=0.7 | -13.58 (6.41 vs 19.98) | -11.53 (5.81 vs 17.35) | -13.54 (6.24 vs 19.78) | -418.0 | 7.74 |  |
| taa=0,fxaa=1 | -1.49 (18.89 vs 20.38) | -1.28 (17.14 vs 18.42) | -0.18 (20.19 vs 20.37) | -49.1 | 20.25 |  |
| maxfps=30 | -0.50 (20.41 vs 20.92) | -0.38 (18.15 vs 18.53) | +0.60 (21.07 vs 20.46) | +0.0 | 33.33 |  |
| maxfps=15 | +0.14 (21.19 vs 21.05) | +1.48 (19.22 vs 17.73) | +2.21 (21.97 vs 19.76) | +16.0 | 66.67 |  |

VRAM is reported by Godot at 2.3-2.7 GB for the whole process and moves with SDFGI (-322 MB), scale (-92 MB), sky (-71 MB), all_off (-408 MB); other VRAM deltas of +-16/32 MB are allocator noise.

## Max-fps (control config, light_probe)
| maxfps | shot | GPU ms/frame | frame p50 ms | fps actual | GPU busy fraction |
|---|---|---|---|---|---|
| 0 (uncapped, ctrl idx1) | mid / under / night | 20.7 / 17.8 / 20.4 | 21.9 / 19.1 / 21.6 | 45.4 / 52.7 / 46.6 | 0.94 / 0.94 / 0.95 |
| 30 | mid / under / night | 20.4 / 18.1 / 21.1 | 33.3 | 30.3 | 0.62 / 0.55 / 0.64 |
| 15 | mid / under / night | 21.2 / 19.2 / 22.0 | 66.7 | 15.2 | 0.32 / 0.29 / 0.33 |
GPU ms per frame does not fall when capped (it rises slightly at 15 fps, likely iGPU clocking down); only the busy fraction falls, linearly with fps.

## Game settings found
- Neither `OS.LowProcessorUsageMode` nor `Engine.MaxFps` is set anywhere in game/**/*.cs (grep of MaxFps, LowProcessor) or game/project.godot. Only the review runners call `DisplayServer.WindowSetVsyncMode(Disabled)` (e.g. game/App/VisualReviewRunner.cs:20, GroundReviewRunner.cs:30, GuardianReviewRunner.cs:166, SpecimenReviewRunner.cs:143, game/Repr/Harness/ReprReviewRunner.cs:63). project.godot has no vsync or fps key, so the normal game runs with Godot defaults: vsync Enabled (display refresh cap), MaxFps 0, low-processor mode off. keep_screen_on=false (project.godot:28). Normal game render scale 0.85 (Bridge.cs:516), Quality forced to 1 (GameSession.cs:94).

## Observations
- Fixed cost is dominated by the stack, not geometry: all_off=1 drops 20.1 -> 7.5 ms (mid), -12.7 ms; the 12-plant scene itself costs ~6-7.5 ms with shadows. Shadows off on top of all_off saves a further ~1.4-2 ms (all_off+shadows=0 = 5.97 ms).
- Biggest single features: SDFGI -3.2 ms (and -322 MB VRAM), TAA -2.5 ms, shadows -2.3 ms (mid), reflection probe -2.1 ms (mid), SSAO -1.2, volumetric fog -1.2. These do not sum to 12.7 (nonlinear/overlapping passes). Sky replacement saves -2.9 ms, probably via sky/radiance and SDFGI sky-light work.
- Render scale 0.7 saves 4.4 ms, but with all_off it saves only ~0.5-1 ms extra: once the heavy passes are gone the cost is geometry-bound, not pixel-bound.
- glow, auto-exposure, shadow atlas 2048 and 1 split are within noise (<0.5 ms); fxaa instead of TAA recovers only ~1.5 of TAA's 2.5 ms.
- Sheets (mid, under): sdfgi, ssao, fog and taa off are very hard to tell from control at sheet scale; I could not see a clear loss of leaf bounce or canopy darkening. Honest caveat: sheets are downscaled and the scene has a flat brown ground and a dark grey backdrop, so GI differences are small to begin with.
- all_off differs clearly: brighter, flatter, lower-contrast image, softer and lighter cast shadows, leaves more pale/washed, bell flowers read near-white instead of tan, backdrop darker blue-grey. Part of this is autoexp=0 and ambient/SSAO removal, so it is not an equal-exposure comparison.
- All-off all_off at scale 0.7 (6.4 ms) looks almost the same as all_off at 0.85 (7.5 ms) on the sheet.
- Capping fps lowers the GPU-busy fraction (0.94 uncapped -> 0.62 at 30 -> 0.32 at 15) but per-frame cost stays ~20 ms, so for an ambient display the lever is frame rate plus the fixed cost; a ~7 ms stack at 15 fps would be ~0.1 busy.
