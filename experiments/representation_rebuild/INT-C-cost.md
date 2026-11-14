# Coverage tangent preparation: accepted bounded result

Coverage now computes the existing `Bridge.GenerateTangents` routine on its geometry worker after building an immutable mesh. The prepared upload arrays carry the optional result; `Bridge.ToArrayMesh` uses it when present and retains the original synchronous fallback for other callers and synchronous startup. The existing incremental batched tangent path is unchanged. `Coverage.Validate`, `Coverage.Upload` and worker `Coverage.PrepareUpload` scopes distinguish validation, publication and worker preparation. This is accepted as an offload of coverage tangent work for the tested configuration; it is not a claim of reduced total CPU work, GPU cost, power use or mature-world performance.

A fixed-camera, input-locked native pair on a GeForce GTX 1080 used the exact same Game and Sim DLLs (`build/coverage_cost/fixed-assembly-hashes.json`), seed 804584441, cached lighting, a 30 fps cap, 180 seconds total and 120 seconds warm-up. The only tangent-path switch was `--soak-sync-coverage-tangents` in the control. Camera position (0, 5, 9), rotation (-0.5, 0, 0) and FOV 62 matched; the control consumed zero prepared tangent uploads and the candidate consumed 844 in the measured window. The ecosystem was young, around three biological days. Both observations started with 178 flora and 77 fauna and ended with 199 flora and 77 fauna. Their measurement boundaries were not bitwise identical ecology states: start ticks were 2016 versus 2015, end tick was 3024 for both, and initial simulation backlog was approximately 0.97 versus 8.27 seconds. They committed 22.174 million versus 22.603 million coverage triangles, while mean visible primitives were very close (7,123,906.75 versus 7,123,955.38). These limits matter when attributing frame-tail differences.

| Measured 60-second window | Synchronous control | Worker tangents |
| --- | ---: | ---: |
| Coverage main-thread scope, ms/frame | 4.2221 | 1.4046 |
| Coverage Commit, ms/frame | 3.9985 | 1.1877 |
| Coverage Validate, ms/frame | 0.0226 | 0.0266 |
| Coverage Upload, ms/frame | 3.9679 | 1.1573 |
| Worker PrepareUpload, aggregate wall ms/frame | 0.7670 | 3.7357 |
| Frame p95 / p99 / worst, ms | 50.1 / 111.7 / 122.061 | 43.7 / 55.2 / 60.497 |
| Managed allocation, bytes/frame | 7,078,977.5 | 7,096,937.9 |

Coverage main-thread time fell 66.7% and Commit fell 70.3% in this pair. Worker preparation increased, so the result demonstrates a main-thread shift; worker wall scopes can overlap and must not be added to main-thread scopes as total CPU. Allocation was essentially similar in this pair. The 30 fps cap limits interpretation of frame median and FPS. Raw telemetry and comparisons are in `build/coverage_cost/fixed-control/`, `fixed-tangents/` and `comparison.json`. Both runs reported one ObjectDB warning at exit; it was not attributed by this comparison.

A real Forward+ native proof on the GTX 1080 passed (`build/coverage_cost/final-tangent-proof.log`): eight vertices and three triangles, worker-thread preparation, one prepared tangent upload consumed, and actual prepared/fallback ArrayMesh vertex, normal, color, UV, UV2, tangent and index arrays equal. It covers concurrent upgrade after generic packing, mirrored and degenerate UVs, an unreferenced vertex, and batched versus synchronous tangent calculations. The headless boot (`build/coverage_cost/final-boot.log`) passed save/load round trip with zero invariant failures. These checks do not substitute for broad visual or performance acceptance beyond the fixed-view fixture.

Reproduction commands (PowerShell; run sequentially, preserving the same built DLLs for both soaks):

```powershell
$godot = 'E:\Vivarium\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
& $godot --path E:\Vivarium\game -- --coverage-tangent-proof
& $godot --headless --path E:\Vivarium\game -- --boot-test
& $godot --path E:\Vivarium\game -- --soak E:\Vivarium\build\coverage_cost\fixed-control --soak-seconds 180 --soak-warmup 120 --seed 804584441 --lighting-mode cached --max-fps 30 --soak-fixed-camera --soak-sync-coverage-tangents
& $godot --path E:\Vivarium\game -- --soak E:\Vivarium\build\coverage_cost\fixed-tangents --soak-seconds 180 --soak-warmup 120 --seed 804584441 --lighting-mode cached --max-fps 30 --soak-fixed-camera
```

The prior age-only fingerprint candidate is archived and deferred under `experiments/representation_rebuild/rejected/Coverage-age-only/`. Its focused correctness tests passed, including real age-only lichen steps, a dry change crossing W < 40, halo changes, no-Touch pending-result rejection, writer protection, deterministic state and coverage serialization. Its native comparison did not establish a benefit: the control camera moved while the candidate camera did not, so the differing coverage times and commit counts are observational and cannot establish a causal result. The earlier first tangent pair had the same camera mismatch and different Sim DLL hashes; it is likewise observational and is superseded for performance assessment by the fixed pair above. Profiling scopes and counters are retained independently of the archived candidate.
