# Mature island performance and loading changes

Implemented October 10, 2026 against the day 268.5 autosave copy with 2,309 plants and 1,156 animals. Live autosave was not overwritten. Benchmark fixture: build/mature-optimization/fixture.vivsave, SHA256 4430CA220B580A6FF6019815489439FBFC5DC8BD22BEBAB38825E6218E209776.

## Changes

- Flatten pilot-tree root segments and use conservative bounding boxes to reject distant segments before exact queries. Tests compare against the original ordered full scan across six tree forms.
- Cache static terrain substrate classification; invalidate on terrain/prop changes and continue checking dynamic water at refresh time.
- Build initial ground-cover tiles asynchronously, prioritize near-camera tiles, and prepare their managed upload arrays on the worker. Godot mesh creation and GPU submission remain on the main thread.
- Capture camera projection data once per flora refresh, prioritize nearby living species for mesh loading, and pace mesh publication to a 2 ms soft budget (at most four steps).
- Cull offscreen fauna before expensive animation/ground work; cache ground normals for 0.12 seconds with movement and terrain/prop invalidation. Held creatures remain visible. Generate native distance LODs for fauna meshes while retaining full near geometry.
- Stagger aquatic, litter, and flora ambient updates away from coincident expensive work. Update intervals remain unchanged, but relative phase/order changes.
- Normal launch paints a progress screen before background save reconstruction, then yields between renderer construction stages. Simulation advancement/autosave are held during stage loading. Plant detail continues loading after the island opens. Existing in-game load dialogs still use the synchronous lifecycle.

## Measured evidence

Same copied mature save, same perf runner, local GTX 1080. Short startup-inclusive probes are affected by mesh warmup; these do not establish several-hour steady performance.

| Probe | Duration | Average FPS | Median frame | p95 frame | Worst frame |
|---|---:|---:|---:|---:|---:|
| Before | 30 s | 4.9 | 186.94 ms | 388.56 ms | 541.09 ms |
| Initial optimizations | 30 s | 9.1 | 95.82 ms | 212.99 ms | 362.32 ms |
| Further optimizations | 90 s | 11.4 | 79.75 ms | 163.49 ms | 355.58 ms |
| Final upload verification | 30 s | 8.4 | 111.53 ms | 198.52 ms | 264.32 ms |

The final 30-second upload check passed without errors, but remained in startup mesh warmup; it does not demonstrate an additional FPS gain from array preparation. The 90-second run reached approximately 13 FPS near its end with all plant species loaded, approximately 9.7 million submitted primitives, and a simulation backlog that was decreasing at the end. Before and initial probes had different amounts of plant detail loaded, so do not treat these as identical steady rendered workloads.

Initial Coverage construction fell from 6,386 ms to approximately 144-146 ms, with remaining tile work queued. Individual startup stages can still block a frame, particularly fauna LOD generation. The loading screen was verified through normal startup using the mature copied save; images are in build/mature-optimization/loading-review. It returned LIVE_DIAGNOSTICS_CHECK_OK without logged errors.

## Validation and limits

- Game build passed with zero warnings/errors.
- Root-focused tests: 47 passed. Final focused root, scheduler, hydrology, time-field and fauna-gait tests: 34 passed.
- Two save/replay continuation tests failed. Both also failed in a temporary reference run with the new root rejection disabled and schedule phases restored; receipts: save-regressions.trx and save-reference.trx. This is an unresolved checkout replay issue, not a passing full test suite.
- No new multi-hour soak or GPU timing capture was performed. Current mature frame times remain well above a smooth 30 FPS target. Whole simulation systems and individual native mesh uploads can exceed the soft frame budget; flora and fauna simulation remain significant costs.
- Runtime receipts: build/mature-optimization/{before,after-second,after-final,after-upload}; test receipts: build/mature-optimization/tests.

Start an unattended recording from the editor-launched game with Ctrl+Shift+D, or the F3 debug panel's recording button. Stop recording before stopping the scene in the editor. F9 is the editor pause key.

