# Rendering plan and implementation review — 2026-10-23

Reviewed the complete `PHOTOREALISTIC_RENDERING_PLAN.md`, execution board, current source, recent Git history, packet worktree status, and local capture directories.

Snapshot: `main` at `e7d65714280e4ebe8567bec395edb15d3aac8c32`. This is a review snapshot; other worktrees contain ongoing edits. No implementation, board, worktree, editor, or save changes were made by this review.

## Result

Substantial implementation is merged and both assemblies build. The overhaul is incomplete, and merged source is not final native visual acceptance. The execution board predates several merges and also overstates some foundation deliverables. The general `.clanker/current-work.md` primarily describes older October 21–22 efforts rather than this packet campaign.

Fresh checks:

- `dotnet build Vivarium.sln -c Debug --no-restore -v quiet`: passed, zero warnings/errors.
- `dotnet build game/Vivarium.csproj -c Debug --no-restore -v quiet`: passed, zero warnings/errors.
- Current main had only two untracked paths: `game/Shaders/guardian_surface.gdshader.uid` and `temp.txt`.
- Existing graph queried for navigation; substantive findings verified in current source.
- No new native Godot capture, full test suite, world-digest comparison, or performance measurement was run. C# builds do not compile/validate all runtime shaders.
- No current-campaign final capture set was located in the searched `build` outputs. `build/verify/wp05` exists but contained no files. The prescribed `build/photorealism/final-acceptance` and `build/reference/photoreal-pass1` were absent. Older captures do not validate later merges.

## Packet status

“Implemented” below means substantial requested mechanisms exist in main, not that every subrequirement or visual gate passed.

| Packet | Current source state | Evidence / remaining work |
| --- | --- | --- |
| 01 | Implemented; native gate outstanding | SDFGI, SSR, SSIL, 8K shadows, TAA and 4x MSAA at quality >=1. Quality 0 still deliberately disables effects and scales resolution to .75. |
| 02 | Procedural implementation present; asset specification incomplete | Enclosure backdrop, HDR procedural canopy, shadowed fill and habitat reflection probe exist. No prescribed day/dusk/night EXR directory/assets found. |
| 03 | Implemented; native gate outstanding | Above-water fog, volumetric scattering and shadowed night lights configured. |
| 04 | Implemented; native gate outstanding | Focus tracking, near/far DOF, bokeh, bloom and exposure configuration present. |
| 05 | Partial despite board DONE | Five-channel resolution/binding and generator exist, but Ground048/037, Moss002, Rock058, Gravel022 and Bark001/013/014 still lack the specified 4K PNG sets. Their generated displacement/AO files are named 1K. ScatteredLeaves007 and the new litter atlas are the visible 4K exceptions. |
| 06 | Substantial implementation | Subdivision 6, Catmull-Rom render height, tangent upload, 4x field textures, damp/litter sampling and POM exist. Full-resolution assets and native visual proof remain prerequisites for the advertised result. |
| 07 | Merged | Subdivided/displaced strata, rootlets and POM/seepage shader work present. |
| 08 | Partial, merged | Whole-domain patches and recursive shadows present. Startup builds every patch synchronously; frame-distributed/off-thread startup is not implemented. Fruit remains StandardMaterial3D rather than the specified tissue shader. |
| 09 | Substantial but partial, merged | New atlas, stack-height grid, curved bark twigs and transmission shader integrated (`1242fb72`). Twig bindings still use 1K Bark014. Separate wp09-builder worktree has additional uncommitted builder changes and three untracked bark JPGs. |
| 10 | Merged | Undercut clods, groundcover UV/geometry changes and bank-fringe wiring present. |
| 11 | Merged | Lit water shaders, smooth fluid normals, revised optical depth/reflection and capillary optics present. Native shadows/glints/continuity still unverified here. |
| 12 | Merged but incomplete integration | Shared chromatic caustic include and terrain/rock/log/underwater changes exist. Prop water-level/field wiring remains incomplete; see findings. |
| 13 | Unmerged work in progress | Main lacks the planned plunge-impact foam/bubble system. wp13-foam has three dirty files: BallisticFluidRenderer, WaterSurfaceRenderer and surface_fluid shader (84 additions / 7 deletions at inspection). Existing water-stream meniscus code alone does not satisfy this packet. |
| 14 | Merged but incomplete integration | 96x48 boulder grid, 32 pebble variants, 650/m² scatter and shadows present. Asset resolution and moisture/lighting integration issues remain. |
| 15 | Outstanding in main | Logs retain 28 radial segments / .05 m longitudinal spacing and the old hollow-end treatment. wp15-logs is clean at the shared starting commit. |
| 16 | Merged | Dense shoot generation, shadows, draping work and moss optical changes present. |
| 17 | Merged | Foliose/fruticose/crustose geometry and lichen normal/AO changes present. |
| 18 | Outstanding in main | No SurfaceFloat/AlgaeBed/AlgaeFloat coverage integration or explicit slime mode 4 implementation found in the target renderer/shader. wp18-aquatic is clean at its starting commit. |
| 19 | Substantial foundation implementation | Higher grids, closed shells, increased LOD fidelity and 25-pixel full-detail threshold present. This does not establish every botanical feature/species is complete. |
| 20 | Substantial foundation implementation with lighting defect | 2K baker, Bezier/reticulate venation, synthesized vascular profiles, wind and thickness-based lighting present. Transmission applies light color twice. |
| 21 | Partial | Bark fluting/buttresses/callus merged. Pilot foliage still calls low-resolution Blade/CurvedBlade rather than the requested Broadleaf/material split. Existing Ghostbonnet already has some real lamellae, so “fungi untouched” is too broad; the requested all-fungi overhaul remains incomplete. wp21-crown is clean at its starting commit. |
| 22 | Partial, explicitly merged as partial | `97e82f15` enables fauna shadows. Requested welded anatomy and articulated multi-joint gait work have not landed in this packet. |
| 23 | Outstanding in main | Planned thin-film, mucus/SSS and eye overhaul not present. Existing clearcoat predates this packet. wp23-fauna-shaders is clean at current main. |
| 24 | Partial scaffolding | Unified surface shader and wetness uniforms merged. Fur uses generic anisotropy, eyes clearcoat/pupil masking, contact radius stays zero and day/night stays 1. No implemented Marschner light function or corneal refraction. IK was already largely present: packet diff adds only root-to-Pelvis/Hips fallback. GPU-contact property is declaration-only. |

