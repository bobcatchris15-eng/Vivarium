# Overhaul baseline (HEAD a193e6d, 2026-11-10)

Hardware: AMD Radeon 860M iGPU, Forward+, Quality=1 (single tier). Evidence: `build/overhaul/baseline/` (gitignored); logs in `logs/`, contact sheets in `sheets/`.
Seed 804584441 for all review harnesses (default preset "Streamside Hollow"). Windowed GPU runs only.

## Commands
```bash
G=C:/Misc/Vivarium/tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe
B=C:/Misc/Vivarium/build/overhaul/baseline; cd /c/Misc/Vivarium
"$G" --path game -- --visual-review $B/visual
"$G" --path game -- --specimen-review $B/specimens
"$G" --path game -- --guardian-review $B/guardians
"$G" --path game -- --guardian-review $B/guardians-populated --guardian-populated
timeout 190 "$G" --path game -- --soak $B/perf/runN    # perf; no self-exit, killed by timeout (exit 124)
```
Contact sheets: `python <sheet.py> <dir> <out.jpg> <cols> <thumb_w> [per_sheet]` (Pillow 12.2 available; script was in session scratchpad, simple PIL grid with filename labels).

## Harnesses
| Harness | Writes | Done signal | Self-exit | Wall | Images |
|---|---|---|---|---|---|
| --visual-review | overview/foliage_terrain/grazing_transition .png + review.json (per-shot fps, p50/95/99 ms, draw calls, primitives, objects) | VISUAL_REVIEW_COMPLETE | yes | 160 s | 3 |
| --specimen-review | per species `NN_id.png` + `_macro.png`, specimens.json (id, height, radius, coverage, published, files) | SPECIMEN_REVIEW_COMPLETE | yes | 189 s | 126 (63 species x2) |
| --guardian-review | `<id>_closeup/_locomotion/_feed.png` + guardian_review.json (no fauna/flora populate) | GUARDIAN_REVIEW_COMPLETE | yes | 61 s | 30 |
| --guardian-review --guardian-populated | same, world populated | same | yes | 95 s | 30 |
| --soak | diagnostics/<ts>/samples.jsonl (1/s), summary.json, metadata.json; initial.vivsave, launch.json | SOAK_READY (stdout) | NO | 190 s (killed) | 0 |

Filters: `--specimen-species a,b`, `--specimen-seed N`, `--guardian-species a,b` (matches id or archetype), `--seed N` (visual), `--preset NAME`.
Exit codes 0 on success, 4 on failure. `--perf-test` has NO runner (only isolates user dir / unlocks Quality in GameSession and Bridge.UserPath); `perf_report.json` is never produced. `--diagnostics <dir>` and `--diagnostics-check` (12 s, quits) also exist.

## Species ids (63, flora, as enumerated by specimens.json)
quillfeather_moss rustbellow_moss wispthread_moss silkgleam_moss stoneknot_moss pinequilt_moss slickmantle hagshair_lichen sunscald_lichen moonshell_lichen ashfingers_lichen capstar_moss duskpuff starsplit reekhorn cradlecup candlecoral ashcomb barrowbole swanfrond sceptrefern emberbracken mirrorstrap_fern quillsedge cloudtuft duskbristle bannerreed ashwire sootband starveil butterglint cinderberry mistviolet hushclover hallowsting burlmantle heddlelace fenfoam marshkettle widowspire brightknot cragset deepquill mirepocket pondstars bogfringe burrflag paddlerib barbarrow shadewrap bitterbine trumpetbind miremyrtle nutglade thornmantle greywatch silverfelt moonbark shiverleaf fenforge banksorrow mirespire witchberry

