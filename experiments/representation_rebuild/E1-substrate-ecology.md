# E1/E2 — Substrate-Coupled Ecology & Water Conservation

## Summary

Connected `LayeredSubstrate` to `VivariumWorld` via `WorldSubstrate` (`src/Vivarium.Sim/Repr/Ecology/WorldSubstrate.cs`) so that substrate water volume, topsoil thickness, drainage, root uptake, and mist weather drive ecological moisture and flora suitability/growth under a closed water-conservation invariant.

## Architecture & Changes

1. **Island-Scale Substrate Geometry (`SubstrateSpec.cs`, `LayeredSubstrate.cs`, `LayeredSubstrate.Edit.cs`)**:
   - Added `SubstrateSpec.MaxLatticeSpacing` (default `0.01 m` for 1 m cube demos, `0.05 m` for island-scale `WorldSubstrate`) and optional `Func<Vec2, double>? BedrockDepth` callback.
   - When `BedrockDepth == null`, `LayeredSubstrate` uses the existing analytic dipping plane and knoll so all 18 `substrate.*` self-tests remain bit-identical.
   - When `BedrockDepth` is provided by `WorldSubstrate`, local bedrock depth derives from `RootZone.RetainedTopsoilDepth` and `TerrainField.DrainagePotential` so soil thickness varies naturally across hollows, flats, and ridges across the 12-16 m island without rising above the terrain surface.
   - Added `LayeredSubstrate.SyncSurface(double simSeconds)` to synchronize edited terrain columns in place while conserving column water volume and leaving unmodified columns untouched.
   - Inlined bilinear table blending in `SampleSubstrate` and added vertical reach bounds in `InStone`, keeping `substrate.performance` at `378 ns/call (0 B alloc)` on AMD Ryzen 5 1600 (Debug build).

2. **`WorldSubstrate` (`src/Vivarium.Sim/Repr/Ecology/WorldSubstrate.cs`)**:
   - Bridges `VivariumWorld` (`Nx x Nz` cell grid, index `j * Nx + i`) with `LayeredSubstrate` (`Nx x Nz` column grid, index `i * Nz + j`) via explicit `ColumnForCell` / `CellForColumn` and `CellCenter` helpers.
   - Computes $dt$-invariant mist precipitation via analytical integration of the multi-harmonic weather cycle (`MeanMistRate(t0, dt)`), plus pond/stream seepage recharge on submerged cells.
   - Syncs fine-root demand (`SyncRoots`) from live vascular flora and the pilot tree into the substrate root grid.
   - Exposes `RootWaterFactor(Vec2 pos, double rootDepth)` for `FloraSystem` growth, `OxygenAt(int cellIdx)` and `TopsoilDepthAt(int cellIdx)` for `FloraSystem.Suitability`, and couples column saturation back into `Fields.Moisture` and `RootZone.DeepWaterReserve`.
   - Tracks `LastStepBudget`, `TotalWaterLedger`, and `LastInfiltrationDelta`, checked by `VivariumWorld.CheckInvariants()` (`|StorageDelta - (Inflow - Outflow)| <= 1e-6 m^3`).
   - Supports full save/load persistence in `WorldSerializer` (`WaterPayload.SubstrateWater`).

3. **Self-Tests (`src/Vivarium.Sim/Repr/Ecology/EcologySelfTests.cs`) & Runner Guard (`experiments/representation_rebuild/selftest/Program.cs`)**:
   - Added empty-filter guard (`REPR_SELFTEST_NO_MATCH`, exit code `2`) to `Program.cs`.
   - Added 8 self-tests covering grid mapping, surface sync continuity, infiltration, mist weather, root uptake, water conservation ledger, $dt$-invariance, and a 90-sim-day coupled vs. uncoupled island soak.

## Verification

Hardware: **AMD Ryzen 5 1600 Six-Core Processor**, **NVIDIA GeForce GTX 1080**.

- `dotnet build/selftest-root/bin/ReprSelfTest/debug/ReprSelfTest.dll eco.`:
  - `PASS eco.grid`
  - `PASS eco.continuity`
  - `PASS eco.infiltration`
  - `PASS eco.weather`
  - `PASS eco.uptake`
  - `PASS eco.ledger`
  - `PASS eco.dtInvariance`
  - `PASS eco.ninetyDay`:
    `[90d coupled: flora=1992 sp=63 H=3.52 wet/mid/dry=0.42/0.39/0.34 envMs=32.81 totalMs=17057 | uncoupled: flora=1996 sp=63 H=3.54]`
- Full `ReprSelfTest`: **63 / 63 PASS** (`colony.*` 12, `eco.*` 8, `light.*` 4, `mesh.*` 10, `plant.*` 11, `substrate.*` 18).
- Headless boot test (`--headless --path game -- --boot-test`):
  `VIVARIUM_BOOT version=0.1.4 schema=5 ticks=33 flora=152 fauna=77 save=True load=True roundtrip=True invariants=0` -> `VIVARIUM_BOOT_OK`.
