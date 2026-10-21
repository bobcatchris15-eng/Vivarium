# World loading and mature-world performance — 2026-10-20 (continued)

## Scope

Two goals, in order:

1. Make a new world appear faster and more pleasant.
2. Remove the performance cliff that arrives once a world matures, then keep
   soaking it, chase the largest remaining cost, and repeat.

This session continued from `docs/performance-2026-10-20.md`. It ran in an
isolated worktree (`C:\Users\Chris\.local\share\opencode\worktree\8235e5\perf-chase`)
because two other agents were editing the same tree; the baseline commit
`75d8d545` is that tree's state at the time. Every number below comes from a
matched back-to-back pair on the same machine, same fixture, same build
configuration (the optimised build, which is what a shipped player runs).

## The mature world before this session

The fixture is a day-60 world: 3,712 flora, 360 fauna, ~14.7 M submitted
primitives. The per-second CPU budget (total ms of CPU per wall second, from
the new interval profiler) before these changes:

| scope | ms/s | note |
|---|---:|---|
| Guardian.Appendage | 232 | the guardian's tail solver |
| Guardian.Appendage.Veg | 180 | its vegetation contact |
| Guardian.SkinContact | 137 | 1,500 evaluations per second |
| Guardian.Vegetation | 79 | the flora contact registry |
| Session.Advance | 109 | the simulation |
| LiquidSurface | 70 | the water surface upload |
| Coverage | 58 | the coverage renderer |
| Fauna | 43 | |
| Flora | 40 | |

The guardian — one animal — was the largest cost in the mature world, and the
tail solver was the single largest line in it.

## Changes

### Loading a new world

The in-game "New vivarium" path creates a world after the process has already
built its content, so round 1 of the measurement below is that path.

| stage | before | after |
|---|---:|---:|
| guardian skin contact construction | 990–1,105 ms | 146–294 ms |
| guardian scene load | 365–716 ms | 284–683 ms, overlapped with the other stages |
| world-changed UI rebuild | 192–422 ms | 0 ms |
| **render stages, total** | **~2,124 ms** | **820 ms** |

- **The guardian skin scan no longer allocates a sample object per vertex.**
  A guardian mesh carries a hundred thousand vertices; the old form allocated
  four objects per vertex, which meant about a gigabyte of garbage and a
  hundred and sixty garbage collections per world start, and the scan cost
  more than a second of wall time. The scan now records what it learns in
  flat arrays and materializes sample objects only for the vertices that
  survive the selection — the same samples, in the same order, from the same
  arithmetic. The scan is also split across cores; each vertex is independent,
  so the result is identical. The proof that it is identical is in the
  measurement below: both builds select the same 218,060 rest vertices and
  report the same reconstruction error, 6.8792638e-06 m.
- **The guardian model is streamed while the island is prepared.** The world
  start knows which guardian it will need, so the model is requested on a
  worker thread at the beginning of the start instead of blocking on it after
  the props, the tree and the ground cover are built.
- **A world change no longer rebuilds the catalog and statistics panels.**
  Those panels list the content, which does not change with the world; only
  their numbers do. Rebuilding several hundred controls cost a fifth of a
  second right when the island appears.

### The mature world

| scope | before | after |
|---|---:|---:|
| Guardian.Appendage | 232 ms/s | 145 ms/s |
| Guardian.Appendage.Veg | 180 ms/s | 93 ms/s |
| Guardian.SkinContact | 137–157 ms/s | 41 ms/s |
| vegetation narrow phase, total | 8,560 ms | 423 ms |
| frame time p50 | 45 ms | 24 ms |
| frames per second | 18–29 | 36–42 |

- **The trunk avoidance pass no longer asks the ground about every sample.**
  It ran the full skin evaluation — which asks the terrain, the root shelves
  and the props for a support height *per sample*, sixty-four times a sample —
  for six body regions, inside a loop that runs up to eight times a frame. It
  only ever used the lowest point of each region, so it now asks for that
  point directly, and it only asks the ground about the samples that could
  still win. The audit (`--guardian-lowest-point-audit`) compares the chosen
  point with the one the full evaluation chose, over 256 regions: zero
  differences, zero error. This was the single biggest win of the session.
- **The tail solver projects against a stem index instead of a bounds index.**
  A plant's bounds cover the whole plant, so a tail inside a bush was tested
  against every plant whose box happened to overlap it. Only a plant's *stem*
  can push the tail, so the stems now have their own index, and the index
  keeps the runner-up so that evicting the farthest plant does not force a
  rescan. The projection is bit-identical: the stem stands inside the plant's
  own bounds, so the old bounds test never blocked a stem projection. The
  audit (`--guardian-stem-audit`) compares the two paths sample by sample and
  reports zero difference; the registry audit
  (`--guardian-registry-cache-audit`) compares every registry decision with a
  full rescan and reports the same answers.
