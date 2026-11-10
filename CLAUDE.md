# Vivarium — project notes for Claude

Godot 4.7.1 (.NET) game in `game/`, simulation library in `src/Vivarium.Sim`. No test project in this checkout (removed in 9650e95; last present at 924c639).
Godot binary: `tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe` (or `$env:VIVARIUM_GODOT`), written `<godot>` below.
Target hardware: Radeon 860M iGPU.

Current effort: reconstruction and polish overhaul. Plan and state live in `.claude/ledger.md`; findings and evidence in `docs/overhaul/`.

## VALIDATE
Fast:    `dotnet build Vivarium.sln --nologo -v minimal` (≈12 s) then
         `<godot> --headless --path game -- --boot-test` → log must contain `VIVARIUM_BOOT_OK` and `invariants=0`
Full:    Fast, plus the Probe for the touched area
Probe:   `<godot> --path game -- --visual-review <ABS_DIR> [--seed N] [--preset NAME]`
         `<godot> --path game -- --specimen-review <ABS_DIR> [--specimen-species ID] [--specimen-seed N]`
         `<godot> --path game -- --guardian-review <ABS_DIR> [--guardian-species ID] [--guardian-populated]`
         Windowed only (needs the GPU). One Godot window at a time. Pass ABSOLUTE dirs.
Perf:    `<godot> --path game -- --soak <ABS_DIR>` / `--diagnostics <ABS_DIR>`; details in `docs/overhaul/baseline.md`
Seed:    worktrees need the import cache: `robocopy game\.godot <wt>\game\.godot /E /XD mono` BEFORE any Godot run.
Cost:    ≈155 MB per worktree; C: has ≈22 GB free. Prefer Mode S.
Notes:   Captures and logs go under `build/overhaul/` (gitignored).
         Perf: discard the first Godot run after a build; use ≥2 runs per side, interleaved.
         After adding or changing assets run `<godot> --headless --editor --path game --import`.
