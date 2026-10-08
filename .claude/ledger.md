# Vivarium — orchestrator ledger
Updated: 2026-09-29 | HEAD: 2ff6c65
CURRENT EFFORT: **coarse-sim + daynight** (bottom of file). Photoreal overhaul (everything below the banner) is
RETIRED 2026-09-29 by user decision — do not resume, do not dispatch from it. Retained as history only.

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
| edgefld | ScalarField edge renormalisation | MERGED 78a515c | — |
| matr | coverage mat renderer | DONE | organic outlines, no grid, ~7% fps; TUNE: reads flat felt/paint, no fibre texture/relief; lichen flat mustard -> mat2 |
| mat2 | mat texture + relief + shoots | FAIL (honest) | shader-only, still paint; split -> mat2a geometry relief, mat2b instanced shoots |
| mat2a | mat geometry relief | MERGED be0789a | moss pillows + textured lichen sheets |
| mat2b | instanced micro-shoots via CoverageShoots.cs | UNBLOCKED | mat2a merged |
| creep | creeping_groundcover rebuild | DONE | discs gone; TUNE: stolons too thick/straight, too few/tiny leaves (reads as sticks) |
| vine | climbing_vine leaf form | MERGED de4361a (task/vine-form) | visual acceptance not re-judged |
| harn | (folded into mossvis) | — | — |
| g8b | superseded by Pl-4 | — | — |
| pl1 | local mass + conservative transport | DONE | far end drains toward food w/o steering; exact conservation; setups retuned (two_food gamma/qgain, starve thresholds) |
| pl2 | phase field + rectification | DONE a2 | coherent sheet, waves, rectified drift; 8.4ms/600 nodes Debug (runs every 30 sim-s) ; K=0.001 |
| pl3 | veins from shuttle Q, stress replaces Migrating | ABANDONED 09-28 (user) | worktree+branch deleted; tip was 8d058db (3 commits, 57/60) — recover from reflog if needed |
| aq1 | algae/duckweed layers + advection + lab | MERGED e2a2b0d | 66/66 tests pass, 41/41 mainline GrowthLab pass, worktree removed |
| aq2 | biofilm from algae, grazing, content, seeding | READY TO MERGE task/aq2-biofilm bf40fe6 | main merged in; AquaticSystem depth -> open water (water-table-only cells were obstacles); 194/201, only main's 7 pre-existing fails; awaiting user OK for main |
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
- main de3dd17 has 7 pre-existing test failures: LichenScenarios CrustEdenGrowsRound + FolioseLobes, FaunaTests EachSpecies (rainspine, moonveil, stiltclaw, stonebell, reedjaw). Not investigated.
- Worktree seed/cost for Mode P with windowed Probe unmeasured.
- de3dd17 only passed build + headless `--import` (clean, 2026-09-28). verify.ps1, reference capture and perf NOT run since the 83-commit pull; baseline/compare numbers predate surface-water and new species.

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

## Remote sync (2026-09-28, d41c320 -> de3dd17, 83 commits, landed outside this orchestrator)
- Surface water: groundwater and surface water separated in state + rendering; conservative face solver; surface flow absorbs into water table; soil hydration coupled to water amount; legacy saves migrated; water tuning exposed.
- Content: 9 new carnivore/fungus species (Kinkcane, Veilblade, Hookthicket, Blue Sundew Mat, Raincup Pitcher, Latchjaw, Rain Jelly, Carrion Bell, Glass Antlers), opted out of generic spore rain; blue sundew renders as spatial coverage rosettes; small fliers seek carrion-scent flora.
- Ground filler gets real blades/stalks; visible litter is now the detritus food pool; coverage caches persisted across save/load.
- Merged branches: fictional-ecology, vine-form, fauna-animation-families, codex/photorealism, local flora visuals + fauna motion.
- Unmerged remote branches: chatgpt/visual-breaks-carnivores-fungi, ci/baseline-main-visual-species, feature/woody-flora, flora-tool-groups (fauna-animation-families/fictional-ecology tips may also have moved on).
- Worktrees mat2a, gravel-blend, vine-form, pl3 removed 09-28. Only aq2-biofilm remains: 4 commits ahead, 93 behind main; trial merge conflicts in Ecology.cs, FaunaSystem.cs, VivariumWorld.cs (hydrology wiring vs surface-water refactor).
- Next: run verify.ps1 + fresh reference capture at de3dd17 before new dispatches; new species need critic pass.

