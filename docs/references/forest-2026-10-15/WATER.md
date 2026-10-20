# Water visibility and reservoir join - October 16

The retained water pass is integrated in the main checkout. Original reference photos remain unchanged. The pure RGB(0,0,0) camera background is preserved; performance work is deferred at the user's request.

## Visible changes

- Fixed water-table surfaces and mobile films share canopy radiance, freshwater absorption and refraction scale. Sky cubemap and water use the same directional canopy function; the sky's camera branch stays black.
- The old blue-green tint is greatly reduced. Advected capillary normals and reflected crown openings make the moving surface visible while allowing the stream bed to show through. Coordinates remain in world space and advect with actual local simulated velocity, avoiding the stationary contour bands caused by a rotating flow-coordinate basis.
- The mobile film continues briefly across the receiver contour, settles exactly onto the fixed water plane over a 12 mm bed-submergence interval, and yields to the reservoir once within 15 micrometres. Domain, solid-support and unsupported-ledge guards remain. The table geometry and configured elevation are unchanged.
- A tighter local contact mask suppresses reflected ghost films on dry uphill banks. Both layers receive the same current canopy energy and sun direction. Table-level uniforms refresh each visible frame, including interactive level edits.
- The explicit screen-transmission/reflection composite avoids adding a second material-lighting response. Stable integer canopy corner keys removed hard angular patches observed with the sine/fract hash in native renders. The exact GPU numerical mechanism was not independently proven.

## Native evidence

Open `build/forest-reference-20261015/water-impact/review.html` for four matched views and corrected before/after motion clips. `audit.json` authenticates both captures, all four accepted camera vectors, the whole paused-world digest and exact liquid snapshot. The retained comparison is `water-impact-baseline-corrected` against `water-impact-main-corrected`.

- Both whole-world digests equal `eb62d49f3aae75b244fbc1e9a3db84b6659ba18d521027fb22874cacebd3d0da`. Water state is identical: 4,025 samples, 0.0056140687977453314 m3 mobile volume at 100 physical seconds, 0.007677906328858251 m3 groundwater recharge, and a balance residual below 8e-15 m3.
- The main fresh-world run and corrected saved-fixture replay both pass forced native build, complete source stability, unchanged static-world state and logging checks. Builds report zero warnings/errors.
- Each corrected clip contains 48 captured frames over two actual physical seconds. Camera and environment both record `underwater=false`; biological time stays paused. The encoding is 24 fps for playback, not a runtime performance measurement. Root inspected representative frames for surface movement and absence of the rejected overlay.
- `water-impact-bayou` adds three native shallow-receiver views. `water-impact-early` checks startup flow at five physical seconds as a supplemental startup view. These are additional scene checks, not matched baseline pairs.
- `main-void.json` records exact-black full-frame checks, including the forced-underwater diagnostic. All eight sampled frames pass. `final-validation.json` records the retained source digest and bounded checks.
- The focused `SurfaceFluidReplacementTests` and `MapDrainageTests` pass 41/41. All simulation source bytes remain unchanged during this visual pass. These tests protect physics, support and drainage contracts; image quality is assessed through the native captures.

The earlier original baseline/main motion clips are rejected for above-water review: the static tour ended with a forced underwater void probe and did not reset that effect before motion. They are preserved with `MOTION-REVIEW-REJECTED.md`. The corrected harness restores the actual camera medium and refreshes presentation before capture. File/codec/state checks alone had not detected that visual state error.

## Remaining work

This is a bounded visual improvement checkpoint. Terrain shore contours still expose angular mesh structure; broader terrain refinement must keep the visible bed and fluid support in agreement. The canopy reflection represents canopy lighting rather than exact local tree/creature reflections. The low-angle `water_run_low` shot is occluded by a foreground bank and is preserved as a diagnostic, excluded from accepted comparisons. The inspected creek and bayou joins are continuous, but arbitrary sculpted receivers, waterfalls and all camera angles are not covered by this pass. A submerged-bed roughness experiment made little visible difference and was not retained. No performance acceptance is claimed.

Existing saves are compatible. Relaunch the game to load the new shaders and renderers; a new world is not required for these material changes. Authored map changes from earlier work are a separate matter and can remain old in existing saves. No live game process was restarted.


2026-10-16 shallow receiver material update: see SHALLOW_WATER.md. Depth-ramped silt softens bank/table visual boundary without geometry or fluid changes. Native ROOT creek/bayou source-stable, states exact,4blackvoid probes. Deep gray pool and wet silver ground remain open.

Wet-ground response retained: terrain silver band isolated and removed; true water optics unchanged. Targeted moss noise also corrects large straight green patch edges beside receiver. WET_GROUND.md/MOSS_NOISE.md; native ROOT source/state/void verified. Deep gray pool remains open.
