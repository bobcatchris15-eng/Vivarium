# Representation Rebuild Completion Plan

Goal: finish the approved rebuild in brief.md using the existing contracts and accepted experiments, then select and integrate measured winners.
Architecture: deterministic pure C# state in src/Vivarium.Sim/Repr; disposable Godot representation caches in game/Repr; production integration uses versioned local changes and bounded work.
Tech stack: Godot 4.7.1 .NET Forward+, .NET 8 simulation. The current desktop has a GTX 1080 and Ryzen 5 1600; Radeon 860M remains a target requiring separate verification.
Spec: brief.md, refined by .claude/ledger.md. The owner resumed work with Luna and Terra agents under root orchestration on 2026-11-13. Accepted historical work and current verification are distinguished below.

## Recovery and implementation milestones
- [x] P2g recovery decision: test and render recovered Forms/catalog. REJECTED for worse quality/higher geometry; source and paired evidence archived, accepted Plant baseline restored (10 tests pass). Production uses existing meshes under INT-F; no new-form rollout claimed.
- [x] E1/E2: accepted in `68f11193`; WorldSubstrate production coupling, weather, uptake and closed water ledger, eight ecology checks and 90-day diagnostics. Independent review and final integrated long-run evidence remain in REVIEW.
- [x] INT-L: accepted in `1ba12681`; production Stock/Cached modes, world-fed occlusion, continuous time tables and frame cap, with matched GTX 1080 real-island measurements. See `experiments/representation_rebuild/INT-L.md`.
- [x] S2b: accepted in `5e8e6e10`; soil cube relief, 3D litter, wetness and outcrop/dig corrections with fixed-camera evidence. This acceptance covers the specimen, not replacement of the production island. See `experiments/representation_rebuild/S2-soil-cube.md`.
- [ ] C2: correct dense-interior growth and damp-rock niches, render attached moss and crust/foliose/fruticose lichen with stable optical coverage across levels; colonizing_stone demo and cost. Own Colony state/render paths.
- [ ] INT-F: connect cached chunked presentation to live production flora, handle births/growth/deaths locally, camera-only selection without population regeneration. Compare identical dense world; keep only measured winning presentation.
- [ ] INT-S/C/E: route shared authoritative substrate/colonies into actual island and remove duplicate state/rendering where replacements win.
- [ ] P4/alternatives: evaluate aggregate foliage/GPU generation against measured bottlenecks; record rejection when complexity/cost lacks benefit. Do not keep unmeasured alternatives as production options.
- [ ] REVIEW: independent adversarial tests/review, six demo captures, repeat real dense-world idle/orbit/day-cycle timings, boot and full selftests; repair regressions.
- [ ] FINAL: architecture map, experiment decisions, reproducible validation/capture commands, current ledger and clean pushed main.

Each implementation unit: inspect current code and spec; add behavioral failure probe for changed logic; implement; run target checks; return files/evidence/caveats; root reviews diff, runs integration checks, commits explicit paths, pushes. GPU capture tasks obtain root lease before launching Godot. No worker commits or touches another worker’s owned paths.

## Current continuation

- C2: separate colony sheets were rejected; their source and failing evidence remain under `rejected/C2-sheets`. The hybrid field/microstructure specimen is checkpointed in `f35bee5a`. All 15 native shots completed; a local edit updated one field tile and one micro tile. Visual acceptance remains partial (broad pale rock coating/basal halo); independent LOD attachment, whole-island locality and pending-builder exit remain open. This does not replace the production island surface.
- INT-F: the 1 m candidate passed 36 camera revisits with zero visits/builds/uploads, local growth/move/birth/removal checks and native guardian reorder/shrink readback. It failed the later live tradeoff: candidate/control GPU 9.2429/8.6268 ms, draws 826.55/765.45, effectively equal pacing, geometry and memory. The previous production flora renderer is restored. Exact candidate source and evidence are archived under `rejected/INT-F-1m`; this negative result does not complete the camera-cache production requirement. See INT-F.md for limits and prior failed variants.
- Coverage correctness is committed in `af4de8aa`: immutable target/halo and terrain/prop inputs, captured result revisions, stable producer epochs and nonblocking scan/commit leases. The targeted regression and sequential builds pass; the full suite passed 72/73 with a retained substrate timing failure (401 ns against 400 ns). Restored production boot passes save/load roundtrip with invariants=0. Production material integration must adapt persisted Coverage and the Terrain/props surface, without duplicating colony simulation or silently changing the physical surface.
- Remaining gates include colony visual/LOD acceptance, flora mature and startup performance, final ecology/backlog evidence, production substrate/colony appearance integration, botanical/alternative decisions, six final demos and long-run resource checks. Current reviews and exact receipts are under `build/reviews/resume-20261113`, `build/int_f`, and `build/c2`.
- Preserve unrelated `.import` metadata, untracked historical documents, user editors and saves. Root stages only explicit verified milestone paths.

Pacing: the owner requested smaller checkpoints on 2026-11-14. Use one bounded Terra implementation at a time with Luna review; reuse completed tests and run native comparisons only for an explicit acceptance decision. End each checkpoint with exact evidence, scoped commits and durable state. The next bounded task should measure a camera-cache representation that avoids multiplying native draw/node groups, or address the independently measured Coverage invalidation/queue cost; do not silently start both.
