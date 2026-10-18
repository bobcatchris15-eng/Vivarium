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
- Full sim xUnit suite exposed numerous unrelated coverage/flora/terrain failures in copied source, so it is **not green**. Limit tests to relevant geometry/ambient coverage subsets.
- No isolated frame-rate acceptance: unrelated Godot sessions occupied the GTX 1080 and the benchmark requires strict isolation. No motion/photographic acceptance or mature-world acceptance.
- Baseline vs candidate comparisons of single plant and integrated scene show too little visible difference; additional experiments should target interaction of direct light, indirect bounce and leaf tissue in a fuller canopy, not simply more noise in pigments.

## Follow-up hypotheses
1. A dedicated test target with strongly directional backlight and dark adjacent unilluminated leaves will measure whether SSS actually contributes to silhouette interior; current broadleaf fixture is insufficient to show transmission.
2. Quantify per-leaf shading variance and color distribution relative to non-watermarked woodland reference photos using matched exposures.
3. Test local indirect lighting or directional wrap separately from global exposure/SSAO. Keep rock/soil contrast stable.
4. Verify quality tiers and test isolated FPS on target hardware before merging into main.