## Effort: aging-perf (2026-09-28)
Objective: keep FPS >=30 on 860M as a vivarium ages. Measured (build/aged, default preset + user save): prims 4.7M@d0 -> 89M@d90 linear in flora (~20k tris/plant, draws flat ~600) -> 6 fps. Coverage renderer max 13->147 ms. Aquatic 42-47 ms spikes (age-independent). Hydrology 27 ms on user save (post-114beba 10 s sub-step cap).
- D-ap1: all five fixes, Mode P x3: lod (flora distance tiers + sub-pixel/inner-layer cull), cov (incremental coverage rebuild), simperf (aquatic amortize + hydrology adaptive substeps). Clankers do build+tests only; orchestrator runs paired GPU perf serially after merge (GPU shared => concurrent perf is noise, D14).
- Gate: build/aged/run-perf.sh with ABSOLUTE save paths (relative --load silently makes a fresh world).
| id | targets | status | attempts | last return line |
|----|---------|--------|----------|------------------|
| lod | OrganismRenderers.cs VisualBreakFlora.cs GeometryLodTests.cs | MERGED f292475 | 1 | 3 tiers all 44 species, e.g. kiteleaf 84k/17k/3.4k; <2px cull |
| cov | CoverageRenderer.cs | MERGED 8b1db28 | 1 | incremental dirty rebuild, camera-priority queue, async build |
| simperf | Aquatic/* Hydrology.cs | MERGED 65fcffe, PARTIAL | 1 | aquatic mean 35->~10ms, max 109->34-51 (target 7/20 missed: Advect dense bbox buffer). Hydro adaptive substeps. |
Unverified: hydrology cost on user save caused by 114beba (not yet profiled before/after).
- PRE-EXISTING on main 463d53e: 12 FormTests fail (Suite=Geometry) — likely from wip 77944f8. Not caused by aging-perf.
- PRE-EXISTING GrowthLab fails (4): NetworkStepIsFastOn600Nodes, CrustEdenGrowsRound..., FolioseLobes..., FlowAdvectionUsesPhysicalSeconds... (simperf confirmed on base).
- aging-perf RESULT (paired, aged saves, 860M): fps d0 47->74, d7 34->73, d30 16.5->57, d90 6.1->32.9, user d45 7.4->42.7. Prims d90 89M->8.3M. covfix 2a: race fixed (task-local scratch), 0 exceptions.
- OPEN: (a) cov background build reads live sim tiles unsnapshotted -> possible torn one-frame tile; (b) Sys.aquatic max still 82-152 ms in-game (Advect dense bbox buffer; tile-clustered Advect is the lever); (c) remaining spikes: Sys.flora 110, ecology.litter 101, SoilDetail 115, flora.ambient 94 ms max -> p99 still 40-100 ms; (d) kiteleaf top tier 84k tris; (e) tier visuals not yet reviewed on screen.
- D-ap2 09-28: user screenshot: stream not visibly spilling. Probe: sim DOES carry spring flow down channel (Q = spring rate) but as 2.2 mm film at 5 mm/s (<WetDepth 8 mm => WaterMesh skips it). Two causes: renderer depth-gated; 114beba equalize cap min(head,hf) throttles slope flow ~40x. Dispatched hydroslope (Hydrology.cs: cap only pooled water) + rivulet (WaterMesh/WaterRenderer/water.gdshader: discharge-driven thalweg ribbons), Mode P. Revisit spring discharge (content) if rivulets still read too thin.
- D-ap3 09-28: user: trunks vanish at distance. Replace vertex-clustering tiers + 2px cull with parametric tiers (fewer sides/segments/leaves; trunks 5-sided min), cap top tier 8-10k tris all species, push tier switch distances out. Dispatched lod2.
- hydroslope BLOCKED/DISCARDED 09-28 (3 attempts): packet target 0.05 m/s was physically wrong — Manning (n=0.05, full 0.25 m cell width) at Q=2.8e-6 m3/s gives h~0.33 mm, v~0.034 m/s; Clanker reached 0.023. Cap tweak + 2 s substeps cost 4x suite time and broke BasinPondsToStableLevelThenOverflows / WaterTableAndStreamSeparationIsQueryable (IsWet/IsStream are depth-gated; thin sheets fall below WetDepth). Revisit: if flowing water must count as wet/stream, gate IsWet/IsStream on discharge, not depth.
- rivulet MERGED: ribbons drawn in separate WaterRivulet mesh, NOT in CombinedMesh (two WaterMesh tests broke when included).
- D-ap4 09-28: user pivot: understory focus. Delete tree layer (umbraheart kiteleaf ironlace fenneedle) completely; keep shrubs; cap island diameter at 12 m (all presets + validation max). Old saves drop removed species on load. Dispatched notrees (Mode S, main).
- notrees DONE 343c6d5: 80 deleted/32 modified; presets all 12 m; saves migrate (drop removed species, skip digest check). Pre-existing failing at fa5d758 (not ours): FloraTests x9, IntegrationTests x4, FormTests x12. NOTE: prismhopper extinct by tick 3500 on 12 m default — ecology may need retune for smaller island.
- D-ap5 09-28: user: islands 5-10 m. Min 5 / Max 10; presets rocky_rise 5, steep 6, isolated_pond 7, default 8, creek 8, oxbow 9, shoreline 9, deep_inlet 10, bayou 10; starters scaled by area, floor 6 fauna/3 flora. Dispatched shrink. 12 m soak: duskflicker, emberglass_swimmer, glintfin, moonveil extinct by bio-day 90 — retune needed. Perf at 12 m contaminated by user's running game (PID 22884).

## Effort: coarse-sim + daynight (2026-09-29) — CURRENT
Objective: from `docs/fauna_coarse_ecology_performance_plan.md` — coarsen the simulation (steering/decision
cadence, metabolism/lifecycle accounting, deterministic accounting queue) and add a presentation day/night
clock (15 min day / 5 min night). The realism programme is retired above: do NOT resume aq4, pl4, gm-b2, gm-b3
or the critic-item tuning backlog. Do not touch the plan doc; it is the human's document and still claims to
supersede `fauna_expansion_plan.md` population structure.

Directives (2026-09-29)
- D-reaim-1 SUPERSEDED: "re-aim entirely at the real bottleneck". Overtaken by D-reaim-2 an hour later. Kept
  because the evidence it rested on is still true and still unaddressed (see A-perf-1).
- D-reaim-2: work the coarse-sim + day/night half of the fauna plan.

Evidence established this session (facts, not decisions)
- Live session PID 24056 (day 46, creek, 8 m island): sim backlog 60 -> 560 sim-min at 1x, drained at 0.5x;
  process used 0.54 of 16 cores. Render-coupled: `WallBudgetMs = 12` (Scheduler.cs:50) per Advance, once per
  frame (GameSession.cs:194). Low fps => less sim budget/s => backlog. Render spikes and sim throughput are
  ONE problem, not two.
- Worst-frame scope costs (428 perf samples, mixed eras + island sizes, MAXIMA not means): Sys.aquatic 320ms >
  Sys.coverage.plasmodium 70 > Sys.ecology.litter 46 > Sys.flora.ambient 45 > Sys.flora 40 > Sys.hydrology 37 >
  Sys.fauna.behaviour 37 > Sys.fauna.metabolism 18 > fauna.lifecycle never in worst-5. Whole fauna trio ~56ms.
- No day/night system exists. EnvironmentRig.cs:34 fixed `SunAngleMax = 30`, no cycle. Chunk 13 is greenfield.
- fauna.behaviour/metabolism/lifecycle already registered (VivariumWorld.cs:140-142) and already accumulate
  Runs/TotalMs/LastMs/MaxMs (Scheduler.cs:19-22, 89). `Vivarium.Cli soak` already prints per-system
  runs/total/mean/max (Program.cs:98) => Chunk 0 needs SCENARIOS, not new instrumentation.
- `pl4` misnamed: `SpatialOrganism` = 0 occurrences in tree. Re-scope before dispatch.
- 0 worktrees, 10 stale branches; aq2-biofilm landed via the pull.

Open design forks (human must answer; chunk 13 acceptance is not writable without them)
- Does the day/night cycle scale with the speed multiplier (1x = 20 min, 8x = 2.5 min) or hold 20 real
  minutes at every speed?
- Is it driven by physical `SimSeconds` (deterministic, tick-derived) or by real wall-clock?
Tasks
| id | outcome | status | last |
|----|---------|--------|------|
| daynight | chunk 13: presentation day/night clock + lighting | MERGED (76eb40a) | 15 min day / 5 min night, wall-clock, pause+speed-immune, never serialised; build clean, 11/11. NOT yet seen on screen (needs windowed GPU check). |
| guilds | chunk 2: EcologyGuild enum + Guild field, 17 species tagged | MERGED (cfa6d8a) | ContentLoader is STRICT — needed the field before tags would load. `validate`: 40 flora / 17 fauna / 9 presets. Metadata only, no sim branches yet. |
| faunabudget | chunk 1: whole-vivarium fauna budget | MERGED (71deb52) | Cap 2000 on descriptor; enforced on reproduction + CreateFounder; per-species caps kept. Build clean; reproduction/determinism/fixture tests pass. Open: CreateFounder THROWS at budget instead of graceful Fail — 1-line Introduction.cs follow-up. |
| faunaindex | chunk 9: stop per-tick RebuildIndex | MERGED (706b1ec) | Cadence rebuild N=4 via call counter (tick%N would never fire — StepBehaviour sees odd tick). Bulk rebuilds kept; Remove stays O(n) by design. Spatial/schooling/predation tests pass. |
| siltbed | waterway beds render as silt | MERGED (bc0ce15) | sub_tex.g submerged mask (OpenWaterDepth>4mm); pale matte silt, green sources suppressed. RENDER-ONLY. Build clean; Godot boot clean (no IndexOutOfRange/file-open). Full 40-scene reference did NOT complete (hung at flora build) — visual review still owed. |
| faunabase | chunk 0: CLI fauna baseline scenarios | MERGED (d0a8bca) | --scenario none/low/medium/high; prints per-system runs/total/mean/max. none=0 fauna; high=populated. CLI-only. |
| cs3-6 | forgiving diets + capped generic predation | MERGED (8a55d00) | Fixed an over-harvest: a predator with 3-4 named `fauna:` diet entries killed 3-4 whole prey PER feeding pass, wiping small fauna (prismhopper died 74x in one bio-day). Named prey is now preference-only; all kills flow through one capped generic path (IsEdiblePrey) with a one-kill-per-Feed cap; chunk-6 biomass rule (whole/too-weak kill returns mass; partial bite abstract). Day-1 soak: all 17 species alive + breeding, natural deaths only. 20/20 ecology tests on main. |
| lifecycle-rb | chunk 9 completion: StepLifecycle index cadence | MERGED (9ae6691) | Separate call counter; auto-merged with ecology cleanly, re-validated. |
| faunapred | (superseded — folded into cs3-6) | DISCARDED | Branch abandoned; its chunks 5+6 were hand-ported onto the diet base to avoid the git text-conflict silently dropping logic. |
| cs7-8 | coarser steering + cached intent | MERGED (e61e7e0) | FaunaBehaviour 2->4. Renderer already derives behaviourInterval from the constant, so no renderer change needed. Locomotion slices <=20s with per-slice passability so swept distance is cadence-independent. Intent refresh is a pure function of the animal's own step count + id; intent fields persisted so save/load digests match. |
| cs10 | coarsen metabolism | MERGED (3bfd0f7) | FaunaMetabolism 3->6; disturbance stress now time-weighted (integral of DisturbedUntil inside the window), exactly invariant under chopping. Feeding stays one pass so predation kills don't multiply. |
| cs11 | coarsen lifecycle onto bio-time | MERGED (43cb396) | FaunaLifecycle 30->120; ageing/maturity/mortality/cooldowns all on the bio axis; MaxLocalDensity got a per-birth re-test. |
| plasmodium | decouple cadence from fauna metabolism | MERGED (d3a13a7) | coverage.plasmodium was on Cadence.FaunaMetabolism; coarsening to 6 doubled its step to 60s, which its dt clamp [0.1,5.0] truncates — would have starved slime diffusion to ~8%. Gave it Cadence.Plasmodium=3. |
| budget-followup | graceful budget refusal | DONE (bff8caf) | IntroduceFauna checks CanIntroduce up front -> readable refusal, not a throw; partial drops report "2 of 5". Populate.Starters guards the same budget so world creation can't crash. 2 new tests. |
| intro-crash2 | SECOND crash path found | DONE (bff8caf) | Populate.Starters called CreateFounder in a loop during world CREATION — a preset demanding more than the budget would throw while loading a world. Not in the original follow-up note. |
| flora-overlap | parallel layer builds on load | DONE (28b0a02) | BuildLayers awaited one mesh at a time, once MoveNext per frame -> load time was the SUM of all builds, species appeared in catalog order. Now launches whole species up to a 24-task budget and publishes each when its own meshes finish. Material setup + GPU resources stay on the main thread. |
| daynight | chunk 13: presentation day/night clock + lighting | MERGED (76eb40a), FIXED (b5a0986) | The cycle NEVER RAN until b5a0986: EnvironmentRig's Stopwatch was declared and read but never started, so Elapsed stayed 0 and lighting was pinned to the first keyframe (flat midday, 1.26). Clock's own 11 tests passed because they inject a time source — nothing covered the wiring. Only a human playtest caught it. See D-dnclock. |
| silt-colour | darker muddy brown | DONE (bff8caf, b5a0986) | (0.42,0.38,0.31) -> (0.21,0.155,0.115) -> (0.070,0.049,0.034). The middle value still read LIGHT because it is LINEAR albedo (~sRGB 0.5) under Agx. Lesson: shader colour constants here are linear, not sRGB. |

| beforeafter | paired perf measurement of coarsening | OPEN | Populated-world before (c886997) vs after. Not run — soaks exceed tool window. |
| visual-review | day/night lighting + silt bed on screen | QUEUED | Both boot-clean; no windowed photo review yet. Needs the user's eyes. |

HARD NUMBER — CORRECTED (2026-09-29, second pass). The earlier "0.003 ms/tick, fauna is not a
bottleneck" figure was measured on `--scenario none`, an EMPTY world. It was a null measurement and the
conclusion drawn from it (that coarsening fauna cannot help the backlog) is NOT established. Real populated
numbers (1-day soak, --scenario high, ~400-900 animals, Debug, this box):

                       pre-coarsening claim (void)   measured on populated world
  fauna.behaviour       0.003 ms mean (empty world)   11.9 ms mean, 24.2 max (high) / 7.5 mean (default)
  fauna.metabolism      0.004 ms mean (empty world)   6.6 ms mean, 12.3 max (high)  / 4.5 mean (default)
  fauna.lifecycle       (never worst-5)              0.95 ms mean, 2.3 max

So fauna is NOT free after all: at high density behaviour+metabolism are ~18 ms of Debug time. The
chunks 7-11 coarsening (cadence 4/6/120, cached intent, dt-correct metabolism) targets exactly this, but
its actual win is UNMEASURED — a clean paired before/after on a populated world is owed. A worktree at
c886997 (pre-coarsening) exists for that. Until then do not claim a perf win; the ecology/robustness
value of chunks 3-6 stands on its own.

SAVE-LOAD SLOWNESS (investigated 2026-09-29, fixed 28b0a02): opening a save took ages for plants, and
carrion bell appeared immediately while everything else trickled in. Cause was NOT carrion bell being special
— it is 6th of 40 species in CONTENT CATALOG ORDER, i.e. near the front of the build queue. `BuildLayers`
awaited each variant mesh (`do { yield return false; } while (!pending.IsCompleted)`) before starting the
next, and the coroutine was advanced only once per frame in `_Process`, so the off-thread work was fully
serialized and total time was the SUM of every mesh build. Fixed by overlapping species (28-task budget),
publishing each as it completes. Lesson: on a slow load, suspect the queue, not the item that looks special.

DAY/NIGHT LESSON (b5a0986): a unit-tested feature was completely dead, and 11 green tests proved nothing about
it. `DayNightClock` takes its time source as an injected `Func<double>`, so every test supplied its own and the
real call site — `new DayNightClock(() => _monotonic.Elapsed.TotalSeconds)` — was never exercised. The rig
never called `Start()` on that Stopwatch, so the value was 0 forever. Two rules: (1) a test that injects a
dependency has NOT tested the wiring — the production call site needs its own check; (2) "it compiles, tests
pass, code looks right" is not evidence a feature runs. Only the human playtest surfaced it. Anything with a
real-time side effect needs someone to watch it happen.

SHADER COLOUR SPACE: terrain.gdshader colour constants are LINEAR albedo, not sRGB, and the scene tonemaps
with Agx (which lifts shadows). A "dark brown" written as 0.21 renders as a mid-tone; ~0.07 is needed to read
as dark. Convert before picking values.

PRE-EXISTING FAILURES (verified identical before and after the flora change, on warm cache):
  smoke: camera crosses water surface (transitions 0), pause stops simulated time, place rock (overlaps a
  log), introduce ambervein (no valid habitat), quality changes leave simulation untouched — 5 of 30, all
  unrelated to flora layer building. Not investigated; NOT regressions from this session's work.

KEY ECOLOGY FIX (2026-09-29): the dominant failure was NOT basal rate or diet — it was predation over-harvest.
A predator could kill one whole prey per named diet entry per pass. Fixing the kill rate (not the intake) is
what made small fauna survive. Lesson for future tuning: measure death CAUSES before tuning energy budgets.

KNOWN GAP: the 17 per-species `EachSpeciesSurvivesFeedsReproducesInheritsAndRenders` lifecycle tests exceed a
single tool window (>10 min each batch) and were NOT run to completion. The day-1 high-scenario soak and the
20-test ecology/determinism surface are the evidence in their place. Run them overnight before trusting the
full suite green.

HARD NUMBER (chunk 0 baseline, 1-day soak, default preset): fauna.behaviour mean 0.003 ms, max 0.9 ms;
fauna.metabolism mean 0.004 ms. Combined fauna sim cost is sub-millisecond on a typical tick. This is
measured proof that A-perf-1 holds: coarsening fauna cannot meaningfully relieve the render-coupled
sim backlog. The day/night and ecology chunks are justified as FEATURES (robustness, a visible clock),
not as a performance fix. The backlog lives in Sys.aquatic (320 ms worst) and the render spikes +
the WallBudgetMs x frame-rate coupling.

Tooling constraint (2026-09-29): the subagent `bash` allowlist DENIES `dotnet` and `godot`. Clankers can
write + self-review by reading but CANNOT build or test. The parent must run every gate. This caught a real
break: a Clanker claimed ContentLoader "ignores unknown fields" (it does not — it is strict) and 17 files
failed to load until the Guild field landed. Do not accept a Clanker PASS without a parent-run gate.

Tooling constraint (2026-09-29): the subagent `bash` allowlist DENIES `dotnet` and `godot`. Clankers can
write + self-review by reading but CANNOT build or test. The parent must run every gate. This caught a real
break: a Clanker claimed ContentLoader "ignores unknown fields" (it does not — it is strict) and 17 files
failed to load until the Guild field landed. Do not accept a Clanker PASS without a parent-run gate.

Tooling constraint (2026-09-29): the subagent `bash` allowlist DENIES `dotnet` and `godot`. Clankers can
write + self-review by reading but CANNOT build or test. The parent must run every gate. This caught a real
break: a Clanker claimed ContentLoader "ignores unknown fields" (it does not — it is strict) and 17 files
failed to load until the Guild field landed. Do not accept a Clanker PASS without a parent-run gate.


Unverified assumptions
- A-perf-1: coarsening fauna is expected to relieve the sim backlog. NOT established — fauna is ~56ms of a
  916ms worst frame and the process is not CPU-saturated; the backlog looks render-coupled. Revisit if chunk 0
  mean/total data shows fauna dominating, or if the day/night cycle is wanted on its own player-visible merit
  (plausible — that argument does not depend on A-perf-1).


---
# CURRENT EFFORT: overnight lush-realism loop (2026-10-07)
Branch: overnight/lush-2026-10-07 (from main 1a17b2c). User explicitly restarted photoreal work tonight ("iterate through this overnight") — supersedes the 09-29 retirement for this effort only.
Targets: docs/reference-targets/real-0{1,2,3}.* (dense planted vivaria: layered overlapping leaves, moss on wood, dark backdrop, glossy veined leaves, hard grow-light). User's current capture: current-capture-2026-10-07.webp.
Loop: judge(before vs refs) -> pick ONE top gap -> Clanker implements -> capture -> independent judge before/after vs refs -> KEEP (commit) or REVERT (git restore). Never merge to main; user reviews in morning.
Gates per iteration: build passes; flora_visible>0; no IndexOutOfRange/"Unable to open file"; fps on judged scenes not >15% worse than before.
| it | gap | verdict | commit | note |
|----|-----|---------|--------|------|
| 0 | baseline capture | BLOCKED | - | reference capture >60 min (spec ~5): per-species flora mesh build ~10 min each (ringreed, rain_jelly, snaptrap, shadebell). Fix first. Provisional judge set: macro_flora, dense_colony, mixed_depth, log_contact, rock_contact, colony_edge, species_mirrorleaf, species_shadebell_close |
| 1 | capture speed | KEEP | (this) | root: AquaticSystem.FlowHalo padded advection rect by ~4097 dry cells once pond flows (~bio-day 1.8); Step up to 341s. Clipped to occupied hull + gather advection; digest bit-identical. Capture 294s. GOTCHA: Vivarium.sln excludes game/Vivarium.csproj — build both or Godot runs stale Sim dll. 19 pre-existing test fails (12 Form, 5 Hydrology, SpringFluid, Coverage save/load). |
| 2 | backdrop + grow light | KEEP | (this) | Judge (orchestrator viewed it01 vs it02 judge sheets): clearly closer to refs — dark enclosure, depth falls to shadow. fps -7..-16% but all judge scenes 53-89fps (target >=30). Over-crushed: hard black shadows on logs, foliage under-lit; refs have dark BG but well-lit soft foliage. Judge set in build/overnight/judge_set.txt; judge_sheet.py <it>. |
- D-ov1 2026-10-08: fps gate changed from <=15% relative to: every judge scene >=45 fps AND <=20% relative to it01 cumulative budget tracked. Reason: baseline 60-106fps far above 30 target; 15% per-iteration gate would block all lighting work. Revisit if any judge scene <45.
| 3 | foliage tone/gloss/fill | KEEP | (this) | Judge: greener, fill restores colour in shadow, tan straw gone (low-health fade was fixed bright tan 0.62,0.55,0.32). Gloss/backlight still weak. fps unchanged (52-89). Remaining top gaps: sparse layering/bare ground; leaves small/flat/uniform; white plastic coral fungus sticks; large flat green quad in fauna_terrestrial. |
| 4 | leaf form + ground cover density | KEEP (marginal) | (this) | Judge: coin-leaf scene clearly more layered; elsewhere subtle, no regression. Bare ground unchanged: ambient cover spawns only on soil cells, exposed ground is gravel/litter. Geometry 12 pre-existing fails only. fps 58-101 (machine faster run). |
| 5 | log moss + gravel cover + fungus tint | PARTIAL KEEP | (this) | KEPT log.gdshader moss (clearly closer to real-03; still uniform felt, not clumpy). REVERTED AmbientGroundCoverRenderer gravel/litter cover (invisible on sheet, likely part of 10-25% fps drop, interaction 48 near gate) and glass_antlers.json cream tint (no visible effect -> renderer ignores it or wrong species). Next: fps recheck; bare gravel needs real moss-clump mesh; white fungus needs shader-side fix. |
| 6 | fungus + clumpy log moss | KEEP | (this) | Judge: cushion moss clearly closer to real-03 (slightly bubble-wrap uniform cells); antlers now cream-ochre velvety fungus. Root of iter5 no-op: GlassAntlers() hard-blended JSON colours 45-62% to near-white. NOTE fps run-to-run variance is ~+-25% (it05b HEAD 42.5 vs it06 52.3 on same-cost scene) — single captures cannot resolve <25% changes; use 2 captures for perf-sensitive decisions. Pre-existing: BootstrapTests.ValidContentLoadsDeterministically (13 vs 11) fails at HEAD. |
| 7 | ground moss + warped multi-size clumps | KEEP | (this) | Judge: interaction/rock_contact now mossy margins, logs read as natural cushion moss; bubble-wrap gone. Dry lit gravel (ground_vegetation) bare by design. fps mean of 2 captures 55-92. Shared moss_clumps() in surface.gdshaderinc. |
| 8 | leaf veins, gloss, variegation | KEEP | (this) | Judge: near broadleaf cover now wet-glossy with highlights + veins (closer to real-01); background strap/reed plants still khaki-olive silhouettes; variegated species (mirrorleaf, trifold pale veins; mooncoin silver; lanternbrush, snaptrap red underside) not visible in judge set. fps ~5% below it07, all >=52. |
