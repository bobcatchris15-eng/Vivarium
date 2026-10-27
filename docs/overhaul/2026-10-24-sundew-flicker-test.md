# Sundew intermittent-fleck isolation — 2026-10-24

The strongest current lead is the rosette material interacting with very small triangles and multisample anti-aliasing (MSAA). The inspected sundew patch is confirmed affected; this test does not establish exclusivity across all species.

Completed native evidence: `build/photorealism/sundew-extended-probe-01/`,52PNG,exit0,source stable,zeroengine errors. Fixed world, camera, daylight and manual exposure. Static repeated captures show changing flecks while biology is paused. The coverage shader contains no TIME-based animation.

| Independent control | Observed result in the inspected patch |
| --- | --- |
| Hide all sundew rosettes, retain blue coverage sheet | Inspected small flecks disappear |
| Hide dew alone or make it neutral | Prior validated test retained black flecks |
| Coverage material unshaded | Colored square flecks persist |
| Coverage shadows disabled | Flecks persist |
| TAA disabled (MSAA retained) | Flecks persist |
| MSAA disabled (TAA retained) | Flecks substantially reduced/less square; rosette structure resolves differently |
| Replace coverage fragment shader with simple lit vertex color | Flecks disappear, but sheet shading and edge cutout appearance also change; broad diagnostic only |

The baseline uses MSAA4X, TAA and FXAA together. This establishes an association with geometry/material rasterization, not an engine defect or the precise offending shader operation. A Luna worker independently inspected the source and native controls and agreed with that narrow conclusion. No concrete duplicate-face defect was found. A possible async live-tile rebuild race was noted in source but is not confirmed causal; paused captures weaken it as an explanation for this flicker.

The coverage shader shares an unguarded sRGB conversion containing fractional pow. A follow-up test for out-of-range interpolated COLOR was started as `sundew-color-input-probe-01`, but a live game launched from the existing editor began sharing the GPU. The owned diagnostic child was stopped after severe upload slowdowns; the live game and editors were preserved. That incomplete run is excluded from conclusions; no clamping result is claimed. This was a resource-coexistence stop, not an approval rejection.

Related primary technical context, not local proof: [Godot issue122349](https://github.com/godotengine/godot/issues/122349) reports MSAA interpolation artifacts on small/grazing triangles in4.7.1; its specific prepass defect has not been demonstrated here. [GLSL specification](https://registry.khronos.org/OpenGL/specs/gl/GLSLangSpec.4.00.pdf) defines negative-base pow results as undefined; negative color input has not been demonstrated here.

No production shader or anti-aliasing policy was changed. All probe shaders and viewport settings were restored. The new controls are opt-in. Review `build/photorealism/sundew-extended-probe-01/review.html` for full images plus enlarged pixel regions.
