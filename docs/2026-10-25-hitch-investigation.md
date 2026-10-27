# Hitch investigation — October 25, 2026

The prior frozen-render optimization did not address the user's noticeable live-world stalls. This pass profiles frame tails with simulation running and protects the single production presentation.

## Reproduction

Two120-second mature live orbits reproduced severe stalls. Untraced baseline:1472frames,81.596ms average,356.555ms p95,449.026ms p99,601.272ms worst;299frames above100ms and128above250ms. Trace run:1440frames,83.423ms average,362.557ms p95,472.333ms p99,607.932ms worst. GPU timestamps generally30–45ms during the largest wall stalls. These are separate prepared worlds and tracing adds overhead; do not infer exact percentage deltas between them.

The original one-second scope reports omitted LoamSurfaceDetailRenderer entirely. Per-frame GC pause data shows some600ms frames contain only1–2ms ofGCpause, so collections are correlated but not the dominant cause of those frames. EventPipe allocation stacks identify repeated LoamAggregateBuilder.AddSpheroid and LoamLitterBuilder.AddLeaf/AddDuff work called from LoamSurfaceDetailRenderer.RebuildOnePatch. The old runtime path rebuilds up to four whole patches synchronously every0.1seconds. Sampled Godot.Vector3[] allocations alone total255.6MB in60seconds;152.6MBattributed to aggregate spheroids and67.4MBto leaves. Actual runtime suspension total1.49seconds over60seconds, max129ms. Native geometry generation/upload accounts for additional main-thread blocking beyond GC.

Evidence: build/hitches-20261025/baseline-live/20261025T152100Z-073895c788024644812095b33cbab538 and trace-live/20261025T152638Z-2a6d599c79b54fd099b2b87fb7a672f4. Runtime trace gc.nettrace and parsed gc-summary.json retained. Dotnet trace8.0.547301 installed locally in ignored build/diagnostics-tools; parser under .clanker/tasks/hitches-20261025/trace-reader.

## Candidate

Loam litter/aggregate builders now expose incremental iterators; synchronous callers still drain the same algorithm. Yield points preserve RNG order and geometry calculations. Runtime loam uses a nominal2ms/frame continuation budget and publishes one completed patch atomically while the old patch stays visible. Domain patches are cached perworld; a fair dirtyqueue avoids starving distant patches. Terrain/prop/quality edits cancel obsolete staging; ecological changes are revisited after the queue drains. This intentionally allows cosmetic dressing to catch up over multiple frames. World state is read-only.

SurfaceTools and incomplete roots are disposed on iterator cancellation. Individual mesh commits and tangent generation are indivisible and can exceed2ms; this is a scheduling budget, not a guaranteed ceiling. A new LoamDetail/BuildStep scope exposes remaining work.

Build succeeds0errors/6existingwarnings. Read-only review caught and corrected cached-domain reset whilequality0. Active120second candidate live run includes an untimed frozen-world native mesh-array equivalence audit for synchronous vs incremental generation. No performance/visual acceptance until completed.

## First retained loam result

Untraced candidate120seconds:3411frames,mean35.189ms,p9542.372ms,p9955.873ms,worst190.447ms. Frames>100ms:1 (versus299 baseline);>250ms:0 (versus128);>50ms:53 (versus358). The one>100msframe occurs0.296seconds into live measurement and has142.815ms LiquidSurface.Upload. Largest later frames74–84ms correlate with aquatic steps45–63ms, recurring approximately3.56seconds. Loam now has measured2ms continuation budget with occasional atomicsteps up to37.6ms; do not claim a hard2ms ceiling.

Native loam audit: four exact mesh-array SHA256matches across synchronous/incremental litter/aggregate generation at two dry-soil locations, with the world frozen. Both builders spanned real frames in the incremental route. World digest preserved. Native run exits0/13checks; source stability verified. Existing performance screening gate still fails and pre-existing reflection warning/texture shutdown leak persist.

Evidence: build/hitches-20261025/batched-loam-live/20261025T153354Z-7af7d1a75a2246658e35eab1897a7be0. Geometry audit loam-batch-audit.json.