## Concrete source findings

1. **Leaf transmission applies light color twice.** `game/Shaders/leaf_direct_light.gdshaderinc:50` includes `light_color` in T_leaf; line 73 multiplies the resulting transport by it again. The transmitted component consequently has quadratic light-color/energy response. Fix the transport/accounting boundary and validate isolated front/back lighting across intensities.
2. **Rock specular mixes coordinate spaces.** `game/Shaders/rock.gdshader:129–137` transforms NORMAL/LIGHT into world space but leaves VIEW in view space, then constructs the half vector from both. Camera rotations can therefore distort specular behavior. Keep the BRDF vectors in a common space and verify a camera orbit.
3. **Guardian contact/day-night uniforms are placeholders.** `game/Render/GuardianRenderer.cs:596–605` sends zero contact radius and constant daytime. `guardian_surface.gdshader` gates contact displacement on a positive radius, so that path is inactive. `GuardianSkinContact.GpuDeformationEnabled` has no use sites. This is not completed GPU contact migration. Surface wetness is also applied globally, while the declared water_level is unused by the shader.
4. **Caustic/wetness prop inputs are incomplete.** PropRenderer initializes rock water_table once, but never sets the log shader's water_table or registers pebble duplicates for subsequent field updates. IslandRenderer's CoverMaskConsumers updates cover_tex/field_rect, not field_tex or water_table. The rock moisture sampler therefore lacks the intended binding; log caustics use default water_table, and changing water levels do not propagate to these props. Rock/log caustics use pond level only, without dynamic stream surface data.
5. **Whole-island litter initialization can create a startup spike.** LoamSurfaceDetailRenderer.Build loops synchronously through all domain patches. This fulfills coverage but not the plan's staggered/off-thread startup requirement. Measure populated loading before accepting it.

## Review of the plan itself

- The visual categories, explicit code targets, presentation/simulation boundary and per-packet gates provide a useful implementation map.
- It should be treated as a working specification rather than a fully verified diagnosis. Some starting-code descriptions are obsolete: guardian analytical IK already existed, and Ghostbonnet already had lamellae.
- The claimed six-file rule is internally inconsistent. WP-20 names seven distinct code files, WP-22 eight, and WP-19 additionally instructs a FloraLeaf.cs edit outside its six-file list. WP-08 also instructs edits to builders outside its declared two-file list. Split/reconcile ownership before dispatch.
- The dependency diagram puts WP-08 before WP-09/10, whereas the execution board serializes WP-09/10 before WP-08 for ownership. Choose and document one dependency/ownership schedule.
- Asset specifications diverge from packet details: three EXR skies are required globally but WP-02 implements a procedural alternative; the global atlas inventory exceeds the packet's 16-element inventory. Decide which is required for completion.
- Normal-map integration recovers a relative height estimate; its normalization does not establish measured physical relief scale. Describe this as synthesis/reconstruction with an explicit scale calibration.
- Prescribed scene captures are useful but the final gate needs explicit current-source identity, runtime shader/error checks, populated-world digest preservation, motion/contact review and recorded actual quality settings. Do not substitute old screenshots or C# builds for these.

## Recommended continuation

First reconcile the board against this snapshot and preserve the two dirty packet worktrees. Repair the current lighting/uniform wiring defects and complete the missing core 4K asset delivery. Then run a bounded native integrated review of current main before adding the remaining large packets. Finish WP-13/15/18/21/22/23 and the substantive remainder of WP-24 with separate merged/build/native/visually-accepted states. Keep the existing editor and unrelated work intact.
