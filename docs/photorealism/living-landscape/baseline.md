# Baseline disposition — 2026-10-08

G0 is **not passed**. Measurement harness implementation is in progress; no performance number in this document is an acceptance result until linked to an actual immutable run.

Target hardware discovered: NVIDIA GeForce GTX 1080, AMD Ryzen 5 1600. Exact driver/RAM/OS/engine/source/window/quality/source hash belong to run provenance. Other rendering processes must be absent; launcher rejects visible Godot/Vivarium/Blender processes and requires the operator's explicit isolation statement. It cannot automatically discover every GPU-intensive application.

Pending evidence: scoped existing geometry regression results; Godot source import/boot; cold and warm frozen captures; complete frame percentile/window gates; measured CPU bottlenecks; all scenario coverage; 20-minute resource soak; exact wind-phase rendering reproducibility and worst-density/event fixtures.

No shader/material/ecology/hydrology assertions were changed. Existing failures are retained and will be enumerated after execution. A successful game build is separate from those results and from windowed visual/performance acceptance.

Initial regression evidence (source-only candidate based on c71ebe8; preserves no dirty root fluid work): 74 focused tests, 62 passed / 12 failed. Existing `scripts/verify.ps1 -Suite Boot,Camera,Time` returned failure: Camera 1 passed; Time 20 passed / 1 failed; Godot Boot passed, save/load roundtrip true, zero world invariants at 33 ticks. `BioClockTests.BioClockAdvancesPerTickAndSurvivesSaveLoad` failed at line 62 due saved-state digest mismatch. No sim/test sources changed in this task, so these are pre-existing for this source snapshot, not a clean full-suite claim.

The 12 `FormTests` failures are:

- VineBladesAreDeterministicCurvedMeshesWithinBudget — degenerate triangle108.
- BracketFungusHasTieredRuffledShelves — rim vertical ruffle assertion.
- FernHasCamberedBladeSurfacesAndGroundedVaryingFronds — anchor -0.0028914 outside ±0.001.
- PairedLeafIsFiniteNonDegenerateAndWithinTriangleBudget — degenerate triangle180.
- IsopodHasArticulatedPlatesAndCurledPose — degenerate triangle0.
- TussockHasGroundedTaperedBladesAndMixedStrawTips — 7228 versus2640 triangles.
- CreeperHasNoLargeFlatDiscsAndSpansExpectedFootprint — maxY0.6267546.
- SpringtailHasArticulatedAnatomyAndAppendages — degenerate triangle0.
- MushroomClusterHasGroundedStagesAndIrregularSilhouettes —12732 above4500 triangle limit.
- TrifoliateIsFiniteNonDegenerateAndWithinTriangleBudget —23028 above6000.
- RoundLeafIsFiniteNonDegenerateAndWithinTriangleBudget —9780 above1944.
- CreeperIsFiniteNonDegenerateAndWithinTriangleBudget —12800 above896.

Raw logs/TRX: `build/living-landscape/preflight/preexisting.trx`, `tests.log`, `build/verify/Time.trx`, `Time.log`, `boot.out.txt`, `summary.json`. Geometry assertions and ecology are left unchanged. Baseline Godot build passed with existing CoverageRenderer.cs417 nullable Task warning. Initial resource import attempt using a nonexistent relative log-file path crashed Godot signal11; retry uses a valid absolute path and preserves import output. That startup failure does not become a cropped successful run.

## Actual attempts and first retained diagnostic — 2026-10-09

All attempts are immutable under this isolated worktree's `build/living-landscape/`:

- `20261009T013925Z-4de1a7a74f8a4a17b2c3aca37cbd0903`: canonical six-biological-day preparation remained CPU-bound for several minutes without frame/report output. Only this run's verified processes were stopped. Startup, identity diagnostics and missing-report rejection are retained.
- `20261009T130729Z-67403e5986e14aab8d493931aa5e1d0e`: supplemental initial-world rendering/captures ran, but final reporting failed because Godot byte-loads assemblies and `Assembly.Location` was empty. The original failed report/logs are retained.
- `20261009T131155Z-a75bd3ed50fb4ce682302d52d8af589b`: engine crashed before measuring in AccessKit Windows initialization, HRESULT 0x80070008. No frame result exists. At diagnosis the machine had about 10 GiB physical and 28 GiB virtual memory free; the generic error is not proof that scene geometry exhausted RAM.
- `20261009T131313Z-308d61491c3441d2ae4863aecff70459`: complete supplemental initial-world frozen run, 1600x900 Forward+, high quality, MSAA4x, VSync disabled, GTX1080. The benchmark-only `--accessibility disabled` recovery condition is recorded and compared. This is not the mature or worst-density acceptance fixture.

For the complete run: p50 4.562 ms; p95 7.389 ms; p99 15.664 ms; p99.9 68.111 ms; worst 221.285 ms; minimum recorded 1-second window 123.82 FPS. Good p99/average does not excuse severe stalls. Renderer-reported memory rose from 479,519,360 to 573,354,784 bytes; short-run growth is a screening failure, not a proven leak or a substitute for a 20-minute soak. Working set rose from 964,890,624 to 1,013,673,984 bytes.

Recorded top three overlapping CPU scope maxima: Flora 215.9669 ms, Island 34.0496 ms, SoilDetail 11.0728 ms. GPU draw/lighting timing remains unavailable and null. These are scope spikes on an initial fixture, not a general mature-world bottleneck ranking.

The executing game and simulator module IDs matched the candidate PE files and their SHA256 hashes. The renderer-only frozen digest and reload check held, but the three-tick replay digest check failed. This is an unresolved runtime finding; it is not waived because the unrelated Time regression already has a saved-state digest failure.

The first validator output wrongly rejected the engine's capitalized `Windows` display name. That raw validation is retained. `validation-runtime-corrected.json` is a separate revalidation after fixing the validator, and still fails for >50ms stalls, replay mismatch and >64MiB renderer-memory growth. `raw_measurement.json` and the original `render_report.json` remain unchanged.

No shader, botanical asset, ecology, hydrology or test expectation was changed to obtain these results. G0 remains unpassed. The canonical mature preparation, worst-density fixture, event bursts, frozen wind/image identity, portable export verification and long soak remain open. Later visual implementation must wait for the reference and profiling gates.

Matched repeat `20261009T131752Z-db8f089be0ce420186eddbfe6f4a10cf` was compatible in hardware/settings/seed/poses/initial and final digests with the prior complete run and again failed the same three checks. p50 4.546 ms; p95 7.729 ms; p99 16.072 ms; p99.9 61.581 ms; worst 236.227 ms. Renderer memory again rose from 479,519,360 to 573,355,552 bytes. The repeated >200ms stall and growth screen are not dismissed as one-off OS noise. A clearly labelled failed-diagnostic comparison HTML links both image sets. No successful G0 result was created.

Final harness regression suite: 21 tests passed, including actual capitalized Windows driver names and rejection of unverified managed module identity. The earlier independent review passed 20 synthetic tests and resolved 12 concrete false passes; later Godot-runtime fixes and the 21st test were checked directly by the coordinator. No live-world or portable-export acceptance is claimed.