Next bounded change: yield the aquatic scheduler job between existing biological stages while keeping each shared-scratch advection call atomic. Preserve physical-vs-biological timing, RNG/order and final state; focused equivalence/mass tests before native retest. No water appearance change or ecological approximation.

## Aquatic scheduling candidate

AquaticSystem is now registered with the existing resumable scheduler at the same cadence, order65 andphase21. Step remains a synchronous compatibility wrapper. Yield points separate wet collection/recruitment/bounds, bed growth, float growth, individual advection calls and projection. No other scheduler system interleaves until this system finishes. Each advection call remains atomic so thread-local scratch cannot be reused while suspended. Physical versus biological time parameters and operation/RNG order remain unchanged. Individual stages can still exceed the6ms scheduler budget; this does not promise an absolute frame ceiling.

Focused tests:5checks cover new calm/flowing exact-world state equivalence across multiple resumed steps plus existing AquaticWorldTests;4additional checks cover advection mass conservation, deterministic duckweed stepping and biofilm behavior/allocation. All9passed. Game builds0errors/6existingwarnings. Next120second combined live run: build/hitches-20261025/batched-aquatic-live, source frozen during measurement.

## Excluded aquatic attempt and isolated validation

The first combined aquatic live attempt is NOT accepted: executing simulation module ID mismatched the DLL on disk. Another user-owned Vivarium chat changed flora.gdshader/FloraVisualCompiler.cs and rebuilt the shared simulation DLL during measurement. Raw rows are retained diagnostically but must not be used as final accepted performance evidence. No concurrent work was reverted or interrupted.

Created a frozen source copy at build/hitches-20261025/frozen-source with a full source SHA256manifest and copied import cache. Benchmark now supports SourceRoot and WorldPath while retaining the original globalGPUlease and disposable user storage. New frozen-source mature validation is active. Actual player autosave is copied read-only to inputs/player-autosave.vivsave; original save/settings hashes retained in inputs/preservation.json for end-of-run checks. Saved-world test will use Preparation initial so no artificial6day advancement occurs. Final normal checkout build/tests can run without touching the frozen measured DLLs.

## Accepted frozen-source mature run

Final frozen-source live120s:3399frames,35.312ms mean,44.916ms p95,55.314ms p99,91.033ms worst. Frames>100ms:0;>250ms:0;>50ms:78. Native7checks passed, both executing module IDs matched, source manifest1061files unchanged. Existing performance gate still fails on the strict p99/30FPS screening thresholds; no blanket smoothness acceptance. First loam-only untraced candidate remains independently valid with1frame>100ms and native mesh audit13checks passed. Combined aquatic attempt in shared checkout remains explicitly excluded.

Accepted evidence: build/hitches-20261025/final-frozen-mature/20261025T154839Z-67ccae003f50465db4bd9ab4ecb4f83b. This is a comparable prepared mature-world route, not an exact-world A/B: asynchronous preparation yields slightly different tick/population/digest. The orders-of-magnitude reduction in recurring stalls is supported by two old live runs, the traced synchronous rebuild path, the isolated loam change, native mesh equality, and this repeat. Do not attribute every average-FPS difference to one code change.

The ordinary checkout has also been rebuilt successfully and all9focused aquatic tests pass together. One production presentation retained. No commits/publication. Isolated actual-autosave test currently running with Preparation initial; end-of-run save/settings hash check pending.


## Player-save bank-fringe diagnosis and event-driven candidate

The actual autosave exposed a separate pathology that the prepared fixture missed. Run `final-player-save/20261025T155520Z-ab90446ea86e48abb9131d2a785b8476` used the copied player save, initial preparation, production quality 1 and frozen source. In 120 seconds: 995 frames, mean 121.042 ms, p95 224.080 ms, p99 1553.671 ms, worst 1947.300 ms; 164 frames exceeded 100 ms and 40 exceeded 250 ms. The worst recurring stalls correlate with AmbientGround CPU scopes up to about 1928 ms. This is distinct from the earlier prepared-world result and means that earlier result cannot establish player-save acceptance.

