# Reference dossier template — real-world evidence to Vivarium visual asset

**Template:** Copy this document into `docs/photorealism/references/<asset-id>/reference.md` and create the companion manifests, boards and reviews described below. This is a **blocking input** to visual implementation, as required by [the living landscape implementation plan](living_landscape_rendering_implementation_plan.md#20-reference-driven-reconstruction--mandatory-research-and-asset-production). Never treat an imagined animal/plant image or generic AI-generated image as anatomical or physically measured evidence.

## Identification and scope

- Vivarium asset ID:
- Asset class: vascular plant | tree | shrub | fern | moss | lichen | fungus | climber | ground detail | substrate | deadwood | wetland/shore | rock | other
- Named fictional design and existing approved silhouette requirements:
- Where used: organism stage, region/habitat, render material slot or surface class:
- Real-world **primary analogue** (scientific name and anatomical similarities):
- Real-world **secondary analogue(s)** (scientific name, specific features borrowed):
- Deliberate fictional modifications (pigment, architecture, behavior, scale):
- Reference researcher / asset author / independent reviewer:
- Research date; source and asset-recipe versions:
- Reference sufficiency: pass | insufficient | conditional (state why):

## Reference discovery matrix

Aim for **8–12 distinct informative views**, preferably original 3,000+ px images for geometry and genuinely detailed licensed 4K+ material masters or scanned PBR sets where applicable. Images are **evidence**, not presumed reusable texture data. If an anatomical reference cannot be found, record that fact rather than inventing it.

| View / evidence sought | Source ID(s) and original image dimensions | Anatomical / material finding | Confidence and gap |
|---|---|---|---|
| Whole organism front/side/back | | | |
| Whole organism overhead / natural habitat | | | |
| Root collar / stem emergence / soil contact | | | |
| Primary and secondary branching / phyllotaxis | | | |
| Upper blade / leaf or relevant tissue macro | | | |
| Underside / veins / margin / thickness macro | | | |
| Juvenile, mature, old, dead / decomposing | | | |
| Backlight / diffuse / grazing highlight | | | |
| Wet and dry material/environment transition | | | |
| Variation between distinct individuals | | | |

**Discovery sites to check as relevant:** [Kew](https://powo.science.kew.org/), botanical gardens, university botany and field manuals, natural history museums/herbaria, [iNaturalist](https://www.inaturalist.org/), [Wikimedia Commons](https://commons.wikimedia.org/), [Poly Haven](https://polyhaven.com/), [ambientCG](https://ambientcg.com/), lawful direct photography/scanning. Source taxonomy and provenance have precedence over automated image labels.

## Source media rights and data lineage

Create `source_manifest.json` beside this file (one item per photo/scan, with fields indicated):

```json
{
  "assetId": "EXAMPLE_REPLACE_ME",
  "sources": [
    {
      "id": "R001",
      "mediaUrl": "https://EXAMPLE_REPLACE_ME",
      "pageUrl": "https://EXAMPLE_REPLACE_ME",
      "author": "REPLACE_ME",
      "collection": "REPLACE_ME",
      "accessed": "YYYY-MM-DD",
      "nativeWidth": 0,
      "nativeHeight": 0,
      "knownPhysicalScaleMm": null,
      "license": "VERIFY_INDIVIDUAL_ASSET",
      "usePermission": "study-only",
      "attribution": "REPLACE_ME",
      "permittedLocalPath": null,
      "sha256": null,
      "notes": "Which anatomical or material property this proves"
    }
  ]
}
```

Use `study-only`, `derivative-permitted` or `redistributable` only after checking rights **for each item**. Default unknown-license media to external links and `study-only`; do **not** commit full-size copyright-restricted photos. Images used to make a texture derivative need appropriately permissive rights or explicit permission. Preserve credits for attributed materials. The released game must never package source-reference images accidentally.

## Measurement sheet (companion: measurements.md)

Every figure needs units and source IDs and must be labeled **measured**, **documented**, **estimated** or **fictional**:

| Dimension or physical property | Value / permitted range | Units | Evidence ID(s) | Confidence |
|---|---|---|---|---|
| Overall height and canopy/ground spread | | m | | |
| Root collar, basal structure and anchoring | | mm / m | | |
| Branching levels, count, spacing and angles | | count / degrees / m | | |
| Internode/taper/curvature, major axes | | ratio / m | | |
| Leaf/stalk count, blade aspect and camber | | ratio / mm | | |
| Petiole attachments and venation hierarchy | | m / qualitative | | |
| Wood/rock/soil surface ridge depth and wavelength | | mm | | |
| Albedo hue/chroma variation and underside difference | | description | | |
| Roughness / sheen, wet/dry difference | | description | | |
| Transmission / tissue thickness | | mm / estimate | | |
| Environmental contact and neighboring vegetation | | description | | |
| Juvenile/senescent/dead variation | | description | | |

For visually important features without measurable source data, describe the planned approximation and how it will be judged.

## Required construction outputs (companion: asset_manifest.json)

| Output | Asset path/hash/recipe | Required comparison to evidence | Status |
|---|---|---|---|
| Hierarchical anatomy sketch / branches and root frame | | Multi-view source anatomy | |
| Reproducible authored source mesh / C# generator / Blender script | | Silhouette and organ proportions | |
| Runtime production geometry, UV0/UV2/custom attributes, tangents | | Scale, normals, branching, attachment | |
| Albedo/top and underside pigmentation | | Neutral-light photo/tissue reference | |
| Normal map (scan/baked/inferred label) | | Grazing-light material source | |
| Height/displacement and real physical scale where useful | | Surface macro/scan | |
| Roughness / specular mask | | Wet/dry, multiple light angles | |
| AO / cavity map where useful | | Actual small geometry cavities | |
| Thickness, transmission, subsurface/backlight mask where useful | | Backlit and anatomical reference | |
| Opacity/cutout, emission, other justified maps | | Physical/fantasy intent | |
| Master texture and compressed mipmapped runtime variant | | Master vs compressed material capture | |
| Stage variants and stable IDs/attachment metadata | | Juvenile, mature, dead and wind | |
| License, provenance and asset-build manifest | | Original image/scan sources | |

**Important:** photo brightness is not height, and a color-to-normal algorithm does not measure physical normals. Texture reconstruction must use actual geometry/scan/light evidence when available, or a clearly labeled approximation. Keep textures physically scaled and channel color spaces correct.

## Mandatory reference comparison gallery (companion: review.md)

Use the **same subject scale and matching camera angle** as far as possible; when lighting or geometry prevents an exact match, annotate the difference explicitly.

- [ ] **Reference** whole silhouette, multiple views, natural surroundings, upper and lower surfaces, scale and life states
- [ ] **Geometry** clay, wireframe, orthographic/oblique silhouette, root/branch/leaf attachment and real scale
- [ ] **Materials** isolated albedo, normal, height, roughness, AO, transmission, upper/underside and final lit
- [ ] **Environment** 5–50 cm macro, 0.5–3 m interaction, 5–25 m island, wet/dry, soil/rock/water contact and natural lighting
- [ ] **Motion/state** wind/orbit frames, growth/death, no geometry-tier pop or texture swimming
- [ ] **Engineering** source hashes, stable deterministic outputs, no new simulation digest differences, target hardware frame budget, rights cleared for derived/runtime materials

| Review dimension | Pass / revise / insufficient evidence | Specific defect or source ID | Action or accepted exception |
|---|---|---|---|
| Morphological and botanical plausibility | | | |
| Whole-plant silhouette / natural variation | | | |
| Mesh quality, UV, tangents and attachment | | | |
| Material microstructure / PBR channels | | | |
| Backlighting and upper/underside appearance | | | |
| Floor/water/rock/neighbor contact | | | |
| Growth, decay and animation continuity | | | |
| License and source-to-asset lineage | | | |
| Determinism and 30-FPS hardware budget | | | |

**Decision:** approve | revise | blocked by insufficient evidence. Include reviewer/date and linked test/capture artifacts. An agent cannot close a visual task by presenting only a visually pleasing final render; it must show the real reference and how the mesh, material maps and normal/roughness responses were derived and evaluated.
