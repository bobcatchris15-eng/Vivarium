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
