# Vivarium photoreal overhaul — handoff (2026-09-25)

Read this first, then `.claude/ledger.md` (task table and decisions), then `CLAUDE.md` (`## VALIDATE`).
Design docs: `docs/overhaul/S1_S2_plan.md` (form kernel), `docs/overhaul/growth_models.md` (coverage growth; **§6R** plasmodium revision; **§15** aquatic flora).
Baseline images: `docs/baseline/v0.1.2_sheet*.jpg`. Latest valid full capture of main: `build/reference/pair_main` (older), per-feature captures in `build/reference/<task>/`.

## 0. Rules that cost us hours (do not skip)

1. **Worktree seed.** A fresh git worktree has no `game/.godot` import cache, so textures fail, `IslandRenderer` throws `IndexOutOfRange`, and **captures come out blank with meaningless FPS**. Before any Godot run in a worktree:
   `robocopy C:\Misc\Vivarium\game\.godot <wt>\game\.godot /E /XD mono /NFL /NDL /NJH /NJS`
   `/XD mono` is mandatory; otherwise a stale game DLL overrides your build. Run `dotnet build game/Vivarium.csproj -c Debug` **after** seeding and right before every Godot run. Run Godot from the main tree: `C:/Misc/Vivarium/tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path <wt>/game -- --reference <ABS_DIR>`.
2. **Capture validity gate.** A capture counts only if its log has no `IndexOutOfRange` and no `Unable to open file`, AND `reference_report.json` scenes have `flora_visible > 0`. Always pass ABSOLUTE output dirs (relative ones land under `game/`).
3. **Vsync is off in perf/reference modes** (commit ef67465). FPS numbers before that were capped at ~60 and meaningless. Compare FPS only between runs made back-to-back in the same session, one Godot at a time. Concurrent Godot instances make perf numbers noise.
4. **Always look at the pictures.** Numeric asserts were gamed repeatedly: foliose lichen "lobes" were holes, the slime "fan" was a square, a disintegrating body passed its centroid tests, and lily pads "visible" were not. Build a before/after contact sheet with PIL and inspect it before merging anything visual. Lab (growth) packets must write legible PNG frames: hue per species/id, brightness by biomass, flags visible.
5. **Packet size.** Clankers (Sonnet impl-workers) refuse or fail packets larger than about one subsystem, ≤6 files, one Validate. They often hit the 60-turn limit; resume them with "finish within ~15 turns and report". Three attempts max per packet, then stop and escalate.
6. **Never** `git add -A` from main while agents work. Stage explicit paths. Merge worktree branches with `--no-ff`. Check `git status` before assuming a tree is clean. Don't delete branches without checking they are merged (`git merge-base --is-ancestor`).
7. **Timing asserts** in unit tests must be tagged `[Trait("Speed","Slow"), Trait("Suite","Perf")]`; they flake under parallel load. The per-task test filter is `...&Speed!=Slow`.
8. The full fast suite takes ~11 min; per task, run only the touched suites: `dotnet test Vivarium.sln -c Debug --filter "(Suite=X|Suite=Y)&Speed!=Slow"`.

## 1. What is DONE on main (HEAD ≈ 5b81aee)

