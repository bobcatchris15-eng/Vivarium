# Moss detail refinement and quieter bokeh — 2026-10-24

Status: implementation and native regression complete; refined art ready for user review. This supersedes the simplified moss/lichen art pass, while retaining the earlier numerical fleck fix.

## User direction

The simplified mat looked like painted graph paper, and its structures lost the older botanical detail. Raised structures must not use the carpet texture. Bokeh should be reduced by about 70% as the first adjustment.

## Mat surface and color

The coverage contour now samples a smooth occupancy field warped in world space. A narrow visual-only fringe extends into adjacent unoccupied cells so the boundary can cross the ecological grid naturally. Stable multiscale clipping feathers only the edge; surviving fragments remain opaque. Interior coverage is never randomly perforated by the edge rule. The simulation raster, occupancy and growth remain unchanged.

Height, health and dormancy are interpolated continuously across occupied cells rather than supplied as per-cell constants. Rounded overlapping pillows replace the small gridlike height noise. Irregular tuft cavities, scanned moss normal/roughness maps and scan-relative luminance restore local depth. Palette swatches are explicitly decoded from sRGB before use, correcting the washed-out first refinement. Four moss forms use separate deep olive/forest palettes; the scan supplies surface variation rather than flattening them into a single tint.

## Raised structures

The older, more intricate species helpers are restored on the rebuilt surface support: sphagnum whorls and occasional spore capsule, pearl hair points and beaked capsules, feathered floodlace runners, velvetweave turf and nodding urn, dichotomous antlerlace bushes, folded ruffle lobes, and embercrust fruiting structures. Seeded placement uses irregular offsets and combines stable clump anchors with the former species density thresholds. Actual rendered-bark support remains in use.

Ruffle inspection also exposed inherited coplanar upper/lower shells. A 0.18 mm separation and back-face culling on its dedicated material remove the large dark patches and stripes; single-sheet moss structures retain two-sided rendering. The focused ruffle rerun produced 18 stable native images with zero engine errors, and temporal small-component triage fell from 22 to zero. This is separate from the earlier numerical chromatic fleck mechanism.

Raised structures use their own draw surfaces and `coverage_structure.gdshader`. It has **no samplers or texture bindings**. Authored stem, leaf, capsule and lichen vertex colors pass through a nonnegative guarded sRGB conversion. Fine procedural surface variation, restrained tip shading and species roughness supply detail without wrapping the carpet texture over the organs. The mat and structures have distinct marker ranges and partitions. The previous generic ellipsoid-clump implementation remains available as code but is no longer the active terrestrial structure path.

## Bokeh

Both camera branches multiply their existing `DofBlurAmount` by 0.30. Near-focus amount therefore ranges from 0.0045 to 0.0255 rather than 0.015 to 0.085; distant amount is 0.0036 rather than 0.012. Focus acquisition, focus distances and transition distances retain their behavior. This is a strength adjustment, not a promise that every displayed blur radius or apparent blur decreases linearly by exactly 70%.

The source manifest now includes `game/Camera`, so camera edits participate in the native source-stability gate. Shared triplanar normal reconstruction also uses the finite normal fallback; the previously validated sRGB guards remain intact.

## Validation

The initial fixed-view habitat captures exposed a harness flaw: their recorded eye/target matched, but the rendered fixed view was looking at the canopy. Motion frames reset the view correctly. Those fixed frames (`world04`/`world06`, and earlier refinement iterations) are excluded from matched acceptance. The review now disables input, explicitly sets the actual active Camera3D pose for every capture, asserts direction alignment and camera identity, and writes 52 per-frame pose records. This is diagnostic-only behavior. No claim about the exact source of the prior camera override is needed for the correction.

Final `moss-detail-world-07` produced 53 PNGs with stable sources, exit 0 and zero engine errors. All 52 per-frame camera direction alignments are 1.0. The canonical world digest, eye and target match the previous review. Focus distances/transitions agree within 0.00001 m (float rounding). Native bokeh amount is 0.012347648 versus 0.041158818, a ratio of 0.300000063.

The tracked purple fleck is absent in all 36 fixed-camera samples. The compact-purple detector found zero candidates across all 53 images. These are bounded checks for the reproduced defect, not proof against all possible artifact classes.

`moss-detail-isolated-04` produced 144 stable native images across eight species. Seven current species use those captures; ruffle uses the corrected `moss-detail-ruffle-06` (18 stable native images). All runs exit 0 with zero engine errors. Their production geometry paths and fixture digests/triangle counts are recorded in the gallery proof. Inspection confirms restored botanical features, separate structure shading and deeper mat color. Extreme macro views still expose planar sphagnum whorls, simple fine leaves and faceted ruffle edges; art acceptance remains with the user.

The main client builds successfully with zero errors and six existing warnings. All owned production source files match the final tested snapshot. The world still reports the existing seven-Texture-RID shutdown warning. No FPS acceptance is claimed.

Review gallery: `build/photorealism/moss-detail-review-20261024/index.html`. Evidence: `proof.json`, `source-proof.json`, final run summaries and camera pose records. Earlier `moss-detail-isolated-01/02/03` and `moss-detail-world-01/02/03/04/06` are intermediate art iterations, not the final review. The existing geometry/sampler/sundew regression suite passed 20/20 during this pass, but it does not test the restored legacy detailed helpers; the final production isolation captures are their runtime evidence. No performance acceptance is claimed.

No save writes, commits or cleanup of unrelated work are part of this task. Concurrent work in the shared checkout is preserved; native review uses a private frozen source tree and serialized GPU lease.

`moss-detail-world-05` was deliberately stopped after the ruffle defect was found; its zero-image incomplete result is excluded. Full-scene before/after images also include concurrent flora work from the shared checkout and are contextual comparisons, not a claim that every changed pixel is caused only by moss or bokeh.
