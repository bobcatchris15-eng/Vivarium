# Performance iteration — October 25, 2026

The user requested repeated measure / select culprit / improve / retest work, then specified one quality setting. Production now applies one common presentation (the previous Medium configuration) regardless of legacy preference; the UI picker is removed. Internal tiers remain diagnostic controls for old review tools and benchmarks.

## Baseline

Native frozen mature-world orbit, 1600 x 900, GTX 1080, 60 seconds. Prepared seed 20260923, tick 6200, 388 flora and 83 fauna. Flora publication finished before measurement. Existing performance gates fail; this is a valid diagnostic baseline, not performance acceptance.

Mean wall: 35.835 ms (27.91 FPS); p50 35.283, p95 42.043, p99 47.063, worst 76.822 ms. Mean viewport GPU: 35.227 ms. Mean rendering CPU: 4.271 ms. Tracked peak VRAM: 6,140,572,848 bytes. Frozen/reload/replay invariants passed. Baseline final render-settings metadata incorrectly describes the runner's later High reset; the measurement explicitly used quality=1 and its raw samples are retained unchanged. Fixed subsequent reports to snapshot settings at measurement time.

Evidence: `build/performance-20261025/baseline/20261025T144305Z-76b3128ec8784bb3939e97dd252f3ba2/`.

## Experiment contract

GPU work serialized through existing exclusive file lease; launcher now owns that lease itself. Disposable benchmark user storage protects real settings and saves. Shadow probes restore casting flags between phases, preserve all displayed meshes, compare overview and ground vegetation, and check frozen digest. Five-second samples after five-second settle are diagnostic and require checking reference drift. No exact-world FPS comparison between separately prepared worlds unless digests match.

## Validation

Actual game project builds, 0 errors and 6 existing warnings. All 24 report-validator checks pass. Original baseline engine logs contain the existing unsupported reflection-probe-resolution warning. Retain that limitation instead of claiming empty stderr.

## Shadow experiments

Broad same-world probe: overview reference 24.889 -> 24.563 ms GPU; ground reference 46.729 -> 46.576. Crust/coverage/fill changes were small and not retained. All-flora shadows off: overview22.888, ground38.818, but visible flattening of ground vegetation was rejected.

Selective probe: overview reference24.800 ->24.626, far-tier casters off23.477/repeat22.999; ground reference46.711 ->46.624, far-tier off45.855/repeat45.679. Reference drift 0.70%/0.19%; intervention exceeds drift in both views. Only far projected tier (nominal <40px with existing hysteresis) no longer casts shadows. Full visible geometry and near/mid casters remain. Matched ground PNGs inspected; nearby shading retained. More aggressive near-only policy measured22.421/43.917 but not selected. Shadow policy now reapplies after asynchronous species publication as well as setting changes. This is bounded visual/performance evidence, not all-view acceptance.

Native selective evidence: `build/performance-20261025/selective-shadows/20261025T145245Z-04e24cdef585464890e28eef3307e84e/`. Frozen digest preserved. Existing gate still fails; shader errors absent; pre-existing reflection warning and seven-texture-RID shutdown leak remain.

Next: retained far-tier policy orbit retest plus shadow filter High/Medium probes against Ultra in the same world. Production remains Ultra until evidence assessed. Smoke flow updated to assert absent picker and common presentation for legacy tier values.

## Filter probe rejected

Ultra->High/Medium filtering was not promoted. Overview reference23.959->23.022ms GPU, first High28.433/Medium26.895, repeated High22.981. This is inconsistent and likely startup/pipeline disturbance; it does not support a repeatable overview benefit. Ground reference45.826->45.958, High44.987/45.320, Medium44.517: modest fixed-view lead, insufficient to override overview instability. Existing Ultra filtering retained.

Final validation runs original/candidate/restored-original through three matched 60-second orbits in ONE frozen world, with same camera equation/light/resolution/quality and all visible geometry unchanged. Only far flora shadow casting differs. Output root build/performance-20261025/final-matched-orbit. Final acceptance awaits rows/digest/drift. All 24 report-validator tests pass after harness changes. Read-only review found no production blocker; exception-path pause restoration in diagnostic sweep should move into finally after current source-stable run completes.

## Final matched 60-second orbit result

Same frozen world, same camera equation, 1600x900, same lighting and Ultra shadow filtering, 388 flora/83 fauna. Only far flora casting changes. Three phases each have five-second settling followed by 60 seconds of orbit. Executing assembly verified, source hash unchanged throughout, frozen digest equal. This is the causal performance comparison for this change.

| Metric | Original start | Retained candidate | Original restored |
| --- | ---: | ---: | ---: |
| Mean wall frame ms | 35.664 | 34.040 | 35.905 |
| Mean FPS | 28.040 | 29.377 | 27.851 |
| Viewport GPU ms | 35.095 | 33.504 | 35.382 |
| p95 frame ms | 41.552 | 39.874 | 41.785 |
| p99 frame ms | 46.677 | 44.275 | 46.506 |
| Worst frame ms | 68.630 | 67.157 | 69.447 |
| Slowest complete approximately 1-second window FPS | 25.985 | 27.223 | 25.797 |

Mean wall reference drift 0.68%; retained change saves 4.55–5.20% frame time against the two references, about5% in this route. GPU reference drift 0.82%; GPU saving 4.53–5.31%. The lower tail improves but isolated67 ms frames remain. Existing 30 FPS/p99 screening gates STILL FAIL. Do not call this a restored universal performance floor or broad gameplay acceptance.

Evidence: `build/performance-20261025/final-matched-orbit/20261025T150309Z-fd6eb37294b44319b370ae6d48ab51a6/`; see `gpu-effect-sweep.json`, `timing-summary.json`, `source-stability.json`, `render_report.json`, and `measured-code/`. Native exit 0, 9 checks pass; validator exit1 is retained performance gate failure. Existing reflection-probe warning and7 texture RID shutdown leak remain. Actual player saves/settings were not used by these disposable runs. Game/editors remain closed; no commits/publication.

Final post-measurement edit only moves diagnostic pause restoration into finally, including exceptions. Measured DLLs and sweep source are preserved before that change in measured-code. Final game build passes 0 errors / 6 existing warnings; all 24validator tests passed. The diagnostic exception-path repair is build-verified, not re-benchmarked; production render policy is identical to the measured candidate.

## Retained changes and next target

One shared production presentation, no graphics quality picker. Legacy saved tier values are ignored during production ApplySettings. Near/mid flora retain their cast shadows; far projected flora tier does not cast, with every displayed mesh unchanged. Benchmark has explicit diagnostic quality selection, correct measurement-time metadata, exclusive GPU lease, reversible caster/filter probes, matched orbit A/B/A, partial timing persistence, and updated smoke assertions.

Rejected: blanket flora shadow removal (visible flattening), crust/coverage/fill shadow removal (small gains near drift), and cheaper filter promotion (inconsistent overview evidence). Next largest lead is excessive geometry/material work for small flora; current far tier still targets60% of near triangles. No far-geometry reduction was implemented because botanical density and transition quality need their own matched experiment. CPU/ecology changes are lower priority while viewport GPU time tracks wall time.

