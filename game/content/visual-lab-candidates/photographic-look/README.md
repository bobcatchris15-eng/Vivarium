# Selected photographic look

`balanced-umber-mild` is the selected visual direction from three native comparison rounds. `selected-look.json` contains the exact numeric settings, shader paths/hashes and evidence pointers. The two shaders are the captured private variants for terrain and coverage. They expect the same field/texture inputs as their original production counterparts.

Terrain uniform mapping: photo_look_moss_scale, photo_look_moss_tint, photo_look_ground_moss_scale, photo_look_damp_tint, photo_look_wet_film, photo_look_wet_roughness. Coverage: photo_look_coverage_saturation and photo_look_coverage_value. The JSON also specifies environment and foliage controls.

The comparison fixture applies these settings. The normal gameplay renderer does not yet load this package; live-habitat integration is the next work item. This package preserves the selected direction for that integration rather than treating the previous look as preferred.

See docs/2026-10-22-photographic-look-selection.md and build/photorealism/substrate-sweep-01/captures for visual evidence. No measured photographic calibration is implied.
