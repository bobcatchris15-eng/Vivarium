# Shared hydrostatic pools — 2026-11-14 checkpoint

The reported defect is standing water below a mature high-flow spring constantly dimpling and peaking in slow motion. A synthetic physical bowl, inlet and open spillway reproduces it through the production Godot renderer without reading or changing user saves. Its spring supplies 2,000 L/hour; terrain, erosion, biological activity, camera and lighting are controlled.

## Cause and change

The old solver established individual wet cells after an age threshold, including moving cells whenever any spring was active. It then retained separate cached heads and conveyed incoming storage directly to a region's selected outlet. That made mean level and outlet discharge look settled while individual columns retained large local peaks. Disabling this behavior flattened the pool but left every cell in the expensive active solver.

The replacement retains the sparse active solver for channels and wet/dry fronts. Connected, fully submerged, low-Froude settled columns can become a shared pool. Each pool owns one conserved volume; its level is derived from exact fully wet capacity, `V = area * (count * head - sum(meanBed))`. Every boundary face is explicitly scheduled, including boundaries against sleeping or absent active neighbors. Accepted inlet/outlet amounts change pool storage, with a limiter shared by all outgoing faces. There is no entry-to-outlet teleport. Interior current derives from the moment of actual accepted boundary transfers and sources; throughflow is preserved.

Draining below fully wet support releases a body through exact capacity inversion. Source, terrain and table edits wake whole bodies; active faces receive the body's accepted mean current. Evaporation, infiltration, recharge, additions and removals stay in the same water ledger. Pool membership and mean current are saved explicitly and validated for connectivity, canonical order, capacity and complete membership. Old pool-model-0 payloads export unchanged until the first authoritative mutation, preserving the existing save digest; then their old pinned-head caches are rebuilt from conserved water. The water-table boundary must be assigned before restoring membership.

## Evidence and scope

Raw logs, frozen source/binary hashes, state and captures are under `build/water-redesign-20261114`. `baseline-v2` is the original solver with the cleared water-only native harness; `pool-v4` is the first native shared-pool observation. Final evidence, source/binary hashes and exact limits are in `results.json`; `final-native` supersedes the initial observation after the production restore-order correction.

Both native observations start at physical age 60 seconds and use exactly the same 213 fixed query positions. The mask is radius <0.12 m, x >-0.05 m, with a 3×3 neighborhood at least 4 mm wet in the baseline. All those queries remain finite. Frozen shots hold physical state while optical animation continues; live shots advance production async liquid only. Water-table triangles are zero in this fixture, so this reproduces a mobile-water solver defect independently of static-table overlap.

In the final native comparison, live pool spatial SurfaceAt P95−P5 fell from 25.16 mm to effectively zero; the 95th percentile of each point's temporal P95−P5 fell from 25.29 mm to 0.335 mm. Remaining change is mostly the whole body's level changing. The false-color normal captures make the original cones and their removal clear. Six-Hz probes are retained for comparison, while the new harness also records every frame; sparse probes can miss faster extrema.

This is a bounded visual and physical acceptance case, not whole-world performance acceptance. The initial optimized 80-second solver benchmark used 27.23 s CPU versus 22.06 s original (+23%), but allocated 34.25 MB versus 309.14 MB (−89%). Wall time was affected by an existing user-owned live game and is not an accepted speedup claim. The replacement performs real conservative transport where the old method used settled-cell routing. Final wall scopes attribute 15.76 s to flux/pressure, 11.38 s to reconstruction, and 8.71 s to heads. Further work should target active-face pressure and reconstruction cost, then immutable render-packet preparation, with matched mature-world measurements. No broad GPU, power, 17 m reach, or long-run resource claim is made.

## Reproduction

Build solution then game sequentially. Run the native diagnostic with the local Godot 4.7.1 Mono executable:

```powershell
$env:WATER_REVIEW_POINTS = 'E:\Vivarium\build\water-redesign-20261114\baseline-v2\query-points.json'
& $godot --path E:\Vivarium\game -- --water-review E:\Vivarium\build\water-redesign-20261114\review
python experiments/water_rework/analyze_review.py build/water-redesign-20261114/review
```

For an isolated pure-Sim probe, build `WaterProbe.csproj` with a unique `--artifacts-path`, then run the resulting explicit DLL with `acceptance` or `benchmark`. `FrozenSimAssembly` can instead reference a frozen optimized Sim DLL for the same benchmark runner. `benchmark serial` forces one numerical worker. The historical `matrix` mode requires the separate diagnostic patch retained in the build evidence; those flags are not production options.

Focused regression checks are the `water.` filter in `experiments/representation_rebuild/selftest`. They cover fixture budgets, legacy exact export, model-1 exact continuation and positive accepted face flow, malformed disconnected pools, production world save/load membership and digest, shared donor positivity, losses and table handoff. The long opt-in acceptance additionally measures 60+20-second high flow, source-off closed water, replay, real terrain edits and full draining. These checks do not replace native inspection.

Final verification: six focused water checks, two substrate-water checks and mesh refresh all pass (9 total). The long opt-in acceptance passes all 11 assertions: 95.45% of source discharge leaves the boundary over the final 20 s, temporal wet-core range P95 is 0.663 mm, exact replay difference is zero, and budget residual is approximately 1.5e-16 m�. Source-off closed water has zero spatial head spread and maximum speed 0.000026 m/s. Final solution/game builds and headless save/load boot pass (`roundtrip=True`, invariants 0). The final native texture audit checks 42 payloads with zero mismatches.