The decorative ambient layer rebuilt its entire bank-fringe mesh when an ecological cover revision changed. Fine soil/water/leaf support checks and all mesh assembly ran synchronously. The player-save run exposed the resulting seconds-long recurring CPU rebuild directly.

Following user steering, static dressing now responds to edits, rather than polling ecological cover/litter stamps. ToolActions emits SurfaceEdited only for successful plant introduction/removal, props, terrain and water edits. GameSession invalidates the presentation. Loam queues intersecting 2 m patches for local edits; prop changes conservatively invalidate the island because both old and new footprints matter. Repeated brush dabs coalesce. Cancellation retains dirty work. Loam no longer scans and rebuilds on ordinary litter/ambient-cover changes.

Ambient ground and bank geometry build through cooperative main-thread iterators with a 2 ms elapsed-time target, yielding between cells, runner trials, leaves and mesh processing chunks. Camera-window changes request visible-area work; ordinary ecology does not. The existing complete bank mesh remains visible until replacement publication. Native mesh uploads and individual support checks are still atomic, so this is a scheduling target rather than a hard frame-time ceiling. Removed redundant MultiMesh.Buffer reads on unchanged uploads.

Placement has no fixed delay. New plant mesh publication uses a temporary translucent ghost, reusing existing juvenile/species geometry where available and a low-poly marker for a first-ever species. It is removed after publication or plant removal, and cleared on world replacement. Props retain their existing pointer preview and immediate published prop. Dressing catches up separately. This does not deliberately hide an already available plant/prop until unrelated decoration finishes.

Benchmark fixture preparation now explicitly invalidates dressing once and waits for publication; paused world-review captures also await queued detail. Runtime counters assert zero ambient/loam rebuilds during a stationary live/frozen measurement, preventing a partial-render speedup from being mistaken for acceptance. Optional native audit exercises idle caching, repeated invalidation/cancellation, eventual publication, world-digest invariance and ghost creation/cleanup.

Validation so far: solution build passes, game build passes (6 existing warnings), 11 focused tests pass, including calm/flowing aquatic exact-state equivalence and new event semantics for successful versus failed plant/sculpt edits. New native source is isolated in `build/hitches-20261025/frozen-event-source`, copied from the previous frozen source with only this candidate overlaid. Player-save 120-second run and native surface audit are queued behind another chat's GPU lease. No performance acceptance yet. Static dressing intentionally samples ecological state when rebuilt instead of continuously tracking every litter/cover change; natural detail aging and all camera views are not visually accepted by this pass.


## Incremental ambient retest and next isolation

First event-driven 120 s raw trace: 1522 frames, mean 78.852 ms, p95 99.300 ms, p99 144.802 ms, worst 270.363 ms; 70 frames >100 ms, 2 >250 ms. Ambient CPU spikes now peak at 254.279 ms rather than ~1928 ms, exposing large final mesh operations. The camera orbits in the live fixture: 10 ambient refresh publications and zero loam rebuilds during measurement. A zero-ambient-refresh assertion initially applied to this orbit was incorrect; it now applies only to fixed-camera fixtures, with a separate live stationary audit. The optional audit completed replacement/cancellation, world-digest and ghost creation/cleanup checks. Full standard report did not finish: the same loaded-save post-measurement capture path as the prior saved-world diagnostic emits singular-basis/frustum errors and stalls after macro_flora. Own process was stopped after timing/audit evidence was written. Neither saved-world diagnostic is full native/capture acceptance.

Next bank candidate stores complete world-space 2 m patches. Camera movement reuses old patches and builds only first visits. Explicit local plant/terrain edits dirty nearby cached bank patches; props conservatively invalidate globally. This bounds final mesh operations by patch size. Whole-patch view selection can show some fringe beyond the old exact per-cell 8 m camera cutoff, so this is not byte-identical view geometry. Fine complete-leaf soil/water support checks, normals and topology construction are retained. World-review bank_fringe.json now manifests per-patch audit JSON files.

A focused HitchTraceOnly launcher mode retains raw 120 s timing, executing assembly identity, the native surface audit and one screenshot without entering the separate failing multi-view/quality/replay route. It is explicitly marked as focused evidence, and never represents passing the full landscape performance/capture gate. Standard mode is unchanged. The cached-bank intermediate timing overlapped focused CPU test execution for part of its measurement and is excluded from quantitative acceptance. Use it only for remaining-cost diagnosis and functional audits.

