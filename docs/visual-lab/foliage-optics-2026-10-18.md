# Foliage optics and lighting experiment  - 2026-10-18

## Intent
Address the painted/artificial foliage in Vivarium without rebuilding plant geometry. No legacy appearance or save compatibility constraint. Target current Godot 4.7.1 Forward+ runtime.

## Source and provenance
Experiments developed on branch `visual/foliage-optics`, local worktree `E:\Vivarium-LeafOptics`. It was initialized at `da58825a` then seeded with a protected snapshot of the actively developed `E:\Vivarium` source. This is **not** a clean-HEAD acceptance result. The original main checkout remains unchanged by this work.

## Implementation
- New `game/Shaders/living_leaf_optics.gdshaderinc`: coherent pigment variation, spectral mesophyll transmission with vein/depth attenuation, UV-space gradient recovery.
- `leaf.gdshader`: organ pigment detail, shared transmission, Godot SSS-transmittance controls.
- `flora.gdshader`: reduced artificial green multipliers, more coherent broad/fine chlorophyll structure, correct normal-gradient coordinates, shared transmission.
- `ground_cover.gdshader` and `AmbientGroundCoverRenderer.cs`: render anonymous ground cover through the same shared tissue model.
- `EnvironmentRig.cs`: trial reduced key-light weighting, increased fill/ambient bounce, reduced SSAO radius/intensity.
- `MaterialPortReview.cs`: compare actual production-mesh leaf shader material plus legacy `ringreed` and `brooklace` and an integrated canopy fixture, fixed direct/back lighting.

## Actual Godot evidence (local, excluded from Git)
`build/visual-lab/manual-foliage-02/`: V1 actual material review succeeded, 44 captured PNGs.
`build/visual-lab/manual-foliage-03/`: V2 actual material review succeeded, 44 captured PNGs.
`build/visual-lab/world-optics-v1/`: actual world-review V1 material/original light; 66 PNGs, integrity checks pass.
`build/visual-lab/world-optics-v2-material/`: actual world-review V2 material/original light; 66 PNGs, integrity checks pass.
`build/visual-lab/world-optics-v2-light/`: actual world-review V2 material/rebalanced light; 66 PNGs, integrity checks pass.
`build/visual-lab/experiments/FOLIAGE-OPTICS-03/`: protected original-source and before captures; candidate after capture rejected due an initial Godot shader identifier error, corrected in later manual runs. Do not claim lab acceptance from these runs.

Visual author inspection: changes remain subtle in real captured side-by-side frames, although shaded foliage looks marginally more readable. Neither PBR leaf material nor global light change has independently demonstrated a major increase in photorealism. Do not accept on that basis.

## Validation and limitations
- `dotnet build game/Vivarium.csproj --no-restore` passed, zero warnings/errors.
- Godot 4.7.1 Forward+ `MATERIAL_PORT_REVIEW_OK`, `VIVARIUM_WORLDREVIEW_OK checks=2 failed=0` after shader fix.
- Visual Lab Python safety tests: 11 passing.
- The complete sim xUnit suite exposed numerous coverage/flora/terrain failures in copied source, so it is **not green**. Focused FloraVisualTests/AmbientGroundCoverTests/FormTests: 66 passed, 4 failed (geometry and budget expectations).
- No isolated frame-rate acceptance: unrelated Godot sessions occupied the GTX 1080 and the benchmark requires strict isolation. No motion/photographic acceptance or mature-world acceptance.
- Baseline vs candidate comparisons of single plant and integrated scene show too little visible difference; additional experiments should target interaction of direct light, indirect bounce and leaf tissue in a fuller canopy, not simply more noise in pigments.

## Follow-up hypotheses
1. A dedicated test target with strongly directional backlight and dark adjacent unilluminated leaves will measure whether SSS actually contributes to silhouette interior; current broadleaf fixture is insufficient to show transmission.
2. Quantify per-leaf shading variance and color distribution relative to non-watermarked woodland reference photos using matched exposures.
3. Test local indirect lighting or directional wrap separately from global exposure/SSAO. Keep rock/soil contrast stable.
4. Verify quality tiers and test isolated FPS on target hardware before merging into main.

## Reproducibility of published branch
A clean detached checkout of the published branch rebuilt using dotnet restore + build with zero errors or warnings after removing the optional, uncommitted BranchModuleLoader mesh-review dependency. This verifies standalone C# compilation, not a fresh import/render acceptance of the clean checkout.

## Continuation: isolated clean-checkout directional-tissue experiment

This follow-up was performed in a **new, detached** worktree `E:\Vivarium-FoliageLab` at committed base `9cc87a3e`. The original main, ecology, and existing optics worktrees had extensive live uncommitted changes and were **not** reset or edited. All experimental captures are local and ignored by Git.

### Independent-run dependency fixed

