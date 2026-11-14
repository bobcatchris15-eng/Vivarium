# Coverage immutable async-build checkpoint

The production coverage mesher previously read live biological arrays on worker threads and could mark a mixed or stale result with a later tile version. The renderer now captures the target and its 3x3 sampling halo, plus immutable heightfield and prop inputs. Every result carries its captured dependencies; stale results remain queued for retry rather than becoming the displayed current revision.

An odd/even per-layer write epoch surrounds the actual asynchronous Mat/Lichen rule calls. Capture requires a stable epoch across its copy. Short nonblocking renderer leases protect occupancy scans and mesh publication; the main thread defers instead of waiting for the biological pass. Tile-map access is synchronized without holding that map lock over rule execution or Parallel.For. Discovery and pending-task polling are separate, so existing tasks continue to be observed while biology is active. Failed startup captures retain their pending keys. Terrain/prop changes remain pending until a successful stable discovery.

The authoritative arrays, biology rules, species and save payload are retained. Age-only invalidation, per-frame queue sorting and the production material underlay are separate unfinished work. No performance improvement is claimed by this correctness change.

Validation on 2026-11-14:
- Isolated `coverage.renderSnapshot` passed before and after the final root review fixes. It pauses an actual writer after array writes and before tile-version publication, rejects capture/publication during that window, verifies immutable pending-worker inputs, and rejects changed target/halo/terrain/prop dependencies.
- Sequential solution and game builds passed. Final game build has the existing PlantFieldDemo nullable warning and zero errors.
- The full representation suite passed72/73 checks. The sole failure was `substrate.performance`:401ns/sample against400ns. This timing failure is retained; no passing full-suite claim is made.
- Luna independently reviewed the discovery/pending split, paired lease cleanup, retry retention and epoch validation with no material blocker.

Exact receipts are `build/reviews/resume-20261113/coverage-validation-receipt.txt`, `coverage-full-suite.log`, and `coverage-root-fix-review.md`; build logs are `build/c2/scanned-solution.log`, `scanned-game.log` and `build/int_f/paced-final-game.log`. Raw output for the full suite was not saved separately; its worker receipt states that limitation.
