# Groundcover density — 2026-10-26

User direction: grass/anonymous groundcover and bank fringe should have comparable dense coverage across essentially all visible ground, leaving simulated plants a little berth and petering out at gravel edges.

The production renderer now uses continuous dry-soil presentation rather than rejecting cells suppressed by the coarse persisted ambient-cover field. Habitat still selects colour and leaf/grass mix. Plants receive capped local collars including leaf reach. Stratified placement removes the old central bunching. Turf grass is 6–10 cm long before scaling; rosette leaves are about 4 cm long. Water, rocks, logs and unsuitable substrate retain exclusions. No simulation or save schema was changed.

The creeping layer covers steep dry slopes and bank shoulders beyond the old narrow water halo. Large seeded patch gaps are removed; connected runners and whole-leaf support/water checks remain. Bank leaves cast shadows. Turf triangles per cluster drop from 120 to 80 for grass and 480 to 240 for broadleaves; the bank leaf grid drops from 14x6 to 8x4. Greater coverage still increases total geometry; no performance claim is made.

Both layers follow GravelPatch.Covers' irregular outline, keeping interiors clear and smoothly thinning/shrinking cover through an 18 cm soil-side band. The taper passed 64 radial checks against the actual irregular boundary.

## Native review

Six 1600x900 views at normal gameplay quality 1, seed 20260923, daylight 420 s. Clean exit 0, stable source, three native checks pass, world digest unchanged. All bank patch reports pass finite/unit normals, clockwise winding and complete triangle support. Sixteen cached bank patches contain 25 runners and 80 leaves (prior inspection: 6/11). The close turf view has 5,686 grass clusters and 4,029 broadleaf clusters, totaling 1,421,840 triangles. These counts are bounded fixture evidence.

Solution and game builds pass. Five focused ambient-cover/understory tests pass. Exact extracted production tuft builders pass finite geometry/unit normals/clockwise checks. The close and medium cameras replay the earlier inspection poses; earlier images used quality 2, so this is a visual direction comparison, not a controlled performance comparison. The water-margin camera is partly obstructed by the production tree; the medium floor view provides a clearer bank view.

User art acceptance and measured performance remain open. Changes are local and uncommitted. Restart the running game to load the new C# meshes; existing worlds need no migration.

## Review images

### Floor close
![Dense turf](/E:/Vivarium/build/forest-reference-20261015/groundcover-dense-01-20261026/forest_floor_close.png)

### Floor and bank
![Dense turf and bank](/E:/Vivarium/build/forest-reference-20261015/groundcover-dense-01-20261026/forest_floor_medium.png)

### Gravel
![Clear gravel and surrounding turf](/E:/Vivarium/build/forest-reference-20261015/groundcover-dense-01-20261026/forest_gravel.png)

Evidence: `build/groundcover-density-20261026/receipt.json`, and full capture `build/forest-reference-20261015/groundcover-dense-01-20261026/`.
