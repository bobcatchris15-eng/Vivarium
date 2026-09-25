# Human handoff — photoreal overhaul

## Autonomous goal
Complete the entire Vivarium photoreal visual overhaul described in the user's 18-campaign brief and the attached master plan, including implementation, visual critique, performance work, integration, and verification.

## Actual user direction
- 2026-09-25: Claude had been running the attached plan; user asked to locate its stopping point.
- 2026-09-25: User asked to pick up and to think about token efficiency.
- 2026-09-25: User corrected the scope explicitly: “I wanted you to complete the plan in its entirety. The entire visual overhaul.”

## Constraints from the approved plan
- Photoreal miniature-world result; user makes final visual judgment.
- At least 30 FPS on the Radeon 860M in populated play; no organism billboard/blob LOD.
- Simulation remains authoritative; renderer derives appearance without changing simulation state.
- Breaking changes permitted. Validate through reference captures, performance probes, tests, and visual critique.

## Interpretation and current evidence
- Complete S1 through S7, not merely Campaign 0 or one checkpoint. Follow dependencies in `.claude/ledger.md` and `docs/overhaul/`.
- Main at `8f2546d`; `task/gma` at `e9a5a9f`; `task/gmb` at `f56a578`. Baseline and harness are done. S1 and S2 growth integration are partial. Many later stages remain.
- Preserve accepted work in commits. Use small scoped work units and independent review before merging.
