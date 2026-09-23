# Progress checklist (survives context compaction)

- [x] 0 Setup: git, Godot 4.7.1 mono in tools/godot, templates in %APPDATA%, solution, contract, ADR, architecture
- [x] 1–8 Sim library + xUnit (123 fast tests green)
- [x] 9–10 Godot client (render, camera, underwater, quality tiers, LOD, tools, all UI panels); render tour screenshots reviewed
- [x] 11 Perf: timing, counters, budget warnings, spatial indexes, render LOD (87.6k → 7.4k fauna tris)
- [x] 12 Package: export preset, scripts/export.ps1 (181 MB bundle + zip), THIRD_PARTY_NOTICES + generated ENGINE_LICENSES,
      smoke-release.ps1 → exported exe passes boot + UI smoke (20 checks) + reload (digest exact), 0 TCP connections
- [x] Integration tests t-179..t-189 written; 7 fast ones pass
- [ ] Slow suite result (build/slow_tests.txt) — soak 4w x2, 8-week stress, 3-week default, drift, active save
- [ ] Clean-clone verify result (scratchpad\clone; verify -Suite Bootstrap,World,Boot,Smoke)
- [ ] docs/REPORT.md with requirement coverage table (t-191/t-195), baseline-log cost numbers, final commit, RC zip = build/Vivarium-0.1.0-win64.zip

Key facts / gotchas:
- Run Godot via PowerShell `scripts/common.ps1` Invoke-Timed (Git Bash `timeout` + pipes hung after a PC crash).
- Never `python -` from PowerShell (starts REPL). Use Bash heredoc for python patches.
- Smoke is headless: `--headless --path game -- --smoke DIR` then `--smoke-reload DIR`.
- Bugs found late: Toast QueueFree infinite loop; missing game/Vivarium.sln broke C# export; $args / array concat in smoke-release.ps1.
- Human interventions so far: plan questions (1), PC crash + reboot (2), "git problems" query (1), several "continue/try again".
- Usage at ~12:25 local: 5-hour window 74% used, weekly 13%, context 857k tokens.
