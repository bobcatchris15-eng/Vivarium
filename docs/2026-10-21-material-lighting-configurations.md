# Photographic material and lighting configurations

Research session begun 2026-10-21; companion to the Vivarium photorealism catalogue. Research and source inspection only: this document does not apply the supplied shader, change renderer settings, or establish new visual/performance acceptance.

## Finding

There are established configurations worth reproducing: calibrated image-based illumination, physically parameterized surface models, energy-aware layers, measured leaf reflection/transmission, and controlled camera/display transforms. Their reliability comes from compatible definitions and validation across several illuminations. A collection of plausible slider values is not itself a calibrated configuration.

The strongest practical direction for Vivarium is to calibrate material families against a consistent light/camera reference, preserve the black camera backdrop independently of environmental illumination, and fit the remaining artistic tissue controls to observed responses. Physical units improve reproducibility; visibility, indirect transport, geometry and material energy accounting still have to work.

## Documented systems and what transfers

| System / evidence | Documented contribution | Transfer limit |
|---|---|---|
| [Filament material model](https://google.github.io/filament/main/filament.html) | GGX-based dielectric/conductor workflow, roughness remapping, energy handling and lighting validation. Its reflectance control maps as F0 = 0.16 × control². | A reflectance slider of 0.5 means F0 0.04 here; it does not mean 50% reflection. Its parameter values need conversion before use elsewhere. |
| [Disney BRDF, Burley 2012](https://disneyanimation.com/publications/physically-based-shading-at-disney/) | A production material model informed by measured reflectance, with restrained artist controls. | Production robustness and photographic matching are different claims. A Disney-family shader does not prescribe one forest-light rig. |
| [OpenPBR Surface 1.1.1](https://academysoftwarefoundation.github.io/OpenPBR/index.html) | Explicit base, coat and fuzz structure with energy-aware relationships; documented dielectric defaults include IOR 1.5, specular roughness 0.3 and anisotropy 0. | Defaults are starting definitions, not measured bark, foliage or moss presets. Its layer accounting is richer than adding independent lobes. |
| [Debevec 1998 image-based lighting](https://www.pauldebevec.com/Research/IBL/debevec-siggraph98.pdf) | Measured HDR scene illumination and local/global transport used to integrate synthetic objects into photographed scenes. | An HDR panorama without radiometric calibration provides relative lighting; local occlusion and bounce still need representation. |
| [Measured leaf optics, Bousquet et al. 2005](https://www.ipgp.fr/~jacquemoud/publications/bousquet2005a.pdf) | Species-dependent surface and internal responses measured over angles and wavelengths. | Fitted effective IOR and roughness are model/sample-specific; their roughness parameter is not Godot's GGX input. |
| [Real-Time Rendering of Plant Leaves, Wang et al. 2005](https://graphics.cs.yale.edu/publications/real-time-rendering-plant-leaves) | Spatially varying BRDF/BTDF plus lighting/visibility treatment for environment and sunlight. | Leaf realism requires both material response and incident-light transport. Translucency gain alone cannot reproduce the method. |
| [Wet materials, Jensen–Legakis–Dorsey 1999](https://graphics.stanford.edu/~henrik/papers/egwr99/egwr99.pdf) | Surface water and absorbed water modeled separately. Wet appearance can be darker, brighter or more specular depending on conditions. | A universal albedo multiplier and roughness target cannot represent all wet materials. |

## Numeric anchors and parameter translation

For a nonabsorbing dielectric interface with air approximated as IOR 1, normal-incidence reflectance is F0 = ((n−1)/(n+1))². Water IOR about 1.333 gives F0 about 0.0204; IOR 1.5 gives 0.04. Vivarium's retained 0.055 corresponds to IOR about 1.613 by this relation. These calculations define interface behavior, not a measured effective IOR for every leaf. See [PBRT's Fresnel and IOR treatment](https://www.pbr-book.org/4ed/Reflection_Models/Specular_Reflection_and_Transmission).

Three similarly named controls need different numbers for the same 4% reference:

| Parameterization | Input for F0 = 0.04 |
|---|---:|
| Vivarium's direct F0 uniform | 0.04 |
| Filament's mapped reflectance control | 0.5 |
| OpenPBR specular IOR, air interface, weight 1 | 1.5 |

For the retained F0 0.055, the calculated Filament control would be about 0.586 and the dielectric IOR about 1.613. These are mathematical conversions, not validated interchange of the complete BRDF.

Likewise, perceptual roughness, GGX distribution width and a measured leaf roughness parameter are not interchangeable. The current shared include calculates alpha = max(0.045, ROUGHNESS²). Consequently inputs below sqrt(0.045), approximately 0.212, share the same minimum isotropic GGX width before other effects. A borrowed roughness 0.1 recipe cannot produce the corresponding sharp lobe through this path. This is a source-derived implementation constraint, not a recommendation to change the clamp.

The Bousquet study's Table 4 reports effective IOR/roughness pairs of 1.22/0.078 for laurel, 1.56/0.29 for beech, and 1.68/0.46 for hazel. The paper relates the variation to differing cuticle/hair structure. These fits support species-specific models; they do not support directly copying those roughness numbers into our shader. PROSPECT-style spectral reflectance/transmittance can constrain integrated optical budgets, but does not supply a full directional BRDF/BTDF. See [PROSPECT-VISIR](https://www.ipgp.fr/~jacquemoud/publications/gerber2010b.pdf).

## Compatible configuration families

The following are engineering syntheses of those references. They describe which mechanisms belong together. Values absent from the sources remain parameters to measure; none is presented as a verified Vivarium photographic preset.

| Family | Material configuration | Lighting/camera configuration | Response to validate |
|---|---|---|---|
| Neutral opaque reference | Dielectric, measured/unlit base color, IOR/F0 specified, isotropic roughness initially; no transmission or decorative coat. | One characterized source, fixed exposure, neutral standards, fixed display transform. | Light energy changes brightness without changing material F0; roughness changes highlight shape. |
| Dry bark, soil, stone | Rough dielectric with surface-scale texture/normal structure; opacity, cavities and real contact represented separately. | Broad sky illumination, local visibility and bounce; add sun only for the sunlit variant. | Recesses respond to actual obstruction; surface identity survives both diffuse and directional lighting. |
| Waxy leaf | Dielectric cuticle reflection over pigmented/scattering tissue; front/back behavior, thickness/veins and orientation preserved. | Same leaf under broad illumination, grazing key and backlight, at fixed exposure. | Cuticle highlight and transmitted color remain distinguishable; overlapping leaves obscure/scatter light plausibly. |
| Soft/translucent tissue | Thin-sheet reflection/transmission with absorption and a shared energy budget; separate it from alpha transparency. | Front/back pairs and one/two/multiple-sheet stacks; known light and exposure. | Transmission is thickness- and wavelength-dependent rather than an added glow. |
| Wet porous substrate | Dry substrate plus absorbed moisture and an air–water surface film where present. | Stable dry/wet illumination and exposure, with an environment worth reflecting. | Darkening and glints depend on porosity, film coverage, roughness and view. Preserve dry areas. |
| Moss or short fibers | Porous canopy/aggregate structure; directional visibility and a fitted fiber/fuzz response where justified. | Broad/grazing illumination and ordinary-view comparisons, with contact shadows. | Porosity and occlusion read at relevant scales; do not reuse leaf transmission/anisotropy indiscriminately. |
| Standing water | Water interface reflectance, normal field, depth-dependent absorption and local reflections. | Sun/sky or broad source with matched surrounding geometry and exposure. | Reflection, refraction, absorption and shoreline behavior refer to the same surface. |

Moss has species-dependent directional reflectance: [Solheim et al. 2000](https://www.sciencedirect.com/science/article/abs/pii/S0034425799000930). A generic sheen slider is not sufficient evidence of a moss model.

### Lighting choices that belong with these materials

Use four reusable light rigs: neutral characterized source; overcast environment; sun plus sky; and a finite broad source for specimen photography. An environment panorama must retain unclipped source information: [Debevec–Malik 1997](https://people.eecs.berkeley.edu/~malik/papers/debevec-malik97.pdf) describes recovering radiance from bracketed photographs. Scale, orientation, shadows and indirect visibility remain part of the rig.

[Godot's physical-unit workflow](https://docs.godotengine.org/en/4.7/tutorials/3d/physical_light_and_camera_units.html) requires configuring project, camera, environment and lights together. Directional intensity is lux; the documented Omni/Spot intensity is lumens; background luminance is nits. Its approximately 100,000-lux clear-sun example is an illuminance reference, not an engine Energy value. Units do not supply missing bounce light.

For [AreaLight3D](https://docs.godotengine.org/en/4.7/classes/class_arealight3d.html), record size, distance, orientation and normalization. The documented attenuation value 2 gives inverse-square behavior; area normalization controls how changing size affects total energy. Its custom-shader integration and GPU cost require native checking. The shared leaf light function currently lacks the documented LIGHT_IS_AREA handling; adding the node alone is incomplete.

## What Vivarium's nine controls currently mean

Defaults below were read from the current shared include. They are not all physical material properties.

| Control | Current default | Interpretation and dependency |
|---|---:|---|
| `tissue_material_f0` | 0.055 | Direct normal-incidence reflectance input. Retained directional appearance, not species-fitted measurement. |
| `tissue_use_explicit_material_f0` | true | Independent material/light behavior; legacy branch remains a comparison control. |
| `tissue_specular_stress` | 1.0 | Diagnostic lobe multiplier. Leave neutral during calibration. |
| `tissue_direct_transmission` | 1.9 | Gain on albedo-relative transport; not 190% measured transmittance. |
| `tissue_diffuse_wrap` | 0 | Heuristic wrap/blend, not diffusion distance; positive settings require energy checking. |
| `tissue_phase_scatter` | 1.6 | Gain on a masked, powered angular term; not a physical phase-function asymmetry parameter. |
| `tissue_fiber_anisotropy` | 0.72 | Masked blend and ellipse-width mapping. Fit tangent direction, mask and lobe together. |
| `tissue_light_diagnostic` | 0 | Component isolation, not an appearance preset. |
| `tissue_material_diagnostic` | 0 | Diagnostic declared by the include; inspect its consumers, not a material measurement. |

Other coupled controls include roughness, normal/vein strength, pigment, tissue thickness, canopy occlusion, source extent/energy, environment radiance and exposure. Two important code observations follow: the diffuse/specular/transmission contributions are independently added without an enforced integrated energy budget, and the material configures built-in transmittance as well as custom BACKLIGHT transport. That motivates isolation and energy tests; it is not proof that native rendering double-counts those paths.

Vivarium already configures AgX and a practical-camera exposure resource: source values include min/max sensitivity 70/125, speed 0.6, scale 0.16 and tonemap exposure 0.92. Lighting changes with presentation phase. The recorded wave-3 exposure diagnostic reported physical light units disabled. Those arbitrary-unit source values cannot be relabeled lux, lumens or EV; a physical-unit experiment must calibrate the whole rig without silently replacing the current authored appearance.

Local source: [lighting include](../game/Shaders/leaf_direct_light.gdshaderinc), [leaf shader](../game/Shaders/leaf.gdshader), [material bindings](../game/Render/FloraVisualProfile.cs), [environment rig](../game/Render/EnvironmentRig.cs).

## Cross-check of the supplied additional results

### Keep the framework; qualify the guarantee

Physical lighting/camera settings, consistent color management, credible material data and microstructure are a sound organization. Many legitimate parameters—F0, IOR, roughness and energy ratios—are dimensionless. Arbitrary light units can also be consistently calibrated. Neither physical units nor AgX automatically resolves visibility, bounce ratios, anatomy/contact or material energy errors.

AgX is useful and was introduced as Blender's default for new files in 4.0; its highlight handling is described in [Blender's release notes](https://developer.blender.org/docs/release_notes/4.0/color_management/). It is not the only photographic output pipeline: [ACES 2's output transform](https://docs.acescentral.com/system-components/output-transforms/) explicitly treats tone scale, chroma and gamut compression. Avoid conflating fitted game-engine ACES curves with every version of the complete ACES system.

Generic contrast/saturation multipliers do not reproduce a named Blender OCIO look by definition. Godot 4.7 provides `tonemap_agx_contrast` and documents its distinction from post-tonemap `adjustment_contrast`. Treat the proposed contrast/saturation values as creative tests, not standard calibration.

### Exposure arithmetic and the supplied code

EV100 = log2((N²/t) × (100/S)), with t in seconds. Calculated values for the supplied table are 14.644, 10.615, 7.907 and 3.907, matching its rounded values. The code's different ISO 200 setting at f/4 and 1/60 gives 8.907, not 9.5.

For installed Godot 4.7.1, `exposure_shutter_speed = 60.0` means 1/60 second: this matches the [class API](https://docs.godotengine.org/en/4.7/classes/class_cameraattributesphysical.html) and installed GodotSharp documentation. A sentence in the general physical-units tutorial contradicts that scripting-unit behavior. Use the class/installed implementation here. Also, physical camera attributes assigned only to WorldEnvironment supply exposure/DOF defaults; their focal-length override of Camera3D's frustum applies when assigned to that camera. The supplied 50mm comment therefore requires camera binding, sensor/framing context and a focus distance.

Lumens alone do not determine subject illuminance: the studio/interior rows still need emitting size, distribution, distance and geometry. All four rows are illustrative starting scenarios, not complete matched material-light-camera calibrations.

### Corrected linear-to-sRGB conversions

Computed with the standard sRGB transfer function, rounded to nearest 8-bit code; these apply to neutral equal-RGB channels, not a whole photographic image or a universal material spectrum. See [W3C conversion reference](https://www.w3.org/TR/css-color-4/#color-conversion-code).

| Linear value/range | Corresponding 8-bit sRGB |
|---|---:|
| 0.02–0.04 | 39–56 |
| 0.03–0.80 | 48–231 |
| 0.05–0.16 | 63–111 |
| 0.07–0.25 | 75–137 |
| 0.80–0.85 | 231–237 |
| 0.50–0.98 | 188–253 |

Thus 0.80 linear is not sRGB 243; sRGB 243 decodes to approximately 0.896 linear. [DONTNOD's original chart](https://seblagarde.wordpress.com/2014/04/14/dontnod-physically-based-rendering-chart-for-unreal-engine-4/) gives 50–243 sRGB as its UE4 diffuse-authoring gradient and explicitly warns about roughness compatibility. It is a production convention with mixed measured/derived examples, not proof of the supplied species-category ranges. Do not enforce it by clamping every RGB channel: that can alter saturated pigment colors. Metals need colored conductor reflectance, not a universal gray albedo interval. [glTF's specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) distinguishes sRGB base-color texture encoding from linear roughness/metalness data.

### Wetness, clearcoat, SSS and occlusion

The claimed `mix(1,0.4,porosity*wetness)` is not the published formula at the cited source. [Lagarde's Water drop 3b](https://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/) uses a porosity-dependent attenuation toward 0.2, blended by wet level, and explicitly describes the gloss adaptation as empirical. It also warns about gloss-convention dependence. The supplied 0.08–0.15 roughness target is a proposed art range, not a universal wet-material law. Surface film and substrate must remain distinct.

Clearcoat can represent an actual protective film; clearcoat 0.6 and roughness 0.1 are unmeasured proposed settings. A broad base specular lobe is not a diffuse highlight. [StandardMaterial3D](https://docs.godotengine.org/en/4.7/tutorials/3d/standard_material_3d.html) already supports multiple material features. A BRDF lobe and the number of scene-light transport bounces are different concepts. SSS should model an appropriately scaled scattering material; it cannot generally repair coarse geometry or a normal-driven shadow-terminator artifact. The supplied shader sets SSS strength but no explicit thickness/transmittance configuration.

The supplied shader is a standalone built-in-lighting material, whereas Vivarium uses custom `light()` functions and species bindings. It is not a compatible whole-file replacement: existing texture arrays, tissue masks, organ markers, animation inputs and light-lobe integration would need deliberate preservation. Likewise, replacing the Environment resource would discard the existing canopy/environment configuration unless those settings are carried across.

Specular anti-aliasing filters unresolved normal variation; bent normals describe directional visibility. Neither is equivalent to lens blur. The derivative formula is a plausible experiment, not a verified interchangeable Toksvig/Kaplanyan implementation; mixing wet roughness downward after filtering can partially undo its protection.

[Environment's SSAO controls](https://docs.godotengine.org/en/4.7/classes/class_environment.html#class-environment-property-ssao-light-affect) describe direct-light affect as an artistic departure from indirect-only AO. [BaseMaterial3D](https://docs.godotengine.org/en/4.7/classes/class_basematerial3d.html#class-basematerial3d-property-ao-light-affect) similarly cautions about this control and documents bent normals/specular occlusion separately. Values 0.6–0.8 do not establish a physically correct specular-visibility solution. Screen-space indirect light also has limited/off-screen information. No global SSAO/SSIL radius/intensity recipe is calibrated without scene scale and reference evidence.

## Recommended next comparison

This is a proposed experiment plan, not executed research evidence:

1. Build a common sample board with actual leaf, bark, soil, moss and wet/dry rock, plus neutral diffuse and glossy references. Preserve scale, geometry and black camera background; record the actual lighting environment separately.
2. Use a fixed view transform/exposure. Compare three lighting families—broad sky, sun+sky and broad finite source—before enabling adaptation. Do not use exposure to compensate for each material.
3. First fit opaque reflection/roughness/normal response with transport extras isolated. Then fit leaf thickness/transmission and anisotropy against front/grazing/backlighting; test overlap and reflection/transmission energy together.
4. Validate wet layering and local occlusion separately. Add creative contrast only after matching the neutral reference. Re-test ordinary, overview and moving views so a close-up improvement does not hide distance aliasing.
5. Keep physically meaningful values and diagnostic gains separate in named profiles. A profile should save model/version, units, texture encodings, masks, source geometry, environment, exposure, transform and validation captures—not just sliders.

Measured reference candidates include the [RGL material database](https://rgl.epfl.ch/materials) and [MERL BRDF database](https://cdfg.mit.edu/wojciech/brdfdatabase/). MERL's stated research/academic-use terms require separate review before production reuse. Neither supplies a universal foliage preset; a measured angular reference can be fitted to the renderer's chosen model.

Prior native Vivarium evidence remains separate: [wave 3](../.clanker/tasks/photorealism-20261021/WAVE3.md) validated the narrow F0/light-separation correction and found no card-coverage response in its bounded exposure diagnostic. It did not validate the supplied photographic stack. No renderer implementation or new runtime measurements were performed for this research document.
