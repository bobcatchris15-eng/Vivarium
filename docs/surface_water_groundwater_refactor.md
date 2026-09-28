# Surface Water / Groundwater Separation Plan

Goal: replace the current single hydrology depth field that simultaneously represents groundwater and runoff with a fully separated, mass-conserving surface-water solver plus a hydrostatic groundwater table, while preserving cheap heightfield rendering and ecological coupling.

## Ground rules
- Work directly toward `main`, but keep every stage independently buildable/testable.
- Groundwater is not stored in the dynamic surface-water depth field.
- Dynamic water is a 2D heightfield shallow-water system: depth + face discharge/velocity, not particles/voxels.
- Rendering remains decoupled from simulation resolution.
- Old saves remain loadable where practical; new saves persist the new authoritative state.
- Tests must cover conservation, downhill flow, pooling/overtopping, groundwater absorption, terrain edits, moisture coupling, persistence, and determinism.

## Stage 0 — Baseline and plan
- Record this implementation plan.
- Run the existing simulation test suite before touching hydrology.
- Capture the current main commit as the migration baseline.

## Stage 1 — Separate authoritative state
- Refactor `Hydrology` so `Depth` means dynamic surface-water depth only.
- Add explicit groundwater queries: table elevation, exposed groundwater depth, exposed groundwater wetness.
- Remove water-table top-up from surface depth initialization and stepping.
- Make combined/open-water queries compose groundwater + surface water without merging storage.
- Extend budget accounting with surface-to-groundwater recharge.
- Keep the old relaxation flow temporarily so this stage isolates the state-model change.
- Update tests for the new invariant: groundwater exists even when surface depth is zero.

## Stage 2 — Surface-water solver
- Replace relaxation diffusion with a local-inertial shallow-water update.
- Store signed face discharges (east and north faces; west/south shared with neighbours).
- Compute discharge from free-surface slope, gravity and friction; limit outflow by available cell volume.
- Update depth conservatively from face flux divergence.
- Derive FlowX/FlowZ/velocity from face discharge for rendering and ecology.
- Use internal adaptive/substeps constrained by a conservative CFL-like maximum travel fraction.
- Preserve deterministic iteration order and explicit boundary outflow accounting.
- Add tests for downhill acceleration, mass conservation, basin filling/overtopping and dam/channel response.

## Stage 3 — Groundwater sink and sources
- Treat cells whose bed is below the groundwater table as fixed-head groundwater regions, not dynamic surface reservoirs.
- Surface water entering an exposed-groundwater cell is removed from dynamic storage and tallied as groundwater recharge.
- Springs and player pour create only dynamic surface water.
- Drain removes only dynamic surface water; groundwater cannot be drained below the table.
- Ensure terrain edits immediately change which cells expose groundwater.
- Add tests for a stream merging into groundwater, digging below the table, and no double-counted water volume.

## Stage 4 — Soil hydration coupling
- Rebuild moisture coupling around independent contributions:
  1. capillary influence from vertical distance to groundwater,
  2. local surface-water depth/infiltration,
  3. neighbouring surface-water influence,
  4. existing soil diffusion/drying.
- Use saturating responses so deeper water does not linearly over-wet already saturated soil.
- Keep surface-water infiltration as a tracked loss and groundwater recharge where appropriate.
- Add tests showing wet-bank gradients, transient trickle vs sustained water, and groundwater capillary fringe.

## Stage 5 — Rendering and water semantics
- Build the groundwater mesh directly from the fixed table clipped against terrain.
- Build the dynamic surface-water mesh solely from surface-water depth/velocity.
- Remove terrain-height heuristics that classify one combined mesh as table vs stream.
- Preserve smoothed sub-cell render reconstruction and flow-aligned shader UV2.
- Keep `IsStream` only as a derived convenience query from dynamic depth + velocity.
- Add renderer/mesh tests proving meshes do not overlap semantically and remain read-only.

## Stage 6 — Persistence, tools, presets and compatibility
- Persist dynamic surface depth and momentum/face discharge.
- Load old water payloads by subtracting the hydrostatic groundwater portion from legacy combined depth.
- Persist/restore the expanded water budget.
- Add solver tuning parameters with safe defaults; retain legacy config fields only as compatibility aliases where useful.
- Verify terrain/water tools, CLI probes, save/load determinism, and all presets.

## Stage 7 — Integration hardening
- Run the full solution test suite.
- Run focused long-duration hydrology stability tests.
- Check invariants for NaN/negative depth and conservation drift.
- Exercise representative presets: default streamside hollow, creek, shoreline, isolated pond.
- Tune only enough to make the new solver stable and visibly functional; leave aesthetic/behavioral tuning for the next pass.
- Merge/push final state to `main` and leave the system ready for tuning.

## Desired end state
`Groundwater` is a hydrostatic environmental boundary. `SurfaceWater` is conserved dynamic fluid. `Moisture` is an ecological field influenced by both. Rendering reconstructs smooth water from those authoritative fields without feeding back into simulation.

## Baseline verification
- Baseline commit: a1ce6c6.
- Focused HydrologyTests + TerrainWaterToolTests: 20/20 passing before refactor.
- Full suite has pre-existing unrelated form/visual/performance failures on this baseline; those are not stage blockers but will be compared again at integration hardening.

