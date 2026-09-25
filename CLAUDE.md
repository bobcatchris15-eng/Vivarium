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
Seed:    worktrees need the import cache: `robocopy game\.godot <wt>\game\.godot /E` (155 MB) BEFORE any Godot run, else textures fail, IslandRenderer throws and captures are blank. Run Godot via the main tree absolute exe path. Gate: log has no IndexOutOfRange / "Unable to open file", and flora_visible > 0.
Cost:    not yet measured for a worktree; measure before any Mode P fan-out that runs Probe.
Notes:   Reference = default preset (seed 20260923) stepped 6 bio-days with the clock paused, 40 scenes at 1600×900, UI hidden.
         Re-runs at the same HEAD give identical sim digest; images differ only by animation (median mean-abs ≈1.2/255); fps varies ±5% median, ±20% worst scene.
         Baseline images: `build/reference/baseline-v0.1.2/` (local, gitignored); committed contact sheets + metrics in `docs/baseline/`.
         Contact sheets: `python scripts/reference-sheet.py <capture> <out_dir> <label>`.
