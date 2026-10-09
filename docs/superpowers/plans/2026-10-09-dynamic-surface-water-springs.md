# Dynamic Surface Water, Spring Hole Orifice, and Substrate Infiltration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement true physical fluid behavior for dynamic surface water: carving a physical depression with a dark orifice prop upon spring placement, fluid generation inside the hole with natural overtopping, substrate-dependent leaching (gravel/soil/rock), and absorption at the water table and island perimeter sinks.

**Architecture:** 
- The existing 2D local-inertial shallow-water solver (`Hydrology.cs`) remains the authoritative simulation engine.
- `ToolActions.ToggleSpring` carves a localized radial depression into `Heightfield`, places a dark `PropKind.SpringVent` orifice prop at the center, and updates `Water.Bed`.
- `Hydrology.Advance` applies substrate-dependent infiltration multipliers (`Rock` = 0, `Gravel` = 4x, `Soil` = 1x), absorbs dynamic depth reaching `WaterTable` into `Budget.GroundwaterRecharge`, and retains island perimeter weir drainage.

**Tech Stack:** C# .NET 8 (`Vivarium.Sim`, `Vivarium.Sim.Tests`), Godot 4.7.1 C# / GDShader (`game/`)

**Spec:** `docs/superpowers/specs/2026-10-09-dynamic-surface-water-springs-design.md`

## Global Constraints
- Strictly maintain separation between static groundwater (`WaterTable`) and dynamic surface water (`Hydrology.Depth`).
- Zero heap allocations inside per-substep loops in `Hydrology.Advance`.
- Mass conservation strictly enforced in `WaterBudget` (`Budget.Volume = Inflow - Outflow - Evaporation - Infiltration`).
- Headless unit tests (`dotnet test`) must exit with code 0.

---

### Task 1: Substrate-Dependent Infiltration in Hydrology

**Files:**
- Modify: `src/Vivarium.Sim/Water/Hydrology.cs`
- Test: `tests/Vivarium.Sim.Tests/HydrologyTests.cs`

**Interfaces:**
- Consumes: `VivariumWorld.SubstrateAt(idx)` / substrate classification from `Terrain`.
- Produces: `Hydrology.Advance(..., Func<int, Substrate> substrateQuery)` or array mapping of substrate types.

- [ ] **Step 1: Write failing tests for substrate-dependent infiltration**
Add tests in `HydrologyTests.cs`:
- Test that water over `Rock` has 0 infiltration.
- Test that water over `Gravel` infiltrates significantly faster than `Soil`.
- Test that water over `Soil` infiltrates and charges `moisture`.

- [ ] **Step 2: Run test to verify failure**
Run: `dotnet test --filter "FullyQualifiedName~HydrologyTests.SubstrateInfiltration"`
Expected: FAIL (method or substrate parameter not implemented).

- [ ] **Step 3: Implement substrate-dependent infiltration**
In `Hydrology.Advance`:
Scale per-cell `infil` by substrate:
`Substrate.Rock => 0.0`, `Substrate.Gravel => 4.0`, `Substrate.Soil => 1.0`.
Update `Budget.Infiltration` accordingly.

- [ ] **Step 4: Run test to verify pass**
Run: `dotnet test --filter "Suite=Hydrology"`
Expected: PASS (all tests pass).

- [ ] **Step 5: Commit**
`git commit -m "feat(hydrology): implement substrate-dependent infiltration rates"`

---

### Task 2: Groundwater Table Absorption Sink in Hydrology

**Files:**
- Modify: `src/Vivarium.Sim/Water/Hydrology.cs`
- Test: `tests/Vivarium.Sim.Tests/HydrologyTests.cs`

**Interfaces:**
- Consumes: `Hydrology.WaterTable`, `Hydrology.Bed`, `Hydrology.Depth`.
- Produces: Updates to `Budget.GroundwaterRecharge` and zeroing out `Depth` for cells at or below the water table.

- [ ] **Step 1: Write failing test for water table absorption**
Add test in `HydrologyTests.cs`:
- Surface water flowing into a cell with `Bed[idx] <= WaterTable` or where surface elevation touches `WaterTable` is transferred to `Budget.GroundwaterRecharge` and cleared from `Depth[idx]`.

- [ ] **Step 2: Run test to verify failure**
Run: `dotnet test --filter "FullyQualifiedName~HydrologyTests.WaterTableSink"`
Expected: FAIL.

- [ ] **Step 3: Implement water table absorption in Hydrology**
In `Hydrology.Advance`, identify wet cells where `Bed[idx] < WaterTable` or `surface <= WaterTable`:
Drain dynamic depth into `Budget.GroundwaterRecharge += Depth[idx] * area; Depth[idx] = 0;`.

- [ ] **Step 4: Run test to verify pass**
Run: `dotnet test --filter "Suite=Hydrology"`
Expected: PASS.

- [ ] **Step 5: Commit**
`git commit -m "feat(hydrology): drain surface water into groundwater recharge at water table"`

---

