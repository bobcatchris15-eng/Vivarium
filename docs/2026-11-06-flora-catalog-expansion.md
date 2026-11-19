# Flora Catalog Expansion — 6 November 2026

## Goal
Roughly double the flora catalog (62 → 125 species) so that no island shows every species, while keeping the set of species that an island *can* show spread roughly evenly across the local condition space (moisture × light × nutrients × substrate × water depth). Overlap in niches is expected and desired; each species must still read as its own organism and fill its own micro-niche.

**Hard rule: no new bioluminescent species.** Do not add `bioluminescenceColor` or `bioluminescenceStrength` to any new species file.

## Mechanism (implemented once, by the species-pool agent)
A deterministic per-world species pool is computed at world creation from the seeded, generated terrain:
- `FloraSpeciesPool.Select(w)` scans every catalog species against sample points across the island using `FloraSystem.PotentialSuitability` (refusals + permissive survival-grade light via `sp.Light.EvalStage(light, 3.0)`), buckets species by `(Moisture.Optimum, Light.Optimum, Nutrients.Optimum)` into 27 bands, and keeps a seeded fraction (52–72 %) of each band with a floor of one surviving species per non-empty band; total capped ≤ 70, floored ≥ 16.
- `VivariumWorld.SpeciesPool` stores `{ Ids, Fitness }`.
- `CoverageSystem.SeedInitial` skips coverage species outside the pool.
- `Populate.Starters` keeps its existing per-entry placement, skips entries outside the pool, then places one individual each of at most 20 unlisted pool species (ordered by Fitness desc) so every island shows its own mix.

## Authoring conventions

### Species JSON
Copy mechanics (growth/spread/competition/litter/shedding/postLife/reproduction/decomposition) from the listed template species, then apply the niche + palette values in this document. Niche values are the point of this task: use the exact `moisture`, `light`, `nutrients`, `hardMinMoisture`, `hardMaxMoisture`, `minSuitability`, water-depth and substrate numbers given here.

Templates to read before writing:
- Coverage moss: `game/content/flora/velvetweave_moss.json`
- Coverage lichen: `game/content/flora/ruffle_lichen.json`
- Individual moss/fungus on ground: `game/content/flora/embercup.json`, `game/content/flora/emberfan_fungus.json`
- Small forbs: `game/content/flora/veilfern.json`, `game/content/flora/prismstar.json`
- Aquatic: `game/content/flora/glasslily.json`, `game/content/flora/fencomb.json`
- Climber: `game/content/flora/spiralvine.json`
- Shrub: `game/content/flora/shadebell.json`
- Tree: `game/content/flora/hollowcrown.json`

### Mesh builder conventions
New builders live in new partial files `src/Vivarium.Sim/Geometry/OrganismMeshes.New<Group>.cs`, namespace `Vivarium.Sim.Geometry`, `public static partial class OrganismMeshes`. Method signature: `private static void <Name>(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)`.

**All new builders author in real metres** into a scratch `MeshData part`, record leaves/spans on `part`, then merge with the existing converter:

```csharp
var part = new MeshData();
// ... author in world metres: 0.8 m stems, 4 cm leaves, etc. using radius/height args ...
NewFloraAppendWorld(m, part, radius, height);   // scales into the unit frame the renderer expects
```

This is the `OrganismMeshes.Hollowcrown.cs` pattern (see its line ~1466). Respect `m.FloraDetailLevel` (0 full / 1 / 2) and `m.Lod` (`lod.Segments(...)`, `lod.Sides(...)`) for tessellation, as the existing builders do. Call `m.RecordLeaf(...)`/`m.RecordLeafSpan(...)` for every lamina, `part.Structural.Add((firstVertex, count, sides, radiusM))` for every closed tube, and set `m.PreserveUpperCanopyOnLod = true` for trees/shrubs. Winding must use `Primitives.TriangleFacing` so `leaf.gdshader`'s `cull_disabled` front-face logic works.

Handy utilities: `Primitives.Tube`, `Primitives.Ellipsoid`, `Primitives.CurvedLeaf`, `Primitives.TriangleFacing`, `Form.Broadleaf.BuildLeaf`, `Form.FoliageBlade.Build`, `Form.LeafBlade.Build`, `Form.SoftTube.Build`, `Form.Axis.Build`, `Form.EpiphyticRosette`.

## New species roster (63)

