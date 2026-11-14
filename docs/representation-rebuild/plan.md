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

- C2: recovering the pre-existing uncommitted colony mesh and growth changes, adding the tile-cached Godot renderer and `colonizing_stone` demo. Initial solution verification found 23 API errors in the recovered mesher; repair and rendered acceptance are required.
- INT-F: direct source review found that production still uses global species buckets. A draft spatial index alone is not an implementation. The next phase must connect persistent chunk buffers and camera-only selection to production. Biology-time population polling is permitted and must be reported separately from local buffer updates.
- Independent Luna review: `build/reviews/resume-20261113/flora-cache-review.md`. Current missing gates are botanical replacement acceptance, colony render acceptance, dense-canopy orbit/locality, final ecology/backlog evidence, and production substrate/colony integration.
- Preserve unrelated `.import` metadata, untracked historical documents, user editors and saves. Root stages only explicit verified milestone paths.
