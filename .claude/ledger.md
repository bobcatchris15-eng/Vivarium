# Photoreal overhaul — orchestrator ledger
Updated: 2026-09-25 | HEAD: 956d37b | Plan: ~/.claude/plans/do-2-it-s-fine-fuzzy-journal.md
HANDOFF: read docs/overhaul/HANDOFF.md first (rules, in-flight worktrees, queue).

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
| p1 | S1 monolith | SPLIT | Clanker refused: SCOPE_TOO_LARGE, no edits |
| p1a | Form kernel + FormTests | DONE | PASS 15 tests, re-verified |
| p1b | roundleaf/pairedleaf migration | DONE a2 | watercress/bacopa improved; giant flat pads = colony instance scale (XZ=r, Y=colony MaxHeight) squashing leaves -> fix in gm |
| p1c | herb/trifoliate + flower cup | PARTIAL | 2026-09-25 resume: 72-tri two-sided herb leaves + smaller flower cups; FormTests 24/24, reference 40/40. Herb is green again but flowers still need an art pass at interaction distance. Capture: build/reference/p1c-resume-final. |
| p1d | 12 variants + shader seed deform + reference/fps | MERGED 9d416ae + 7455b73 | Five migrated species, blade-only seed deformation; Geometry 25/25; 40-scene reference and independent review pass. Combined visual art and isolated FPS impact still to judge. |
| p2 | growth sim — spec docs/overhaul/growth_models.md §13 (G1..G10, P3) | IN PROGRESS | — |
| g12 | G1+G2 | DONE | PASS 26 tests re-verified, merged b791069 |
| g3 | G3 | DONE | PASS 58 re-verified, merged 9a37c31 |
| g4 | moss rules + lab | DONE a3 | merged; seam+drought recovery OK; dense 19.7ms (phys 3.2, spread 3.0, D2E 10.6 on pathological unbounded case) -> re-measure on real island in gm |
| g5a | lichen rules + lab | DONE a2 | merged; 3.4ms dense. TUNE LATER: foliose reads coral/DLA not radial rosette |
| g6 | plasmodium front + lab | DONE a3 (partial) | merged; fan+gradient OK, 0.97ms. Fusion scenario NOT demonstrated (seeds never meet) -> carried into G7 acceptance. Hit 60-turn limit. |
| g7 | Tero network + fusion | DONE | merged; two_food 1-trunk, maze=BFS, fusion 2->1; 2.4ms/600 nodes (budget relaxed to 3ms Debug). TUNE: retraction leaves lattice chevron shapes |
| gm-a | vascular groundcovers -> plain plants | MERGED f9d7d95 | Bootstrap 10/10; reference 40 scenes. Flora count 899→602 at 6 bio-days; `species_dichondra` still has oversized pale foreground pads, so S2b form/scale work remains. |
| gm-b | monolith | SPLIT | refused SCOPE_TOO_LARGE, no edits |
| gm-b1 | content mat/lichen blocks + CoverageSystem + seeding + scheduler; flora skips moss/lichen | WIP COMMITTED task/gmb f56a578 | builds; two-day determinism and seven-species six-day establishment tests pass; remaining integration tests and visual check pending |
| g8a | slime lifecycle rules + lab | DONE | merged 5d84b4f; starve->migrate->fruit OK; dry sclerotium OK but seed never grows; residue rectangle odd (tune in P3) |
| rev1 | review of agy session work | DONE | 30+ commits; moss/lichen invisible, creeping_groundcover = worst disc icon, fish cartoon, rocks low-poly; 3 stale tests fixed 145fa2b; agy WIP kept f704b97 |
| cull | remove occlusion pop-in | DONE 9590c61 | ray occlusion removed, frustum+2m margin; perf 60->59.9 fps (noise) |
| islbug | IslandRenderer IndexOutOfRange | RESOLVED | not a code bug: worktrees lack game/.godot import cache -> textures fail -> crash + blank captures |
| cullfix | cull merge inverted frustum sign culled all flora on main | FIXED fa766b8 | caught via floraVis=0 in perf samples |
| logend | log ends not rendering | DONE | hollow-end tris wound inward + backface culled; fixed, regression test; recheck all log scenes in next full capture |
| haze | humid haze + AO | DONE 4856315 | depth fog 0.02 + aerial persp + SSAO tier1; 0.03 washed overview; no measurable cost |
| vsync | perf/reference vsync off | DONE | uncapped main: 43.7 fps mean, min-second 30, p95 36ms (fresh world, sim live) |
| mossvis | coverage-aware reference scenes | MERGED (harness only) | sim areas healthy (carpet 0.45 m², lichens ~0.09); claimed mats visible but images show only log-shader moss tint + terrain green; lichen absent -> covvis |
| covvis | PROVE coverage renderer draws, fix | DONE (winding flipped, single-sided; 95.9 fps) | root: fan winding reversed -> backface culled (debug magenta proved 266k tris draped correctly incl rock tops). doubleSided fix cost 9% -> asked to flip winding instead |
| introcov | introduce/remove moss & lichen clumps via tools | DONE | 52/52 + smoke OK; default back to carpet_moss |
| edgefld | ScalarField.Sample bilinear near domain edge diluted by out-of-domain cells (moisture 0.7 reads ~0.35) | QUEUED | — |
| matr | coverage mat renderer | DONE | organic outlines, no grid, ~7% fps; TUNE: reads flat felt/paint, no fibre texture/relief; lichen flat mustard -> mat2 |
| mat2 | mat texture + relief + shoots | FAIL (honest) | shader-only, still paint; WIP on branch task/mat2 unmerged; split -> mat2a geometry relief, mat2b instanced shoots |
| creep | creeping_groundcover rebuild | DONE | discs gone; TUNE: stolons too thick/straight, too few/tiny leaves (reads as sticks) |
| vine | climbing_vine giant flat faceted leaves = next icon | QUEUED | — |
| harn | (folded into mossvis) | — | — |
| g8b | superseded by Pl-4 | — | — |
| pl1 | local mass + conservative transport | DONE | far end drains toward food w/o steering; exact conservation; setups retuned (two_food gamma/qgain, starve thresholds) |
| pl2 | phase field + rectification | DONE a2 | coherent sheet, waves, rectified drift; 8.4ms/600 nodes Debug (runs every 30 sim-s) ; K=0.001 |
| pl3 | veins from shuttle Q, stress replaces Migrating | DISPATCHED wt pl3 | — |
| aq1 | algae/duckweed layers + advection + lab | DISPATCHED wt aq1 | — |
| aq2 | biofilm from algae, grazing, content, seeding | QUEUED | — |
| aq3 | lily pad species + mesh | DONE a3 | round notched flat pads confirmed visually; a2: pads visible but lanceolate blades tilted on tall stalks, not round flat floating pads; | a1: tests pass, 14 plants; visual FAIL: only petioles visible, no pads on surface (suspect non-uniform depth scale/culling/below surface) |
| aq4 | aquatic renderers | QUEUED | — |
| pl4 | SpatialOrganism refactor + in-game slime migration | QUEUED | — |
| gm-b2 | delete colony code + tests + grazing/tools/stats hooks | QUEUED | — |
| gm-b3 | interim draped raster renderer + reference | QUEUED | — |

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

