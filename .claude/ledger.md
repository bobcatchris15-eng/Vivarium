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
| t3 wet | moss json, cress/bacopa, sphagnum (user req: saturated/boggy margins, colonial, green→ochre→red palette, upright capitula) | DONE | 1 | PASS |
| t4 dry | 3 species + meshes | DONE | 1 | PASS; 5 new meshes, diets |
| t6 soil | terrain shader + new soil-detail renderer, gravel shader | DONE | 1 | user req: topsoil as real layer (displaced relief, instanced crumbs/clods/twigs/leaf litter density by moisture/detritus, visible soil profile at island edge); desaturate gravel |

## Unverified assumptions
- verify.ps1 default suite runs headless in reasonable time.

## Open after round 5 (visual review of render tour)
- Moss colonies still render as large polygonal patches at day 5 — cell radius/rendering not reading as small bits; verify cellRadius & per-cell mesh scale
- Gravel still bluish-slate; dark-soil hill still reads flat at distance
- t7 moss cell radius DONE; t6c gravel/soil shaders DONE (inline); t8 colony bound DONE: cap 90/colony, global 2200, merge; perf run 4771 entities
- Open: "before" per-species count not captured; SustainedAcceleratedRunStaysBounded now passes
- Critter gloss DONE. Watch: closeup fps 12-15 on soil scenes (was 24-29 pre soil-detail) — suspect SoilDetailRenderer density
