# World loading and mature-world performance — 2026-10-20/21 (continued)

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
| Guardian.Appendage | 232 ms/s | 136–169 ms/s |
| Guardian.Appendage.Veg | 180 ms/s | 79–120 ms/s |
| Guardian.SkinContact | 137–157 ms/s | 41 ms/s |
| Flora.Lights | 98 ms per call | 0 in daylight |
| LiquidSurface.Upload | 78 ms/s | 12 ms/s |
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
- **The luminous light registry is a lookup, not a scan.** Registering a light
  searched the whole list of registered lights — up to 660 of them — and the
  registry is consulted once per luminous plant per refresh. A mature world
  has several hundred of them, so the scan was two million weak-reference
  lookups per refresh: ninety-eight milliseconds, every second. It is now a
  dictionary lookup.
- **Daylight does not move invisible lights.** In daylight every luminous
  plant's light is at zero energy and cannot be seen, so the update that
  repositions and re-energises six hundred lights was work nobody could see.
  It runs at night, when it matters.
- **The water surface reuses its upload images.** Every fluid snapshot
  allocated a native image for every water tile's texture pair. The images are
  now reused, which cut the upload cost about six times.

### A bug this session introduced, and caught

The water surface also stopped uploading tiles the camera could not see. The
frustum test was written with the comparison inverted: Godot's frustum planes
face outward, so a point *inside* the frustum has a negative distance, and the
test was culling exactly the tiles that were visible. The water disappeared
from the island, and the profiler showed the upload cost collapsing to nearly
zero — which looked like a large win and was the opposite.

Two things came out of it:

- The renderer now reports how many tiles it uploaded, culled and resynchronised
  each refresh, so "the water is not being uploaded" can never again be
  mistaken for "the water is cheap".
- The tile counts are part of the perf report, so a culling test that removes
  everything shows up as `waterTilesUploaded: 0`.

The water is visible again and the captured frames before and after the fix
are the same.

### Measurement instrumentation

- `FrameProfiler` now reports the total CPU milliseconds per scope per
  interval, not just the slowest frame in it. That is what shows which costs
  *grow* as a world matures; a maximum hides it.
- The perf runner records the render stage times of a world start, the
  guardian's own build steps, the skin scan's internals, the water tile
  counts, and a machine calibration, so a slow measurement can be told from a
  slow machine.
- A new `--new-world-test` mode measures the insertion path: world generation
  plus every render stage, for a world created after the process has already
  built its content — which is what the in-game "New vivarium" does.
- The environment tick reports its four parts, which is how the light field's
  recompute was found to be a 370 ms stall after the water eroded the terrain.

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

## A/B of the whole change set, under identical machine load

Both builds measured back to back, twice, on the same mature world, on a
machine that was busy with other agents throughout:

| | baseline r1 | changed r1 | baseline r2 | changed r2 |
|---|---:|---:|---:|---:|
| frames per second | 8.7 | 8.9 | 8.0 | **16.1** |
| frame time p50 (ms) | 92.0 | 82.2 | 94.6 | **50.7** |
| frame time p95 (ms) | 278 | 308 | 336 | **119** |
| frame time p99 (ms) | 446 | 438 | 526 | **174** |
| Guardian.Appendage (total ms) | 27,987 | **11,124** | 24,925 | **21,083** |
| Guardian.SkinContact (total ms) | 7,271 | **1,273** | 9,346 | **3,071** |
| Flora.Lights (total ms) | 576 | **0** | 755 | **0** |

The frame-rate row is mostly the machine's mood — one agent's stray Godot
process was consuming a core and a half for the whole comparison — but the
per-scope totals are stable: the skin contact costs a quarter to a third of
what it did, the luminous light update is gone in daylight, and the tail
solver is down by a third to a half.

The same comparison on a quiet machine earlier in the session measured 28–29
frames per second for the baseline and 36–42 for the changed build, with the
frame time falling from 45 ms to 24 ms.

## The second round of loading work

The insertion was measured again after the first round, and the remaining cost
was the guardian: its model was still read from disk during the guardian stage,
and its skin scan still cost most of a second.

| | before | after |
|---|---:|---:|
| guardian scene load | 480–1,205 ms | **0.5–3.6 ms** |
| guardian skin scan | 990–2,400 ms | **158–329 ms** |
| render stages, total | 1,100–3,200 ms | **629–1,162 ms** |

- **The guardian model now loads while the world is being generated.** The
  archetype a world will grow follows from its descriptor, so the client can
  ask for the model before the world exists and let it stream in beside the
  terrain, the water and the props. The load itself is unchanged; it simply
  no longer sits on the critical path.
- **The skin scan computes one matrix per skin bind.** The scan evaluates
  every vertex against the bone rest poses and the inverse binds. The product
  of a bone pose and an inverse bind is the same for every vertex that uses
  that bind, so it is built once per bind rather than once per vertex
  influence. The arithmetic and its order are unchanged, so the sampled
  vertices and the reconstruction error are identical.
- **The scan runs on one thread.** Splitting it across cores was tried three
  ways — a captured local function, an explicit partition over a context
  object, and a plain loop. On one guardian mesh the parallel forms were ten
  times slower and allocated two and a half gigabytes, while the plain loop
  took 172 ms. The scan is now plain, and the reason is recorded in the code
  so it is not "optimised" back into the slow form.

The world generation itself is now broken down by part, which is how the
remaining cost was found: on a quiet machine it is about 670 ms, of which the
starter placement is 296 ms (it scores every other cell of the island for
every starter species) and the coverage seeding 181 ms.

## The third round: the three costs that were left

The previous round ended with a list of three. All three were taken on.

### 1. The starter placement now scores the island across cores

