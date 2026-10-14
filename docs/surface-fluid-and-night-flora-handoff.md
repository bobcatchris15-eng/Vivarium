# Surface fluid, night flora, and fruit recruitment handoff

**Status superseded October 11, 2026:** the direct fluid rebuild and gentle concurrent lighting correction are now implemented. See [surface-liquid-rebuild.md](surface-liquid-rebuild.md) for current ownership, integration and evidence. The remaining text preserves the earlier handoff and candidate failures.

Prepared October 10, 2026, America/Chicago. The user requested a handoff to conserve quota. Implementation and worker testing have stopped. Do not interpret this document as acceptance of the fluid replacement.

## Resume intent and binding requirements

The user repeatedly rejected a field/grid as the visible surface water. A maxed spring must supply persistent actual water that fills a hollow, raises its free surface, overtops a physical sill, and continues downhill into the standing water-table pond. Particles are optional: the user explicitly allows vector calculations or another numerical representation, provided the behavior and appearance are fluid. An internal support discretization is permissible; painted wet cells, tracer-derived ground ribbons, or a cosmetic stream that expires are not the outcome.

Groundwater/water-table rendering needs to look like a pond. Keep its standing reservoir distinct from moving surface volume, with named transfers at actual exposed-body contact. Do not clear all mobile water merely because a support's bed is below the table.

The user authorized detailed planning and Luna low/medium Clankers. Built-in collaboration workers were used in isolated worktrees. No external StatefulClanker workers were launched. On resume, reuse useful worktrees and evidence; do not start over or replace dirty work.

Latest flora refinement request, still unimplemented:

- Bonnet fungus: only gills glow. User called it “moonbonnet”; the new catalog ID is `ghostbonnet`. Treat this as the bonnet fungus unless later context establishes another species; do not silently rename it.
- `lanternsedge`: only seed heads glow.
- `siltstars`: a broad luminous stripe down the middle of each leaf.
- `tidebell`: a luminous strip around each leaf edge.
- All seven luminous species must cast visible colored light on nearby terrain/plants/objects at night. This explicitly supersedes the earlier emission-only/no-point-light implementation choice.
- Preserve Glowbriar's dedicated low arching cane mesh and underside mint nodules, the pampas-like multi-tuft grass, and all seven night species. Grass is the eighth species and is non-luminous.

## Workspace and preservation

Main checkout: `E:\Vivarium`. Base revision at dispatch: `8f65b553d0278b0fe543d5736671a2460d93c804`.

The checkout already contained substantial unrelated dirty tracked and untracked work. Never reset, clean, broadly stage, or overwrite it. Main hashes captured before this work are in `.clanker/tasks/surface-fluid-coordination/baseline-hashes.json`, with `baseline.patch`. Individual task baseline hashes also exist. Main source has subsequently received the scoped flora and fruit changes listed below, so the initial hash list is a preservation baseline, not the current expected hash for those integrated files.

The user's Godot game and editor were left running. Do not kill, restart, install into, or use those processes for fixtures. No push, release, or user-save migration occurred. Compiler servers are not test sessions: the solver worker reported PID 26404 as Roslyn VBCSCompiler; do not kill it just because it is `dotnet`. The worker stopped its own two stalled test sessions before handoff.

Read repository AGENTS.md and the installed Clanker contracts before dispatch. The existing migration directive was not a request to upgrade AGENTS.md during this task.

## Worktrees and evidence locations

| Location | Contents / status |
|---|---|
| `.clanker/wt/surface-fluid-solver` | Candidate solver and shared terrain capacity. Latest face-flux experiment is UNVALIDATED. No production integration. |
| `.clanker/wt/night-flora-visual` | Integrated flora visual source snapshot plus isolated conserved-water mesh, copied solver/helper, and unrun water review fixture. Its solver copy differs from the newest solver worktree. |
| `.clanker/wt/fruit-lifecycle` | Integrated fruit fix snapshot, independent fluid audit, and actual saved-terrain probe pinned to an older solver SHA. |
| `.clanker/wt/night-flora-light` | Exact dirty main-source snapshot created for latest glow/light refinement. Worker read instructions and inspected code, then stopped. NO worker edits, processes, builds, or tests. |
| `.clanker/wt/night-flora-content` | Completed species content implementation; integrated into main. |
| `.clanker/wt/surface-fluid-tests` and `surface-fluid-audit` | Earlier independent contract tests/audit. Preserve historical evidence. |

