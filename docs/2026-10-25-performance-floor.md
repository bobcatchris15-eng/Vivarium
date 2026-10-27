# Performance floor iteration

Single-agent continuation of the hitch investigation. Production fixture is the captured player autosave, quality 1, 1600x900. Keep source/binary provenance and exclusive GPU lease; do not run builds/tests during timed measurements. Current settings/saves are not benchmark inputs or outputs.

User authorizes visible tradeoffs (mesh detail, texture resolution, shader features), exposed individually in Settings. One base presentation remains. Optional distance DOF may be evaluated with earlier LOD transitions; it must remain independently switchable and measured for its own cost. No artificial loading delay.

Initial code review: a previous unfinished pass already introduced frame scope totals, water unchanged-payload checks, hidden-soil suppression, and bank preparation scopes. These are part of the current baseline and require validation, not attribution as new gains. Effect sweep incorrectly forced SSIL on in its baseline and could not execute before HitchTraceOnly returned. Correct these contracts first, freeze source, then measure.

GPU diagnostic sweep restores captured production feature states and visibility between interventions. Diagnostic removal is attribution only. Paused environment processing is suspended so it cannot overwrite fog interventions. Flora shadow tier accepts -1 for a genuine no-caster diagnostic.

Baseline snapshot: build/perf-floor-20261025/baseline-source. Native runs, manifests, receipts, and later candidates live under build/perf-floor-20261025.

## Diagnostic evidence

Baseline sweep: sweep/20261026T031802Z-d693bf7b58ce4f5cbb61daab42a61eec, source stable, native exit 0 / 3 checks, paused digest equal. Overview GPU base 42.52/42.81 ms, flora hidden 25.85, sun shadows off 39.63. Ground view base 40.77/40.42, flora hidden 30.41, sun shadows off 33.70, ground hidden 37.58, water/coverage hidden 38.73. SDFGI/fog gains smaller; SSAO near noise. Diagnostic removals are not shipped default changes.

Control screen: controls/20261026T032606Z-9f6245aa248348719e09d2a66058d600. UI applies/restores and JSON round-trip audit native exit 0 / 21 checks; ecosystem digest equal. Overview base 43.50/42.17 ms, earlier LOD 38.00, LOD+DOF 38.22, simple lighting 41.07, 75% view resolution 36.39. Ground base 40.46/40.99, LOD39.68, LOD+DOF39.87, simple40.24, 75%34.93. First/last reference flora triangle counts differed because LOD histories differed; these numbers screen candidates, not an accepted exact percentage. Confirmation resets histories and blocks user camera input.

DOF screenshot at amount .06 was too soft across the overview. Revised optional far-only blur to .015 with 8m transition, still begins at 4.8 m before the6m detail boundary. Default remains off pending user preference. All optional features currently preserve established defaults until confirmation. UI controls are in one scrolling Settings window.

10 focused simulation tests pass, including distance-band hysteresis and existing aquatic exact-state/event regressions. Game builds with six prior warnings. One initial concurrent build/test collided on the same Sim assembly; reran sequentially and passed. Future builds/tests are sequential and excluded from native timing.

## Confirmed rendering tradeoff and defaults

Four matched 60-second paused orbits in one unchanged world: GPU 63.102 ms reference, 58.743 earlier LOD, 59.055 earlier LOD plus revised far blur, 63.259 restored reference. Rendering-only digest unchanged. Earlier LOD reduces GPU time about 7.0% relative to the bracketing references; optional blur adds 0.31 ms. Every phase resets tier history and starts from the same camera pose. Asynchronous GPU samples are not frame-aligned timestamps. Evidence: confirmation/20261026T033415Z-9d579156e5d446dfa9270b3d68210680/gpu-effect-sweep.json.