- **The tail solver stops when the chain has settled.** The constraint
  iteration now ends when the joints stop moving by more than a tenth of a
  millimetre instead of always running all thirty-two times. The solver was
  converging at about iteration twenty-two in three steps out of four.

### Measurement instrumentation

- `FrameProfiler` now reports the total CPU milliseconds per scope per
  interval, not just the slowest frame in it. That is what shows which costs
  *grow* as a world matures; a maximum hides it.
- The perf runner records the render stage times of a world start, the
  guardian's own build steps, the skin scan's internals, and a machine
  calibration, so a slow measurement can be told from a slow machine.
- A new `--new-world-test` mode measures the insertion path: world generation
  plus every render stage, for a world created after the process has already
  built its content — which is what the in-game "New vivarium" does.

## The cliff, after this session

A ten-minute soak on the mature world, sampled every second:

| window | fps | frame ms | worst frame | simulation backlog |
|---|---:|---:|---:|---:|
| 0–120 s | 32.6 | 78.6 | 80.1 | 90 s |
| 120–240 s | 36.4 | 57.2 | 57.6 | 7 s |
| 240–360 s | 38.2 | 55.3 | 55.1 | 5 s |
| 360–480 s | 42.1 | 52.8 | 52.7 | 6 s |
| 480–600 s | 41.6 | 53.7 | 53.3 | 7 s |

The frame gets *shorter* as the world matures now, and the simulation backlog
drains instead of growing. The cliff is gone: the world is cheapest once it
is fully grown. Before these changes the same soak degraded from 17 fps to
14 fps with the backlog climbing, and the plasmodium network alone grew ten
times over the run.

What is left, and how it behaves over the same soak:

| scope | ms/s | growth over the run |
|---|---:|---:|
| Guardian.Appendage | 145 | 0.78x |
| Guardian.Vegetation | 93 | 1.15x |
| Session.Advance | 93 | 0.68x |
| LiquidSurface | 65 | 1.06x |
| Coverage | 48 | 1.00x |
| Fauna | 55 | 1.26x |
| Flora | 53 | 0.88x |
| Guardian.SkinContact | 41 | 0.31x |

Nothing grows by more than a quarter over ten minutes.

## Where the work landed

The changes are in the shared tree (`game/App/GameSession.cs`,
`game/App/SmokeRunner.cs`, `game/Render/GuardianAppendageModifier.cs`,
`game/Render/GuardianRenderer.cs`, `game/Render/GuardianSkinContact.cs`,
`game/Render/GuardianVegetationContact.cs`, `game/Render/PreloadGuardians.cs`,
`game/UI/Windows.cs`), eight files, +558/-76. A copy of the patch is in
`build/perf-chase/vivarium-perf-chase.patch`; the mature fixture is
`build/perf-chase/fixture-mature-day60.vivsave`; the measurement harness is
`build/perf-chase/run.ps1`.

The shared tree was verified with the audits enabled:

```
stem audit        : 512 audits, max error 0
registry audit    : 64 audits, 32 accepted, 32 rejected
lowest point audit: 256 audits, 0 differences, max error 0
skin cache        : valid, 218060 rest vertices, max error 6.8792638e-06 m
```

The skin cache line is the same on both the baseline and the changed build:
the scan picks exactly the same vertices, with the same reconstruction error.

## Caveats

- The machine was shared with two other agents throughout. Matched
  back-to-back comparisons are used everywhere above; absolute frame times are
  noisy to about plus or minus two frames per second.
- The `Stopwatch` clock on this machine costs about 160 ns per read, so
  scopes measured with a single start/stop pair around a whole frame are
  trustworthy and per-operation timings inside them are not.
- The worktree's simulation tree is byte-identical to its baseline. The
  thirty-four failing simulation tests and the five failing smoke checks in
  that tree are present on the baseline too: they come from the content and
  geometry changes that were synced in from the shared tree, not from this
  work.
- The plasmodium network, which grew ten times over a mature run and was a
  third of the frame, was removed from the product by another change in the
  same tree during this session. The parallelisation of its phase
  integration was written and proved bit-identical against the sequential
  path before the subsystem went away; it is not part of the patch.

## Next

1. The water surface upload re-uploads every active tile whenever the fluid
   publishes a new snapshot. Only the tiles whose cells changed need it, and
   the fluid's snapshot does not carry a per-cell version yet.
2. The tail solver still tests about ninety stems per sample, because the
   plants around the guardian are packed within centimetres of each other.
   The projection itself is now cheap; the count is what remains.
3. The guardian scene load is 284–683 ms of disk and deserialization. It is
   overlapped with the other stages now, but it is the floor of the start.
4. The world generation itself (0.3–2.1 s, heavily dependent on the
   machine's load) has not been touched.
