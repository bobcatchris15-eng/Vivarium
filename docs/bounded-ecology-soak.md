# Bounded ecology updates and seeded unattended run

Implemented October 10, 2026. Current run directory: `E:/Vivarium/build/soaks/20261010-bounded-island`. The game is left running at 1x with no timeout; closing the game ends the run. Ctrl+Shift+D stops only diagnostic recording.

## Implementation

Scheduler now supports ordered resumable systems. It retains the in-progress tick and owed simulated time, yields between work batches when the client budget expires, and commits the authoritative clock only after every due system completes. No later simulation system interleaves with an unfinished one. Direct StepTick/RunUntilTick still drain the same ordered work synchronously. The client allowance is 6 ms, a soft budget: individual atomic operations can exceed it.

Flora growth/counting/recruitment yields between individuals; litter decay and ambient ground cover yield in groups of 16 cells. Update cadences, simulated dt and plant processing order are retained. Frame rendering can see partially updated visual attributes between batches. Saves and ToolActions finish a pending tick before taking snapshots or mutating the world. Automatic saves retain their timer while work is pending and ask the scheduler to yield at the next completed tick, preventing save starvation under continuous backlog.

Soil-detail geometry now builds in resumable chunks with a 2 ms soft budget and publishes its seven instance groups in stages. Conservative per-cell plant candidate lists preserve original plant order for exclusion and root-collar/tint checks while avoiding the prior cell-by-all-plants scan. Geometry remains on the Godot thread. A native upload or one large chunk can exceed the budget.

Distant parametric flora now targets 8% of near-tier triangles rather than 12%; near and medium targets remain unchanged. Structural and essential spans remain protected by the existing tier compiler. Geometry tests verify nonempty monotone tiers, solid trunks, main stems and ringreed landmarks.

## Validation

- Final game build: zero warnings/errors.
- Final time/field/schedule/spring tests: 20 passed.
- Final bounded scheduler/autosave tests: 6 passed, including no partial clock commits, ordered continuation, identical state at equal ticks, save/tool boundaries and autosave with continuous backlog.
- Geometry/recognizable ringreed tests: 8 passed.
- Related broader checks: 32 passed, 1 shadebell leaf-identity failure. The same shadebell test failed with original far-tier budgets (reference: 3 passed, 1 failed). This is not a passing full suite.
- Preliminary copied mature-save probe: 60 seconds, 12.1 FPS mean, p95 172.99 ms, worst 262.02 ms, backlog near 4,950 simulated seconds. It overlapped test execution and preceded the final 6 ms allowance/ambient batching. It is integration evidence, not a clean performance comparison or a final mature throughput pass.
- Raw test/probe receipts: `build/bounded-soak/tests` and `build/bounded-soak/mature-check`.

## Running scene

Seed 804584441, shoreline preset, 2548x968 viewport, initial 27 flora and 35 fauna, one spring at 25 L/hour. Initial save and launch receipt exist. Early diagnostic samples show active 1x simulation, roughly 17 completed ticks/s and no errors. Startup includes multi-second native mesh construction; early FPS is not mature performance proof.

The run has its own initial save, five-minute autosaves, diagnostics, stdout/stderr and loading preview. Settings are transient. The pre-existing normal autosave SHA256 remained `68AF185796E89A9E08139FC95668F4FEAAB1DB9E643B9DE6AED42D374C672945` after launch.

`simulation_pending_tick` was added to diagnostic samples. For batched systems, `scope_max_ms` now measures individual timed work segments; it must not be compared with the previous whole-step scope as if total simulation work vanished. Scheduler system LastMs/TotalMs still aggregate complete updates.

To start another separate seeded run:

```powershell
./scripts/start-seeded-soak.ps1 -Preset shoreline -Seed 804584441
```

Use a fresh output directory per run. The launcher deliberately does not restore the normal autosave and does not modify its path. No push or commit was made.
