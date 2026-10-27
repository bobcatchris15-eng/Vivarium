# Performance investigation — 2026-10-24

This pass preserves the existing dirty rendering work. The user authorized closing editors; original editor PIDs 15536/21724 were already gone when inspected. A normal close was mistakenly sent to a newer moss review capture (32524); its command line identified a copied-source review rather than an editor. Subsequent captures are protected by the shared exclusive `.clanker/visual-lab-gpu.lease`. No saves, settings, commits, or publication are part of this pass.

## Measurement corrections

`SmokeRunner.Landscape` previously started its ten-second warmup without waiting for `Flora.PopulationReady`. In the first attempt, mesh publication continued throughout measurement and later captures. Its 915 frames averaged 65.648 ms, with p95 96.313 ms and p99 126.129 ms. Flora CPU reached 131.425 ms and Island reached 32.553 ms. These are diagnostic startup-contaminated measurements, excluded from steady-state comparisons. That attempt also failed executing-module identity after a later build replaced its on-disk DLL; it cannot establish source-matched acceptance.

The corrected scenario library (version 2) waits for flora publication with a five-minute bound before the existing ten-second warmup. It records readiness at measurement entry. It captures the eight required reference regimes plus twelve motion images rather than the entire species catalogue. Old library-version-1 reports remain readable and cannot silently match version 2 in the comparison validator.

The run now records engine viewport GPU timestamps and viewport rendering CPU times, plus mean CPU scope cost per measured frame. GPU timestamps can be delayed and are not paired to individual wall-time samples. Zero/non-finite GPU values are excluded; an unavailable GPU mean is null. The reserved v1 `gpu_timing` field remains null; new aggregate data lives in `viewport_gpu_timing`. CPU scopes overlap and must not be summed as independent work.

## Candidate: cache the static gravel mask

`IslandRenderer` refreshes terrain textures every 1.5 seconds. Its gravel/rock mask repeatedly evaluated the same prop distances and two-octave noise for every high-resolution texel, even when only water or ecological fields had changed. Cache the exact mask bytes, invalidate on terrain/prop version changes and world build, and keep silt, dynamic liquid and biological contact channels live.

The original renderer file at task entry is retained under `build/performance-20261024/IslandRenderer.before.cs` so this patch can be distinguished from existing uncommitted rendering changes. `VIVARIUM_TERRAIN_CACHE_BASELINE=1` forces the original recomputation only in `--perf-test`; `VIVARIUM_AUDIT_TERRAIN_CACHE=1` compares every cached pixel against the original calculation on the first reuse. Audit differences and build/reuse counts are reported. `Island.FieldTexture` and `Island.Substrate` scopes isolate the refresh cost.

## Validation status

- Godot client build succeeds with zero errors and six pre-existing warnings on rebuild.
- All 24 report-validator tests pass, including readiness rejection and unavailable/invalid GPU timestamp handling.
- Two settled native attempts completed successfully (`VIVARIUM_PERF_OK`), including executing-module identity, reload/replay, frozen-world invariants, and the cache audit. Both still fail the existing performance/memory screening thresholds.
- The initial diagnostic recorded 388 flora, 83 fauna at tick 6200. Its startup-contaminated memory peak was 6,505,637,392 tracked VRAM bytes, with up to 68,966,631 submitted primitives per frame. Those counters suggest geometry/shadow/memory pressure deserves investigation after a settled GPU measurement; they do not isolate a root cause.

Evidence root: `build/performance-20261024/`. The benchmark's pre-existing 30 FPS/p99 and memory screening gates remain unchanged. A failed gate is retained as evidence; process success alone does not establish performance acceptance.

## Settled attempt results and matching limit

| Metric | Recompute gravel | Cached gravel |
| --- | ---: | ---: |
| Mean wall frame | 58.617 ms | 58.525 ms |
| Mean FPS | 17.060 | 17.087 |
| Mean viewport GPU | 58.038 ms | 58.002 ms |
| p95 frame | 65.186 ms | 64.843 ms |
| p99 frame | 70.332 ms | 71.981 ms |
| Island CPU per rendered frame | 0.432 ms | 0.220 ms |
| Maximum field refresh | 13.311 ms | 9.220 ms |

The cache audit compared all 14,336 mask pixels with zero differences; the candidate recorded one build and 84 reuses. Both attempts used seed 20260923, tick 6200, 388 flora and 83 fauna, matching camera captures/render settings and identical executing game/simulation assemblies. Their prepared-world digests **differ**, although each attempt independently preserves its frozen digest and same-tick replay. Consequently this is not an exact-world A/B FPS result. The native byte audit establishes mask equality; the CPU timing improvement is provisional until the within-world cached/uncached sweep. No overall FPS gain is established.

The measured viewport GPU time nearly equals total frame time, establishing a GPU bottleneck in this route. A diagnostic fixed-overview sweep completed: SSAO, SSIL, SSR, SDFGI, MSAA, sun/fill shadows, and a 4096 shadow atlas, restoring production settings after each intervention. It also compared cached and uncached terrain refreshes within one frozen world. Five-second samples after five-second settling are diagnostic; they are not route-wide performance acceptance.

