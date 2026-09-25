# Photoreal overhaul — orchestrator ledger
Updated: 2026-09-25 | HEAD: a6d5b71 (+C0 uncommitted) | Plan: ~/.claude/plans/do-2-it-s-fine-fuzzy-journal.md

## Objective
Move Vivarium from procedural-game look to photographed-miniature-terrarium realism (user's 18-campaign brief, 2026-09-25). Photorealism outranks ecology accuracy, species count, compatibility. ≥30 FPS populated play on Radeon 860M; no billboard/blob LOD; sim authoritative, renderer non-authoritative; breaking changes free ("pull the system apart"). Claude critiques each stage; user does final review.

## Stage graph (collapsed campaigns)
S0 baseline+harness (C0,C16) → S1 form-language kernel + per-individual variation (C1) → S2a slime ‖ S2b vascular+fictional clades (C3,C5) ‖ S2c colony mats (C4) → S2d fungi (C6) + S3 fauna (C7) → S4 contact/soil/rocks/logs/water (C8–12) → S5 materials+lighting+haze (C13,14) → S6 visibility perf (C15) → S7 taxonomy+integration (C17,18). Critic loop every stage.

## Decisions
- D1 09-25: reference scenes defined in C# (`game/App/SmokeRunner.Reference.cs`) as deterministic world queries, not JSON — targets need world queries anyway. Revisit if non-programmers need to add scenes.
- D2 09-25: reference warm-up = step ticks to 6 bio-days with clock paused (deterministic), not real-time accelerated play.
- D3 09-25: humidity haze (user idea). OBS: miniature terrarium air + distant models over-detailed/costly → DECISION: S5 adds subtle depth-scattered humid haze (aerial perspective, not fog wall); S6 may then suppress sub-pixel/under-haze feature detail as "genuinely imperceptible" → EFFECT: depth separation, softer distance, perf headroom → TEST: overview/mixed_depth scenes read as humid air, organisms still identifiable at interaction distance.
- D4 09-25: perf is ALREADY below target before any fidelity work (see baseline). S6 chunking/culling cannot wait until the end: schedule a perf spike right after S1 lands. Revisit once S1 numbers exist.

## Tasks
| id | outcome | status | last |
|----|---------|--------|------|
| c0 | reference mode, perf summary, compare tool, baseline, VALIDATE | DONE | VIVARIUM_REFERENCE_OK 40 scenes; digest repeatable |
| p1 | S1 kernel + broadleaf migration (docs/overhaul/S1_S2_plan.md) | DISPATCHED sonnet, Mode S on main | — |
| p2 | S2 CoverageLayer sim: moss/lichen CA + Physarum front + Tero network | PLANNED (after p1) | — |

## Baseline v0.1.2 (860M, 1600×900, paused sim, 899 flora/300 fauna)
- fps range 16 (overview, cutaway) – 36; most close scenes 25–30. p95 frame ≈50 ms even at ~30 fps mean (pacing hitch — investigate in S6).
- Overview 3.9M primitives / 258 draws — geometry volume, not draw calls, is the cost.

## Critic pass #0 — ranked illusion breakers (evidence: docs/baseline/v0.1.2_sheet*.jpg)
1. Slime mold: flat yellow cardboard fan sheets + long straight stretched "stick" veins floating over ground/logs (slime_macro, species_slime_mold, water_margin). Reads as twigs/paper. Worst element.
2. Flat single-sided polygon leaves everywhere: dichondra/watercress/bacopa discs = coins on stalks; big herb/vine leaves = flat green cards with hard facets; no camber, no thickness, flat unlit green.
3. Iconic flowers: clover/ornamental_herb flowers are giant flat lilac star polygons (species_clover) — cartoon glyphs, scale-wrong.
4. Stamp repetition: carpobrotus = identical parallel sausages; sphagnum/wetbank moss = hairbrush bristle clumps with hard outlines; watercress field = identical lily-pads. 5-variant cloning obvious at interaction distance.
5. Mushrooms: textbook uniform cones on white straight sticks, evenly spaced, no base/contact (ground_vegetation). Bracket fungus = flat tan discs intersecting log.
6. Contact: every organism sits on the ground with a hard seam; moss patches are raised dark slabs with cut edges (colony_edge); colonies = scattered chips, not continuous mats.
7. Ground texture smeared/low texel density at macro scale (fauna_terrestrial, species_*) — blurry streaks ruin macro shots; soil scatter tiny relative to camera.
8. Lichen: crust = orange fried-egg decal on rock; foliose = flat 5-point star cutouts.
9. Rocks read as smooth dark blobs half-sunk; logs = perfect cylinders with bark texture and clean ring end caps.
10. Water: flat pale translucent sheet with hard boundary; no damp gradient at margin.
Fauna mid-grade: springtail readable but toy-like; aquatic fauna invisible specks at normal view.

## Known harness issues
- Some species_* shots are blocked by nearby foliage (bonnet_mushroom, creeping_groundcover, turkey_tail, dichondra) — `Orbit` only tests opaque props/terrain, not flora. Fix when those species are reworked.
- No sky scene yet (mixed_depth shows a dark blob in the sky at 0.1.2 — unidentified; check in S5).

- D5 09-25: ~~D1 colony = many FloraIndividual cells~~ superseded — cell instances are intrinsically a field of chips. OBS: colonies/slime read as scattered chips/sticks → DECISION: sim-authoritative sparse 1.5cm CoverageLayer raster (moss/lichen CA, Physarum front + Tero flow-adaptation network) → EFFECT: continuous mats, hierarchical veins emerging from dynamics → TEST: dense_colony, colony_edge, slime_* scenes. Revisit if sim cost > Perf suite budget.

## Unverified assumptions
- Worktree seed/cost for Mode P with windowed Probe unmeasured.