Field order: `id | Name | shape | substrates | moisture o/t | light o/t | nutrients o/t | extra habitat | height m | radius m | c1 → c2 | mechanics template | differentiation note`

### A. Coverage mosses (mat block; archetype moss, placementGroup moss_lichen) — 7
- `feathermoss` | Feathermoss | carpet (generic) | soil 1, wood .8, rock .4 | .62/.20 | .30/.28 | .30/.30 | hardMax .92 | .04 | — | velvetweave_moss | Prostrate feather fronds for shaded woodland floor; reads as a flat moss with visible pinnate fronds.
- `rustsphagnum` | Rustsphagnum | carpet (generic) | soil 1, water .5 | .88/.12 | .62/.30 | .12/.25 | hardMin .60 | .06 | — | bogglass_moss | Acid-bog hummock builder; rust-red and green, heightRule "wet", denser dome than bogglass.
- `threadmoss` | Threadmoss | carpet (generic) | wood 1, rock .6, soil .4 | .80/.18 | .20/.25 | .40/.30 | hardMin .50 | .03 | — | velvetweave_moss | Dark wispy threads over wet, deep-shaded logs and rock; the shade specialist of wet wood.
- `silkmoss` | Silkmoss | carpet (generic) | soil 1, gravel .3 | .75/.18 | .80/.25 | .30/.30 | hardMax .97 | .04 | — | floodlace_moss | Golden-green sun-lit bank mat; the bright end of the carpet mosses.
- `rockcap_moss` | Rockcap moss | cushion (generic) | rock 1, gravel .7, soil .2 | .35/.25 | .85/.25 | .10/.25 | hardMin .15 | .05 | — | pearl_cushion_moss | Charcoal-green pincushion on bare stone and scree; drought-tolerant, the driest moss.
- `pinemoss` | Pine-tuft moss | carpet (generic) | soil 1, rock .3 | .50/.22 | .42/.28 | .18/.30 | — | .035 | — | velvetweave_moss | Tufty acid-litter moss of dry conifer shade; khaki-green, upright short shoots.
- `ribbon_liverwort` | Ribbon liverwort | mat (generic) | rock .8, wood .6, soil .4 | .85/.15 | .25/.25 | .35/.30 | hardMin .55 | .022 | — | floodlace_moss | Thallose liverwort: flat olive tongue-lobes on dripping rock and wet bark; the wettest-shade specialist.

### B. Coverage lichens (lichen block; archetype lichen) — 4
- `beardlace_lichen` | Beardlace lichen | fruticose | wood 1, rock .4 | .55/.25 | .50/.30 | .06/.25 | — | .03 | — | antlerlace_lichen | Hanging curly beard tufts for old wood in humid air; pale sage.
- `sunburst_lichen` | Sunburst lichen | crust | rock 1, gravel .8 | .30/.25 | .95/.20 | .05/.25 | — | .012 | — | embercrust_lichen | Flat radiating mustard-orange rosette on bare sun-baked stone; the driest, brightest lichen.
- `porcelain_lichen` | Porcelain lichen | foliose | wood .8, rock 1 | .60/.25 | .30/.28 | .12/.30 | — | .012 | — | ruffle_lichen | White-blue adhesive lobes for shaded bark and rock; the shaded foliose.
- `reindeer_lichen` | Reindeer lichen | fruticose | soil 1, rock .5 | .40/.25 | .85/.25 | .08/.30 | — | .025 | — | antlerlace_lichen | Pale branching clumps on dry heath soil; geometric, brittle, the pale ground fruticose.

### C. Individual moss — 1
- `haircap_moss` | Tall haircap | polytrichum (new) | soil 1, wood .5 | .70/.20 | .55/.30 | .30/.30 | — | .12 | .09 | embercup | Star-leaved tall shoots with golden setae and nodding capsules; the upright hero moss, taller than embercup and green rather than copper.

