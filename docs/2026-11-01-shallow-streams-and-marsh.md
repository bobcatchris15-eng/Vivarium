# Shallow stream presets and Lowland Marsh

This supersedes the Springbraid depth/layout details in the earlier terrain-preset
review. It applies to newly generated worlds. Existing saves retain the terrain
descriptor stored in the save.

## Springbraid Hollow

The surrounding land now supplies the general downhill slope. Channels cut 8.5 cm
below that local surface; pools cut 13 cm below it, with a small bowl variation.
The terminal pool still intersects the water table. The original branch, bypass,
and reconvergence layout is retained.

The new `TerrainFeature.CutDepth` supports channel/basin excavation relative to the
uncut ground. All overlapping cuts compare against the same original ground and
take the lower result, so intersections do not double-excavate. Absolute graded
`BedHeight` features retain their existing behavior. Cut depths cannot be combined
with absolute bed heights, survive footprint resizing and remain fixed when the
relief control changes the land's slope.

`TerrainProfile.MicroRelief` makes the existing fine relief configurable. Older
descriptors default to the original 4 cm amplitude. The shallow presets use 1.5–2 mm
micro-relief so small ground humps do not overpower their gentle downhill grade.
Prop and automatic tree clearance both recognize relative-depth drainage routes.

Native Springbraid samples measured channel depths of 8.397–8.498 cm and pool-center
depths of 12.954–12.998 cm. Its 120-second additional physical run conserved water
to 8.34e-14 cubic metres and delivered 0.825 L to the receiving water table.

## Winding Pools (`winding_pools`)

One spring feeds a single long course alternating across the island through five
bean/lobed pools before exiting the far edge. The land slopes generally along Z;
the winding course does not depend on increasingly deep excavation. It uses the
same 8.5 cm channels and nominal 13 cm pools. The water table sits below this course;
these pools fill from spring water and spill onward over their shallow outlet sills.
The complete course is more than three times its endpoint distance.

Native samples measured 8.433–8.498 cm channel depths and 11.826–12.991 cm pool-center
depths in the world including tree soil adjustments. At 120 additional physical
seconds the upper pool was still filling; this bounded run does not establish that
all five pools have filled or that water has reached the boundary. The terrain
course is continuous to the edge and its surrounding grade is checked separately.

## Lowland Marsh (`permanent_marsh`)

A spring-free hydrostatic wetland. Base ground is 1.2 cm above the water table, with
3.2 cm broad relief and 4 mm micro-relief. Broad, smooth shallow swales intersect the
water table and connect permanent channels between low hummocks. Three perimeter
pools are approximately 24, 30 and 35 cm below the water table. Flora/fauna starters
come from the existing wetland roster.

The native generated world measured 35.70% submerged terrain and 87.19% of terrain
vertices within 8 cm of the water table, including the deeper pools. Water-table
water exists immediately and does not depend on spring flow or mobile-water storage.

## Verification and evidence

- Solution build passed with zero warnings/errors. Game build passed with the same
  three existing warnings in LoamGroundReview, SmokeRunner and GuardianSkinContact.
- 48 targeted checks passed: generated depths over three seeds at 5/8/20 m,
  surrounding grade, course continuity, marsh wet connectivity and edge pools,
  all-preset boot, drainage, descriptor round-trip, content loading, fluid
  persistence and physical/render terrain agreement.
- Both new presets are registered in the normal content index. The New World picker
  enumerates that index dynamically.
- Native evidence is under `build/shallow-presets-20261101/`. `native-braided_springs`
  holds the shallow-bank close-up and 120-second flow review. Final captures for the
  marsh and winding course use `final-permanent_marsh` and `final-winding_pools`.
  Both final runs emitted `VIVARIUM_WORLDREVIEW_OK`, had no engine errors, and kept
  their source manifest stable. The winding flow budget residual was -5.31e-15 m³;
  the spring-free marsh's mobile-water budget remained exactly zero.
- The initial capture report exposed nonfinite mobile-water samples in hydrostatic
  pools; the reporter now handles absent mobile water and the standing water table
  explicitly. The initial winding review also exposed a terrain-review-only support
  validation error while invisible groundcover was rebuilding during manual water
  stepping. The terrain-only harness now pauses that hidden renderer, and the final
  runner rejects engine errors in addition to failed checks.

These are native production terrain/water captures with vegetation and props hidden
to expose shallow geometry. They are not a performance benchmark or final water
rendering quality acceptance. Existing open editors and user saves/settings were
preserved, and native reviews used the shared GPU lease and isolated review data.