Lighting baseline hash file is in MAIN at `E:\Vivarium\.clanker\tasks\night-flora-light\baseline-hashes.json`, not inside the lighting worktree. The worker's report that it could not find it refers to the worktree-relative location; the main file was written successfully.

Canonical plans/audits already in main `docs/`:

- `particle-surface-water-replacement-plan.md` — stable filename; particles are provisional/optional, not binding.
- `adaptive-water-volume-mesh-brief.md`.
- `fluid-removal-integration-audit.md` and `fluid-behavior-contract.md`.
- `fluid-solver-candidate-audit.md`.
- `actualworld-saved-geometry-fluid-probe.md` — main copy may precede the last point-source addition; newest report remains in fruit-lifecycle worktree.
- `night-flora-expansion-plan.md`, `night-flora-catalog.md`, `night-flora-visuals.md`.
- `fruit-recruitment-audit.md`.

## Integrated main-source changes and established evidence

### Eight flora species

Content, catalog registration, schema, and suitability changes are integrated. The seven luminous IDs are `glowbriar`, `lanternsedge`, `tidebell`, `siltstars`, `ghostbonnet`, `starmoss`, `moonfern`. `plumegrass` is non-luminous. Siltstars is a substrate-rooted aquatic floor grower with `unlimitedWaterDepth=true`, not a huge maximum-depth sentinel; it refuses dry establishment.

Night emission is bound through `game/Render/NightFloraMaterials.cs`, flora/leaf shaders, renderer materials, and EnvironmentRig. The 1,200-second presentation cycle fades in from 660–900 seconds, stays high through 1,140, then fades out by 1,200. Current glow masks remain broader than the latest user request, and current emission does NOT cast local light.

Glowbriar initially reused `shrub_lanternbrush`. User flagged it as a clone. It now uses dedicated `shrub_glowbriar`: low spreading arching canes, narrow alternating leaves, and small mint nodules beneath the canes. Pampas plume pigment is separately marked so it remains pale instead of being recolored green by foliage material profiles. Siltstars has low rooted star-rosette geometry.

Integrated files include OrganismMeshes dispatch/new NightFlora partial, NightFloraMaterials, shader bindings, EnvironmentRig, FloraSurfaceProfiles, OrganismRenderers, NightFloraReview, content schema/loader, FloraSystem, eight JSON definitions/index, focused tests, and docs. Integration receipt: `.clanker/tasks/night-flora-visual/integration.json`.

Validation: main combined NightFloraContentTests/NightFloraGeometryTests passed 8/8. Isolated Godot 4.7.1 Forward+ review on GTX 1080 exited 0 with `NIGHT_FLORA_REVIEW_OK` and no reported shader errors. Captures: `.clanker/tasks/night-flora-visual/captures/{day.png,night.png,plumegrass-close.png,review.json}`. These validate fixture materials/morphology, not full-world ecological performance or cast light. Review lighting was adjusted for legibility and is not exact gameplay illumination. No live-world restart was performed by this session.

### Fruit and shrub recruitment

Fruit release already existed. Reproduction-enabled shrubs skip generic spreading and depended on a broken seed path: seed lots retried one crowded cell, tiny viable lots were discarded, and nonviable kernel mass was omitted from released material accounting.

Integrated repair in `src/Vivarium.Sim/Flora/Reproduction.cs`:

- Woody gravity/animal-fallback fruit lands once in a deterministic per-individual crown-edge cell within the authored parent-centered dispersal radius and outside adult minimum spacing.
- Fruit and seeds share that landing point; seed retries never relocate the seed.
- Trace viable lots are retained down to `1e-9` and can aggregate.
- Germination waits for enough viable mass to fund the full configured initial biomass, then consumes that amount.
- Nonviable kernel mass stays in fallen-fruit litter; total release mass is accounted.

Shadebell's old dispersal radius 0.55 m was smaller than its minimum spacing 0.62 m, making parent-centered recruitment impossible. Its reproduction.dispersalRadius field was narrowly changed to 0.85 m (mature crown radius 0.78 m), preserving other dirty JSON work.

Main combined FruitRecruitmentRegressionTests/ReproductionTests passed 12/12, including actual Embercrown/Lanternbrush/Shadebell definitions over 900 direct-system simulated days, bounded deposition/recruitment distances, fixed positions, dormancy, and mass. Receipt: `.clanker/tasks/fruit-lifecycle/integration.json`. This is not proof of indefinite population stability in the full scheduled live world. Animal carriage and water seed transport remain deferred; `animal` still uses a local fallback. No persistence schema change was made.

