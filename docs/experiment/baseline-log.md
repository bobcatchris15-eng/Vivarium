# Vibecode baseline — experiment log

Arm: **single strong agent, no Clanker orchestration** (Claude Opus 5.5 in Claude Code desktop).
Requirements: `docs/VivariumSpec.txt` (196 tasks), treated as the acceptance bar and not as a work breakdown.

## Cost
| Phase | Start (local) | End | Notes |
|---|---|---|---|
| 0 Setup, stack, docs | 2026-09-23 08:51 | 09:00 | Downloads: Godot 4.7.1 mono editor + templates |
| 1–8 Simulation core (content, world, time, fields, water, flora, fauna, genetics, ecology, tools, persistence, diagnostics, geometry) + tests | 09:00 | 11:00 (approx.) | 123 fast tests green after 2 fix rounds |

Human interventions: 2 (plan-mode clarifying questions before the build started: executor/arm, scope, engine, download approval, metrics). None during the build so far.

Tokens / turns: recorded at the end via session usage.

## Quality (running)
- First full test run: 112/123 passing. Failures: 4 real defects (lichen growth too slow to spread; pond not reaching the cut edge; animals in poor habitat not escaping; test-global log interference), 7 test-design mistakes.
- Second run: 121/123. Third: 123/123 (fast suites).

## Release
(pending)
