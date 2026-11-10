# Vivarium overhaul — orchestrator ledger
Updated: 2026-11-10 | HEAD: a193e6d | Graph: none | Checkpoint tag: overhaul-base

## Objective
Make Vivarium physically continuous, biologically distinctive, efficient and pleasant to use, per the
owner's reconstruction directive: remove visible gaps and artificial joints, give every species a
believable/integrated/distinctive identity, cut measured CPU/GPU/memory cost at equal or better visual
quality, overhaul UI/UX. Evidence (renders, measurements) required for every claim. Record in `docs/overhaul/`.

## Decisions
- D1 2026-11-10: Repo is `C:\Misc\Vivarium` (directive says E:\; owner confirmed C:).
- D2 2026-11-10: Recreated CLAUDE.md with a VALIDATE block; upstream 9650e95 deleted it, the tests and the
  scripts. Validation is build + headless boot-test + in-game review runners. Revisit if owner restores tests.
- D3 2026-11-10: Mode S for all edits (22 GB free, one GPU). Read-only audits run concurrently because they
  write only their own new file under docs/overhaul/ and never run Godot. Only one Clanker may run Godot at a time.
- D4 2026-11-10: Owner rule — each Clanker gets the lightest model/effort that can reliably do its task.
  Default sonnet/low for scoped edits, sonnet/medium for audits, haiku for re-validation. Escalate only on evidence.
- D5 2026-11-10: Wholesale botanical geometry replacement previously made visuals worse (directive). Fix
  junctions/attachments in place; replace a species' geometry only with before/after renders approved here.

## Tasks
| id | targets | status | attempts | last return line |
|----|---------|--------|----------|------------------|
| b0 | build/overhaul/baseline, docs/overhaul/baseline.md | DISPATCHED | 1 | |
| a1 | docs/overhaul/audit-flora.md | DISPATCHED | 1 | |
| a2 | docs/overhaul/audit-fauna-guardians.md | DISPATCHED | 1 | |
| a3 | docs/overhaul/audit-ecology.md | DISPATCHED | 1 | |
| a4 | docs/overhaul/audit-performance-architecture.md | DISPATCHED | 1 | |
| a5 | docs/overhaul/audit-ui-ux.md | DISPATCHED | 1 | |

## Unverified assumptions
- `--perf-test` still has a runner (only flag checks were found by grep; b0 to confirm).
- Review runners still work after the cleanup commit (b0 to confirm).
- 173 locally modified `.import` files are line-ending noise except 3 crested_canopy files; never stage them.