### Task 3: Spring Depression Carving in Terrain & Water Tools

**Files:**
- Modify: `src/Vivarium.Sim/World/TerrainEditing.cs`
- Modify: `src/Vivarium.Sim/Tools/ToolActions.cs`
- Test: `tests/Vivarium.Sim.Tests/TerrainWaterToolTests.cs`

**Interfaces:**
- Consumes: `Heightfield`, `TerrainEditing.CarveSpringHole(world, pos, radius, depth)`
- Produces: Modified `Heightfield`, updated `world.Water.RefreshBed()`, spring registered at depression center.

- [ ] **Step 1: Write failing test for spring hole carving**
In `TerrainWaterToolTests.cs`:
Verify that placing a spring via `ToolActions.ToggleSpring`:
- Lowers terrain height within $r \le 0.20\text{ m}$ by $\approx 0.08\text{–}0.10\text{ m}$.
- Updates `w.Water.Bed` so the bottom cell is lower than neighbors.

- [ ] **Step 2: Run test to verify failure**
Run: `dotnet test --filter "FullyQualifiedName~TerrainWaterToolTests"`
Expected: FAIL.

- [ ] **Step 3: Implement spring depression carving**
In `TerrainEditing.cs`, add `CarveSpringHole(Heightfield hf, Vec2 pos, double radius, double depth)`.
In `ToolActions.ToggleSpring`, call `CarveSpringHole` when adding a spring and call `world.Water.RefreshBed(world.Terrain.Heightfield)`.

- [ ] **Step 4: Run test to verify pass**
Run: `dotnet test --filter "FullyQualifiedName~TerrainWaterToolTests"`
Expected: PASS.

- [ ] **Step 5: Commit**
`git commit -m "feat(terrain): carve physical depression when placing spring"`

---

### Task 4: Spring Orifice Prop Mesh & Visual Conduit

**Files:**
- Modify: `src/Vivarium.Sim/World/Props.cs`
- Modify: `src/Vivarium.Sim/Geometry/PropMeshes.cs`
- Modify: `src/Vivarium.Sim/Tools/ToolActions.cs`
- Test: `tests/Vivarium.Sim.Tests/TerrainWaterToolTests.cs`

**Interfaces:**
- Consumes: `PropKind.SpringVent`
- Produces: Dark rock orifice prop placed at spring origin, removed when spring toggled off.

- [ ] **Step 1: Write failing test for spring orifice prop**
In `TerrainWaterToolTests.cs`:
- Verify `world.Props` contains a `SpringVent` prop at the spring coordinate.
- Verify removing the spring deletes the prop.

- [ ] **Step 2: Run test to verify failure**
Run: `dotnet test --filter "FullyQualifiedName~TerrainWaterToolTests.SpringVentProp"`
Expected: FAIL.

- [ ] **Step 3: Implement spring vent prop and mesh generator**
In `Props.cs`, add `PropKind.SpringVent`.
In `PropMeshes.cs`, define geometry for `SpringVent` (dark rocky aperture / lip with deep black cavity interior).
In `ToolActions.ToggleSpring`, spawn prop on spring creation, remove on spring deletion.

- [ ] **Step 4: Run test to verify pass**
Run: `dotnet test --filter "FullyQualifiedName~TerrainWaterToolTests"`
Expected: PASS.

- [ ] **Step 5: Commit**
`git commit -m "feat(props): add spring vent orifice prop and geometry"`

---

### Task 5: End-to-End Overtopping Flow & Tool Configuration

**Files:**
- Modify: `src/Vivarium.Sim/Tools/ToolActions.cs`
- Modify: `game/App/ToolController.cs`
- Test: `tests/Vivarium.Sim.Tests/SpringFluidTests.cs`

**Interfaces:**
- Consumes: Configurable spring discharge ($0.0002$ to $0.005\text{ m}^3/\text{s}$).
- Produces: Physical pooling inside hole, overtopping rim, flow down slope, mass-conservation.

- [ ] **Step 1: Write failing end-to-end test**
In `SpringFluidTests.cs`:
- Place spring in depression. Advance hydrology in sub-steps.
- Verify depth fills hole until rim sill elevation.
- Verify water then spills over rim and flows to downhill cells.
- Verify mass balance: `SpringInflow == Stored + Leached + GroundwaterRecharge + BoundaryOutflow`.

- [ ] **Step 2: Run test to verify failure**
Run: `dotnet test --filter "FullyQualifiedName~SpringFluidTests.OvertoppingFlow"`
Expected: FAIL.

- [ ] **Step 3: Implement integration & tool parameter handling**
Ensure `ToolController.cs` exposes discharge adjustments and wires into `ToggleSpring`.
Verify `Hydrology.Advance` seamlessly transfers spilling volume without negative depth spikes.

- [ ] **Step 4: Run full test suite**
Run: `dotnet test --filter "Suite=Hydrology"`
Expected: PASS (all tests pass).

- [ ] **Step 5: Commit**
`git commit -m "feat(water): complete spring overtopping flow and tool configuration"`
