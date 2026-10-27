# Earlier surface-water appearance restored — 2026-10-24

The user prefers the surface-water appearance from before the rendering overhaul and specifies ambient motion only for fauna and surface water.

## Restored appearance

Commit `438b340d624595a89e6e004864b0a2f2bd26154e` introduced WP-11 lit Forward+ water. The earlier implementation is present in `150c1217`. Three production shader files were restored exactly from that revision:

- `game/Shaders/surface_fluid.gdshader`
- `game/Shaders/water_stream.gdshader`
- `game/Shaders/mobile_water_optics.gdshaderinc`

The historical and restored Git blob hashes match for all three files; evidence is `build/water-look-restore-20261024/shader-provenance.json`. This restores the earlier unshaded teal/soft-reflection optical treatment for mobile flow, standing water and the shared airborne-water optics. Current fluid simulation, save schema, terrain work and renderer upload/culling fixes are retained. The newer plunge-bubble layer is hidden, and airborne sheets/droplets again do not cast shadows. Current source copies from before the change are preserved under `build/water-look-restore-20261024/before/`.

This is a restoration of the water treatment under current lighting and terrain, not a whole-project rollback or a claim of historical whole-frame identity. Earlier erosion retirement and performance changes remain in place.

## Ambient motion

Production flora wind/sway and pilot foliage animation were already disabled. Remaining autonomous flora/slime streaming phases now default to a fixed clock, while explicit review clocks remain available for diagnostics. Aquatic coverage no longer bends or changes normals as shader TIME advances. Fauna/guardian animation and water motion remain. Growth and direct interactions are separate from ambient wind/breathing motion.

## Verification

The client builds with zero errors and six existing warnings. Historical shader blob identity is verified. The focused native Vulkan probe rendered all six affected water/vegetation shaders without shader errors (`SHADER_PROBE_FINISHED 6`). The initial broad 27-shader probe hit its 30-second timeout without reporting a shader error and is excluded; it was replaced by the focused check.

The production `SurfaceFluidReview`, scenario D, passed (`SURFACE_FLUID_REVIEW_OK`). Nine PNGs cover the initial state, 2/8/25/45 physical seconds, ground/macro/pond views, and reload. At 45 seconds and reload, both records retain 1002 fluid cells and 113 airborne parcels. Water conservation/reload checks completed without exceptions. Oblique and macro images were visually inspected. This proves integration of the restored shaders with current fluid geometry; it does not establish a whole-habitat visual preference or a historical exact-frame match.

Evidence: `build/water-look-restore-20261024/captures/`, `water-stdout.log`, `water-stderr.log`, and `probe-focused-*.log`. The existing seven-texture-RID shutdown warning remains. No FPS gain or user visual acceptance is claimed. All edits remain local and uncommitted.

![Restored earlier water treatment in the current native ledge-flow fixture](../../build/water-look-restore-20261024/captures/45-oblique.png)