The island is scored once per starter species: for every starter, every other
cell of the island is asked how suitable it is. A cell's answer does not
depend on the other cells, so the scan is split across cores. The random
tie-break is still drawn in the original order and the sort is unchanged, so
the chosen cells are the same as before.

| world creation (`soak --days 1`) | runs | fastest |
|---|---:|---:|
| sequential | 3,713 / 3,269 / 3,610 ms | 3,269 ms |
| across cores | 3,073 / 2,703 / 2,470 ms | **2,470 ms** |

That is a 24% shorter world creation, measured back-to-back on the same
machine.

Three pieces of shared state had to go before the scan could be split:

- The flora and fauna neighbour queries filled one scratch list each. A
  shared list is a race, so both are now per thread.
- The canopy shade field is rebuilt by clearing it and adding the crowns, so a
  rebuild in the middle of a scan would be read half written. It is now
  brought up to date before the cores start.
- The flora's spatial index is rebuilt lazily. It is now rebuilt before the
  cores start as well.

**The equivalence is proved, not assumed.** Two tests were added: one scores
every cell on one thread and across cores and compares the answers bit for
bit, the other grows a whole world both ways and compares the world digest.
The parallel scan grows the same island, plant for plant.

### 2. The tail solver asks about far fewer stems

The tail's contact is resolved against the stems of nearby plants. The stem
index had a fixed margin of 0.25 m around each query — a guess made for the
widest tail the game has. A tail that is 5 cm wide only needs the stems
within about 10 cm of it, so the margin now follows the caller's own contact
reach instead.

| | before | after |
|---|---:|---:|
| stems tested per sample | 89–104 | **40.6** |
| vegetation projection per solver step | 3.3–9.9 ms | **1.7 ms** |

The stem audit is unchanged: 512 audits, 0 error. The projection still lands
the tail on exactly the same stems.

A finer index cell (6.25 cm instead of 12.5 cm) was tried as well. It found
fewer stems still, but the index then had to be built four times as finely
and the whole thing came out slower, so the cell size stayed.

### 3. The night-time light overlap

Six hundred luminous plants in view, and every one of them is re-energised
twice a second: each light's brightness is damped by how much its neighbours
overlap it. The damping is a sum over every pair of lights within reach, and
in a mature colony that is about 195,000 pairs per refresh.

Three changes were made, all of which leave the result identical:

- The pair loop read the whole light record for each pair, which meant
  copying thirty bytes of position and range nearly two hundred thousand
  times. The positions and squared ranges now live in flat arrays.
- A neighbour further away than its own reach contributes nothing, and the
  division was the expensive part of the pair. The division is now skipped
  for those pairs.
- The lights are binned into one-metre cells. A cell whose nearest point is
  further away than the widest light in it cannot hold a light within reach,
  so the cell is skipped whole instead of being walked light by light. In a
  mature colony that removes 61% of the cell visits and 40% of the pairs.

Measured with the same build, the same fixture and the night factor forced on
(two runs of each, interleaved):

| | pairs per refresh | overlap ms per refresh |
|---|---:|---:|
| as it was | 195,414 / 195,670 | 11.44 / 11.38 |
| flat arrays and the skipped division | 196,058 / 195,852 | 10.31 / 10.39 |
| and the cell-level skip | 116,586 / 116,594 | **7.65 / 7.58** |

A third of the loop's time, and the overlap value is bit for bit what the old
code produced: the light audit reports **0 error**.

What is left is the pair count itself, and the pair count is set by how many
luminous plants are in view. Capping the number of lights would cut the cost
and the engine's per-light cost with it, but that is a decision about what a
colony is supposed to look like, not a performance change, so it was not
taken.

One attempt was made and reverted: bounding each light's neighbour search by
its own reach. That is wrong — a light's reach to its neighbour is decided by
the *neighbour's* range — and the light audit caught it with an overlap
error of 0.017 before it could ship.

## The shared tree's fixture went stale

The content in the shared tree moved on while this work was in the isolated
worktree: species were added and reworked, so the day-60 fixture no longer
matches the content and the game refuses to load it ("save state digest
mismatch"). The measurements above are therefore from the worktree, whose
content and fixture still agree. A fresh fixture has to be grown before the
shared tree can be measured again.

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

## Where the work landed

Thirteen files, +719/−84, applied in the shared tree. A copy of the patch is
in `build/perf-chase/vivarium-perf-chase.patch`; the mature fixtures and the
measurement harness are in `build/perf-chase/`.

The shared tree was verified with the audits enabled:

```
stem audit        : 512 audits, max error 0
registry audit    : 64 audits, 32 accepted, 32 rejected
lowest point audit: 256 audits, 0 differences, max error 0
skin cache        : valid, 218060 rest vertices, max error 6.8792638e-06 m
water tiles       : 28 total, 26 uploaded, 0 culled
```

The skin cache line is the same on both the baseline and the changed build:
the scan picks exactly the same vertices, with the same reconstruction error.

## Next

1. The starter placement is across cores now. The coverage seeding (181 ms)
   is the next largest part of the world generation, and the guardian scene
   load (300–700 ms of disk and deserialization) is still on the critical
   path, even though it is hidden behind the terrain.
2. The tail solver still tests about forty stems per sample, because the
   plants around the guardian are packed within centimetres of each other.
   The projection itself is now cheap; the count is what remains.
3. The luminous light overlap is a third shorter and free of wasted work
   now, but it is still a pair loop over the lights in view. The lever is
   the number of lights, and that is a decision about the look of a colony.
4. The world generation's own cost (0.3–2.1 s, heavily dependent on the
   machine's load) has only been measured, not yet reduced.
5. The shared tree's mature fixture no longer matches its content. A fresh
   one has to be grown before the mature world can be measured there.
