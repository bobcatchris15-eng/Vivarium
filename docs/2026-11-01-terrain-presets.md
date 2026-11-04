# Terrain presets: ledge, meanders, and Springbraid Hollow

These changes affect newly generated worlds. Existing saves retain their serialized
descriptors and terrain edits. No save migration is needed.

## Terraced Cascades

The old rounded hills and paired ridges are replaced by a directed `ledge` terrain
feature. Its left side is raised by 0.65 m. Sharp ledges transition across one
authoritative heightfield cell (12.5 cm at the default cell size), producing a steep
face between two broad shelves. This is a heightfield ledge, not an exactly vertical
overhanging mesh. Rendering, collision and liquid support consume that same face;
render height remains within 0.5 mm of physical support.

The winding stream crosses the lip at Z=0.4, with its 0.60 m drop concentrated in the
same cell. Pools sit on either side, and a lower organic basin meets the water table.
The shelf elevation survives map resizing and the relief control.

## Courses across the library

Broader alternating bends replace nearly straight courses in alpine_tundra,
bayou_marsh, creek, deep_inlet, default, estuary_delta, isolated_pond, peat_fen,
rocky_rise, shoreline and woodland_vernal_pools. Pool centers move with their
channels; spring coordinates, receiver coordinates and authored downhill beds are
preserved. Bayou's two courses stay separate and use smaller offsets to avoid
obstructions at the smallest footprint.

The steeper arid_canyon, misty_cloud_forest and steep_rocky_slope courses remain
direct. Oxbow's existing long meander remains unchanged. Terraced Cascades has its
own newly authored winding route.

Representative course length / endpoint distance changes:

| Preset | Before | After |
| --- | ---: | ---: |
| Whispering Creek | 1.07 | 1.40 |
| default | 1.12 | 1.56 |
| Peat Fen | 1.11 | 1.54 |
| woodland_vernal_pools | 1.07 | 1.34 |
| Terraced Cascades | 1.08 | 1.51 |

## Springbraid Hollow (`braided_springs`)

Registered in the ordinary content index and therefore available in the New World
preset picker. One northern spring feeds an upper bean-shaped pool, a larger middle
lobed pool, winding bypasses and reconverging channels. The final channel enters a
lobed water-table cove at the southern edge. Surrounding land stays above water.
Upstream pools use mobile spring water; only the terminal cove is a hydrostatic
water-table body. Branch discharge is determined by the fluid solver, not a forced
equal split.

## Verification

- `dotnet build Vivarium.sln --no-restore`: passed, zero warnings/errors.
- `dotnet build game/Vivarium.csproj --no-restore`: passed, three existing warnings
  in LoamGroundReview, SmokeRunner and GuardianSkinContact.
- Focused drainage, terrain revision, all-preset boot and visible/physical terrain
  agreement checks: 30 passed. Drainage covers minimum, authored and maximum map
  sizes across every registered preset.
- Native Godot Forward+ reviews completed for cascading_falls and braided_springs.
  Each advanced the real liquid solver by 120 additional physical seconds after
  normal world initialization. Both emitted `VIVARIUM_WORLDREVIEW_OK`.
- Cascade: 11.772 L mobile water, 3.189 L groundwater recharge; budget residual
  -1.43e-14 cubic metres. Springbraid: 17.147 L mobile water, 22.752 L groundwater
  recharge; budget residual -8.17e-14 cubic metres.
- Source manifest remained stable during the final review. Existing editors were
  preserved, capture processes held the shared GPU lease, and review user data was
  isolated. The renderer emitted its existing reflection-probe resolution warning.

Evidence: `build/terrain-presets-20261101/`, including test/build logs, native images,
per-preset flow receipts, course metrics and source hashes. Native diagnostic entry:
`--world-review <absolute output directory> --preset <id> --terrain-review`.

The native images hide vegetation and props to expose terrain and water geometry.
They verify the ledge and branching layout, and show spring water reaching the
receiving water table. Narrow banks and mobile-water margins still reveal the
underlying grids at close range. This is not a performance benchmark or a final
water-rendering quality acceptance claim.