### D. Fungi (archetype fungus, placementGroup decomposer, feeds detritus, decomposition block) — 7
All fungi: `light.tolerance` 2 like existing fungi, refuse water, minSuitability .15, copy decomposition mechanics from `emberfan_fungus` with sensible radius multipliers.
- `puffball` | Dustpuff | puffball (new) | soil .8, wood .6 | .60/.25 | .50/2 | detritus | — | .09 | — | embercup | Round papery puffball on humus; matte cream-umber, squat.
- `earthstar` | Earthstar | earthstar (new) | soil .9, wood .4 | .55/.25 | .55/2 | detritus | — | .06 | — | bonecup | Star-lobed puffball that splits open; thick tan rays on the ground.
- `stinkhorn` | Stinkhorn | stinkhorn (new) | soil 1, wood .5 | .68/.22 | .45/2 | detritus | — | .22 | — | carrion_bell | Tall slime-capped spike with a lace skirt; the verticallly dramatic decomposer.
- `birdsnest_fungus` | Bird's-nest cups | birdsnest (new) | wood .9, soil .5 | .70/.20 | .40/2 | detritus | requiresFeature log .12 | .03 | — | emberfan_fungus | Tiny ochre cups holding egg-like peridioles on wet wood.
- `coral_fungus` | Coral tuft | coralfungus (new) | wood .9, soil .4 | .72/.20 | .35/2 | detritus | — | .11 | — | emberfan_fungus | Upright cream-and-pink coral branching from buried wood.
- `morel` | Ashcap morel | morel (new) | soil 1 | .62/.22 | .50/2 | detritus | — | .16 | — | bonecup | Honeycombed ash-grey cap on a cream stipe; spring flush under woody shade.
- `boletus` | Fatbolete | bolete (new) | soil 1, wood .4 | .66/.22 | .40/2 | detritus | — | .12 | — | bonecup | Stout brown-capped, thick-stemmed bolete with a cream pore face.

### E. Ferns — 4
- `ladyfern` | Ladyfern | ladyfern (new) | soil 1, wood .5 | .72/.22 | .42/.28 | .50/.35 | hardMin .35 | .55 | .45 | veilfern | Soft pale-green bipinnate shuttlecocks in rich moist shade.
- `royalfern` | Royalfern | royalfern (new) | soil 1, water .4 | .85/.18 | .55/.30 | .55/.32 | hardMin .50, maxWaterDepth .02 | .85 | .50 | veilfern | Tall crown of widely-separated pinnae; the wet-woodland giant fern.
- `brackenfern` | Bracken | brackenfern (new) | soil 1, gravel .4 | .40/.28 | .80/.25 | .30/.30 | hardMin .18 | .70 | .65 | T=tussock for spread; aggressive (interval 10, radius 1.6, propagules 3) | Stiff triangular fronds on dry open slopes; the aggressive pioneer fern.
- `hartstonguefern` | Hart's-tongue | hartstonguefern (new) | rock .9, soil .7, wood .4 | .62/.25 | .28/.26 | .45/.30 | — | .40 | .28 | moonfern | Undivided strap fronds from a shaded crevice; the deep-shade evergreen fern.

### F. Graminoids — 6
- `sedgefountain` | Fountain sedge | sedge (new) | soil 1, gravel .5 | .70/.30 | .70/.30 | .40/.35 | maxWaterDepth .03, hardMin .30, hardMax .97 | .50 | .34 | ringreed | Arching fine-bladed fountain with brown spikelets; the generalist sedge.
- `cottongrass` | Cottongrass | cottongrass (new) | soil 1, water .6 | .92/.10 | .80/.25 | .15/.30 | hardMin .65, maxWaterDepth .12 | .55 | .18 | fencomb | Thin stems topped with white cotton tufts; the acid-bog emblem.
- `woodrush` | Wood-rush | woodrush (new) | soil 1, wood .4 | .50/.25 | .35/.28 | .35/.30 | — | .30 | .22 | frosttussock | Shaggy hairy-leaved tuft of shaded dry woodland floors.
- `reedgrass` | Filter reed-grass | reedgrass (new) | soil 1, water .6 | .80/.22 | .75/.28 | .60/.35 | maxWaterDepth .25 | 1.10 | .50 | coalspike | Tall vigorous marsh grass with soft panicles; the aggressive eutrophic grass.
- `sheepsfescue` | Sheep's fescue | tussock (reuse) | soil 1, gravel .4 | .35/.28 | .90/.20 | .20/.30 | — | .18 | .16 | frosttussock | Fine blue-grey bristle tuft of dry exposed turf.
- `horsetail` | Jointgrass | horsetail (new) | soil 1, water .3 | .80/.20 | .60/.30 | .50/.32 | maxWaterDepth .02 | .60 | .20 | ringreed | Jointed hollow stems with whorled branches; ancestral, horsetail silhouette.

