# Vibecode baseline — experiment log

Arm: **single strong agent, no Clanker orchestration** (Claude Opus 5.5 in Claude Code desktop, one session).
Requirements: `docs/VivariumSpec.txt` (196 tasks) used as the acceptance bar, not as a work breakdown.

## Cost
| Phase | Local time (2026-09-23) |
|---|---|
| Setup, stack, contract/ADR/architecture | 08:51 – 09:00 |
| Simulation core + 123 xUnit tests | 09:00 – ~11:00 |
| Godot client, render tour review, visual fixes | ~11:00 – ~(crash) |
| Post-crash recovery, UI smoke, export, release smoke, integration, report | after reboots – 12:55 |

- Session usage at the end: 5-hour plan window ~74 % used, weekly 13 %; context ~860k tokens (single 1M-context session, no compaction).
- Commits: 11. Code: sim ~7k lines C#, client ~3k lines C#, tests ~2.5k lines, 6 shaders, JSON content generated from 2 Python sources.
- Human interventions: plan-mode clarifications (1); PC crash + reboot (2 reboots); "git is throwing problems" (1); several "continue / try again / do it" after interrupted tool calls (~5). No design decisions were needed from the human after the plan.

## Quality
- Tests: 130 fast (xUnit) + 5 slow soaks, all green; Godot headless boot, UI smoke (20 checks), reload (4 checks); exported-build release smoke.
- Determinism digests match across frame cadence, camera/observation load, quality tiers, save/load, and scripted replay.
- Defects found by my own verification (not by reviewers): 4 sim defects on first test run; sRGB/linear colour bug and mis-scaled cursor (screenshots);
  toast infinite loop (UI smoke hang); missing `.sln` silently dropping .NET assemblies from the export; unbuffered log flushing; 3 script bugs.
- Independent review not performed here (left to the scoring step).

## Release
- `build/Vivarium-0.1.0-win64.zip` (181 MB bundle). Exported exe: boot OK, UI smoke OK, reload digest exact, 0 TCP connections, no networking APIs referenced.
- Not done: true network-unplugged run; windowed smoke of the *exported* build (headless only, after a GPU-driver crash during a windowed run); custom exe icon.

## Post-release iteration (user feedback rounds, 2026-09-23)
- Requests: UI overlap and glass, radial menu, camera speed and controls, grab precision, pill bugs, photo-real
  surfaces, clear aquarium water, terrain and water tools.
- Human decisions requested: texture source (CC0 download vs procedural), then the exact download list (7 files, 66 MB).
- Defects found in this round: 3 tests silently failing since 8d2a121, which contradicted the report's "all green";
  settings migration never firing (a property default masked the missing field); export shipping a locked
  `~RF*.TMP` exe backup while the game was running; water refraction offset 4x too strong (blocky artefacts);
  shoreline banding from interpolating a categorical substrate code.

