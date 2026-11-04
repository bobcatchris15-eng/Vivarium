# Rootcloister island canopy

Subsequent refinement: the dense drop-root colonnade described below has been
thinned to at most seven outer supporting columns. The original placement draws
are still consumed, so crown axes, leaf positions, canopy coverage and shade do not
shift. The broad space below the canopy is open between these widely spaced piers.
Updated evidence is in `build/rootcloister-open-floor-20261101/` and native captures
in `build/pilot-structure/rootcloister-open-floor-final-20261101/`. The final geometry
passed the same 15 focused checks and both builds; its first native repeat passed
with zero engine errors, with a subsequent repeat improving the ground-level view.

Rootcloister now grows an island-scale banyan crown, targeting roughly 70% actual
projected leaf cover. Its effective canopy radius is 70% of island diameter rather
than a fixed 5.6 m. Other pilot tree forms retain their existing radius behavior.
Canopy habitat influence and preview framing use the same effective radius.

The crown has fourteen primary spreading limbs (previously eight), twelve lateral
branches per limb, twelve leaves per lateral branch, and three or four potential
aerial-root columns per primary limb. Columns are only emitted where their feet
fit inside the island and reach the ground. Daughter branches and drop roots still
attach to the sampled parent woody axis. Uneven heights, sweeps, angles and taper
remain seeded. Broad heart-shaped leaves and fig fruit are retained.

## Measured coverage

Coverage is the rasterized union of actual leaf triangles in orthographic plan,
restricted to samples inside the hexagon. It is not crown bounding-box area or
summed leaf area. Measurements use a 160 by 160 footprint grid.

| Island diameter | Corner | Seed | Leaf cover |
| --- | --- | --- | --- |
| 5 m | 0 | 76 | 67.38% |
| 10 m | 2 | 424242 | 67.85% |
| 20 m | 5 | 731921 | 70.67% |

Each case keeps 2,016 leaves. Leaf shell tessellation is reduced specifically for
Rootcloister, taking trunk/crown geometry from approximately 2.21 million triangles
in the first candidate to 0.97 million in the retained version. Actual cover changes
by under 0.12 percentage points. Other species' leaf detail remains unchanged.

## Validation

- Solution and game builds pass.
- 15 focused tests pass, including canopy coverage at three sizes/corners/seeds,
  all ten forms' deterministic finite bounded anatomy, and authoritative root-radius
  query checks.
- Final native Forward+ review:
  `build/pilot-structure/rootcloister-70pct-final-20261101/run.json` records complete,
  source-stable, exit code 0 and zero engine errors. The production tree/island
  renderers produced plan, crown, whole-tree, root and cutaway captures. Renderer
  root-refresh and removal contracts passed for the selected tree.
- The first high-tessellation candidate hit `VK_ERROR_OUT_OF_HOST_MEMORY`; its
  captures are retained as rejected evidence. The lower-tessellation repeat passed.
- This is a structural/native review, not populated-world FPS or final material
  quality acceptance. Existing editors and unrelated dirty work were preserved.

Rootcloister meshes regenerate from the saved tree identity and seed, so both new
and existing Rootcloister worlds receive the expanded crown when loaded/rebuilt.
The persistent tree identity, ground root paths and save schema are unchanged.