### G. Forbs — 11
- `woodanemone` | Wood anemone | anemone (new) | soil 1, wood .4 | .65/.25 | .22/.22 | .55/.35 | — | .18 | .16 | moonfern | White star flowers over palmate leaves; the deep-shade spring ephemeral.
- `buttercup` | Meadow buttercup | buttercup (new) | soil 1 | .60/.30 | .78/.28 | .50/.35 | — | .25 | .18 | prismstar | Glossy cupped yellow flowers on erect leafy stems; the bright mesic meadow forb.
- `wildstrawberry` | Wild strawberry | strawberry (new) | soil 1, gravel .4 | .45/.28 | .68/.30 | .45/.35 | — | .10 | .30 | trifold + reproduction berry/animal | Red fruit, trifoliate leaves and runners; the dry-bank fruiting creeper.
- `marshviolet` | Marsh violet | violet (new) | soil 1, water .4 | .88/.14 | .55/.30 | .50/.32 | hardMin .55 | .14 | .12 | fenbead | Violet flowers over round leaves in spring flushes; the wet-meadow specialist.
- `woodsorrel` | Wood sorrel | sorrel (new) | soil 1, wood .5 | .60/.25 | .25/.24 | .40/.32 | — | .10 | .13 | moonfern | Shamrock leaves with white veined flowers; the acid-shade clumper.
- `nettle` | Sting-nettle | nettle (new) | soil 1 | .60/.30 | .60/.32 | .85/.20 | hardMin .45 | .80 | .50 | brackenfern spread for aggression (interval 9, radius 1.3, propagules 3) | Serrated opposite leaves, drooping green tassels; the eutrophic disturbance specialist.
- `burdock` | Burdock | burdock (new) | soil 1 | .55/.30 | .65/.30 | .80/.22 | hardMin .40 | .90 | .60 | nettle template | Huge woolly basal leaves and hooked burrs; the tall rich-soil biennial.
- `cowparsley` | Hedge-parsley | cowparsley (new) | soil 1 | .58/.28 | .60/.32 | .72/.24 | hardMin .35 | .85 | .45 | nettle template | Lace umbels on hollow stems; the classic white-umbel hedgerow forb.
- `meadowsweet` | Meadowsweet | meadowsweet (new) | soil 1, water .4 | .80/.20 | .68/.30 | .60/.32 | maxWaterDepth .02 | .70 | .45 | rainshield | Frothy cream plumes over wine-stained leaves; the fen bright forb.
- `marshmarigold` | Marsh-marigold | marigold (new) | soil 1, water .5 | .90/.12 | .70/.30 | .55/.32 | hardMin .60, maxWaterDepth .03 | .20 | .24 | brooklace | Glossy gold cups above lush leaves; the early spring pool-edge.
- `loosestrife` | Purple loosestrife | loosestrife (new) | soil 1, water .5 | .85/.16 | .80/.25 | .65/.30 | maxWaterDepth .05, hardMin .55 | 1.00 | .45 | reedgrass template (aggressive) | Tall magenta flower spikes over willow leaves; the aggressive wetland tall forb.

### H. Succulents — 2
- `stonecrop` | Biting stonecrop | stonecrop (new) | rock 1, gravel .8, soil .3 | .28/.25 | .92/.20 | .12/.25 | hardMin .10 | .08 | .14 | sunstone_rosette | Low golden-starred succulent mat on dry stone; the driest green plant.
- `houseleek` | Houseleek | houseleek (new; reuse Form.EpiphyticRosette if it fits, else write it) | rock 1, gravel .5 | .32/.25 | .88/.22 | .18/.28 | hardMin .12 | .10 | .12 | sunstone_rosette | Tight blue-grey rosette wedged into rock crevices.

