# Vivarium photoreal overhaul — handoff (2026-09-25)

## Latest checkpoint — 2026-09-25

`main` at `be0789a` has been pushed to `origin/main`. It includes the edge field sampling fix, the continuous gravel blend, and the latest moss/lichen volume and rendering pass. The moss now has merged pillow relief, while lichen has a crinkled sheet surface. Moss texture projection was changed to avoid stretching over logs; review further renders before calling either material final. The moss/lichen reference pass completed 44/44 scenes; the paired moss performance comparison had median FPS ratio 0.995 and minimum 0.94 across 41 shared scenes. See `build/reference/mat2a-paired-candidate` and `build/reference/mat2a-paired-main`.

Three unfinished branches are pushed to origin and remain separate from main:

- `task/aq2-biofilm` at `921c4e5`: aquatic world integration and biofilm work, including a WIP performance pass. The temporary benchmark is saved as `AquaticBenchTemporary.cs.disabled` so it cannot make the test suite fail by design. Focused tests passed 47/47 before this latest WIP; rerun them and complete visual/performance review before merging.
- `task/pl3` at `8d058db`: plasmodium cycle averaged flow work. Focused tests pass 62/63; two-food sheet retraction remains unresolved. Do not merge yet.
- `task/vine-form` at `dbf7214`: climbing vine form work. Form tests passed 39/39; visual and performance review remain before merge.

The sections below preserve the original project plan and historical checkpoint; their worktree status table predates these merges. The full S1–S7 overhaul remains in progress.

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
5. **Packet size.** Clankers (Sonnet/Flash impl-workers) refuse or fail packets larger than about one subsystem, ≤6 files, one Validate. They often hit turn limits; resume them with "finish within ~15 turns and report". Three attempts max per packet, then stop and escalate.
6. **Never** `git add -A` from main while agents work. Stage explicit paths. Merge worktree branches with `--no-ff`. Check `git status` before assuming a tree is clean. Don't delete branches without checking they are merged (`git merge-base --is-ancestor`).
7. **Timing asserts** in unit tests must be tagged `[Trait("Speed","Slow"), Trait("Suite","Perf")]`; they flake under parallel load. The per-task test filter is `...&Speed!=Slow`.
8. The full fast suite takes ~11 min; per task, run only the touched suites: `dotnet test Vivarium.sln -c Debug --filter "(Suite=X|Suite=Y)&Speed!=Slow"`.

## 1. What is DONE on main (HEAD ≈ e2a2b0d)

- **Aquatic coverage sim (aq1 - MERGED in e2a2b0d):** Duckweed (`SurfaceFloat`) and algae (`AlgaeBed`, `AlgaeFloat`) coverage rules with mass-conserving upwind flow advection, `IAquaticEnv` interface (`src/Vivarium.Sim/Coverage/Aquatic/`). All 66 tests in `AquaticScenarios` pass; 41/41 mainline `GrowthLab` tests pass.
- **Harness:** `--reference DIR [--preset]` = 41 deterministic scenes (including coverage-aware moss/lichen scenes and `species_<id>` for every species), per-scene fps/p95/draws/primitives/visible counts, covered-area facts; `scripts/compare-reference.ps1`, `scripts/reference-sheet.py`; the perf probe has a summary block.
- **Form kernel:** `src/Vivarium.Sim/Geometry/Form/*` (Axis, LeafBlade, SoftTube, Sheet, Variation). Migrated: roundleaf, pairedleaf, herb/trifoliate (partial), creeper (creeping_groundcover), fern, blue fescue, mushrooms, turkey tail, rocks, logs.
- **Coverage (SpatialOrganism) sim:** `src/Vivarium.Sim/Coverage/*`. Moss (MatRules) and lichen (LichenRules) run on 2 cm sparse tiles; the 7 moss/lichen species live there only. Tools can introduce and remove moss/lichen clumps (default tool species: carpet_moss).
- **Coverage renderer:** `game/Render/CoverageRenderer.cs` + `game/Shaders/coverage_mat.gdshader`: organic feathered mats (the winding bug is fixed). Debug mode: env `VIVARIUM_COVERAGE_DEBUG=1` (magenta = moss, cyan = lichen).
- **Plasmodium sim (lab only, not in the game yet):** `src/Vivarium.Sim/Coverage/Plasmodium/*`: local per-cell mass with conservative flow (Pl-1), contraction phase field with rectified drift (Pl-2), Tero network, life cycle.
- **Lily pads:** (`game/content/flora/lily_pad.json`, shape `floatleaf`): round, notched, flat floating pads.
- **Rendering fixes:** no occlusion culling (partially hidden plants no longer pop), closed hollow log ends, humid depth haze (`EnvironmentRig.HazeFogDensity = 0.02f`) + SSAO at default quality.
- **Uncapped perf on main:** ~44 fps mean, min-second 30, p95 36 ms (fresh world, sim live, 860M).

