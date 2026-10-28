# General app performance pass — 2026-10-27

## Scope and status

CPU hitch candidate implemented in AquaticSystem.cs. General-app GPU/FPS acceptance remains pending editor isolation. Godot editor PIDs 2812 and 29976 were preserved; permission to close them was requested and not received during this pass.

## Evidence

Frozen source and harness: build/app-perf-20261028. The folder named baseline-source currently contains the candidate AquaticSystem; its original is preserved as AquaticSystem.baseline.cs. Probe.csproj references that frozen simulation. Explicit Rebuild was used for final reference and candidate runs.

Copied player autosave SHA256: 424832114FB2FFA76FD5B31D47589CC749BF3DC7C2D3240E95A191762D71EEBD. The live save still had this hash after testing. Loaded day 137.886, 7013 flora, 177 fauna. CPU-only probe warms 120 ticks then measures 600 ticks. Ground coverage is synchronous in this probe but asynchronous in GameSession; its timing is not proof of a game main-thread hitch.

| Final rebuilt run | Longest recruitment block | Longest aquatic batch | Total simulation wall time |
|---|---:|---:|---:|
| reference-restored.json | 145.87 ms | 146.06 ms | 2880.31 ms |
| candidate-confirmed.json | 8.76 ms | 30.21 ms | 3014.24 ms |

This is evidence for bounded scheduling of recruitment, not improved overall throughput or native frame pacing. Shared-machine timing varied; initial candidate batches ranged from 3.08 to 15.23 ms. The file baseline-2.json is INVALID as a reference: incremental build retained candidate code after restoring an older source timestamp. Final runs explicitly rebuild and supersede it.

All measured runs reached the same complete authoritative world digest:
2d933e4c70cd4dd08cb372b5573219428c99fe604ad682acd1e6f6d480aff2b8

## Change

Recruitment and lateral colony spreading now yield every 64 coarse-cell visits and every 256 fine-cell/application visits. Enumeration, calculations, queued-write order and scheduler ordering are retained. Recruitment timing sums active work, excluding time spent suspended between frames. No renderer or presentation setting was altered.

## Validation

- 32 aquatic and bounded-scheduler tests passed, including resumable world-state equality and save/tool completion of pending ticks.
- Frozen candidate solution build: success, zero warnings.
- Frozen candidate game build: success, three pre-existing warnings (LoamGroundReview, SmokeRunner, GuardianSkinContact).
- git diff --check passed for AquaticSystem.cs.
- No GPU benchmark, native visual acceptance, wall-frame p99 or overall FPS claim.

Next: once editors are closed, run copied-save readiness-gated native baseline and attribution, then measure one renderer candidate against repeated reference drift. Preserve the approved water appearance.