### I. Aquatics (placementGroup waterside_aquatic) — 7
- `coontail` | Coontail | coontail (new) | water 1, soil .8 | 1/.05 | .45/.35 | .40/.35 | minWaterDepth .25, maxWaterDepth 1.30, hardMin .95 | .50 | .22 | streamribbon | Fluffy dark submerged whorls; the deep-water drifter.
- `bladderwort` | Bladderwort | bladderwort (new) | water 1, soil .6 | 1/.06 | .70/.30 | .20/.30 | minWaterDepth .12, maxWaterDepth .90, hardMin .95 | .30 | .20 | silverweed | Feathery submerged stems with tiny bladders and yellow snapdragon flowers above the surface; the carnivorous floater.
- `waterstarwort` | Water starwort | starwort (new) | water 1, soil .6 | 1/.05 | .60/.32 | .50/.35 | minWaterDepth .03, maxWaterDepth .50, hardMin .92 | .02 | .10 | fenbead | Tiny floating star rosettes; the surface micro-plant.
- `bogbean` | Bogbean | bogbean (new) | water 1, soil .8 | 1/.06 | .65/.30 | .35/.32 | minWaterDepth .04, maxWaterDepth .60, hardMin .92 | .14 | .30 | glasslily | Trifoliate floating leaves and white fringed flowers; the fen-pool specialist.
- `burreed` | Bur-reed | burreed (new) | water 1, soil .7 | .97/.10 | .75/.28 | .60/.32 | maxWaterDepth .30, hardMin .75 | .55 | .30 | ringreed | Floating ribbons rising to spiky burr heads; the shallow-margin colonist.
- `waterplantain` | Water-plantain | waterplantain (new) | water .8, soil 1 | .95/.12 | .70/.30 | .55/.32 | maxWaterDepth .15, hardMin .70 | .45 | .28 | glassrush | Broad ribbed leaves and airy whorled flower stems; the drawdown-mud specialist.
- `arrowhead` | Arrowhead | arrowhead (new) | water .8, soil 1 | .96/.10 | .75/.28 | .60/.32 | maxWaterDepth .25, hardMin .75 | .50 | .26 | coalspike | Arrow-shaped leaves with white triple flowers; the shallow emergent.

### J. Climbers (climber block; copy from spiralvine.json) — 3
- `ivylace` | Ivilace | vine_ivy (new node builder) | soil 1, wood .8, rock .6 | .60/.28 | .30/.30 | .55/.32 | — | .25 | — | clinglace | Dark lobed leaves that clothe shaded trunks and stone; the deep-shade climber.
- `hop` | Bine hop | vine_hop (new) | soil 1, wood .5 | .70/.24 | .70/.28 | .70/.28 | hardMin .40 | .35 | — | spiralvine | Coarse twining bine with papery cones; the vigorous rich-soil climber.
- `honeysuckle` | Woodbine | vine_honeysuckle (new) | soil 1, wood .6 | .55/.28 | .55/.30 | .60/.30 | — | .30 | — | fenhook | Opposite leaves and cream-to-pink tubular flowers; the wood-edge twiner.

### K. Shrubs (woody shrub; copy shadebell.json woody/mechanics) — 5
- `bogmyrtle` | Bog myrtle | shrub_bogmyrtle (new) | soil 1, water .5 | .88/.14 | .65/.30 | .25/.30 | hardMin .55, maxWaterDepth .06 | .90 | .50 | shadebell | Grey-green aromatic shrub with catkins; the fen/peat specialist.
- `hazel` | Hazel | shrub_hazel (new) | soil 1 | .60/.28 | .55/.32 | .60/.32 | hardMin .30 | 1.80 | .85 | shadebell | Round scalloped leaves and dangling catkins; the mesic coppice shrub.
- `hawthorn` | Hawthorn | shrub_hawthorn (new) | soil 1, gravel .3 | .45/.30 | .75/.28 | .50/.35 | hardMin .22 | 2.00 | .95 | embercrown | Thorny twigs, white blossom and red haws; the dry-edge thicket shrub.
- `juniper` | Crag juniper | shrub_juniper (new) | rock .8, gravel .6, soil 1 | .30/.25 | .85/.25 | .25/.30 | hardMin .12 | 1.20 | .70 | cragparasol | Blue-green needle sprays and dark berries; the dry rock evergreen.
- `woollywillow` | Woolly willow | shrub_woollywillow (new) | soil 1, gravel .5 | .55/.25 | .80/.25 | .20/.30 | — | .60 | .45 | frosttussock template for mechanics; woody shrub block | Silver-downy leaves and furry catkins; the tundra-peat low shrub.