## Fluid: what exists and what does NOT pass

Production still uses saved `Hydrology.Depth` and face momentum, plus real-time non-authoritative `SpringFluidSystem` cosmetic droplets. GameSession attaches `WaterRenderer` and tracer-derived `WaterSurfaceRenderer`. Decorative droplets expire, and the surface is not the requested fluid. `ParticleFluid` was an unowned candidate; its prototype measured prohibitively expensive and did not prove basin pressure. Do not integrate it or relabel unchanged Hydrology as completion.

New isolated candidate: `src/Vivarium.Sim/Water/WaterVolumeGraph.cs`, `WaterSupportCapacity.cs`, and `tests/Vivarium.Sim.Tests/FluidSolverFoundationTests.cs` in solver worktree.

It stores sparse conserved m³ by terrain support and signed edge discharge, uses local-inertial pressure/flux, adaptive CFL stepping, explicit owed time, queued source-history segments, snapshot restore, and named boundary outflow. Internal topology remains rectilinear four-neighbor supports. It is not yet adaptive/irregular geometry and has no production caller.

Terrain capacity now integrates `V(head)` over each support's actual SW→NE two terrain triangles and inverts to head using safeguarded Newton/wetted-area derivative with bisection fallback. Solver and renderer must use identical terrain geometry. The correct fully-wet mean is `(2*SW + SE + 2*NE + NW)/6`; four-corner average was found to introduce a capacity discontinuity and corrected. Finite corner validation and asymmetric-corner regression were added. Node head is cached to avoid repeated inversion in each edge calculation.

Source injection must be localized at the real outlet. Earlier probes used arbitrary radius 0.3 m support discs, wider than the actual carved spring hole radius 0.20 m/depth 0.08 m. These measurements are comparison data; newer solver tests use one NodeAt support. A support itself still may be too coarse for the precise vent; source geometry/local refinement is unresolved.

Last complete solver suite BEFORE the newest wet-face flux change: 20/20 and build 0 warnings/errors. Earlier checks included conservation, queued source turn-off history, downhill movement from rest, pause/restore, boundary transfer, quiet-bowl equilibrium (~0.66 mm head spread), and coarse/fine resolution comparisons. Frame batching 20 versus 10+10 seconds had ~6 micrometres head difference, NOT an established bitwise-digest equivalence for arbitrary Advance partitions.

The original “no spill by 25 seconds” diagnostic needed correction: center-based outside classification counted partially wet shoreline supports. New wholly-outside-support measurement was zero at 10 s and ~0.00014665 m³ at 25 s. At 25 s basin max head ~0.488061 m exceeded measured minimum crossing sill ~0.477545 m; this was a legitimate computed sill crossing, not proof of pre-sill leakage. Revalidate against exact geometry/analytic capacity rather than accepting or relaxing a timestamp gate without a physical explanation.

### Latest current solver is UNVALIDATED

The newest experiment stores both face endpoint elevations and integrates the actually wetted sloping face section, rather than transporting through the full edge width at a minimum-endpoint sill. It exposed severe adaptive/CFL throughput failure: a full suite became catastrophically slow in a 3,125-second fixture (worker reported >825 CPU seconds) and was interrupted. A second run after relaxing CFL to full sill-depth was stopped at the user's handoff request BEFORE results. Old 20/20 logs do not validate this current source.

Current solver handoff hashes:

| File | SHA-256 |
|---|---|
| `WaterSupportCapacity.cs` | `1FA0BC7C547E9614C6650C54ACFD6F0C56A1E625C0849EE3013935BDEBF78279` |
| `WaterVolumeGraph.cs` | `ECF9151609A9A9472EB49E84636D69927F5F88B2FFA13AC206B1410095BFE3A8` |
| `FluidSolverFoundationTests.cs` | `E76F91CF40727CF93FEC27CAB3925AF5B6AA968963F5F60720F368E4174DD6AA` |

Do not silently discard the experiment. Inspect/diff it, preserve failure evidence, then choose a bounded stable correction or an explicit documented rollback of the face-flux experiment while retaining shared terrain capacity.

Clock costs matter: physical authoritative ticks are 10 seconds; current legacy hydrology cadence is 20 seconds. Default wall-to-sim rate is 168 sim seconds per real second, rising to 2,688 at 16×. Early fixed 1/30 stepping was too expensive. Adaptive flat-support basin improved to about 70 ms for 125 sim seconds, but later exact capacity/partial-face changes require new performance evidence. Default and high-speed throughput/backlog are unpassed. No time/source-volume dropping to hide cost.

