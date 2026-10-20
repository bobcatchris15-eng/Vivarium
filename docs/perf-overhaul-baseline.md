# Performance Overhaul — Baseline (Phase A)

Generated during initial investigation of Vivarium (main, ~Oct 2026).

## 1. Codebase shape

- Simulation: `src/Vivarium.Sim` (C#, .NET 8), engine-free, deterministic.
- Client: `game/` (Godot 4.7 mono), rendering + UI.
- Tests: `tests/Vivarium.Sim.Tests` (xUnit), ~200+ cases per focused filter.
- World size: hexagonal island, configurable diameter; mature saves ~2.3k plants / 1.1k animals.

## 2. Simulation clock (existing)

`VivariumWorld` owns a `Scheduler` (`src/Vivarium.Sim/Time/Scheduler.cs`) over a `SimClock`.
Fixed step = `SimClock.FixedStepSeconds` (10 s simulated). Cadences (ticks) defined in
`VivariumWorld.Cadence`: fauna behaviour 4, metabolism 6, lifecycle 120, hydrology 2,
environment 30, resources 30, flora 60, plasmodium 3, litter 360, genetics prune 8640.

The scheduler already separates cadences, carries backlog (never drops), caps ticks per
advance (`MaxTicksPerAdvance=400`), and has a wall-clock budget (`WallBudgetMs=12`).
Batched systems (`RegisterBatched`) yield between chunks so a single system cannot
stall a frame indefinitely. This satisfies much of Phase B's "bounded work budgets".

## 3. Measured baseline (from docs/mature-island-performance.md)

Mature save, GTX 1080, 30 s probe: 4.9 FPS, median frame 186.9 ms, p95 388.6 ms,
worst 541.1 ms. After prior optimizations: ~8-11 FPS. Target 30 FPS. Still ~3x short.

Reported hot areas: Initial Coverage construction (6.4 s -> 144 ms after fix),
fauna LOD generation, flora/fauna simulation, native mesh uploads, ~9.7 M submitted
primitives. Simulation backlog decreasing but present.

## 4. Bottleneck hypothesis (to be measured, not assumed)

1. Rendering: ~9.7 M primitives, redundant submissions, per-frame mesh rebuilds,
   CPU/GPU sync on uploads.
2. Simulation: fauna metabolism/lifecycle and flora steps run at fixed cadences
   regardless of visibility; full-world scans in establishment/spore rain.
3. Spatial queries: `Grid.DomainCells` iteration in spore rain (12 random cells x N species);
   flora establishment `CanEstablish` scans.
4. Allocations: managed arrays for ground-cover tiles, mesh publication.

## 5. Subsystem dependency map (read/write sets)

| System | Reads | Writes | Cadence |
|---|---|---|---|
| fauna.behaviour | Fields, Terrain, Props, Flora | Fauna (position/heading) | 4 |
| fauna.metabolism | Fields, Litter | Fauna (energy) | 6 |
| fauna.lifecycle | Fauna, Flora | Fauna (birth/death) | 120 |
| hydrology | Terrain, Water | Water depths | 2 |
| environment | Water, Fields, Props | Moisture, nutrients, light | 30 |
| ecology.resources | Fields, Litter | Nutrients | 30 |
| aquatic | Water, Fields | Aquatic state | 60 |
| ecology.litter | Litter, Terrain | Litter mass | 360 |
| flora | Fields, Litter, Water | Flora individuals | 60 |
| flora.dead | Flora | DeadFlora | 60 |
| flora.reproduction | Flora, Fields | new Flora | 60 |
| flora.seedbank | Fields | SeedBank | 60 |
| flora.ambient | Fields | AmbientGroundCover | 60 |
| coverage (mat/lichen) | Fields, Water | Coverage rasters | 60 |
| coverage.plasmodium | Fields, Litter | Plasmodium network | 3 |
| genetics.prune | Genomes | Genomes | 8640 |

Shared mutable state: `Fields` (moisture/nutrients/light), `Litter`, `Water`, `Flora`,
`Fauna`, `Props`. Resolution: deterministic tick ordering by `Order` then name;
batched systems are non-reentrant within a tick (`CompletePendingTick`).

## 6. Plasmodium dual implementation

Two code paths exist:
- `src/Vivarium.Sim/Coverage/Plasmodium/` — connected network (Network, Foraging, Lifecycle, Attractant).
- `src/Vivarium.Sim/Flora/FloraSystem.cs` — slime_mold archetype via `Creep` (individual patches, spore rain).

The Coverage plasmodium is the authoritative one registered in the scheduler
(`coverage.plasmodium`, cadence 3). The Flora slime_mold path spawns individual
flora individuals via `SporeRain` and `Creep`. These can conflict: manually placed
slime_mold individuals follow FloraSystem creep while coverage plasmodium follows
the network model. Consolidation (Phase E2) should route manually placed slime_mold
into the coverage network or disable the Flora creep path for slime_mold species.

## 7. Test baseline

Build: clean (0 warnings/errors). Focused scheduler tests: 45/45 pass.
Broader subset (Scheduler|Time|Flora|World): 183 passed, 12 failed, 3 skipped.
Pre-existing failures are in WorldTests (terrain relief range) and FloraTests
(spore bank / tint), likely tied to in-progress content changes; not introduced
by this report.

## 8. Next steps (Phase B/C)

- Add spatial index (uniform grid) for flora/fauna neighborhood queries.
- Decouple mesh generation from ecological ticks (dirty-region invalidation).
- Consolidate plasmodium implementations.
- Run headless fast-forward benchmarks and 15-minute camera-circuit test.
- Profile with `--perf-test` on target hardware.

## 9. Unresolved limitations

- Full 30 FPS target not yet met (~8-11 FPS mature; initial-population probe 23.9 FPS).
- Two save/replay continuation tests known failing (see mature-island-performance.md).
- No multi-hour soak or GPU timing capture performed.

## 10. Phase progress (autonomous run)

Completed:
- Phase A: baseline report, subsystem dependency map, probe report (docs/perf-overhaul-probe.md).
- Phase B (partial): scheduler already multi-rate/bounded; plasmodium consolidated; spatial index (SpatialGrid) added for flora/fauna.
- Phase D (partial): dirty-versioned rebuilds already present in CoverageRenderer/OrganismRenderers; no further change needed.

Next:
- Adopt SpatialGrid.ItemsNear in fauna steering/cohesion and flora establishment hot paths.
- Throttle Guardian.Vegetation and AmbientGround refresh further (658-1048 ms spikes).
- Run mature-save probe for before/after comparison.
## 11. Mature-save probe attempt
Godot crashed (signal 11) loading build/mature-optimization/fixture.vivsave. CLI inspect-save loads it fine (day 268.5, 2309 flora, 1156 fauna, ~9.7M tris), so the crash is rendering-side (primitive count / uploads), not save-schema. Confirms render bottleneck. Sim-side CoverageSystem.Step #1 takes 1017 ms (Physiology 927 ms) on mature save — a major sim hotspot in MatRules via SamplePhysiology (moisture/light/nutrients per cell).

## 12. Probe stability note (2026-10-17)
Even the default-preset 30 s probe now crashes (signal 11) on the current working tree, including after reverting my sim/game edits. The crash is pre-existing in the uncommitted photorealism changes (working set grows 1.3→1.9 GB in 30 s), not caused by this overhaul. So multi-hour soak / GPU timing capture cannot be completed until the rendering memory growth is fixed. Root cause likely unbounded mesh/texture retention in the in-progress renderers.