## Within-world sweep and retained GPU change

Native sweep: `build/performance-20261024/effect-sweep/20261024T231253Z-b7135c660f054e1b8f5d80a24cec3862/gpu-effect-sweep.json`. The run finishes `VIVARIUM_PERF_OK`, all ten checks pass, and the sweep's frozen simulation digest is unchanged. The shutdown still emits the existing seven-texture-RID leak warning. No shader errors were recorded.

| Intervention in fixed overview | Mean GPU time |
| --- | ---: |
| Reference at start | 42.357 ms |
| SSAO disabled | 42.249 ms |
| SSIL disabled | 40.788 ms |
| SSR disabled | 42.025 ms |
| SDFGI disabled | 41.011 ms |
| MSAA disabled, TAA/FXAA retained | 33.935 ms |
| Sun shadows disabled | 35.946 ms |
| Fill shadows disabled | 37.626 ms |
| 4096 shadow atlas | 41.976 ms |
| Reference at end | 42.217 ms |

Reference drift was -0.33%. Disabling MSAA reduced viewport GPU time by 19.9% and wall frame time from 42.785 to 34.375 ms in this overview. That corresponds to roughly 23.4 to 29.1 FPS in these short fixed-view samples, **not** a measured 25% gain over the entire route. Native comparison stills were inspected: no obvious loss at this overview scale; moving thin foliage and close views remain a visual-review limitation.

Production **Medium** quality now disables MSAA while retaining TAA and FXAA. **High** keeps 4x MSAA. Other lighting effects and shadow settings are retained. This is an explicit AA-quality tradeoff, not a geometry reduction. A standalone native Vulkan policy check instantiated the production `EnvironmentRig` and passed Low → Medium → High → Medium, confirming actual viewport MSAA/TAA/FXAA values. Evidence: `build/performance-20261024/quality-policy-stdout.log` (`QUALITY_POLICY_OK`), with empty stderr. The existing uncommitted rig was preserved in `EnvironmentRig.before.cs` before this one-line policy change.

The within-world terrain control reduced `Island.FieldTexture` CPU per rendered frame from 0.371608 to 0.215725 ms (41.9%). Island total fell from 0.381873 to 0.226596 ms. GPU time was unchanged, as expected. The byte audit and unchanged frozen digest support retaining the cache. In the captured build the `Island.Substrate` label accidentally covered only tree-list preparation; its placement was corrected afterward, and `Island.TreeContacts` now names that small scope. Use the correctly measured parent `Island.FieldTexture` for the figures above; do not interpret the old child label as full substrate cost.

A live GPU sample during the mature run showed 100% GPU utilization, 7125 MiB resident memory, 66 C, 1949 MHz graphics and 5005 MHz memory. This supports the GPU-bound diagnosis rather than an idle or thermally throttled device explanation; it is one sample, not a thermal soak.

## Remaining performance work

The first retained GPU change is measured in one overview and verified as the actual Medium AA policy; a fresh 60-second Medium route and longer motion/quality review remain unmeasured. The existing 30 FPS/p99/memory gate still fails in the High-quality runs. Geometry/shadow submission and video-memory pressure are the next substantial targets; disabling all lighting is not justified by this sweep. The pale, occluding canopy remains an existing visual defect and was not changed for performance.

For another controlled sweep, set `VIVARIUM_BENCHMARK_EFFECT_SWEEP=1` and use `scripts/visuals/benchmark.ps1 -ConfirmIsolation -Scenario frozen -Preparation mature -Seconds 60`, with exclusive GPU-lease ownership and no other rendering processes. The runner uses disposable user storage. Clear the variable afterward. These runs and all changes remain local and uncommitted.

## User correction: terrain stays player-authored

The user retired erosion as a gameplay requirement. `SurfaceErosion.Enabled` now defaults to false for both new and deserialized worlds. Existing terrain heights and legacy erosion archive data are preserved; pending legacy wear does not resume on load. The solver remains available only through explicit programmatic opt-in for its legacy diagnostics. Production code does not opt in.

The terrain renderer already gates mesh rebuilds on `Terrain.Version`, rather than rebuilding the mesh periodically. The user then explicitly prioritized presentation over ecological update depth and authorized a 10–30 second terrain-texture cadence. The periodic material-field refresh (moisture, liquid coverage, litter and colony/contact masks) now runs every **10 seconds**, previously 1.5 seconds. Player sculpting and prop edits still publish promptly. Sculpt publication resets the periodic timer so it cannot redundantly refresh again in the same frame. This is 85% fewer scheduled refreshes by cadence, not a measured FPS improvement. Ground wetness/contact/coverage shading may lag simulation by up to ten seconds; the water simulation and its separate renderer are not slowed by this change.

Disabling erosion prevents automatic liquid-driven terrain-version changes and the downstream geometry invalidations they caused; no measured FPS saving from this retirement is claimed.

Focused regression checks cover synchronous/asynchronous flowing-water mass conservation with unchanged terrain heights/version, legacy erosion solver compatibility under explicit opt-in, loading pending historical wear without reactivating it, and continued player sculpt edits. No save migration or restoration of previously eroded heights is performed.