## 2. IN FLIGHT worktrees (under `.claude/worktrees/`)

Each has a branch `task/<name>`. The work is **uncommitted in the worktree**. To resume one:
`git -C .claude/worktrees/<name> status` → review the diff → run the packet's Validate → inspect frames/images → commit in the worktree with explicit paths → `git merge --no-ff task/<name>` from main → `git worktree remove --force .claude/worktrees/<name>` → `git branch -D task/<name>`.

| worktree | branch | packet | acceptance (summary) | Validate | Current Status |
|---|---|---|---|---|---|
| `pl3` | `task/pl3` | Veins grow ONLY from cycle-averaged shuttle flow (remove Network's separate CG source/sink solve); delete `Migrating` state, light/dryness as local ω/maintenance costs. §6R.7–8 | two_food ≤1.2× Euclidean with 1 dominant path; maze within 15% of BFS; fusion; `stress_avoidance` drifts into dark/moist; coherence; exact mass conservation; **no solver reintroduced** | `dotnet build src/Vivarium.Sim -c Debug` + `dotnet test Vivarium.sln -c Debug --filter "(Suite=Coverage\|Suite=GrowthLab)&Speed!=Slow"` | 57/60 pass. 3 failures: `TwoFoodSheetConvergesToOnePathAndRetracts` (sheet retraction off-vein), `StarvationFruitsAtFormerHubs` (fruiting transition), `MazeSurvivingPathMatchesShortestPath` (S-to-E connectivity). In `Foraging.cs`, boundary cells were preventing retraction; needs completion. |
| `mat2a` | `task/mat2a` | Mat geometry relief in `game/Render/CoverageRenderer.cs`. Visible thickness and domes (cushion/sphagnum via D2E, carpet 2–4 mm, lichen ~0.5 mm, rounded edge falloff), normals recomputed from displaced surface. | Displace coverage mesh vertices along surface normal; cushion domes; carpet/lichen thickness; recompute normals for lighting/AO. Paired perf ≥ 85%. | `dotnet build game/Vivarium.csproj -c Debug` + reference capture inspection | Worktree created from `task/mat2`, merged with `main`, seeded with `game/.godot`. Ready for / undergoing displacement implementation in `CoverageRenderer.cs`. |
| `edgefld` | `task/edgefld` | `ScalarField.Sample` bilinear near domain edge averages out-of-domain cells (dilutes e.g. moisture 0.7 to ~0.35). | Clamp/renormalize weights over strictly in-domain cells so boundary sampling doesn't halve values. Add unit tests in `FieldTests`. | `dotnet test tests/Vivarium.Sim.Tests -c Debug --filter "FullyQualifiedName~FieldTests"` | Worktree created. Test `UniformFieldSamplingAtOrNearBoundaryReturnsUniformValue` added to `TimeFieldTests.cs`. Needs implementation in `src/Vivarium.Sim/Fields/Fields.cs`. |

## 3. QUEUE (next packets, in recommended order)

1. **Aq-2: aquatic in the world (UNBLOCKED by aq1 merge):**
   - Derive `Biofilm` field from `AlgaeBed` biomass (grazers eat algae biomass directly);
   - Duckweed and algae species content definitions + world seeding + hydrology scheduler;
   - Real `IAquaticEnv` adapter over hydrology (`Water.Depth`, `FlowX`/`FlowZ`);
   - Lily pads block duckweed advection locally.
   - Depends on `aq1` (which is already merged on `main`!).
2. **mat2b — instanced micro-shoots:**
   - New `game/Render/CoverageShoots.cs`: MultiMesh of tiny tapered moss shoots / star tips (and pale branch tips for reindeer lichen), count from biomass B, concentrated at rim/young cells and cushion surface, hashed deterministic placement, frustum-culled per tile, skip sub-pixel. This is the highest-leverage item for "moss, not paint".
   - Depends on `mat2a`.
3. **Pl-4: plasmodium in the game:**
   - `SpatialOrganism` base class (`Plasmodium` + `ColonialMat`);
   - Dedicated `plasmodium` scheduler system every 3 ticks (30 sim-s);
   - Remove the legacy `slime_mold` `FloraIndividual` path and old bud-tree/vein renderer in `FloraSystem` + `OrganismRenderers`;
   - Seed plasmodia from spore bank/starters; save/load + digest.
   - Depends on `pl3`.
4. **P3-slime renderer:**
   - Thin glossy translucent front, broad low vein ridges merged into the sheet (from D), duller withdrawing regions, fading silvery residue;
   - Vein swelling wave in shader from sim θ with a wall-clock phase offset (natural pulse at any game speed);
   - Fruiting bodies as small kernel meshes.
   - Read-only on the sim.
5. **Aq-4: aquatic renderers:**
   - Duckweed: instanced 2–4 mm fronds, count from density, hashed placement, bobbing with water shader, continuous frond-mat texture in dense areas;
   - Floating algae: thin stringy translucent sheets with bubbles;
   - Bed film: colour/roughness overlay on submerged terrain.
   - Depends on `Aq-2`.
6. **Climbing vine leaves (`vine`):**
   - The next-worst flat icon: huge flat faceted leaves (`species_creeping_groundcover` and `species_climbing_vine`). Rebuild with form kernel at same scale.
7. **Creeper tuning (`creep`):**
   - `creeping_groundcover` reads as thick straight sticks with too few tiny leaves; make stolons thinner and curved, with more leaves.
8. **Re-baseline FPS:**
   - Capture `docs/baseline/` again with vsync off on current main, one Godot only, for honest comparisons.

## 4. Known open issues / tuning notes

- Foliose lichen reads as coral/DLA instead of a radial rosette (`LichenRules` tip bias).
- The Tero retraction left lattice chevron artefacts; the lifecycle residue is an odd rectangle; drought scenario seed never grows (lab).
- Moss dense-step cost is 19.7 ms on a pathological fully-dense lab case (D2E dominates); not yet re-measured on the real island.
- Dark blob in the sky in `mixed_depth` scene is unidentified.
- Some species close-ups are still occluded by foreground foliage (harness orbit ignores flora).
- Stale branches (`task/gma`, `gmb`, `p1d`, `s2b-fern`, etc.) should be checked with `git merge-base --is-ancestor` before deleting.

## 5. Standing user decisions

- Photorealism outranks ecology accuracy and compatibility; breaking changes are free. ≥30 FPS on a Radeon 860M. **No billboard/blob LOD**; a visible organism stays the same organism. Haze may hide genuinely imperceptible detail.
- Slime mold = spatial organism (no transform, no steering); shortest paths must EMERGE (never program them). The contraction rhythm runs in sim-seconds (= real time at 1×) plus a wall-clock visual pulse.
- Algae/duckweed patterns come from hydrology flow, not placement. Lily pads are rooted individuals.
- User can introduce/remove moss and lichen clumps.
- Agent critiques visuals; user does final subjective look.
