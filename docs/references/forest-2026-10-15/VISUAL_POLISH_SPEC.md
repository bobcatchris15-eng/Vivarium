# User visual polish and natural lushness specification

Received October 15, 2026, during the active photo-driven graphics iteration. This is steering for the ongoing mission, not a separate project. The supplied code/formulas are illustrative; inspect and adapt the existing C# mesh, scene and shader contracts.

## Hard visual invariant

The ecosystem is a bounded floating slice in an unexplained, pure RGB (0,0,0) black void. Preserve its stark silhouette. Camera-visible sky, global haze bleeding into the void, and screen-space light added to the void violate this direction. Bright canopy irradiance/reflections may exist independently of the camera background.

## Requested work

1. **Canopy lighting and atmosphere:** separate black camera sky from bright humid canopy cubemap through `AT_CUBEMAP_PASS`; zero global volumetric density; use a soft-edged FogVolume confined inside the enclosure; soften directional shadow filtering and tune bias; ground small objects with SSAO and appropriate high-tier indirect lighting.
2. **Layered forest floor:** height-modulated material boundaries and triplanar steep slopes; three scales of terrain-aligned creeping leaf mats, curved fibrous runners/dead stalks, and arching pleated/serrated understory. Encode root-to-crown AO in an available attribute without overwriting existing pigment/material channels.
3. **Moss cushions:** boundary-aware rounded inflation along the supporting surface normal; smooth tapered edges; olive recessed growth with chartreuse tuft tips, roughness, grazing response and backlight. Preserve actual occupancy and species thickness. Ordinary moss must not become emissive merely to hide missing illumination.
4. **Stems and foliage:** irregular tapered, root-flared phototropic spines; gravity droop and longitudinal folds; controlled normal smoothing; vein anatomy primarily in relief/roughness; warm bounded transmission and normal-variance roughness to reduce specular sparkle.
5. **Rock, bark, fungi and seep water:** stratified shelves and planar talus instead of round blobs; varied bark/fungus spacing and size; calm small ripples, meaningful depth/absorption and narrow damp shoreline bands across substrates. Terrain geometry changes must keep simulation/query/render surfaces in agreement.
6. **Creature materials:** local root-to-tip anatomy coordinates for scales/feathers; layered keratin response and anisotropy; head/snout breakup; shorter undercoat under quills, restrained grazing fur response, and readable dark faces in indirect light.

## Execution policy

Preserve fictional species identity and the black-void product composition. Do not apply the sample snippets as whole-file replacements. Existing dirty work, live worlds, simulation state, data-channel contracts and historical evidence remain protected. Separate candidate implementation/build/render evidence from final motion/performance/visual acceptance. Continue revising from actual rendered defects.

## Current mapping to implementation

- Groundcover geometry/material candidates and litter/soil normal corrections already underway.
- Priority moved to canopy radiance plus bounded atmosphere following this specification.
- Authoritative moss cushion implementation delegated as a bounded candidate, with no extra tessellation or ordinary-moss emission.
- Leaf normal-variance roughness, multi-tier runners/understory, rock/shoreline work and creature-specific anatomy remain explicit follow-on work.