Production keeps quality 1 as its base. Settings now exposes independent shadows, indirect lighting, contact shading, volumetric mist, reduced distant plant detail, optional far-only blur, simpler foliage lighting, and 100/75/50% 3D resolution. UI remains full resolution. Earlier distance LOD is enabled by default after confirmation; all other defaults preserve the existing presentation. DOF stays off by default. Distance tiers start at 6 m and 12 m with 10% hysteresis; far blur starts at 4.8 m, transitions over 8 m, amount 0.015, foreground blur disabled. The normal projected-size tiers continue to apply. Camera motion requests bounded flora refreshes rather than waiting four seconds. No plant is removed solely for distance.

The first confirmation's surface-cache digest audit failed after timing: queued simulation ticks could still drain while the clock was paused. The audit now completes a partial tick, joins liquid work, suspends the host, then takes the digest, preserving remaining backlog. Resuming resets the host wall-time origin so audit duration is not queued as fictional catch-up. This is a harness correction; the failed attempt is retained and is not a passing native run.

## CPU iterations and correctness

Bank mesh packing yields every 2048 vertices. Tangent preparation yields between triangle and vertex chunks, retaining the synchronous arithmetic order. Native arrays, mesh creation, and assignment remain separately timed atomic operations. Independent native comparisons pass for packed arrays, tangents, and the final uploaded mesh. A cooperative 2 ms preparation target is not a ceiling on native calls.

The exact depth cache now retains one validated sample per coarse cell instead of one sample globally. Liquid version, water table, stored depth, bed, terrain version and prop version invalidate the relevant result. In the120s checkpoint, misses fall from 1,371,782 to 46,816. Recruitment rejects occupied/already-queued neighbours before hydrology queries and avoids a duplicate obstacle query in the sealed adapter where obstacle means depth<=0.

Advection now rents operation-owned scratch, captures all densities, velocities and blockers before its first yield, prepares transport in chunks, and publishes the complete result atomically. The old synchronous implementation remains the independent reference. Tests compare cell float values, occupancy, tile versions and substep counts, including changed environment inputs, interleaved work and cancelled preparation. Per-batch CPU timing excludes suspended wall time; total operation statistics are retained separately instead of being falsely charged to one frame.

Water lookup now adds each distinct owner/halo tile directly rather than performing nine redundant tile-key calculations/inserts for every fluid cell. The independent native oracle still enumerates all nine neighbours and compares active tiles and complete texture payloads, including halos and support channels. Water-only60s iteration exits 0 / 3 checks. Median lookup CPU falls from 9.76 ms to3.14 ms; full water-update median falls 24.83->17.26 ms in these diagnostic runs. Evidence: water-lookup/. Physical snapshot rates, shader interpolation and optical motion are unchanged.

## Latest measured result

Latest 120 s live orbit: final-batched-live/20261026T041204Z-997ce42743204aa1b0598c1897f95aec, immutable batch-source.2029 frames, mean 59.150 ms, median 57.924 ms, p95 72.558 ms, p99 81.477 ms, worst 111.793 ms; 1 frame>100 ms, 0 >250 ms. The sole>100 ms frame is the measured initial overview-to-orbit transition at 0.161s. Worst subsequent frame 98.91 ms. GPU mean 58.628 ms remains dominant.

Previous recorded checkpoint, same captured input save: mean 75.835 ms, p99 99.577 ms, worst 148.459 ms, 13 frames>100 ms. These live worlds evolve asynchronously and are not byte-identical A/B trajectories. Use the dedicated paused LOD comparison for causal attribution; do not attribute the whole live delta to one candidate. Plot and numeric comparison: build/perf-floor-20261025/performance-floor.png and comparison.json.

Against the preceding120s CPU checkpoint, recruitment median 30.76->19.12 ms / maximum 96.88->43.81 ms. Atomic advection maximum 78.35 ms is replaced by batches whose maximum is 19.52 ms; capture/publication still contain atomic work. Water lookup median 3.34 ms / max 4.39 ms. Scheduler tick progress remains approximately 7009->8000 during measurement, with 6.40sim-seconds backlog at the final sampled frame. No biological speed reduction or silently dropped backlog is used.