The previously published branch compiled cleanly but crashed before rendering: `EnvironmentRig` requested `res://Shaders/canopy_sky.gdshader`, whose file and `forest_canopy_radiance.gdshaderinc` were absent from the Git branch and only present as original-worktree changes. Their existing source was included in this experimental follow-up so a clean checkout can import and render. This is a reproducibility fix, not evidence of improved plant appearance.

### New lighting hypothesis and implementation

Earlier `BACKLIGHT` values carried several multiplicative attenuations and were ineffective when measured under the fixture's ambiguous "backlit" sun angle. We introduced `leaf_direct_light.gdshaderinc` into the dedicated leaf, legacy flora, and ambient ground-cover shaders, implementing a **custom Godot Forward+ per-light** surface response:

- Lambert-style reflected lamina term, spectrally selective Beer-Lambert thin-tissue transmission against illumination from the far side of the viewed leaf, plus a roughness-dependent GGX cuticle reflection.
- The existing material's `BACKLIGHT` is interpreted as the transmitted-tissue input and adjusted to the diffuse irradiance convention in Godot's custom `light()`.
- A shared spectral leaf function now produces a meaningful backlighting signal with vein/thickness attenuation and leaf-family controls; this remains an artist-calibrated model, **not a measured biophysical fit**.
- Diagnostic controls separately isolate direct reflection, direct transmission, and specular lighting, and display albedo, normal orientation, roughness, transmission, and venation.

The new `light()` applies to all three material families and therefore also changes some other surface/organ lighting in those materials. It requires a gameplay visual review, especially foliage in shade and non-leaf surfaces, before promotion.

### Reproducible GPU evidence

Godot 4.7.1 Mono / Vulkan Forward+ on the GTX 1080, after a fresh Godot asset import and `.NET 8` restore/build:

- `build/foliage-diagnostics/candidate06/`: 44 real material-review PNGs, runtime reported `MATERIAL_PORT_REVIEW_OK` with **no shader compilation errors**. This is the first valid custom-light candidate. Earlier `candidate02` through `candidate04` produced rejected errors or failed fixture setup; `candidate05` erroneously darkened leaves by applying albedo twice and was rejected.
- `build/foliage-diagnostics/direct-light-rig02/`: ambient disabled, source positioned behind the viewed leaf cluster. Isolated **transmission-only foliage was visibly green**, while reflection-only leaves were nearly unilluminated. This proves the custom directional transmission term is engaged in the actual renderer. The former nominal 135-degree "backlight" was a poor directional test for the viewed leaves.
- `build/foliage-diagnostics/material-light-rig03/`: texture/material instrument captures distinguish reflected color, normal, roughness, transmission, vein-mask; reveals that much of the plant remains flat-looking in normal views.
- `build/foliage-diagnostics/world-v6/`: actual full-world review, 57 PNGs, `VIVARIUM_WORLDREVIEW_OK checks=2 failed=0` and no shader compile errors.
- `build/foliage-diagnostics/final-direct-audit/`: final isolated audit with production shadebell triangles and shader modes.

The leaf winding audit had to use **Godot's clockwise front-face convention**, not right-hand cross-product orientation. After correcting the diagnostic to interpret the authored winding, the shadebell audit reported **5,344 triangles, zero reversed, zero perpendicular, zero degenerates, mean visible-normal dot +0.997**. Do not reverse plant normals based on right-hand triangle cross alone.

Comparison caution: `candidate06` full-resolution mean absolute differences versus the committed fixture baseline, including black backdrop, were shadebell front 0.285/255, shadebell backlit 0.334/255, legacy ringreed backlit 0.044/255, integrated day 0.514/255, and integrated night 0.052/255. These tiny whole-image deltas are not acceptance measurements, but they reinforce what the matched visible side-by-side renders show: **ordinary gameplay views remain insufficiently different and insufficiently lifelike**. The direct-transmission test is a working mechanism, **not** a satisfactory environmental/material overhaul.

### Validation

- Clean standalone `dotnet build game/Vivarium.csproj --no-restore`: success, zero warnings/errors.
- Visual-Lab Python tests `11 passed`.
- Focused xUnit material/geometry-relevant selection: **66 passed, 4 failed** (embercrown and shadebell triangle/form budgets, isopod articulated plates, creeper triangle budget), matching the previous handoff; no claimed green suite.
- No isolated GPU frame-rate certification: other Godot sessions were present during some work. The 30-FPS floor remains unverified.
- Do **not** merge into `main` or classify this as a photographic-realism acceptance.

### Remaining decisive work

The direct-light diagnostic is now useful for proving what a foliage optical effect contributes instead of relying on full-screen pixel deltas. The largest unresolved deficiency is **indirect spatially varied illumination of overlapping laminae and correctly differentiated material response at gameplay distance**. Next investigate directional skylight/bounce, light-to-leaf shadowing in dense canopy, actual normal/cuticle lobe response at grazing sunlight, and controls for preserving unrelated non-leaf materials. Evaluate reference-matched renders; replace this custom approximation if the measured visual quality is not competitive. Capture a proper isolated GTX 1080 benchmark before any merge.