- **Harness:** `--reference DIR [--preset]` = 41 deterministic scenes (including coverage-aware moss/lichen scenes and `species_<id>` for every species), per-scene fps/p95/draws/primitives/visible counts, covered-area facts; `scripts/compare-reference.ps1`, `scripts/reference-sheet.py`; the perf probe has a summary block.
- **Form kernel** `src/Vivarium.Sim/Geometry/Form/*` (Axis, LeafBlade, SoftTube, Sheet, Variation). Migrated: roundleaf, pairedleaf, herb/trifoliate (partial), creeper (creeping_groundcover), fern, blue fescue, mushrooms, turkey tail, rocks, logs (partly by the previous agy session).
- **Coverage (SpatialOrganism) sim:** `src/Vivarium.Sim/Coverage/*`. Moss (MatRules) and lichen (LichenRules) run on 2 cm sparse tiles; the 7 moss/lichen species live there only. Tools can introduce and remove moss/lichen clumps (default tool species: carpet_moss).
- **Coverage renderer** `game/Render/CoverageRenderer.cs` + `game/Shaders/coverage_mat.gdshader`: organic feathered mats (the winding bug is fixed). Debug mode: env `VIVARIUM_COVERAGE_DEBUG=1` (magenta = moss, cyan = lichen).
- **Plasmodium sim (lab only, not in the game yet)** `src/Vivarium.Sim/Coverage/Plasmodium/*`: local per-cell mass with conservative flow (Pl-1), contraction phase field with rectified drift (Pl-2), Tero network, life cycle.
- **Lily pads** (`game/content/flora/lily_pad.json`, shape `floatleaf`): round, notched, flat floating pads.
- **Rendering fixes:** no occlusion culling (partially hidden plants no longer pop), closed hollow log ends, humid depth haze (`EnvironmentRig.HazeFogDensity = 0.02f`) + SSAO at default quality.
- **Uncapped perf on main:** ~44 fps mean, min-second 30, p95 36 ms (fresh world, sim live, 860M).

## 2. IN FLIGHT when quota ran out (worktrees under `.claude/worktrees/`)

Each has a branch `task/<name>`. The work is **uncommitted in the worktree**. To resume one:
`git -C .claude/worktrees/<name> status` → review the diff → run the packet's Validate (below) → inspect frames/images → commit in the worktree with explicit paths → `git merge --no-ff task/<name>` from main → `git worktree remove --force .claude/worktrees/<name>` → `git branch -D task/<name>`.
If the work is broken or half-done, discard it: `git worktree remove --force` + `git branch -D`, then re-dispatch.