Latest focused native run exits 0 with 31 checks and no engine errors. Surface idle/revisit/invalidation/cancellation, ghost cleanup, prepared mesh equality, water texture oracle, real UI toggles, settings round trip, ecosystem digests and both executing module IDs pass.25 focused simulation tests pass. Solution/game builds pass; clean game compilation has the same six prior warnings.

The full stationary route now completes eight named views, twelve motion images and same-tick replay: final-full-stationary/20261026T041645Z-f26b170824274595ad9947fbebec17e9, native exit 0 / 9 checks /zero engine errors. The separate performance validator still fails its existing 30 FPS/p99/hitch limits. A final capture-readiness retest additionally waits for queued dressing and complete flora at each fixed view. Overall performance and subjective all-view art acceptance are not declared.

The validator now checks actual contiguous window coverage and one memory/CPU sample per window, rather than requiring 59 windows when frame-boundary intervals can legitimately be longer than exactly1s. A regression covers 58 valid 1.025 s windows, missing memory and a hidden complete tail.25 validator tests pass. The 30 FPS and hitch thresholds remain unchanged.

## Limits and next costs

GPU rendering remains the floor. Among the measured optional reductions, 75% 3D resolution and disabling sun shadows offer larger additional gains than simplifying leaf lighting, but have visible tradeoffs. Large native bank creation, liquid publication/erosion, and aquatic input capture/publication can still exceed cooperative budgets. Do not call those budgets hard frame limits.

The captured fixture is an older copied autosave, not a replacement for the user's newer live save. Static detail aging, every camera/speed, a 20 minute soak, and actual first-species/prop placement stress are not established by this pass. The ghost audit checks creation/cleanup for an already-published plant. Cached-return is natively checked; a separate long repeat-traversal performance run remains unmeasured. Preserve these boundaries when continuing.

## Final verification receipt

Capture-readiness retest: ready-full-stationary/20261026T042340Z-52a9301f7fe94c4ca96dc988f399c007. All eight fixed views await queued ground details and a complete flora refresh. Twenty images total; native exit 0, nine checks pass, no engine errors. Dense-colony, water-margin and overview images inspected for scene presence. Both executing assembly module IDs match the built assemblies; render-only digest and same-tick replay pass. Full performance validation still fails only FPS/p99/hitch criteria, with no temporal-series or missing-capture errors.

source-stability.json verifies every manifest entry across all seven frozen sources. Latest capture-ready source has 1,063 unchanged files and matches the current workspace game/src bytes exactly. The batch-source measurement differs from the latest source only by post-measurement capture readiness and whitespace cleanup, with the live performance changes identical.

preservation-after.json confirms the captured fixture, actual player autosave and settings hashes are unchanged across the recorded final verification window. Benchmark user storage is disposable. No owned renderer remains, no commits or staging, and the original editor was closed with explicit authorization. Both game and solution builds pass; 25 focused simulation tests and 25 benchmark-validator tests pass. Full clean game compilation retains six pre-existing warnings. Owned-file diff checks pass, with line-ending conversion warnings only.

Final presentation defaults: earlier distance LOD on, far blur off, simpler foliage lighting off, 3D resolution 100%, existing shadows/indirect/contact/mist enabled. User can change each independently in Settings.

2026-10-26 follow-up: the workday log `docs/2026-10-26-gpu-workday.md` supersedes those presentation defaults (95% 3D scale retained after matched confirmation). It also identifies an unused-MultiMesh sentinel bug that invalidates earlier absolute flora-triangle estimates, including positive totals with negative contributions. Direct GPU timings and native primitive counters remain independent valid evidence. Corrected flora estimates are verified against native surface/instance metadata in four views; do not reuse the earlier flora totals for attribution.
