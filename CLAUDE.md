# Vivarium — project notes for Claude

Godot 4.7.1 (.NET) game in `game/`, deterministic simulation library in `src/Vivarium.Sim`, xUnit tests in `tests/`.
Godot binary: `tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe` (or `$env:VIVARIUM_GODOT`).
Target hardware: Radeon 860M iGPU — every visual change is judged against ≥30 FPS in normal populated play.

Current effort: photoreal visual overhaul. Plan and state live in `.claude/ledger.md`.

## VALIDATE
Fast:    `dotnet test Vivarium.sln -c Debug --filter "Speed!=Slow"`  (167 tests, ~11 min on this box — too slow per task; filter by `--filter Suite=X` for the touched area)
Full:    `./scripts/verify.ps1` (build + all fast suites + headless boot/smoke/reload); `-Suite All -IncludeSlow` adds windowed Render, Reference, Package
Probe:   `<godot> --path game -- --reference <ABS_DIR> [--preset NAME]` then
         `./scripts/compare-reference.ps1 -Before build/reference/baseline-v0.1.2 -After <ABS_DIR>` → `build/reference/compare/index.html`
         Windowed only (needs a GPU); ~4–5 min; prints `VIVARIUM_REFERENCE_OK`. Pass an ABSOLUTE dir — relative paths resolve under `game/`.
Perf:    `<godot> --path game -- --perf-test <ABS_DIR>`; env `VIVARIUM_PERF_SECONDS`, `VIVARIUM_PERF_LOW=1`, `VIVARIUM_PERF_SCULPT=1`. `perf_report.json` facts.summary has fps/p50/p95/p99/worst.
Seed:    worktrees need the import cache: `robocopy game\.godot <wt>\game\.godot /E /XD mono` (155 MB; /XD mono or a stale game DLL overrides your build) BEFORE any Godot run, else textures fail, IslandRenderer throws and captures are blank. Run Godot via the main tree absolute exe path. Gate: log has no IndexOutOfRange / "Unable to open file", and flora_visible > 0.
Cost:    not yet measured for a worktree; measure before any Mode P fan-out that runs Probe.
Notes:   Reference = default preset (seed 20260923) stepped 6 bio-days with the clock paused, 40 scenes at 1600×900, UI hidden.
         Warm-up is NOT a fixed tick count any more (2026-10-28: 6081–6188 ticks), so digests and fauna framing differ run to run; static scenes still differ only by ≈1.3/255.
         baseline-v0.1.2 is gone and compare-reference.ps1 rejects --reference reports: A/B by checking out the old game/src files, rebuild, capture, restore, then diff images per scene.
         Perf: discard the first Godot run after a build (cold shader cache, ~15% slow); use ≥2 runs per side, interleaved.
         Baseline images: `build/reference/baseline-v0.1.2/` (local, gitignored); committed contact sheets + metrics in `docs/baseline/`.
         Contact sheets: `python scripts/reference-sheet.py <capture> <out_dir> <label>`.
