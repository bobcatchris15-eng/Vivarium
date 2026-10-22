# Fictional Ecology Conversion Plan

## Goal
Replace the Vivarium's recognizable real-world species identities with fictional species that retain the same ecological jobs, while expanding the newly added woody layer into a small structural ecosystem. Ecological behavior remains grounded and legible; morphology and identity become invented. Save compatibility is explicitly out of scope.

## Design rules
- Preserve niche first: moisture, substrate, light, nutrient demand, growth rate, carrying limits, food-web role and broad life history remain recognizable from the current simulation.
- Do not make a renamed real species. Fictional organisms should combine visual traits from multiple real organisms or introduce a clearly invented trait while still reading as living things.
- Keep common names short and naturalistic rather than high-fantasy.
- Woody plants remain structurally scarce: trees share the existing approximately 1 per 15 m² island-wide carrying budget; shrubs use the looser shrub budget.
- Climbers are not proximity-gated decorations. They establish on suitable ground, grow horizontally as connected stem networks, search a wide neighborhood for structure, physically reach it, attach, then transition to vertical growth.
- Break old saves rather than carrying legacy taxonomy or aliases.

## Target roster

### Mosses and lichens
- Velvetweave moss — carpet-moss niche: humid mat former.
- Pearl-cushion moss — cushion-moss niche: compact mineral-surface hummocks.
- Floodlace moss — wet-bank niche: saturated margins.
- Bogglass moss — bog mat niche: saturated acidic ground.
- Embercrust lichen — crustose pioneer.
- Ruffle lichen — foliose lichen.
- Antlerlace lichen — dry fruticose lichen.

### Low vascular plants
- Coinrunner — rich-soil creeping groundcover.
- Mooncoin — dry low mat groundcover.
- Trifold — moderate dry meadow groundcover.
- Glassfinger — drought-tolerant succulent groundcover.
- Sunstone rosette — rock/dry succulent niche.
- Frosttussock — dry tufted grasslike plant.
- Prismstar — small bright-ground flowering herb.
- Veilfern — humid shade understory.
- Glassrush — emergent saturated-margin plant.
- Brooklace — shallow-water runner.
- Fenbead — soft-stem aquatic-margin plant.
- Mirrorleaf — rooted floating leaf plant.

### Decomposers
- Dewbonnet fungus — ephemeral litter mushroom.
- Emberfan fungus — bracket wood decomposer.
- Ambervein plasmodium — mobile network decomposer.

### Woody structure
- Ironlace — fast open-canopy dry pioneer tree.
- Umbraheart — fertile moist broad-crowned deep-shade tree.
- Fenneedle — wet-ground narrow-crowned soft-needle tree.
- Kiteleaf — extremely fast riparian pioneer with broad dispersal.
- Embercrown — dry disturbed clonal shrub.
- Lanternbrush — wet-margin arching shrub.
- Shadebell — canopy-dependent understory shrub.

### Climbers
- Clinglace — slow shade-tolerant adhesive climber; long-lived ground search.
- Spiralvine — fast sun-seeking twiner; strongest bias toward tall woody supports.
- Fenhook — wet-ground scrambler; favors shrubs and low woody structure.

### Fauna
- Prismhopper — springtail niche.
- Marbleback — pill-bug/isopod niche.
- Ghostbristle — silverfish/bristletail niche.
- Coalback beetle — darkling-detritivore niche.
- Emberglass swimmer — small aquatic grazer equivalent to shrimp niche.
- Siltshield — temporary-water benthic omnivore equivalent to triops niche.
- Glintfin — schooling plankton-feeding microminnow niche.

## Climber simulation
A climber species owns a `climber` content block:
- `searchRadius`: support-sensing neighborhood. This biases growth; it is not teleportation.
- `groundSpeedPerDay`: stem extension production, measured along the stem.
- `segmentLength`: distance between persistent stem nodes.
- `attachmentRadius`: physical contact distance for support capture.
- `maxUnsupportedLength`: practical search limit before a tip exhausts.
- `verticalGrowthMultiplier`: visual/growth advantage after attachment.
- `nodeCap`: per-plant persistent stem-node budget so branching networks remain bounded.
- `supportTypes`: any of `woody`, `log`, `rock`.
- `climbWeight`, `driftWeight`, `lightWeight`: the weights the surface-bound growth law uses to
  choose a heading inside the surface's tangent plane. `driftWeight` is the twining term.