- D6 09-25: open Qs defaulted (user said proceed): slime advances over accelerated time, renderer interpolates front; thick mats (cushion/sphagnum) block seedlings. Revisit on user feedback.
- D7 09-25: token plan — packets cite doc sections, orchestrator never reads source, one-command re-verify, sim-only packets in worktrees parallel to render packets on main.

- D8 09-25: Agent isolation:worktree bases on origin/main (a6d5b71), not local HEAD -> use manual `git worktree add -b task/<slug> .claude/worktrees/<slug> HEAD` and pass abs path in packet. Revisit if we push main regularly.

- D9 09-25: FPS only comparable in back-to-back paired runs — p1b run showed every scene capped at 60 with unchanged primitives (session vsync/power state differs from baseline). Gate fps on paired runs (baseline commit + candidate same session).

- D10 09-25: numeric lab asserts are gameable (foliose passed via holes, fan passed as square) -> orchestrator always eyeballs lab frames before merge; packets must write legible frames (species hue, flags, B brightness).

- D18 09-25: user: add algae, duckweed, lily pads. Algae+duckweed = SpatialOrganism coverage on water bed/surface with flow advection (patterns from hydrology, not placement); algae becomes the source of the Biofilm field; lily = rooted FloraIndividual with floating kernel leaves. Spec growth_models.md §15.
- D17 09-25: user: plasmodium = spatial organism (no transform), local mass moved only by flow, contraction phase field on own 30 sim-s cadence (explicit, option a), rhythm in sim-seconds (=real time at 1x) + wall-clock shader pulse. Spec docs/overhaul/growth_models.md §6R.
- D16 09-25: every fps number before this commit in this session was vsync-capped at ~60 (baseline v0.1.2 wasn't capped then) -> re-baseline needed for true comparison.
- D15 09-25: WORKTREE SEED: robocopy game/.godot (155 MB) into every worktree before any Godot run, and gate every capture on log containing no 'IndexOutOfRange'/'Unable to open file' AND flora_visible>0. Unseeded worktree results are void (cull+haze perf numbers were void). Also update CLAUDE.md Seed line.
- D14 09-25: user: occlusion culling pops partially hidden objects -> remove ray occlusion, conservative frustum only. Paired perf runs done while 3 Godot instances share the GPU are noisy; re-measure serially before trusting.
- D13 09-25: timing asserts flake under parallel Clanker load -> all ms asserts tagged Speed=Slow + Suite=Perf; per-task validate uses Speed!=Slow.
- D12 09-25: Tero network budget 1.5->3 ms Debug/600 nodes — runs once per flora step (600 sim-s) per plasmodium; Release faster. Revisit if many plasmodia coexist.
- D11 09-25: Clankers refuse packets spanning >~8 files / >2 subsystems (p1, gm-b both). Default packet size: one subsystem, ≤6 files, one validate.

## Unverified assumptions
- Worktree seed/cost for Mode P with windowed Probe unmeasured.

## Resume checkpoint (2026-09-25)
- Main: reduced herb leaf detail from 128 to 72 triangles, retained both faces and nondegenerate geometry, and reduced flower-head size from 0.32 to 0.12 so petals no longer cover the crown. `FormTests` 24/24; `dotnet build game/Vivarium.csproj` clean; reference returned `VIVARIUM_REFERENCE_OK` for 40 scenes. `species_ornamental_herb` now shows a green crown, though its flowers remain visually small. Sim digest matches the earlier p1c capture.
- `task/gma` is committed in its worktree and awaits integration after the remaining p1c art review.
- `task/gmb` coverage integration is preserved at `f56a578`. It compiles; its two-day determinism and seven-species six-day establishment tests pass. Remaining integration tests and visual check are pending; do not merge yet.

## Full-overhaul continuation (2026-09-25)
- User clarified that the requested outcome is the *entire* visual overhaul, S1–S7. Durable instructions: `.clanker/HUMAN_HANDOFF.md` and `.clanker/CURRENT_WORK.md`.
- `gm-a` was independently reviewed, cherry-picked, and captured at `build/reference/gma-merged`. The scene shows smaller but still pale oversized dichondra pads; do not treat the broadleaf art as complete.
- Main populated perf at `build/perf/overhaul-current/perf_report.json`: mean 43.1 FPS, min second 31, p95 frame 51.1 ms, p99 140.5 ms, worst 196.24 ms, 523/3747 frames over 33 ms. Profiler evidence points to synchronous Flora rebuild hitches. RayOpaque's terrain march currently ignores the caller's near max-distance until after marching (Selection.cs); bounded fix and re-probe underway. Paired runs remain necessary for FPS comparisons.
- `8f8bede` bounds terrain visibility marching by the caller's ray distance; regression and game build pass. The later absolute probe at `build/perf/bounded-ray` recorded mean 51.7 FPS, min second 40, p95 41.51 ms, p99 81.55 ms, 358/3747 frames over 33 ms. Groundcover migration changed the population between probes, so these are not an isolated before/after attribution.
- S1 variation commits `9d416ae` and `7455b73` merged after independent review fixed stem/petiole deformation. Combined capture: `build/reference/s1-combined` (40 scenes). Flat silhouettes and oversized groundcover pads remain; S1 geometry foundations have landed, but photoreal visual acceptance remains open.
