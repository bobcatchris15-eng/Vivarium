# Sundew clump redesign — 2026-10-24

Implemented the user-directed replacement: discrete taller clumps of scaled stalks, vivid blue dewy terminal clubs, and layered shrubs of small round dark-green basal leaves. This is a first art pass ready for review, not a declaration of photographic acceptance.

## Why the old sundew was unusually vulnerable

The all-species isolated survey reproduced abrupt per-pixel hue changes overwhelmingly on sundew (416small temporal candidate regions; next species11). The old form combined a moss/coverage sheet shader with many small closed leaves, tentacles and bead meshes. Leaf-face UV2 data also selected dew gloss on some leaf faces. Flecks survived bead removal, unshaded rendering and shadow removal; MSAA-off substantially changed/reduced instability. This implicated the geometry/material/sampling path, but did not establish the precise shader operation or an engine defect.

## Replacement

- New deterministic meter-scale geometry: roughly4–7cm tall, 4–6cm wide,4–7upright stalks and24–32layered round basal leaves per clump. Rounded scale plates are actual closed geometry; blue terminal clubs have attached rounded lobes. There are no detached micro-droplets.
- Sundew occupied cells no longer contribute to the continuous sheet or its corner colors/opacity. Deterministic local-minimum anchors produce spaced clumps; an isolated occupied cell can still render a clump. No neighboring moss/lichen species is removed.
- Three dedicated material surfaces for leaves, scaled stalks and blue clubs. The new shader uses per-surface tissue uniforms, bounded palettes, bounded roughness/clearcoat and guarded normal normalization. It avoids interpolated vertex pigment and ambiguous organ/dew flags, moss textures, custom light code and animation.
- The simulation still owns the same Mat occupancy, growth, moisture preferences and spreading rules. No simulation rule or save was changed. The visual description was updated. The standalone sundew preview now routes through production coverage geometry; its legacy normalized generator also delegates to the new form.
- Production MSAA4X/TAA/FXAA settings remain enabled. The change does not hide the issue by globally disabling anti-aliasing.

## Verification

24focused tests pass (new sundew finite/shape/role/determinism checks plus FloraVisual and HabitatDynamics regressions). Both client and simulation build without errors; six existing client warnings remain.

Final native isolated02:54PNG across sundew, bogglass moss and ruffle lichen; source stable, zero engine errors. Sundew has0screened temporal outliers across both views, versus416on the old form with the same camera, scale, light, seeded cells and fixture simulation digest. The fixture geometry drops from110,104 to53,380triangles. Bogglass has1small candidate and ruffle0; neither reproduces the sundew pattern. These short samples are not a universal stability or performance guarantee.

Final habitat01:35PNG (three habitat views plus32motion frames), source stable, zero engine errors and unchanged simulation digest. Three scenes match the earlier integration capture's recorded world/camera settings; a comparison is retained. That older habitat capture also predates the removal of ambient plant wind, so other foliage poses may differ; it is not a pixel-isolated sundew ablation.

Luna independently reviewed the final isolated images and material/corner routing. No actionable routing defect or detached colored fleck was found. Art limits remain: the basal leaves are smooth rounded volumes, and the terminal tentacles are short rounded lobes; further surface/anatomical refinement can follow visual review. The material's subtle UV color variation is circumferential, not a botanical base-to-tip gradient.

Review: `build/photorealism/sundew-clump-review-2026-10-24/index.html`. Evidence includes matching proof, native run manifests, per-species geometry metadata and temporal analysis. Existing editors and unrelated dirty work were preserved. All edits remain local and uncommitted.