### L. Trees (woody tree; copy hollowcrown.json mechanics/woody/postLife) — 6
- `silverbirch` | Silverbirch | birch_tree (new) | soil 1, gravel .4 | .50/.30 | .85/.25 | .40/.35 | hardMin .25 | 3.40 | 1.10 | hollowcrown | White peeling bark and airy light crown; the dry-mesic pioneer tree.
- `quakingaspen` | Quaking aspen | aspen_tree (new) | soil 1 | .60/.28 | .80/.28 | .50/.35 | hardMin .30 | 3.00 | 1.00 | hollowcrown | Pale smooth bark and round trembling leaves; the clonal moist pioneer.
- `alder` | Alder | alder_tree (new) | soil 1, water .5 | .85/.16 | .70/.30 | .50/.35 | hardMin .50, maxWaterDepth .10 | 3.60 | 1.20 | hollowcrown | Dark fissured bark and cone-like catkins; the waterside nitrogen-fixer.
- `waterwillow` | Water-willow | willow_tree (new) | soil 1, water .5 | .80/.20 | .75/.28 | .60/.32 | hardMin .45, maxWaterDepth .05 | 2.60 | .90 | hollowcrown | Silky narrow leaves and leaning stance; the fast bank tree.
- `mirepine` | Mire pine | pine_tree (new) | soil 1, water .4 | .75/.22 | .80/.25 | .20/.30 | hardMin .40, maxWaterDepth .10 | 3.80 | 1.00 | hollowcrown | Sparse conifer over bog; the evergreen bog tree.
- `rowan` | Rowan | rowan_tree (new) | soil 1, rock .4 | .60/.28 | .60/.32 | .55/.32 | hardMin .28 | 2.80 | .95 | hollowcrown + reproduction berry/animal | Pinnate leaves and scarlet berry clusters; the wood-edge fruiting tree.

## Preset membership
Preset agent adds each new species (with a count) to the presets listed here. Only add where listed. (Coverage mosses/lichens: counts 5–7; trees/shrubs: 1–3; everything else: 2–5.)