## Guardian ids (10 archetypes; roster order from log)
Image file prefixes = archetype: crested_canopy armored_forest canopy_serpent great_ground woodland_omnivore quilled_arboreal fernback dewhorn hushbill ruffleback.
Guardian ids -> archetype: hoatzin_crested->crested_canopy pangolin_armored->armored_forest serpent_canopy->canopy_serpent monitor_great->great_ground coati_woodland->woodland_omnivore porcupine_quilled->quilled_arboreal thylacine_fernback->fernback saiga_dewhorn->dewhorn shoebill_hushbill->hushbill matamata_ruffleback->ruffleback.

## Visual-review perf (end-to-end frame intervals, vsync off, 240 frames/shot)
| Shot | fps med | ms p50/p95/p99 | draws | primitives |
|---|---|---|---|---|
| overview | 42.1 | 23.8/26.1/30.5 | 279 | 1.43 M |
| foliage_terrain | 12.9 | 77.5/80.7/84.3 | 810 | 9.68 M |
| grazing_transition | 12.3 | 81.3/84.0/87.5 | 924 | 10.44 M |
World: 172 flora, 77 fauna. Close-up shots are far below the 30 FPS target.

## Soak perf (normal play, sim running, UI visible; run0 discarded as cold; runs 1,2 below; run0 matches)
Steady window = elapsed >= 90 s (startup/flora layer build before that: fps ~0.6-5, worst 1.5 s).
| Metric | run1 | run2 |
|---|---|---|
| fps mean | 25.0 | 24.8 |
| frame ms p50 / p95 / p99 (median of per-second values) | 38.5 / 52.2 / 73.7 | 38.8 / 59.2 / 72.9 |
| worst frame (steady / whole run) | 187 / 1659 ms | 194 / 1549 ms |
| draws / primitives / nodes | 707 / 7.17 M / 2569 | same |
| video memory (RenderVideoMemUsed) | 4255 MB | 4223 MB |
| managed / engine / working set | 259 / 272 / 7668 MB | 370 / 252 / 7553 MB |
| GC gen0/1/2 counts (whole run) | 5021/2026/332 | 4999/2029/336 |
| sim step last ms (mean) | 0.76 | 0.34 |
| flora_visible / fauna_drawn | 347 / 77 | 354 / 77 |
Top CPU scope max-ms (mean per-second max, steady): Coverage 25-27, LiquidSurface 8-9 (Upload 7-9), Session.Advance 7.5, Guardian.Vegetation 5.5, Host.Advance.Liquid 4.5-4.9. Scopes overlap; no GPU timings exist.
Camera is the default start view and static. Samples at `perf/runN/diagnostics/*/samples.jsonl`.

## Warnings/errors in logs
- Every run: `WARNING: 7 RIDs of type "Texture" were leaked` at exit (rendering_device.cpp); specimens also "1 ObjectDB instance leaked".
- `[Ecology] Placed 0/2 starter Burlmantle (limited suitable habitat)` (seed 804584441).
- GUARDIAN_SKIN_CONTACT lines are info (valid=True). No IndexOutOfRange / "Unable to open file" / exceptions / *_FAILED in any log.

## Limitations
- No real perf runner: `--perf-test` is dead; soak never exits and is killed by timeout, so last summary.json says completed=false.
- Metrics unavailable: GPU time, per-pass timing, texture-memory split (only total RenderVideoMemUsed), p99 is 1 ms-binned in summary.json.
- Frame timing includes startup streaming unless windowed by elapsed time; visual-review fps is end-to-end, not isolated CPU/GPU.
- Nondeterminism: world/flora generation is seeded but timings vary run to run (~+-2 fps); shader TIME animation not frozen in reviews; guardian-review dists vary slightly (skin error values differ per run).
- Reviews write user data under `<out>/user-data`; run 3 times discarding first for perf.