## Conserved surface prototype and unrun fixture

In night-flora-visual worktree only:

- `src/Vivarium.Sim/Geometry/ConservedWaterSurface.cs`.
- `tests/Vivarium.Sim.Tests/ConservedWaterSurfaceTests.cs`.
- `game/App/ConservedWaterReview.cs` and minimal Main mode dispatch `--conserved-water-review OUT`.
- Copied `WaterSupportCapacity.cs` and `WaterVolumeGraph.cs` from an older solver snapshot.
- `docs/conserved-water-surface.md` and main `.clanker/tasks/conserved-water-surface-prototype/receipt.json`.

Initial positive-depth interpolation was rejected: dry head=terrain elevation generated triangular fringes and ramps. Actual flat-support snapshots then revealed rendered volume 12–20% below stored volume. Exact capacity extraction replaced that assumption. Reported per-support mesh errors: 4.025% at10s, 0.953% at25s, 0.010% at125s, 0.195% after30s source-off. These used older copied helper/solver and broad source input; rerun against corrected current source, point outlet and current weighted mean before using them as acceptance.

Major unresolved blocker: adjacent wet supports have unequal heads; seam counts were 23/52/460/533 at these checkpoints. A set of separated flat support patches is still unacceptable as the final surface. Do not cosmetically smooth it while inventing/losing volume. Need conservative free-surface/head reconstruction or another representation that maintains continuous actual water and the same volume/pressure geometry.

Fixture builds with zero warnings/errors and focused mesh tests passed 4/4 at its snapshot. Godot fluid fixture has NOT been launched/captured. It currently uses an infinite paraboloid bowl, 0.10 m supports, a broad 0.22 m source footprint, and `water_surface.gdshader`; it does NOT yet test a spill sill/downhill channel and is not the same actual-rate .8m/.05m basin. Correct those fixture assumptions before treating its images as task evidence. Shader expects thickness in UV2.x; candidate mesh currently does not supply physical thickness consistently, so optical review also needs that wiring. The fixture's large Advance calls should explicitly drain pending work before labeling snapshots 10/25/125 seconds.

Keep fixture user data isolated. The visual worktree's project.godot uses `Vivarium-night-flora-review`; never copy that project.godot into main. An isolated Godot process is allowed for evidence, but leave the user's game/editor untouched.

## Actual saved-world numerical evidence

Read-only source save was copied to MAIN `.clanker/tasks/surface-fluid-coordination/user-autosave-reference.vivsave`. Original `%APPDATA%\Vivarium\saves\autosave.vivsave` was untouched. Copied manifest: Twin Meadows Creek #3481, seed63481, preset oxbow_creek, ~45.56 bio days, saved October10 ~8:37PM Chicago. It may differ from the user's current play state.

Actual saved world spring is at `(-1.7753101059412013, 2.1490232174376147)`, discharge `0.0005555555555555556 m³/s` (2,000 L/hour). World spring list is authoritative; descriptor legacy spring `.12` is not this rate. Water table is0. Config evaporation/infiltration `.004/.02` are converted per simulated day in old Hydrology; do not confuse them with per-second loss.

Probe lives in fruit-lifecycle worktree `SavedTerrainFluidProbeTests.cs`, report and `.clanker/tasks/actualworld-fluid-probe/`. It is pinned to old graph SHA `05A43A4F65E300767FD8A8962645067E72DBB0506E93FCDC52657958BABB0B83`, using cell-center beds and no new exact capacity. It started mobile water empty and sampled loaded GroundHeight including saved edits, roots and props.

Broad source at1000s: supplied0.5555555556 = retained0.3424069921 + boundary0.2131485635, residual1.35e-12m³. Initial apparent loss was incomplete accounting, corrected before handoff. Point-source variant at1000s: retained0.342542485, boundary0.213013071, residual~6.9e-12. Point-source path absent10/25s, present125/1000s. Queued physical time lag existed early and caught up by125s.

The bent 4-neighbor wet path to a receiver was verified; straight-line dry samples did not establish disconnection. Broad path was connected972/1000 one-second samples at positive depth and961/1000 above1e-5m. Downhill receiver `(0.875,0.875)` bed~-0.60541, depth~0.098m at125s and~0.451m at1000s. Route first entered below-table ground at `(0.875,1.125)`. Mobile surface there remained below standing table0, so pond joining/transfer remains unimplemented.

