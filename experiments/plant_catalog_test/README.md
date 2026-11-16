# Full graph flora catalog — 2026-11-15

All 125 current flora definitions now have authored graph designs: 29 woody/climbing forms, 61 herbaceous forms, and 35 fungi/moss/lichen forms. Each record specifies structural proportions, branching or colony topology, foliage arrangement, reproductive structures and a growth sequence. The shared builders cover related growth families; colors and ID hashes are not the only species differences. Existing visual identity and old save compatibility are not design constraints.

Sources are `src/Vivarium.Sim/Repr/Plant/Catalog/{Woody,Herb,Cryptogam}GraphSpecies.cs`, registered through `PlantGraphCatalog`. The three previously tuned generators remain usable with their JSON controls. Radial organ surfaces support needles, open cups and trumpets, mushroom domes, shelves, rounded fruits, discs and lobed stars. Fungal stipes have their own tissue and basal radius handling; underground mycelial links stay in the graph but are hidden by the compiler. Tree-fern pinnae and grass cross sections were refined after actual native inspection.

Ordinary play enables graph presentation by default. `--legacy-plants` is a diagnostic override. The 106 species eligible for `Flora.Items` use the live graph bridge, with age/biomass/health snapshots and local worker-built presentation. Climbers copy up to 96 actual simulation edges; floating species use a finite water surface when present. Attached individual forms capture solid support height and a local surface normal. Health changes affect tissue health independently of the fixed design vigour.

The other 19 definitions use coverage layers: 11 mosses, all seven lichens, and blue sundew. Their existing occupied living cells now host small cached graph shoot prototypes. A prototype is at most 32 organs and 2,000 triangles, with at most 48 sites and 12,000 added graph triangles per tile. A deterministic cell permutation spreads the limited budget over the tile. The continuous coverage surface, dead-cell behavior, and hagshair log-shoulder support remain in use. This is graph-derived microstructure, not a full colony duplicated at every 2 cm cell.

## Reproduce numerical validation

From repository root, build into an isolated artifacts directory and invoke the resulting DLL explicitly:

```powershell
dotnet build experiments/plant_catalog_test/plant_catalog_test.csproj --artifacts-path build/plant-catalog-final-tests
dotnet build/plant-catalog-final-tests/bin/plant_catalog_test/debug/plant_catalog_test.dll game/content build/plant-graph-tuning-20261115/catalog-validation.json
```

The current sweep covers all 125 species, three identities, ages .15/.4/.7/1 and LOD 0/1/2. It checks exact roster coverage, parent order, stable organ identities and attachment positions during growth, mature completion, bounded organ counts, finite geometry, usable normals, mesh indices and bounds. It reports zero errors and zero exact structural fingerprint collisions. Fingerprints exclude identity/color but retain physical dimensions; this is not proof that every pair is visually distinctive at gameplay distance. The report contains authored silhouette/habit notes and geometry counts. The existing 12 focused plant self-tests also pass.

## Native catalog and integration evidence

```powershell
$godot = 'E:/Vivarium/tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
& $godot --path E:/Vivarium/game -- --repr-review E:/Vivarium/build/plant-graph-tuning-20261115/catalog-final --repr-demo plant_catalog --plant-config E:/Vivarium/experiments/representation_rebuild/plant-tuning/organic-v4.json
python experiments/plant_catalog_test/review_gallery.py build/plant-graph-tuning-20261115/catalog-final build/plant-graph-tuning-20261115/catalog-validation.json
& $godot --path E:/Vivarium/game -- --graph-plant-review E:/Vivarium/build/plant-graph-tuning-20261115/catalog-live-final --graph-plants --graph-catalog-proof --plant-config E:/Vivarium/experiments/representation_rebuild/plant-tuning/organic-v4.json
```

`catalog-final/index.html` presents all 125 native captures with habit notes, growth stages and family filters. Each image shows 20%, 55% and mature specimens, normalized for catalog framing; physical sizes between species are not comparable in these images. `--plant-catalog-batch woody|herb|cryptogam` restricts capture to an authoring batch. The renderer report records Game/Sim module IDs and camera positions. Root inspected the seven contact sheets and corrected excessive grass cupping, green/pinched fungal stems, sparse tree-fern pinnae and hair-thin coral forks.

The live fixture publishes 112 actual individuals spanning all 106 eligible species, checks all 19 coverage prototypes, and seeds occupied coverage patches. It verifies biological growth/health, exactly one old chunk changed by a single growth edit, movement/contact, birth/removal, and zero descriptor builds/uploads/chunk rebuilds on warmed camera revisits. The coverage fixture built 31 tiles; its cumulative triangle counter is not a per-frame primitive count. Settings are transient and user saves are not used.

## Acceptance boundary

This completes the authored graph roster and initial production visual routing for owner playtesting. It does not finish per-species art acceptance. Some family templates remain simplified, juvenile fungi expose stems before their caps, tree cavities and full compound branching are not modeled, and the coverage base surface can still dominate tiny shoots at normal distance. Surface normals estimated across prop edges can blend a prop/soil discontinuity. Corpses still use the established corpse renderer, and ecology still uses its existing growth rules rather than deriving all physiology from these visual graphs.

Whole mature-world throughput, final material/lighting acceptance and target Radeon 860M performance remain open. Native catalog captures are visual evidence, not a controlled performance benchmark. The separate matched construction benchmark under `experiments/plant_graph_cost` records the earlier 180-plan speedup and its allocation tradeoff. The catalog harness still emits the previously observed seven Texture RID/one ObjectDB exit warnings; it completes its captures successfully.
