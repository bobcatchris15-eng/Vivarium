# Progress checklist (survives context compaction)

- [x] 0 Setup: git, Godot 4.7.1 mono in tools/godot, templates in %APPDATA%, solution, contract, ADR, architecture
- [x] 1–8 Sim library `src/Vivarium.Sim` + xUnit `tests/Vivarium.Sim.Tests` (fast suites green; Slow = soak tests)
- [ ] 9 Godot client `game/` (render, camera, underwater, quality tiers)
- [ ] 10 Tools + UI
- [ ] 11 Perf (client LOD + counters wired)
- [ ] 12 Package: export preset, scripts/export.ps1, offline audit, attributions, smoke-release
- [ ] 13 Integration tests t-179..t-189, full matrix, coverage audit, acceptance run, clean-clone check, REPORT.md, RC zip
- [ ] scripts/verify.ps1 wraps all suites

Key facts:
- CLI: `dotnet run --project src/Vivarium.Cli -- soak|water|validate|schedule`
- Content JSON is generated from `scripts/content/gen_flora.py` / `gen_fauna.py` (edit those, re-run).
- Godot winding: front face = right-hand normal points AWAY from viewer (into surface).
- Tests that call a system's Step directly must advance `w.Clock.Tick` (keyed randomness uses the tick).
