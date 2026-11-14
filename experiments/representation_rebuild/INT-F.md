# INT-F: 1 m flora chunk experiment rejected

The chunk implementation passed its camera-cache and mutation checks, but it did not establish a better overall production tradeoff. The previous production flora renderer has been restored. The exact candidate diff, new review runners, binary hashes and comparison report are preserved under `rejected/INT-F-1m/`. Coverage async-input correctness and the optional colony specimen are independent checkpoints and remain.

## Measured decision

One paired live observation used seed 804584441, cached lighting, a 30 FPS cap, 180 seconds total and 120 seconds warmup on the GTX 1080 / Ryzen 5 1600. Both runs began with 169 flora / 77 fauna and ended with 199 flora / 77 fauna. The frozen original game control and candidate loaded the same simulation DLL; recorded hashes identify both binaries. The candidate also contained the independently reviewed coverage snapshot fix, so this is a stack comparison rather than isolated proof of flora attribution.

| Later live window | Control | Candidate |
|---|---:|---:|
| Mean FPS, cap 30 |29.1249|29.1307|
| Frame p50 / p95 / p99, ms |33.2 /51.8 /107.8|33.2 /51.8 /110.5|
| Worst frame, ms |120.175|125.423|
| Mean GPU, ms |8.6268|9.2429|
| Draw calls/frame |765.45|826.55|
| Primitives/frame |7.1232M|7.1229M|
| Video memory |2.1269GB|2.1274GB|
| Managed allocation/frame |6.838MB|6.828MB|
| Flora CPU wall/frame, ms |0.1116|0.1513|
| Coverage CPU wall/frame, ms |3.9156|4.1201|

The observed candidate adds 7.14% GPU time and 7.98% draws without a meaningful frame-pacing or memory benefit. Both runs submitted essentially the same primitive count. This pair is sufficient to reject the measured candidate; it is not a repeated/interleaved performance acceptance result. Scope times overlap and are not GPU timings. No 860M result is claimed.

Candidate readiness was true with no layer builder or recipe worker at both measured-window boundaries (62 then 63 species); one species appeared during the window and readiness was false for 125 frames. End-of-window backlog was 8.89 simulation seconds; the immediately preceding 1 Hz sample was 0.81 seconds. Thus backlog is phase-dependent, not a zero-backlog claim. Exact reports and raw samples remain under `build/int_f/paced-mature-candidate` and `paced-mature-control`; `paced-comparison.json` retains both summaries. The accompanying analyzer distinguishes approximate startup 1 Hz intervals from pooled frame percentiles.

## Useful verified results retained as experiment evidence

The candidate used persistent 1 m chunks with species/morph/organ/LOD MultiMeshes and legacy assembled geometry. Cached bounds drove camera-only selection; biology-time snapshot comparison still walked the population. Parent/host dependencies dirtied affected chunks, while terrain/props/water table remained conservative global invalidations.

The final native `build/int_f/paced-final-orbit/report.json` passed 36 warmed camera revisits with zero population visits, chunk builds and flora instance-buffer uploads. Growth/movement/birth/removal touched 1/2/1/1 expected chunks, while an unrelated chunk stayed stable. Distinct-species birth raised cache nodes 498 -> 501; removal restored 498. The fixture had no actual host-link dependency, so attachment invalidation remains unproven. A native guardian fixture reordered two reused slots, removed one, and passed four native pose reads; 169 contact sources were audited. Those guardian conversion changes are archived with the rejected backend, because the restored backend still uses its original source layout.

Earlier 8 m and 4 m variants were rejected for excessive close-view geometry/GPU cost. A quiet eight-shot 1 m static comparison reduced draw calls about 34% and video memory about 128 MiB at approximately 1.5% higher mean GPU time; the grazing-transition row was invalid and excluded. That static result did not generalize to the running initial camera. Earlier 75 s / warm 25 s live candidates also failed while initial recipes/publication overlapped their measurement windows; those reports are retained, not replaced by a passing startup claim.

The last candidate separated ever-presented state from full readiness to avoid restoring a 50 ms startup publication budget for a new species, and stopped aborting refreshes when layers published. These changes are preserved for a future representation experiment. A synchronous mesh publication can still exceed its nominal 2 ms budget.

## Reproduction and next bounded task

The rejected source can be reconstructed in an isolated source copy of `f35bee5a` by applying `rejected/INT-F-1m/candidate.patch` and copying the two `.cs.txt` review runners into `game/App` with their `.cs` names. Do not apply the patch to the dirty live checkout. The frozen native control project and actual assemblies remain under `build/int_f/control-project` and the recorded build paths. Rebuilding can change module/binary hashes; use the retained hashes for these exact observations.

Build solution then game serially. Candidate runs used `--flora-cache-review ABS_DIR --seed 804584441 --lighting-mode cached --max-fps 0` and live runs used `--soak ABS_DIR --soak-seconds 180 --soak-warmup 120 --seed 804584441 --lighting-mode cached --max-fps 30`. The review CLI exists only in the archived candidate. The retained production `--review-camera-manifest` support locks timing and screenshot cameras and records manifest/module provenance.

Next: measure a representation that preserves camera-only cache reuse without multiplying native draw/node groups in the common live view. Coverage age-only invalidation and queue scans are a separate measured CPU opportunity. No additional optimization was started in this paced checkpoint.