- feathermoss: default, misty_cloud_forest, woodland_vernal_pools, creek, bayou_marsh
- rustsphagnum: peat_fen, permanent_marsh, bayou_marsh, alpine_tundra
- threadmoss: default, misty_cloud_forest, creek, deep_inlet, woodland_vernal_pools
- silkmoss: creek, braided_springs, shoreline, oxbow_creek, default
- rockcap_moss: rocky_rise, steep_rocky_slope, arid_canyon, alpine_tundra, cascading_falls
- pinemoss: alpine_tundra, rocky_rise, steep_rocky_slope, misty_cloud_forest
- ribbon_liverwort: deep_inlet, cascading_falls, creek, misty_cloud_forest, default
- beardlace_lichen: misty_cloud_forest, alpine_tundra, deep_inlet, steep_rocky_slope
- sunburst_lichen: arid_canyon, rocky_rise, steep_rocky_slope, alpine_tundra, shoreline
- porcelain_lichen: misty_cloud_forest, deep_inlet, woodland_vernal_pools, default
- reindeer_lichen: alpine_tundra, rocky_rise, steep_rocky_slope, arid_canyon
- haircap_moss: default, woodland_vernal_pools, misty_cloud_forest, creek, braided_springs
- puffball: default, woodland_vernal_pools, misty_cloud_forest, creek
- earthstar: arid_canyon, rocky_rise, steep_rocky_slope, alpine_tundra
- stinkhorn: default, bayou_marsh, woodland_vernal_pools, creek
- birdsnest_fungus: misty_cloud_forest, deep_inlet, creek, default
- coral_fungus: default, misty_cloud_forest, woodland_vernal_pools, bayou_marsh
- morel: woodland_vernal_pools, default, misty_cloud_forest, creek
- boletus: misty_cloud_forest, default, deep_inlet, alpine_tundra
- ladyfern: default, misty_cloud_forest, woodland_vernal_pools, creek, oxbow_creek
- royalfern: bayou_marsh, permanent_marsh, oxbow_creek, peat_fen, creek
- brackenfern: steep_rocky_slope, rocky_rise, arid_canyon, alpine_tundra
- hartstonguefern: deep_inlet, cascading_falls, rocky_rise, misty_cloud_forest
- sedgefountain: default, shoreline, creek, oxbow_creek, winding_pools, braided_springs
- cottongrass: peat_fen, permanent_marsh, alpine_tundra, bayou_marsh
- woodrush: default, misty_cloud_forest, woodland_vernal_pools, alpine_tundra
- reedgrass: permanent_marsh, bayou_marsh, estuary_delta, oxbow_creek, peat_fen
- sheepsfescue: alpine_tundra, rocky_rise, steep_rocky_slope, arid_canyon, shoreline
- horsetail: default, creek, braided_springs, bayou_marsh, winding_pools
- woodanemone: default, misty_cloud_forest, woodland_vernal_pools, deep_inlet
- buttercup: default, creek, woodland_vernal_pools, winding_pools, braided_springs
- wildstrawberry: default, rocky_rise, woodland_vernal_pools, steep_rocky_slope
- marshviolet: peat_fen, permanent_marsh, woodland_vernal_pools, bayou_marsh, oxbow_creek
- woodsorrel: misty_cloud_forest, alpine_tundra, woodland_vernal_pools, default
- nettle: default, bayou_marsh, estuary_delta, creek
- burdock: default, estuary_delta, bayou_marsh, winding_pools
- cowparsley: default, estuary_delta, braided_springs, creek, winding_pools
- meadowsweet: permanent_marsh, bayou_marsh, oxbow_creek, shoreline, braided_springs
- marshmarigold: woodland_vernal_pools, peat_fen, oxbow_creek, braided_springs, permanent_marsh
- loosestrife: permanent_marsh, bayou_marsh, estuary_delta, oxbow_creek
- stonecrop: rocky_rise, steep_rocky_slope, arid_canyon, alpine_tundra, shoreline
- houseleek: steep_rocky_slope, rocky_rise, cascading_falls, alpine_tundra
- coontail: isolated_pond, deep_inlet, winding_pools, estuary_delta
- bladderwort: isolated_pond, peat_fen, permanent_marsh, winding_pools
- waterstarwort: isolated_pond, winding_pools, oxbow_creek, bayou_marsh
- bogbean: peat_fen, permanent_marsh, oxbow_creek, bayou_marsh
- burreed: shoreline, estuary_delta, permanent_marsh, oxbow_creek, braided_springs
- waterplantain: shoreline, oxbow_creek, winding_pools, estuary_delta
- arrowhead: shoreline, oxbow_creek, braided_springs, permanent_marsh
- ivylace: default, misty_cloud_forest, deep_inlet, creek
- hop: default, creek, bayou_marsh, woodland_vernal_pools
- honeysuckle: default, woodland_vernal_pools, misty_cloud_forest, rocky_rise
- bogmyrtle: peat_fen, permanent_marsh, bayou_marsh, alpine_tundra
- hazel: default, woodland_vernal_pools, misty_cloud_forest, creek
- hawthorn: default, rocky_rise, winding_pools, steep_rocky_slope, braided_springs
- juniper: arid_canyon, rocky_rise, steep_rocky_slope, alpine_tundra
- woollywillow: alpine_tundra, peat_fen, braided_springs
- silverbirch: default, rocky_rise, alpine_tundra, misty_cloud_forest, steep_rocky_slope
- quakingaspen: default, creek, braided_springs, woodland_vernal_pools
- alder: creek, bayou_marsh, oxbow_creek, estuary_delta, braided_springs, permanent_marsh
- waterwillow: shoreline, creek, winding_pools, oxbow_creek, braided_springs
- mirepine: peat_fen, alpine_tundra, permanent_marsh
- rowan: default, misty_cloud_forest, alpine_tundra, deep_inlet

## New interactions (flora_interactions.json)
Add ~14 relations using the required `reason` field, e.g.: feathermoss benefits from shadebell/hazel (deep shade keeps it moist); nettle competes with cowparsley (same rich-soil niche); reedgrass competes with loosestrife; brackenfern competes with hawthorn seedlings; cottongrass benefits from rustsphagnum; beardlace_lichen refuses near nettle? (reason). Keep `strength` 0.6–1.6, `bonus` 0.05–0.2, radii 0.3–0.8, and never duplicate an (a,b) pair.

## Agent waves
1. **Round 1** — content JSON (7 agents), species pool C# (1), CoverageRenderer structures (1), FloraSurfaceProfiles optics (1), presets (1), interactions (1).
2. **Round 2** — 11 mesh-builder agents (new partial files only).
3. **Round 3** — wiring agent (OrganismMeshes dispatch + OrganismRenderers shape classification + FloraGeometryPlacement morphs + FloraVisualProfile family tweaks) then validation agent (harness builds every species at 3 LOD tiers from loaded content; boot test).
4. **Round 4** — I run `--boot-test` and `--visual-review` and direct fixes.
