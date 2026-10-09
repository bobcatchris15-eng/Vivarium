# Design Specification: Dynamic Surface Water, Spring Hole Orifice, and Substrate Infiltration

**Date**: 2026-10-09  
**Status**: Approved (Brainstorming Phase)  
**Target Systems**: `Vivarium.Sim` (Hydrology, Terrain, Props), `Vivarium.Game` (Godot Render, ToolController)

---

## 1. Executive Summary

Vivarium retains a strict dual-layer water separation:
1. **Hydrostatic Groundwater Table (`WaterTable`)**: A static planar boundary below which ground is saturated and standing ponds/shorelines exist.
2. **Dynamic Surface Water (`Hydrology`)**: A local-inertial shallow-water fluid simulation.

This design introduces:
- **Physical Spring Depression & Orifice Prop**: Placing a spring carves a physical depression into the terrain heightfield and places a dark orifice/vent prop at the center. Fluid generates at a configurable rate inside the hole, pools, fills the depression, and naturally overflows down slopes.
- **Substrate-Dependent Infiltration**: Fluid sitting or moving over gravel or soil leaches away according to substrate infiltration rates, whereas rock prevents leaching. Soil leaching directly couples to `ScalarField moisture`.
- **Water Table & Perimeter Sinks**: Fluid reaching the water table level ($z_{\text{bed}} \le \text{WaterTable}$ or surface level entering the water table) is absorbed into `Budget.GroundwaterRecharge`. Fluid reaching the hex boundary drains off into `Budget.BoundaryOutflow`.

---

## 2. Spring Placement & Orifice Architecture

### 2.1 Terrain Depression
When a spring is placed via `ToolActions.ToggleSpring(world, position, discharge)`:
- Carves a localized radial depression into `Heightfield` at `position` ($x, z$):
  $$z_{\text{new}}(r) = z_{\text{bed}}(r) - h_{\text{depth}} \cdot \cos^2\left(\frac{\pi r}{2 r_{\text{hole}}}\right) \quad \text{for } r < r_{\text{hole}}$$
  - Radius $r_{\text{hole}} \approx 0.20\text{ m}$ (40 cm wide bowl).
  - Depth $h_{\text{depth}} \approx 0.08\text{–}0.10\text{ m}$.
- Calls `world.Water.RefreshBed(world.Terrain.Heightfield)` to update the simulation grid's bed elevations.
- When the spring is toggled off, the orifice prop is removed.

### 2.2 Spring Orifice Prop
- Spawns a small dark rock aperture/orifice prop (`PropKind.SpringVent` or `Orifice`) centered at $(x, z, z_{\text{new}}(0))$ with dark interior shading.
- Serves as the unexplained natural subterranean conduit from which fluid emerges.

### 2.3 Spring Flow Rate Configuration
- Default discharge rate: $0.001\text{ m}^3/\text{s}$ (approx. $1\text{ L/s}$).
- At $r_{\text{hole}} \approx 0.20\text{ m}$ and $h_{\text{depth}} \approx 0.08\text{ m}$, hole capacity is $\approx 5\text{–}8\text{ L}$, taking $\approx 5\text{–}8\text{ seconds}$ to fill before overflowing the rim.
- Exposed in `ToolController` / `ToolActions` with an intensity/discharge slider ($0.0002$ to $0.005\text{ m}^3/\text{s}$).

---

## 3. Hydrology Fluid Simulation Refinement

### 3.1 Basin Pooling & Overtopping Flow
- Springs inject volume into the depression cell: `Depth[c] += vol * invArea`.
- In [`Hydrology.FaceDischarge`](file:///c:/Misc/Vivarium/src/Vivarium.Sim/Water/Hydrology.cs):
  - Head gradient $\Delta H = (z_{\text{bed}} + d)_b - (z_{\text{bed}} + d)_a$.
  - While water is below the rim sill, $\Delta H \le 0$ toward surrounding cells, holding the fluid inside the hole.
  - When water reaches the lip, $\Delta H > 0$ toward downhill neighbors, spilling fluid over the rim.
  - Fluid accelerates down slopes driven by gravity ($g = 9.81$) and damped by Manning friction ($n = 0.05$).

### 3.2 Substrate-Dependent Infiltration
- In [`Hydrology.Advance`](file:///c:/Misc/Vivarium/src/Vivarium.Sim/Water/Hydrology.cs), per-cell loss queries the local substrate:
  ```csharp
  double substrateRate = substrate switch {
      Substrate.Rock => 0.0,
      Substrate.Gravel => Config.Infiltration * 4.0,  // fast percolation
      Substrate.Soil => Config.Infiltration * 1.0,    // standard soil absorption
      _ => Config.Infiltration
  };
  ```
- Soil infiltration directly feeds the ecological moisture field in `CoupleMoisture`.
- Infiltration is tracked in `Budget.Infiltration`.

### 3.3 Water Table Absorption (Sink)
- Any cell where terrain elevation is below the water table ($z_{\text{bed}} \le \text{WaterTable}$) or where water surface elevation meets the groundwater table drains dynamic surface water:
  - `double absorbed = Depth[idx] * area;`
  - `Budget.GroundwaterRecharge += absorbed;`
  - `Depth[idx] = 0;`
- This ensures fluid flowing into ponds or lakes is absorbed into the groundwater table, preventing redundant double-surface rendering.

### 3.4 Island Boundary Drainage (Sink)
- Cells on the hex perimeter already drain over the island cut via `BoundaryDischarge()` into `Budget.BoundaryOutflow`.

---

## 4. Verification & Testing

1. **Hydrology Unit Tests (`HydrologyTests.cs`)**:
   - Verify spring depression filling before overflowing.
   - Verify substrate infiltration rates (Rock vs Soil vs Gravel).
   - Verify dynamic water drainage into `WaterTable` sink and budget tracking (`GroundwaterRecharge`).
2. **Terrain Water Tool Tests (`TerrainWaterToolTests.cs`)**:
   - Verify spring tool carves depression, places orifice prop, and updates hydrology bed.
   - Verify spring toggle removal cleans up prop.
3. **End-to-End Visual Test (`SpringFluidPreview.cs` / Godot headless)**:
   - Verify spring hole geometry and fluid overflow in visual simulation.