## Baseline2 (2026-11-10, after harness batch h1/h3/h45)
Captures in `build/overhaul/baseline2/` (manifest: `MANIFEST.md`). Flora 375 PNG flat + 375 slope, 453 growth-stage
(10 fruiting, 16 juvenile), fauna 51, guardians 100 (10 species x closeup/side/feet/feed/motion) + 18 slope, visual 9 scenes.
Soak, 150 s after 90 s warmup, two runs: 26.4 / 27.2 fps, frame p50 36.1 / 35.8 ms, p95 62.6 / 57.4 ms, GPU frame 35.7 / 34.8 ms,
705 draw calls, 7.15M primitives, VRAM 4.46 GB, managed alloc 7.80 / 7.58 MB per frame, GC gen0/1/2 235/218/82 and 238/223/86.
This is the comparison point for all later batches.

## Per-renderer GPU attribution m1b (2026-11-11, after species batches SP1-SP13)
Method: 90 s soak after 60 s warmup per run; a control (nothing hidden) run between every hidden run; each delta is vs the
mean of its two adjacent controls. Controls: 32.53-32.95 ms GPU (spread <= 0.37 ms), so drift is negligible.
`build/overhaul/m1b/RESULTS.md`. Hidden = Visible=false (renderer still processes; CPU unchanged).

| hidden | GPU ms saved | primitives | draws |
|---|---|---|---|
| pilot tree | 4.92 | 0.82M | 8 |
| water + water surface | 3.16 | 1.55M | 85 |
| island (terrain + cut face) | 2.58 | 0.20M | 4 |
| flora | 2.30 | 0.68M | 243 |
| soil detail | 1.62 | 0.47M | 73 |
| fauna + guardian | 1.38 | 0.25M | 114 |
| props (rocks/logs) | 1.25 | 1.06M | 44 |
| ambient ground cover | 0.20 | 0.05M | 2 |
| coverage mats | 0.04 | ~0 | 0 |

Sum of attributable renderers ~17.5 ms of ~32.7 ms; the remaining ~15 ms is fixed per-frame cost (lighting, shadows,
SSAO/fog/GI post stack, sky) -> owner item P12 is the largest single lever. Best geometry targets: the single pilot tree
(4.9 ms for one object), water (3.2 ms; 1.55M prims for a small pond), props (1.06M prims for small, mostly buried rocks).

## P12 render-stack experiments (2026-11-11, build/overhaul/P12b)
Each override soaked 90 s after 60 s warmup between two plain controls (controls 33.39-33.68 ms GPU). Visual: 9 visual-review
scenes per override vs base, mean abs pixel diff (noise floor ~2/255 from wind animation).

| override | GPU ms | VRAM | visual |
|---|---|---|---|
| fog_res=64 (from 128) | -3.25 | -123 MB | indistinguishable (diffs at noise floor) |
| fog off | -3.84 | -121 MB | small loss of depth haze/light shafts; only 0.6 ms more than 64 |
| render_scale 0.85 (from 0.95, bilinear) | -2.51 | -19 MB | slightly softer image; diffs 2-4 |
| sdfgi off | -2.20 | -292 MB | visibly flatter: foliage paler/washed, milky haze in shaded foliage (diffs 6-9 in foliage/shoreline) |
| ssao half_size | -0.24 | -5 MB | none; not worth it |
| GI mode Dynamic on MMIs | +0.15 | -16 MB | no gain |
| shadow atlas 4096 | n/a | | already the engine default |

## pc5: P12 defaults applied (2026-11-12)

Owner choice: volumetric fog 64x64 froxels (was 128) and default 3D resolution 85% (was 95%). Two 150 s soaks after a discarded warm run, compared with pc4:

| | pc4 | pc5 |
|---|---|---|
| GPU ms | 33.9 / 33.4 | 30.6 / 30.5 |
| fps | 27.6 / 28.1 | 30.0 / 30.2 |
| p95 frame ms | 58.1 / 53.9 | 52.5 / 53.9 |
| VRAM GB | 4.26 / 4.30 | 4.15 / 4.12 |
| alloc MB/frame | 7.44 / 7.42 | 7.09 / 6.96 |

The combined saving (about 3.1 ms) is smaller than the two isolated tests added together (3.25 + 2.51). Captures: `build/overhaul/pc5/compare.jpg` (left P12b base, right pc5).
