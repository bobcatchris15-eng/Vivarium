# Loading and mature-world performance — 2026-10-20

## Scope and provenance

The requested objective is better new-world loading/entry and removal or mitigation of the mature-world performance cliff, without an FPS floor. Work began by fast-forwarding `main` from `be0f9aac` to `8c17bad5`. All 51 pre-existing untracked files matched their pre-pull SHA256 records immediately afterward. No remote publication is part of this work.

Evidence lives under `build/performance-20261020/`. Its launch records identify the actual game and simulation binaries; `final-source-manifest.json` identifies changed runtime source for the final validation sequence. The machine is a Ryzen 5 1600, approximately 32 GiB RAM, GTX 1080, NVIDIA driver `32.0.15.8157`, Godot 4.7.1 Mono. Native runs use a 1600×900 window and the existing uncapped orbit performance runner at normal simulation speed. Two pre-existing Godot processes were preserved. These are matched local comparisons, not isolated-machine or other-hardware guarantees. CPU scopes overlap and are not GPU timings.

## Retained changes

- The in-game New action now paints a modal loading screen before background world generation, prevents duplicate actions, pauses the previous world during generation, and awaits staged renderer setup. Teardown guards avoid touching freed controls. Loading stage labels precede their work, and camera-local layers begin at the entry pose.
- Fauna geometry, materials, morphs, LODs and matching GPU/CPU buffer capacities survive world replacement when content is unchanged. Instance visibility and motion tracks reset. All existing fauna morphology and detail remain.
- Existing mesh workers prepare immutable stem/leaf splits and upload arrays. Review transforms remain on the main thread. Materials and native GPU upload remain on the main thread.
- Guardian skin preparation caches immutable bind-region metadata and mesh placement. Selected tail samples cache immutable chain membership and denominators. Constraint station radii are computed once per modifier call. Solver iterations, time steps and sample stations are unchanged.
- Plant-light overlap calculations read native position/range once per light, preserving the original overlap traversal and arithmetic. The guardian vegetation registry caches its farthest replacement candidate within a frame, invalidating it whenever membership or base placement changes.
- Positive-radius tree-root queries use conservative footprint masks and original segment order. Exceptional or oversized queries retain the full scan; the zero-radius path is unchanged. A low-hit per-call penetration dictionary experiment was removed.
- Plasmodium transport uses indexed reusable buffers while preserving arithmetic and sequential transfer order. Habitat suitability samples the same moisture/substrate fields without unused all-plant shading, humidity, slope or gradient work. Cheap detritus rejection precedes that sampling.
- Opt-in performance runs honor their isolated user directory. Reports include CPU scopes, native audit counters and final world invariant checks.

## Save restoration defect discovered during profiling

The retained mature save and a newly recorded same-content save both initially failed state-digest verification. Deserialization discarded spores whose coverage-cell centre lay outside the island, even though their original release destination was valid before quantization. Restore now preserves the sorted persisted bank exactly. Archive checks and state-digest validation remain enabled; original saves were not modified.

Both archives subsequently loaded with all eleven payloads byte-identical after deserialization/reserialization. The new boundary-spore regression uses the actual release path. Evidence: `save-diagnostic/`, `PlasmodiumSporePersistenceTests`.

## Iteration measurements

The mature comparisons load the same retained day-31 world, initially about 1,400 plants and 1,100 animals. The baseline includes only the restoration fix needed to load it. Each comparison runs for 120 seconds and records viewport stills every 30 seconds; readback stalls remain in the timings. End populations match at 1,437 flora and 1,115 fauna.

| Run | Mean FPS | Median frame ms | p95 ms | p99 ms | Worst ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline, original runtime plus restore fix | 18.5 | 45.47 | 97.07 | 142.24 | 226.94 |
| Initial transport/entry/skin changes | 18.2 | 47.62 | 99.89 | 139.21 | 277.85 |
| Native light read cache | 18.5 | 45.41 | 95.21 | 135.80 | 262.29 |
| Habitat, constraint stations, vegetation registry | 27.9 | 28.88 | 62.54 | 83.80 | 194.38 |
| Root footprint index; low-hit dictionary removed | 29.3 | 27.73 | 60.17 | 90.83 | 290.86 |

The first attempts did not provide a meaningful whole-run gain. The largest improvement came after identifying redundant full habitat sampling and vegetation registry scans. The last iteration improved mean/median cost but did not improve every tail statistic. These runs include cold background mesh preparation; later windows and long continuations are needed to assess sustained behavior.

For context, the original fresh-world 600-second baseline completed at 44.5 FPS, p95 36.13 ms, p99 67.56 ms, worst 460.13 ms, ending near biological day 10 with 326 flora and 97 fauna. Its duration and population differ from the mature comparisons, so it is not their direct baseline.

## Validation

- Final focused selection: 29 passed, covering exact transport equivalence, habitat sampling, boundary-spore persistence, root query equivalence, habitat behavior and guardian simulation.
- Root index: seven new exact-result tests, plus 84 existing PilotTree/GuardianNeeds checks, passed. Independent full scans cover all six trees, boundaries, varied radii, fallback forms and terrain-edited penetration normals/depths.
- Transport tests retain the old dictionary transport and compare double bits across multiple substeps, retention, tiny/zero mass, disconnected occupancy and shrinking/regrowing capacity. They share the unchanged phase integrator; they are not an independent reference for every ecological subsystem.
- Native light audit: 836 lights and 188,936 overlap pairs, zero error.
- Native registry audit: 32 accepted and 32 rejected decisions; 33 audits exercised actual cache reuse.
- Native chain audit: 32 calls, 3,456 samples and 13,824 influences, zero error. The compared day-31 route had zero penetrating tail samples, limiting contact coverage.
- Native creation checks passed all five assertions with clean engine stderr after correcting an early buffer-capacity warning. Reused fauna setup measured below one millisecond versus approximately 1.5–1.8 seconds for a cold build. Other loading-stage timings varied and are not a universal launch-time guarantee.
- One existing `SeepageSaturatesGroundWithin3Cm` test fails identically against the protected baseline assembly. It was not changed or hidden. Evidence: `seepage-baseline-test.log` and `save-diagnostic/baseline-seepage/`.

Final fresh and extended mature-soak results are pending. This document is an execution checkpoint, not a completed acceptance claim.