Source geometry discrepancy: exact GroundHeight at source0.476995m versus mapped support center0.511015m, a34mm error. Radial terrain scan found an escape direction with no sampled rise above the source; the saved sample does not prove a closed hollow around that point. Do not declare the user's observed issue solved on that basis; maintain controlled bowl and actual saved-terrain tests separately, and resolve the exact outlet/vent geometry.

## Latest lighting task: ready to resume, no implementation

The lighting worker stopped without edits/tests/processes. Worktree `.clanker/wt/night-flora-light` contains a current main snapshot. Inspect main baseline hashes before integration.

Useful findings:

- NightFloraMaterials currently binds shader emission only.
- Current shader mask modes: whole body1, foliage0, Glowbriar nodule2 using UV2.x~1.4.
- Existing mushroom mesh already marks gills UV2.x=1 and cap/stalk UV2.x=0: use a fungus-specific gill mask, not a whole-body mask.
- Lanternsedge uses Glassrush geometry; seed pods need an explicit organ marker (stems/pods currently unmarked).
- Tidebell uses generic paired-leaf geometry; its edge mask must follow actual blade perimeter, including tips/attachment where appropriate.
- Siltstars uses FoliageBlade; implement a broad center stripe in organ UVs without lighting the full blade or runners.
- Add actual local Godot light contribution, ideally stable pooled/spatially bounded shadowless lights or conservative emitter clusters. All seven species must contribute visibly; no silent exclusion of small moss/fungus or aquatic-floor emitters. Day fade must disable contribution. Test/capture receiving non-emissive ground and neighboring objects, not merely a bright emissive plant on black background. Measure whole-world light count/frame cost before claiming performance.

## Recommended next order and commands

1. Read this handoff, inspect current source/hash/diff in each worktree, and verify all paused worker checkpoints. Avoid blindly trusting older validation fields.
2. Resume the new luminous-organ/cast-light task in its isolated worktree independently of fluid. Capture organ closeups and cast light; integrate only scoped reviewed changes against main hashes.
3. Fix or explicitly unwind the unvalidated narrow wet-face/CFL experiment. Run short focused cases first with a timeout and preserved output; do not repeat the unbounded 3,125s test before a small throughput probe passes.
4. Validate current shared terrain capacity, point-source vent resolution, source-history accounting, equilibrium, geometry-aware pre-sill/outside classification, boundary transfer, and default/high-speed throughput. No stale old-suite result as current acceptance.
5. Solve continuous free-surface reconstruction with actual mesh volume correspondence; replace the fixture assumptions and capture filling/rising/overtopping/downhill/source-off persistence. Actual saved-terrain path/pond joining then follows.
6. Only after credible candidate evidence, do W3–W6 runtime/consumer, save migration, tool/collision, renderer and obsolete-authority removal in isolated integration. One moving-water owner; no legacy depth field or decorative tracer surface remaining alongside it. Preserve the independent standing pond.
7. W7: target hardware/game time sequence and sustained performance with explicit frame percentiles, memory, solver/mesh timings, source budget and backlog. No user-world restart without coordination with the user.

Already passed main checks (do not rerun just to reproduce old receipts):

```powershell
dotnet test tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj --no-restore --filter 'FullyQualifiedName~NightFloraContentTests|FullyQualifiedName~NightFloraGeometryTests' --verbosity minimal
dotnet test tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj --no-restore --filter 'FullyQualifiedName~FruitRecruitmentRegressionTests|FullyQualifiedName~ReproductionTests' --verbosity minimal
```

Candidate focused command, from SOLVER worktree, only after addressing latest slow experiment:

```powershell
dotnet test tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj --no-restore --filter FullyQualifiedName~FluidSolverFoundationTests --logger 'console;verbosity=detailed'
```

Godot binary: `E:\Vivarium\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe`. Fixture command shape is `--path <isolated worktree>\game --log-file <evidence log> -- --conserved-water-review <output>`. Launch any helper with `Start-Process -WindowStyle Hidden`; never substitute the running user's process. Import/build isolated source as needed. No fluid runtime capture exists yet.

Resume instruction for next agent: **Finish physical, continuous, conserved surface water; do not deliver another painted field or cosmetic disappearing stream. Also complete the latest organ-only glow and real cast-light refinements. Respect the quota pause and preserved live/dirty workspace.**