Remaining first-retest CPU spikes include Sys.aquatic up to 163 ms. Its fine 2 cm coverage queries repeatedly ask the same coarse habitat cell for identical liquid depth. A one-cell cache now keys on coarse cell, authoritative liquid snapshot version, standing-water table, stored depth and bed height. It returns exactly the same coarse depth rather than resampling the liquid surface for each neighbouring fine cell. This also fixes stale same-fine-coordinate queries after water edits in the previous one-slot cache. No cadence, growth law, advection order or physical-vs-biological timing changes. Independent direct hydrology comparisons across water/bed edits plus the earlier focused tests pass: 12 checks. Final combined native run must remain free of builds/test CPU work during measurement.


## Final quiet combined run — focused native evidence

Evidence: `build/hitches-20261025/final-event-player-save/20261025T203337Z-6736bcbec9254ee98575b5125dd23ac6`. Same captured player-save SHA256 90e1e27ac4e5f40f4c2d0e657905b50eab312a25ff19a06dfadc037571c784c0, initial preparation, live orbit, 1600×900, GTX 1080, common production setting 1. No builds or CPU test suites ran during this final 120-second measurement. Rendering still evolves asynchronously, so the input is matched but the live world digest/tick path is not byte-identical to earlier attempts.

1583 measured frames /120.047573 s: mean 75.835 ms, p95 91.011 ms, p99 99.577 ms, worst 148.459 ms; 13 frames >100 ms and zero >250 ms. The old same-save diagnostic trace had mean 121.042 ms, p99 1553.671 ms, worst 1947.300 ms; 164 frames >100 ms, 40 >250 ms. The old trace's later standard multi-view capture failed, so retain this as a diagnostic-before/focused-after comparison, not a fully accepted standard landscape A/B. Plot: `build/hitches-20261025/player-save-hitches.png`.

Native focused run exits 0: VIVARIUM_PERF_OK, 9 checks, zero failures. Live stationary surface dressing stays idle while ecology advances; returning to cached bank patches does not rebuild; repeated invalidation/cancellation eventually publishes; world digest unchanged by rendering work; pending ghost appears and removes after publication. Both executing managed assembly module IDs match the built modules. 1061 frozen source files hash-identical at verification. 64 loam patches remain drawn, with zero rebuilds in the timed orbit. Bank geometry builds 58 patches on first visits during the orbit, then cached revisits are covered by the native audit. Ambient instance-buffer refreshes still follow camera-window changes; there is no claim that these camera-dependent buffers do zero work while orbiting.

The new depth cache recorded 20,680,055 hits and 1,371,809 misses during measurement (93.8% reused coarse samples). Aquatic CPU peaks in the final trace are 57.15 ms versus 163–181 ms in preceding candidates. Biggest remaining frame is 148.459 ms at 97.8 s; other high frames commonly follow measured GPU times around 100 ms. A first-visit bank upload still reaches 95.44 ms, so the 2 ms cooperative CPU target is not a hard native/GPU upload ceiling.

The recurring multi-second CPU rebuild is removed in this tested trace. Overall performance is not accepted: the dense scene still averages ~13.2 FPS with ~75 ms GPU work and ~17 million visible flora triangles in preceding counters. Next performance investigation should target sustained GPU rendering and occasional native uploads, without conflating them with the eliminated periodic CPU regeneration. Full standard landscape performance/capture gate remains unevaluated for this focused mode; paused all-view visuals and pending-ghost appearance have not received player-facing art acceptance.

Final validation: 12 focused unit tests pass, solution and game builds pass; full clean game compilation retains 6 prior warnings. Native screenshot inspected for scene presence only. Current save/settings and benchmark-input copy are unchanged across the final run; preservation-after-final.json records this. The user's actual autosave was already newer (14:49) than the earlier captured input before this final test; it was preserved rather than overwritten with the older fixture. No commits or publication; no own GPU process remains. Work remains single-agent.