| worktree | packet | acceptance (summary) | Validate |
|---|---|---|---|
| `pl3` | Veins grow ONLY from cycle-averaged shuttle flow (remove Network's separate CG source/sink solve); delete the `Migrating` state, with light/dryness as local ω/maintenance costs. §6R.7–8 | two_food ≤1.2× Euclidean with one dominant path; maze within 15% of BFS; fusion; new `stress_avoidance` drifts into the dark/moist half; coherence (1 component, ≥80% fill); exact mass conservation; **no solver reintroduced** — if emergence fails, report the missing mechanism | `dotnet build src/Vivarium.Sim -c Debug` + `dotnet test Vivarium.sln -c Debug --filter "(Suite=Coverage\|Suite=GrowthLab)&Speed!=Slow"` |
| `aq1` | Duckweed (`SurfaceFloat`) + algae (`AlgaeBed`, `AlgaeFloat`) coverage rules with upwind mass-conserving flow advection, `IAquaticEnv` interface. §15.1–15.2 | lab: duckweed fills a still bay (≥3× channel density), piles upstream of an obstacle; algae scoured by flow; duckweed shading suppresses bed algae; floating mats bloom in still, nutrient-rich water and drift to the margin; determinism | same as pl3 |

**mat2 finished as FAIL (honest):** shader-only micro-detail, still reads as paint. Its shader work is committed on branch `task/mat2` (worktree removed, NOT merged). Split into mat2a + mat2b below; mat2a may start from `task/mat2`.

## 3. QUEUE (next packets, in recommended order)

0a. **mat2a — mat geometry relief.** `game/Render/CoverageRenderer.cs` only: visible thickness and domes (cushion/sphagnum via D2E, carpet 2–4 mm, lichen ~0.5 mm, rounded edge falloff), normals recomputed from the displaced surface. Validate by capture inspection (species_cushion_moss must show a dome, dense_colony visible thickness at the edge). Paired perf ≥ 85%.
0b. **mat2b — instanced micro-shoots.** New `game/Render/CoverageShoots.cs`: MultiMesh of tiny tapered moss shoots / star tips (and pale branch tips for reindeer lichen), count from B, concentrated at rim/young cells and the cushion surface, hashed deterministic placement, frustum-culled per tile, skip only sub-pixel. This is the highest-leverage item for "moss, not paint". Paired perf ≥ 85%.

Dispatch at most 3 at once, each in its own seeded worktree. Packets must stay one subsystem and ≤6 files.

1. **Pl-4: plasmodium in the game.** `SpatialOrganism` base class (Plasmodium + ColonialMat); a `plasmodium` scheduler system every **3 ticks (30 sim-s)**; remove the slime_mold `FloraIndividual` path and its old bud-tree/vein renderer in `FloraSystem` + `OrganismRenderers`; seed plasmodia from the spore bank/starters; save/load + digest. Depends on pl3. (The old in-game slime is still the crawling budding version.)
2. **P3-slime renderer.** Per §6R "Rendering":
   - thin glossy translucent front, broad low vein ridges merged into the sheet (from D), duller withdrawing regions, fading silvery residue;
   - vein swelling wave in the shader from sim θ with a **wall-clock** phase offset (natural pulse at any game speed);
   - fruiting bodies as small kernel meshes.

   Read-only on the sim.
3. **Aq-2: aquatic in the world.**
   - `Biofilm` field derived from AlgaeBed biomass (grazers eat algae);
   - duckweed/algae species content + world seeding + scheduler; real `IAquaticEnv` adapter over hydrology;
   - lily pads block duckweed advection.

   Depends on aq1.
4. **Aq-4: aquatic renderers.**
   - duckweed = instanced 2–4 mm fronds, count from density, hashed placement, bobbing with the water shader, with a continuous frond-mat texture in dense areas;
   - floating algae = thin stringy translucent sheets with bubbles;
   - bed film = colour/roughness overlay on submerged terrain.
5. **Climbing vine leaves.** The next-worst flat icon: huge flat faceted leaves (seen in species_creeping_groundcover and species_climbing_vine). Rebuild with the kernel at the SAME scale.
6. **Creeper tuning.** creeping_groundcover reads as thick straight sticks with too few tiny leaves; make the stolons thinner and curved, with more leaves.
7. **Edge field bug.** `ScalarField.Sample` bilinear near the domain edge averages in out-of-domain cells (moisture 0.7 reads ~0.35). Clamp/renormalise weights to in-domain cells. Affects habitat near the island edge.
8. **Re-baseline FPS.** Capture `docs/baseline/` again with vsync off on current main, one Godot only, for honest comparisons.
9. Later campaigns (see the ledger stage graph): fauna body plans (fish are flat cartoons), rocks (currently low-poly faceted — reads as game art), turkey tail still a flat banded disc, soil/litter micro-detail, materials, lighting, visibility-first perf (chunked MultiMeshes), taxonomy.

## 4. Known open issues / tuning notes

- Foliose lichen reads as coral/DLA instead of a radial rosette (LichenRules tip bias).
- The Tero retraction left lattice chevron artefacts; the lifecycle residue is an odd rectangle; the drought scenario seed never grows (lab).
- Moss dense-step cost is 19.7 ms on a pathological fully-dense lab case (D2E dominates); not yet re-measured on the real island.
- The dark blob in the sky in the `mixed_depth` scene is unidentified.
- Some species close-ups are still occluded by foreground foliage (the harness orbit ignores flora).
- Old stale branches (`task/gma`, `gmb`, `p1d`, `s2b-fern`, `s2b-grass`, `s2d-mushroom`, `s4-rock`, `worktree-agent-a971fc…`) were left untouched; check each is merged before deleting.

## 5. Standing user decisions

- Photorealism outranks ecology accuracy and compatibility; breaking changes are free. ≥30 FPS on a Radeon 860M. **No billboard/blob LOD**; a visible organism stays the same organism. Haze may hide genuinely imperceptible detail.
- Slime mold = spatial organism (no transform, no steering); shortest paths must EMERGE (never program them). The contraction rhythm runs in sim-seconds (= real time at 1×) plus a wall-clock visual pulse.
- Algae/duckweed patterns come from hydrology flow, not placement. Lily pads are rooted individuals.
- The user can introduce/remove moss and lichen clumps.
- Claude (or the active agent) critiques visuals; the user does the final look.
