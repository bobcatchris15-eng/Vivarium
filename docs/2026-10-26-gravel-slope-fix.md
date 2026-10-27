# Gravel slope mapping correction — 2026-10-26

The production terrain shader previously combined a top-down gravel parallax
raymarch with side-projected gravel colour. The height texture was sampled at
the base terrain frequency but the visible stones at 2.4 times that frequency.
Projection normal maps were mixed in their separate tangent frames and then
treated as a horizontal-ground perturbation. The camera vector feeding the
old raymarch is also based on local vertex position rather than camera position.

The native diagnostic reproduced long curving streaks with the original shader,
including on flat ground. Those streaks disappear in the corrected captures.

## Production change

Only `game/Shaders/terrain.gdshader` was edited by this task. Existing dirty
shader work was preserved in place and snapshotted before editing.

- Remove gravel parallax and its shadow contribution. Retain the existing soil
  relief when gravel coverage is absent; fade that relief through mixed patches.
- Sample gravel colour, roughness, and normals at the same physical frequency
  in world-space X, Y, and Z projections, without camera-dependent displacement
  or the horizontal noise warp.
- Subtract 0.2 from absolute normal components before the fourth-power projection
  weights. Nearly edge-on projections no longer contribute elongated texels.
- Use the existing cotangent-frame helper to transform each sampled normal onto
  the actual surface before blending. Convert the result to a tangent surface
  perturbation for the terrain's existing final normal composition.

No shared surface helper, terrain geometry, habitat masks, gravel asset, guardian
behaviour, save, settings file, or project configuration was edited. The general
soil parallax camera calculation remains outside this gravel-only correction.

## Verification

Both `dotnet build Vivarium.sln --no-restore -v quiet` and
`dotnet build game/Vivarium.csproj --no-restore -v quiet` succeeded with zero
warnings and errors. `git diff --check -- game/Shaders/terrain.gdshader` passes.

Native fixture: `build/gravel-slope-20261026/review.gd`, launched by
`run-review.ps1` with an exclusive `.clanker/visual-lab-gpu.lease`. Godot
4.7.1 mono / Forward+ / Vulkan / GTX 1080. No existing editor was closed.
The fixture uses the production terrain shader, shared includes, production
4K surface textures selected in the same priority as `Bridge.ResolveSurfaceSet`,
and controlled field textures on subdivided planes. It bypasses the game entry
point and never loads or writes the player's world/settings.

Eleven cases each capture original/corrected/original-restored: flat; 30, 45, 60,
and 75 degrees; a 75-degree slope on the other axis; a negative 75-degree slope;
a diagonal 60-degree slope; a grazing view at 75 degrees; bare soil; mixed gravel
and soil at 60 degrees. Camera and lighting rotate with each plane to maintain
the same relative viewing/lighting conditions. Three-dimensional resolution is
1000 by 800 at scale 1, 4x MSAA, production TAA enabled. Thirty-three native PNGs,
exit 0, zero engine/shader errors. Production shader hash remained stable during
the native run. Captures and the contact sheet were visually inspected.

Each stage warms 35 frames then samples 120 asynchronous viewport GPU timings.
These are isolated material diagnostics, not game frame-rate acceptance:

| Case | Mean of original and restored GPU time | Corrected GPU time | Reference timing drift |
|---|---:|---:|---:|
| Flat gravel | 4.447 ms | 2.088 ms | 2.86% |
| 45-degree gravel | 4.770 ms | 2.486 ms | 0.05% |
| 75-degree gravel | 4.771 ms | 3.479 ms | 0.18% |
| Bare soil | 4.043 ms | 4.025 ms | 0.15% |
| 60-degree mixed patch | 5.052 ms | 4.638 ms | 0.22% |

The native capture uses production temporal antialiasing, so original/restored
pixels are not byte identical. All eleven reference repeats differ by less than
2/255 in average channel value across the full image. Bare soil original/corrected
differs by 0.817/255 versus 0.813/255 original/restored, consistent with that
reference drift. The first analysis assertion requiring byte identity correctly
failed; `analysis.json` reports reference drift explicitly rather than claiming
exact pixels. No deterministic no-TAA rerun was needed for this limited control.

These results establish native production-shader correction on controlled
slopes. They do not establish whole-world performance or final acceptance in
the user's current world. Source is ready for the next normal game launch;
the running editors were preserved.

## Evidence

- `build/gravel-slope-20261026/comparison.png`: paired central crops, no colour edits.
- `run.json`, `fixture.json`, `analysis.json`: native result, camera/normal poses,
  device, GPU samples, image drift, and source stability.
- `terrain-before.gdshader`, `terrain-after.gdshader`, `surface-before.gdshaderinc`:
  source snapshots. The shared helper remains unchanged.
- `solution-build.log`, `game-build.log`, `native.out.txt`, `native.err.txt`.

Original terrain SHA256:
`05F688D4355B1D870F22D01083520E3249CD59F382D5660D99984345B6A9257D`.
Corrected terrain SHA256:
`041BE19284E76A7E7D868207A522DE8630B55D1D41171AAE2926738FF4CDE485`.
Unchanged shared helper SHA256:
`94833F40655715070D903F3A253CAB979D13675204DE13F0CD99BD73932D4962`.
