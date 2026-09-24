# Round 5 — orchestrator ledger
Updated: 2026-09-24 | HEAD: 59a221b

## Objective
Colonial edge-growth for moss/lichen (per-cell tint palettes, lichen banding), slime tint by generation, moss wet-intolerance + watercress/bacopa, dry groundcovers (dichondra/clover/carpobrotus), stackable rocks/logs. Plan: ~/.claude/plans/a-couple-things-for-elegant-prism.md

## Decisions
- D1: colony = many small FloraIndividuals with ColonyRoot/RingDist, not a new grid — reuses spatial buckets/bud machinery. Revisit if instance count blows perf.
- D2: Mode S, order 5,1,2,3,4.

## Tasks
| id | targets | status | attempts | last |
|----|---------|--------|----------|------|
| t5 stacking | PropPlacement, WorldTests | DONE | 1 | PASS; full dotnet test stalls in FloraTests.DecomposersTurnDeadMatter (pre-existing?) |
| t1 colony | 9 files | DONE | 2 | PASS 16 suites; only carpet_moss+foliose_lichen got colony blocks |
| t2 slime tint | +3 colony json | DONE | 1 | PASS |
| t3 wet | moss json, cress/bacopa, sphagnum (user req: saturated/boggy margins, colonial, green→ochre→red palette, upright capitula) | TODO | 0 | |
| t4 dry | 3 species + meshes | TODO | 0 | |
| t6 soil | terrain shader + new soil-detail renderer, gravel shader | TODO | 0 | user req: topsoil as real layer (displaced relief, instanced crumbs/clods/twigs/leaf litter density by moisture/detritus, visible soil profile at island edge); desaturate gravel |

## Unverified assumptions
- verify.ps1 default suite runs headless in reasonable time.
