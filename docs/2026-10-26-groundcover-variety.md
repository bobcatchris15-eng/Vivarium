# Groundcover variety and habitat colour â€” 2026-10-26

The user approved the density and requested more shape/position variety, matte materials, drastic moisture-dependent value changes, and yellow/blue shifts with light exposure.

## Retained implementation

Turf uses independently offset, fully jittered strata with a small continuous world-space warp. Density targets, plant berths and gravel taper remain. Tufts vary in aspect ratio, height, static lean and curl. Blade lengths, leaf widths, angles, tip profiles and twist differ inside the base meshes. The static shader deformation uses its analytic inverse-transpose normal and no camera/time input. Root collars remain anchored. Tuft mesh costs remain 80 grass / 240 broadleaf triangles.

Bank leaves vary in breadth, tip shape, asymmetry and arch. Bank anchors are scattered across independently offset strata; 16 attempts compensate for the complete-leaf exclusion losses. Checked world-coordinate bank geometry is never deformed by the instance shader. The final fixture has 29 runners and 94 leaves, with complete support/water/winding/normal checks passing. These are fixture counts rather than a universal density guarantee.

Both materials share IslandRenderer's live field texture (R moisture, A canopy-adjusted light). Image updates recolour existing turf and cached banks without rebuilding geometry. Dry growth is much lighter; wet growth is much darker. Exposure interpolates yellow-green sun pigment and blue-green shade pigment. The wet endpoint was lifted from the first candidate to retain readable leaf detail. The moisture value range is independent of exposure hue. Bilinear world-space sampling keeps colour gradients continuous across cells.

Groundcover roughness is .94-.975, engine specular .035, direct cuticle gain .12, material F0 .02, and direct transmission .20. Pigment-dependent transmission masking prevents the shared green chlorophyll lobe from washing out habitat colour. Shared leaf optics and simulated-species materials were not edited by this task.

## Verification and limits

Final native run: build/forest-reference-20261015/groundcover-variety-03-20261026. Eight images, exit 0, all 11 checks pass, source stable, paused world digest unchanged. Actual turf and bank materials share the production texture; matte bindings are checked. Updating one bound control image produces dry/shade, dry/sun, wet/shade and wet/sun views without changing meshes, instance resources, refresh count or bank build count. Production bindings are restored afterward. The final dry/wet display-luma ratios are about 3.34 in sun and 4.03 in shade on the response-selected lower-half mask. Shade is substantially bluer relative to red than sun at both moisture levels. These are bounded display-image measurements, not physical reflectance or semantic segmentation.

Both solution and game builds pass. Extracted exact production tuft builders pass finite attributes, unit normals and clockwise faces. 1,000 analytic deformation Jacobian checks pass. The existing 64 irregular gravel-boundary checks pass. Native bank geometry support checks pass. Performance was not measured; user art acceptance remains open.

Run 01 is superseded and excluded from source-stable acceptance: an unrelated creek preset and Godot-generated UID changed during it. Run 02 passes source/native checks and establishes the same-world turf count was preserved (9,721 versus 9,715 in the density pass), but its bank scatter was too sparse. Between 02 and 03, unrelated preset edits changed the generated world and automatic camera targets. Those edits were preserved; final 03 is internally source-stable and world-unchanged. It must not be used as a matched visual/performance comparison against 02 or the earlier density pass.

Changes are local and uncommitted. Restart the game for the C# placement and material-binding changes; existing worlds need no migration. Existing editor and game processes were not stopped by this task. No simulation or save-schema source was changed by this task.

## Native control views

![Moisture and exposure control matrix](/E:/Vivarium/build/groundcover-variety-20261026/habitat-response.png)

## Production view in the updated preset

![Current production cover](/E:/Vivarium/build/forest-reference-20261015/groundcover-variety-03-20261026/forest_floor_medium.png)

Receipt: build/groundcover-variety-20261026/receipt.json. Native control image values, restoration checks and measurement method are retained alongside it.