- `wanderAmp`, `wanderPeriod`: circumnutation: how far and over what wavelength the shoot's
  heading oscillates inside the surface.
- `branchRate`, `branchAngle`, `lateralSpeed`, `lateralClimb`, `maxBranchOrder`, `branchLength`:
  how side shoots leave the stem, how fast they run, and how long they may be.
- `baseRadius`, `stemTaper`, `tipRadius`: the stem's thickness at the base of the plant (which is also the
  cap), its per-metre falloff from base to tip, and the thinnest the stem may get.
- `grabRadius`: how close a shoot must come to a host before it takes hold.
- `seekRate`: how eagerly a searching shoot turns toward the nearest host it can sense.

Growth is surface-bound: a shoot grows from its tip, lays stem down on whatever surface it is
touching, and the surface's geometry decides where the stem ends up. The species supplies only
weights; the host supplies the frame. Because the shoot's lateral reference is parallel-
transported along the surface, the same law winds a vine around a round trunk and runs it
straight up a flat wall, because a wall gives it nothing to wind around. See
`docs/overhaul/surface-bound-vine-growth.md` for the derivation.

Each founder begins as a ground tip. Tips accumulate extension credit and extend only after
physically covering segmentLength. Without a support they persist in exploratory growth with
directional inertia, jitter and circumnutation. A shoot takes hold when the host's surface
comes within grabRadius of where it is actually growing; it never teleports onto a support.
Mature attached nodes resume ordinary propagule production, so a successful climb is not a
reproductive dead end. Branching produces lateral shoots that leave the parent at the branch
angle on any surface. Unsupported length is tracked along the lineage and each species has a
node budget so runners cannot become immortal spaghetti or consume the world entity budget.

Woody individuals are valid supports based on their actual simulated positions. Logs and rocks remain supports. Rendering draws the persistent ground stems between parent and child nodes as one tapering stem; attached nodes stretch/leaf upward against the selected support.

## Structural succession target
Bare/open ground -> low pioneers -> shrubs/trees -> canopy shade -> shade shrub and climbers -> mixed mature vertical structure. Disturbance or woody death can reverse this progression.

## Implementation campaigns
1. Identity conversion: fictional IDs, names, descriptions and documentation; preserve ecological parameter sets.
2. Woody expansion: add Lanternbrush and Shadebell with distinct moisture/light niches and procedural shrub forms.
3. Climber system: add ClimberDef, persistent tip/attachment state, support sensing, horizontal stem growth, woody support detection and ground-stem rendering.
4. Climber expansion: convert Clinglace and add Spiralvine/Fenhook with genuinely different search rates, tolerances and support preferences.
5. Visual fiction pass: rename recognizable woody mesh archetypes and alter silhouettes so they are not direct copies of locust/catalpa/tamarack/cottonwood/sumac.
6. Validation: content cross-reference tests, 32 flora / 7 fauna counts, woody budgets, climber ground-search test, climber woody-attachment test, rendering mesh coverage, full simulation test suite and Godot C# build.

## Acceptance criteria
- Catalog exposes 39 fictional organisms: 32 flora and 7 fauna.
- No active content uses the old real-species IDs or display names.
- Three climber species can establish without a support immediately adjacent.
- A searching climber advances horizontally over multiple nodes toward woody/log/rock structure within its configured search radius.
- Climbers do not jump directly to supports and cannot extend indefinitely without finding one.
- Attached climbers visibly transition from prostrate ground stems to vertical growth.
- Trees and shrubs remain subject to separate shared structural budgets.
- Lanternbrush occupies wet bright margins; Shadebell is favored beneath partial woody shade.
- Existing ecological simulation, determinism checks, content validation and renderer build remain green.
