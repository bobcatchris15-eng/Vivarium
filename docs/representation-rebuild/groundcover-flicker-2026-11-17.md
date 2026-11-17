# Groundcover lighting flicker — 2026-11-17

The owner reported separate parts of groundcover plants alternating lighter/darker, resembling overlapping meshes, including with the camera still and simulation paused.

## Confirmed cause and fix

`ground_cover.gdshader` did not initialize the fragment-to-light varying `tissue_rim`, which the shared `leaf_direct_light.gdshaderinc` reads in its richer direct-light path. Other foliage shaders initialize this input. Assigning `tissue_rim = 0.0` follows the existing no-fuzz convention and preserves reflection, transmission and specular lighting. No normal lighting feature was disabled by the fix.

A close saved-world native capture on AMD Radeon 860M reproduced rapid changes in stable interior pixels of AmbientGround chunk turf. The world was paused and camera/daylight held fixed; the source autosave was copied read-only and checksummed, and runs used isolated application-data directories with autosaving disabled.

Root independently decoded all 48 frames of the saved luminance traces and verified these display-coordinate ranges (0–255):

| Leaf interior | Original, normal effects | Fixed, normal effects | Original, frozen ground/no sun shadows | Fixed, same controlled setup |
| --- | --- | --- | --- | --- |
| 1048,424 | 58–93 | 58–58 | 148–207 | 148–148 |
| 104,492 | 75–108 | 75–75 | 191–225 | 191–191 |
| 1288,684 | 65–159 | 65–65 | 162–215 | 162–162 |

The final bounded regression also retained constant 148,191,162 over 48 frames. Soil reference pixels stayed constant. Normal-effects verification restored wind, sun shadows, TAA, SSAO and the full direct-leaf lighting branch. A Lambert-only diagnostic had independently eliminated the symptom before the targeted initialization, helping isolate the faulty path.

## Separate geometry correction

SoilDetail filler blade/stalk meshes emitted coincident reverse-winding triangle copies while using a double-sided shader. The duplicate pairs were removed (9 and 11 pairs respectively to zero), retaining two-sided rendering and all forward geometry. This is a separate confirmed defect; its removal did not establish a fix for the owner's flicker. The lighting-input fix above did.

## Evidence and limits

Local evidence is under `build/grass-temporal/`: `close-baseline`, `close-fixed-default`, `close-frozen-no-shadow`, `close-rim-init`, and `final-regression`. Each includes `ground_temporal.json`, compressed stride-4 luminance samples and representative native PNGs. Sample layout is 400×225 bytes/frame for 48 frames from the 1600×900 close view.

Earlier synthetic scenes, low-resolution aggregates and visibility controls that the renderer reset each frame were inconclusive and are not evidence for the final attribution. Later controls checked effective post-draw visibility, material parameters, fixed camera/sun/pause, and stable instance buffers. The fix is supported by this saved-world/hardware reproduction; broader performance or art acceptance is not claimed.
