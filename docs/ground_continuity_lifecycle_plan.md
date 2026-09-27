# Ground continuity, plant lifecycle, litter, fruiting, and decomposition plan

## Purpose

The Vivarium currently has a large flora catalog, but too much of the island still reads as clean exposed ground. The problem is not simply species count. Most vascular flora are discrete specimens, moss and lichen begin from a few small coverage patches, dead plants disappear immediately into abstract detritus, and the existing SoilDetailRenderer invents leaves and twigs for visual texture rather than rendering accumulated ecological history.

This campaign should make the ground remember what has happened on it.

The desired mature scene is not “plants everywhere.” It is a layered surface assembled from living vascular cover, moss/lichen, dead standing vegetation, fallen plant material, fruit and seed debris, humus, and genuinely exposed substrate. Clean mineral soil should be common after disturbance or in inherently sparse habitats, not the default negative space between specimen plants.

The systems in this plan should be implemented as one connected material/lifecycle pipeline:

living plant
→ senescence
→ standing dead plant
→ collapsed plant / coarse litter
→ fine litter / fruit debris
→ bioavailable detritus
→ decomposition
→ soil nutrients
→ new plant growth

Reproduction should join the same physical pipeline:

living mature plant
→ flower / fruit / cone / pod
→ attached reproductive load
→ dispersal or drop
→ animal consumption, seed bank, germination, or decay
→ litter / detritus / nutrients

The result should create visible accumulated time. A plant that lived in a patch should leave a trace after it dies; a fruiting shrub should temporarily change the ground beneath it; a wet shaded hollow should process litter differently from an exposed dry rise; and a mature island should retain a legible history even when no event is currently happening.

---

# 1. Design rules

## 1.1 Ground state is composited, not binary

Do not model a location as either “plant” or “bare ground.”

At any point the visible surface may contain overlapping contributions from:

- exposed soil, gravel, rock, wood, or water,
- vascular groundcover,
- moss or lichen coverage,
- loose fine litter,
- coarse stems, twigs, bark, cones, pods, or deadwood,
- fruit and seed material,
- humified dark organic matter,
- upright or collapsed dead vegetation.

The renderer should blend these contributions by scale. Broad coverage belongs in fields/textures; close-up recognizable pieces belong in deterministic procedural instances.

## 1.2 Matter should not disappear when organisms die

Biological death ends participation in living growth/reproduction, but it does not immediately remove geometry or biomass.

A dead plant should pass through an explicit post-life state appropriate to its form. Small herbs can wilt and collapse quickly. Grasslike plants can remain straw-colored. Ferns can flatten into the litter. Shrubs can persist as dead stems. Trees can become snags and later fallen woody structure.

The explicit corpse can disappear only after most of its mass has moved into the ordinary litter/detritus pathway and the remaining geometry has visually converged with its surroundings.

## 1.3 Background decomposition must always exist

Visible decomposers must matter, but they must not be mandatory for basic material cycling.

Treat bacteria and diffuse mycelium as an implicit background decomposer community. In the absence of an explicit fungus, slime mold, detritivore, or future specialist, organic material still decomposes according to a slower environment-dependent baseline.

Explicit decomposers act as local acceleration and redistribution, not as the sole mechanism preventing an immortal litter pile.

## 1.4 Fruit is ecological matter, not decoration

Fruit, seeds, cones, pods, and similar reproductive structures should be tied to actual reproductive state and material accounting.

Attached fruit load can be aggregated per plant and rendered procedurally. Dropped material can be aggregated into ground patches or litter fields except when a large individual object is ecologically useful. Consumption, decay, dispersal, and germination should operate on the same authoritative state that drives the visuals.

## 1.5 Use explicit objects only where identity matters

Do not create tens of thousands of authoritative leaves or berries.

Use explicit entities for:
- living plants and fauna,
- persistent dead plants whose shape/history matters,
- large fallen woody structures where geometry and support/collision matter,
- unusually large fruit or event objects when useful.

Use fields or sparse patches for:
- fine leaf/needle litter,
- humus,
- ordinary fallen fruit mass,
- seeds in the seed bank,
- background microbial decomposition.

## 1.6 Determinism remains a hard constraint

Collapse headings, fruit placement, seed dispersal, visual scatter, decay variation, and groundcover frontier noise must come from named deterministic RNG streams or stable hashes.

A save/load round trip must not change the visible or ecological history of the island.

## 1.7 Break the save schema rather than carrying compatibility scaffolding

This is still an active overhaul. New authoritative corpse, litter, reproductive, and seed-bank state should receive an explicit schema bump. Do not distort the design to deserialize obsolete worlds.

---

# 2. Current-state observations that motivate the work

Current main already contains useful pieces:

- The environment grid has moisture, nutrients, detritus, light, and substrate.
- The 2 cm sparse CoverageWorld supports true spatial moss and lichen growth.
- FloraSystem already produces litter continuously through shedding and sends killed flora through ReturnOrganicMatter.
- EcologySystem already converts the detritus field into nutrients.
- Decomposer flora already consume detritus explicitly.
- SoilDetailRenderer already scatters close-up flakes and twigs and biases deposition toward hollows/shelter.
- FloraRenderer already supports per-instance tint/custom data and rebuilds from authoritative state.
- Trees and shrubs already produce shared canopy shade.
- Existing “groundcover” species Coinrunner, Mooncoin, and Trifold are still ordinary FloraIndividuals with small radial footprints rather than spatial carpets.

The main architectural change is therefore not replacement of everything. It is inserting physical surface organic matter and post-life state between living organisms and the already-existing detritus/nutrient cycle, then teaching existing renderers to read that real history.

---

# 3. System overview

Introduce four related authoritative subsystems:

1. **LitterSystem**
   - stores visible surface organic matter and local litter appearance/composition,
   - accepts shedding, corpses, fruit debris, branch material, and future event deposits,
   - transfers decomposed material into the existing detritus field.

2. **DeadFloraSystem**
   - owns plants after biological death,
   - preserves geometry through senescence/standing/collapse stages,
   - gradually transfers corpse biomass into litter,
   - removes the explicit corpse only at advanced decay.

3. **ReproductionSystem**
   - tracks reproductive reserve/load for vascular plants,
   - produces attached fruit/seed structures,
   - handles drop and dispersal,
   - feeds a sparse SeedBank and/or ground reproductive material.

4. **VascularGroundcoverSystem**
   - gives genuinely carpet-forming/rhizomatous vascular plants a spatial growth representation,
   - fills the ecological middle between moss and discrete specimen plants,
   - supplies the continuous living layer currently missing from ordinary soil.

These should all use the existing environment fields and content definitions rather than creating parallel habitat logic.

---

# 4. Litter and surface-organic layer

## 4.1 Separate visible litter from bioavailable detritus

Keep the existing Detritus field as the broadly bioavailable organic resource consumed by decomposers and eventually mineralized into nutrients.

Add a new physical litter reservoir above it.

Recommended first-pass environment-grid state per cell:

- FineMass — leaves, needles, tiny stems, collapsed herbs, soft fruit residue.
- CoarseMass — twigs, bark, woody stems, cone/pod husks, fragmented deadwood.
- FruitMass — short-lived high-energy reproductive material; can eventually fold into FineMass if separate tracking proves unnecessary.
- Humification — 0..1 maturity of the local litter bed.
- SourceColor accumulator — weighted RGB plus weight, representing the average recent plant material entering the patch.
- Optional composition fractions — broadleaf / needle / grass-stem / wood / fruit. Keep these compact; they are primarily useful to choose render motifs and decay rates.

Do not duplicate mass between litter and Detritus. A unit of biomass exists in one reservoir at a time.

Preferred flow:

corpse / shedding / fruit
→ visible litter
→ microbial breakdown
→ Detritus
→ existing decomposer consumption and/or EcologySystem mineralization
→ nutrients

## 4.2 Deposition

All organic inputs should go through a single API such as:

- AddFineLitter(position, mass, sourceProfile)
- AddCoarseLitter(position, mass, sourceProfile)
- AddFruitLitter(position, mass, sourceProfile)
- AddDeadwood(position, mass, sourceProfile)

The API should distribute material over nearby grid cells according to the source.

Examples:

- a small herb mostly deposits in its own cell and immediate neighbours,
- a shrub sheds under its crown,
- a tree sheds over a broader crown footprint,
- a collapsed plant leaves a directional streak,
- a Pilot Tree event can deposit from outside the patch,
- a steep-slope archetype can later bias loose fine material downhill.

## 4.3 Environment-dependent litter appearance

Litter color should be derived, not hard-coded to one brown.

Compute a local target appearance from:

- weighted source color,
- moisture,
- effective light / exposure,
- humification,
- fine/coarse composition,
- optional substrate contribution when cover is thin.

A useful conceptual progression:

fresh species material
→ oxidized/dried species material
→ dark or bleached environment-conditioned litter
→ local humus color

Wet/shaded litter should trend darker, cooler, and more saturated. Dry/exposed litter should trend paler, straw-grey, and more desaturated. Highly humified material should converge toward dark soil-organic colors.

The exact color function belongs in one shared simulation/render helper so dead plants and the litter renderer converge toward the same target.

## 4.4 Litter rendering

Refactor SoilDetailRenderer rather than replacing it.

Near camera:
- continue using deterministic MultiMesh flakes, twigs, crumbs, and clods,
- drive leaf/twig density from authoritative FineMass/CoarseMass rather than random decorative abundance,
- choose motifs from litter composition,
- tint from local litter target color,
- include occasional cones, pods, seeds, fruit remnants, and dead blades where composition calls for them.

Mid/far distance:
- provide the terrain shader with a litter coverage / humification texture,
- blend a low-frequency litter material over the soil so the ground remains visually covered after close-detail geometry culls,
- avoid a hard cell lattice by filtering/noise/jitter in shader space.

The existing shelter/hollow calculations in SoilDetailRenderer remain useful as deposition/retention modifiers, but should no longer manufacture biomass. They determine where deposited material tends to collect or how it is visually arranged.

## 4.5 Litter retention and movement

First pass:
- hollows and prop shelter retain more loose material,
- steep slopes retain less,
- submerged cells should rapidly export or transform ordinary terrestrial litter.

Later:
- slope creep,
- runoff transport,
- stream drift,
- wind redistribution,
- event-specific deposits.

Do not block the initial campaign on full transport physics.

---

# 5. Plant death and corpse persistence

## 5.1 Replace instant kill/remove with biological death transition

FloraSystem currently removes a failed/senescent FloraIndividual and immediately returns organic matter.

Change the biological-death path:

1. living FloraIndividual stops living physiology,
2. living population/tally records the death,
3. a DeadPlant record is created using the same species identity and preferably the same entity id,
4. the dead plant enters a post-life progression,
5. its biomass transfers gradually into LitterSystem,
6. the explicit dead object is finally removed when little meaningful structure remains.

A separate DeadPlantPopulation is preferable to leaving dead plants in FloraPopulation because dead plants must not:
- consume nutrients,
- reproduce,
- count as living population,
- participate in ordinary living competition,
- consume tree/shrub carrying-cap slots indefinitely.

However, dead woody plants may still:
- occupy visible/physical space,
- act as climber support,
- cast a small structural shadow if warranted,
- create bark/deadwood habitat,
- later become fallen log structure.

## 5.2 DeadPlant state

Suggested authoritative fields:

- Id
- SpeciesId
- position
- original biomass / remaining structural biomass
- death biological time
- death cause
- Stage
- stage progress
- structural integrity
- standing fraction / lean fraction
- collapse heading
- local decomposition multiplier
- initial dead tint variation
- source material profile

Suggested stages:

### Senescent
Use when death is gradual rather than catastrophic.
- chlorophyll drains,
- health/growth already zero,
- geometry remains mostly alive in pose,
- leaves/fronds begin to dull.

This stage can either remain on FloraIndividual immediately before death or be the first DeadPlant stage. Prefer whichever produces the cleanest physiology split.

### StandingDead
- plant is biologically dead,
- original silhouette remains,
- tint moves toward species-specific dead color,
- some fine biomass sheds continuously,
- leaf-bearing species can lose leaf fraction,
- grasslike plants become straw,
- shrubs become progressively twig-dominant,
- trees become snags.

### Collapsing
- deterministic lean/fold progression,
- herbaceous plants flatten,
- fern fronds sag,
- shrubs partially lodge,
- trunks/large stems fall along a stable hashed heading modified by local slope if desired.

No rigid-body simulation is required. A deterministic animated transform is sufficient and much easier to persist.

### Fallen
- geometry lies on/near the surface,
- fine material continues transferring to litter,
- woody material remains comparatively persistent,
- large trees may promote their trunk/major stem into a persistent deadwood/log representation.

### AdvancedDecay
- remaining explicit geometry loses contrast,
- tint increasingly samples local litter target color,
- scale/volume can shrink or fragment,
- structural biomass approaches the despawn threshold.

### Assimilated
- no explicit corpse remains,
- all remaining biomass is deposited into litter/detritus,
- only ordinary ground-state history remains.

## 5.3 Species-dependent post-life behavior

Add a content block for post-life behavior rather than one global timer.

Possible fields:

- senescentDays
- standingDeadDays
- collapseDays
- fallenPersistenceDays
- fineFraction
- coarseFraction
- woodyFraction
- standingRetention
- collapseStyle: wilt | lodge | frond_fold | stem_tangle | snag | trunk_fall
- deadColor
- dryBleach
- wetDarken
- litterColorInfluence
- deadwoodPersistenceMultiplier

Examples:

- Prismstar: short senescence, collapses in days, mostly fine litter.
- Frosttussock: little structural collapse, remains standing straw for a long period.
- Veilfern: fronds sag and flatten rapidly.
- Coinrunner-like carpet: browns in place and enters fine litter with little explicit corpse geometry.
- Embercrown / Shadebell: dead twig framework persists.
- Ironlace / Umbraheart / Fenneedle / Kiteleaf: long standing-snag stage and large coarse-wood reservoir.

## 5.4 Corpse color convergence

Do not simply fade dead plants to generic brown.

Use:

corpseTint = mix(speciesDeadTint, localLitterTint, environmentAndDecayBlend)

The local litter contribution should be low when newly dead and strong late in decay.

Moisture and exposure should modify both sides:
- wet shaded corpses darken and may gain moss/fungal tint later,
- exposed dry corpses bleach,
- material under heavy litter converges more quickly with the ground.

By the time explicit geometry despawns, it should already be visually difficult to distinguish from the litter bed it is becoming.

## 5.5 Woody collapse and logs

For ordinary trees:
- biological death creates a snag,
- snag persists for a species-configured interval,
- eventual collapse creates a deterministic trunk orientation,
- if the remaining coarse biomass exceeds a threshold, instantiate a deadwood/log structure using the tree’s position, height, and trunk scale,
- transfer canopy/leaf material separately into fine litter.

This gives tree death long visual memory and creates new substrate/support opportunities instead of silently erasing major structure.

Pilot Tree mortality is out of scope; Pilot Tree branch/fruit/litter events should feed the same material APIs.

---

# 6. Decomposition

## 6.1 Background microbial decomposition

LitterSystem should process every occupied litter cell with a baseline microbial rate.

Conceptual rate:

rate =
base material decay
× moisture response
× temperature proxy if one later exists
× exposure/light response
× material digestibility
× humification response
× explicit decomposer bonus

No visible decomposer is required.

Suggested material ordering:
- fleshy fruit: fastest,
- soft green leaves/herbs: fast,
- ordinary broadleaf litter: moderate,
- grass stems / tough leaves: slower,
- needles/cones/pods: slow,
- bark/coarse wood: slowest.

## 6.2 Explicit decomposer bonus

Existing fungi and slime molds should accelerate nearby breakdown where appropriate.

Do not simply multiply all decay because a decomposer exists somewhere nearby. Sample local abundance/activity.

Possible first pass:
- Dewbonnet accelerates FineMass and FruitMass conversion locally.
- Emberfan accelerates CoarseMass / deadwood conversion.
- Ambervein benefits from the Detritus produced by active litter breakdown and can additionally accelerate fine organic conversion near its network.

This preserves their ecological jobs while keeping background decomposition robust.

## 6.3 Detritivores

Existing fauna that consume detritus can continue consuming the Detritus field.

Later refinement can let large detritivores directly fragment coarse/fine litter, increasing the transfer rate into Detritus. This is useful but should not block the core campaign.

## 6.4 Mass accounting

Add explicit diagnostic tallies for:
- biomass entering fine litter,
- biomass entering coarse litter,
- fruit biomass dropped,
- litter converted to detritus,
- coarse wood fragmented,
- microbial mineralization,
- decomposer-accelerated processing.

Tests should be able to account for organic mass across:
living flora + dead flora + litter + detritus + nutrient returns

within defined losses/respiration.

---

# 7. Fruiting, seeds, and reproductive material

## 7.1 Plant reproductive state

Do not overload the current FloraIndividual.Fruiting flag used by fungus/slime behavior.

Add vascular reproductive state, either on FloraIndividual or a dedicated per-plant record:

- reproductive reserve,
- phenology state,
- attached reproductive load,
- last reproductive pulse,
- optional stress/abortion fraction.

Suggested states:
- Dormant
- Budding
- Flowering
- Developing
- Ripe
- Releasing
- Spent

The simulation does not need Earth calendar seasons. Trigger reproductive pulses from maturity plus accumulated environmental/resource conditions.

Useful inputs:
- minimum maturity,
- recent light sufficiency,
- recent moisture sufficiency,
- nutrient/biomass reserve,
- species cooldown,
- deterministic individual phase offset.

This naturally produces pulses instead of perpetual fruit.

## 7.2 Content-driven reproductive structures

Add a reproduction block to species that use the system.

Possible fields:

- form: berry | drupe | nut | pod | cone | capsule | wind_seed | achene
- maturityAge
- reserveRate
- pulseThreshold
- maxAttachedMass
- developmentDays
- ripeDays
- dropRate
- fruitColor / ripeColor / spentColor
- displaySize
- fleshFraction
- seedFraction
- seedsPerMass
- viability
- dormancyDays
- persistenceDays
- dispersal: gravity | wind | animal | water | ballistic
- dispersalRadius / windBias
- litterMaterialProfile
- faunaFoodValue

The first pass does not need every field if a simpler schema can express the same behaviors. Keep it content driven.

## 7.3 Initial woody reproductive identities

Every tree and most shrubs should have a distinct reproductive structure so the woody guild is visibly and ecologically different.

Provisional assignments:

- Ironlace — dry hanging pods; split after ripening and scatter seeds.
- Umbraheart — heavy fleshy or nutlike fruit; most drops under/near crown; high local food pulse.
- Fenneedle — persistent cone or berry-cone structures; slower release.
- Kiteleaf — abundant lightweight windborne seed; widest ordinary dispersal.
- Embercrown — conspicuous clustered berries or dry fruit; strong shrub-level food resource.
- Lanternbrush — wet-margin fruit/seed heads that drop into saturated ground and can eventually support water-biased dispersal.
- Shadebell — shade-shrub berries, favoring animal movement beneath woody canopy.

Final morphology can remain fictional; the important requirement is different material/dispersal behavior.

Groundcover/herb species may also fruit later, but trees and most shrubs are the first mandatory roster.

## 7.4 Attached fruit rendering

Attached fruit should not require one simulation entity per fruit.

Render a deterministic number of fruit/cone/pod instances from:
- current attached reproductive load,
- plant size,
- individual hash,
- species form.

Use MultiMesh and stable attachment positions generated from the procedural plant form or a repeatable approximate crown/stem distribution.

The renderer must show:
- developing vs ripe tint,
- changing abundance,
- disappearance as material releases.

## 7.5 Dropped fruit

On release, split material among:
- ground FruitMass,
- immediate seed-bank deposit,
- wind/water dispersal,
- fauna-consumable resource.

Ordinary small fruit should become a patch/field contribution, with close-up procedural fruit instances generated from FruitMass.

Large fruit may be represented as short-lived explicit objects only if their scale makes interaction/visibility worthwhile.

Fruit should visibly rot:
fresh colored specks
→ bruised/darkened fruit
→ moldy/collapsed material
→ ordinary fine litter/humus

## 7.6 Seed bank

Add a sparse seed-bank representation keyed by environment-grid cell and species id.

Track only cells/species with nonzero viable seed.

Minimum state:
- species id,
- viable amount/count,
- age or viability fraction,
- dormancy remaining.

Germination checks should use the existing FloraSystem suitability rules or the new vascular-groundcover suitability path.

Seed bank behavior:
- seeds can wait through unsuitable periods,
- viability decays,
- disturbance can later become a germination trigger,
- successful germination consumes seed-bank material,
- not every seed becomes an explicit simulation attempt.

This replaces much of the current arbitrary “two propagules every N days” behavior for woody species once mature.

## 7.7 Dispersal

Initial mechanisms:

### Gravity
Deposit mostly beneath/near the crown.

### Wind
Create a wider deterministic distribution with a species-specific distance tail. A full wind simulation is not required initially; a stable world/event wind direction can be added later.

### Animal
Fauna can consume suitable fruit and receive a small carried-seed payload:
- source species,
- viable amount,
- digestion/retention timer.

When the payload is passed, deposit seed-bank material at the fauna’s current location and add waste/organic matter normally.

The first integration can support a generic fruit-eating behavior/resource rather than species-specific digestive modeling.

### Water
Defer unless a particular wet-margin species needs it immediately. The architecture should leave room for seeds/fruit entering water and moving through hydrology.

## 7.8 Pilot Tree integration

The Pilot Tree is not an ordinary FloraIndividual, but its fruit/cone fall events should call the same litter/fruit/seed APIs.

A heavy Pilot Tree fruiting pulse can therefore:
- change visible ground color,
- add food,
- attract itinerants/resident fauna,
- increase decomposer activity,
- add seed material,
- later enrich litter/nutrients.

This makes Pilot Tree fruit fall an ecological event rather than a particle effect.

---

# 8. Vascular groundcover system

## 8.1 Why the current individual model is insufficient

Coinrunner, Mooncoin, and Trifold are tagged groundcover but remain ordinary individuals. Their small circular radii and point-propagation behavior mean the sim can contain many “groundcover plants” while still rendering large amounts of naked soil between them.

The existing 2 cm coverage system demonstrates the appropriate general pattern: spatial organisms should own area.

Do not solve the problem solely by raising starter counts. That increases entities without producing the continuous sward/mat behavior wanted.

## 8.2 Add a vascular carpet/rhizome coverage mode

Preferred direction: extend CoverageWorld with a distinct vascular-groundcover layer or a closely related sparse-tile system.

Do not force vascular plants onto the moss Mat layer if doing so makes it impossible to represent vertical blades/leaves separately.

A useful split is:

- coverage state represents occupancy/biomass/rooted carpet at 2–5 cm spatial resolution,
- renderer derives repeated blades/leaves/rosettes from local occupied biomass,
- ecological competition/light operate on area coverage rather than thousands of individual plants.

Suggested state per occupied cell:
- species occupant,
- biomass/density,
- age/maturity proxy,
- stress/dormancy,
- local height/density,
- optional reproductive readiness.

Front growth should:
- expand into suitable adjacent cells,
- use species lateral rate,
- include deterministic directional noise,
- be slowed by crowding/competing vascular cover,
- respond to moisture/light/nutrients,
- die back spatially when habitat deteriorates.

Seed-bank germination should be able to found new patches.

## 8.3 Initial missing groundcover guilds

Names are provisional; ecological jobs are the requirement.

### A. Mesic rhizomatous graminoid — “Silkneedle”

This is the largest missing matrix species.

Role:
- ordinary middle-moisture soil filler,
- narrow grass/sedge-like blades,
- forms irregular swards rather than tussocks,
- common in sun through partial shade.

Target niche:
- soil strongly preferred,
- moisture roughly 0.35–0.75,
- moderate nutrient tolerance,
- broad light tolerance,
- medium-fast lateral rhizome growth,
- 8–18 cm rendered height.

It should be visually boring enough to serve as background vegetation.

### B. Deep-shade broadleaf carpet — “Gloamleaf”

Role:
- low overlapping leaves beneath trees/shrubs,
- fills the gap between moss and Veilfern,
- strongest in humid partial/deep shade.

Target niche:
- moisture roughly 0.5–0.85,
- optimum effective light around 0.2–0.3,
- 4–8 cm height,
- slower than the mesic graminoid,
- tolerates litter better than open-ground pioneers.

### C. Ruderal gap filler

Role:
- ecological equivalent of chickweed/speedwell/bittercress-type opportunists,
- specializes in recent disturbance and empty soil,
- very fast establishment, short life, high seed production,
- weak competitor once longer-lived cover closes.

This is the plant whose key adaptation is “there is exposed dirt here.”

It should make disturbances visibly green over quickly without becoming the permanent mature matrix.

### D. Dry creeping mat

Role:
- prostrate thyme/sandwort/stonecrop-like carpet,
- occupies sunny dry soil and gravel between Glassfinger, Sunstone rosette, and Frosttussock,
- low biomass and low height but substantial plan-view area.

Target niche:
- dry/exposed,
- lean soil/gravel,
- drought resistant,
- slow-to-moderate lateral spread,
- poor performance under heavy litter/shade.

### E. Humid litter-floor creeper / clubmoss analogue

Role:
- 5–15 cm layer between true moss and Veilfern,
- creeps through humid litter, beside logs, and under open canopy,
- visually textural rather than broad-leaved.

This may overlap with Gloamleaf enough that one can be deferred after visual tests. Do not add species solely to hit a number.

## 8.4 Existing groundcover migration

Once the new mode works, evaluate migration of:
- Coinrunner,
- Mooncoin,
- Trifold,
- possibly Glassfinger if its visual form benefits from patch growth.

Coinrunner is the strongest candidate and should become the first migration/reference species.

Do not migrate all three simultaneously before the system proves itself.

## 8.5 Litter interactions

Vascular carpets should not simply overwrite litter.

Render and ecology should allow:
- litter visible through sparse blades,
- dense broadleaf carpet obscuring much of the litter,
- moss/litter coexisting visually,
- heavy litter slowing some tiny pioneer germination,
- humid litter benefiting shade creepers,
- disturbance exposing mineral soil and resetting local succession.

A simple litter-opacity / germination modifier is enough initially.

---

# 9. Rendering composition

The visual ground stack should be explicit.

From bottom to top:

1. terrain substrate material,
2. broad litter/humus shader blend,
3. moss/lichen coverage,
4. vascular groundcover procedural instances,
5. close-up litter fragments/fruit/twigs,
6. discrete living flora,
7. dead standing/fallen flora,
8. props/deadwood.

Exact draw order can differ where Godot transparency/depth requires it, but the conceptual ownership should remain clear.

## 9.1 Dead flora renderer

Prefer a separate DeadFloraRenderer or clearly separated dead buckets inside FloraRenderer.

Requirements:
- reuse species geometry where possible,
- support deterministic pose deformation by corpse stage,
- allow dead tint independent of living-health tint,
- allow local litter tint sampling,
- cull/LOD similarly to living flora,
- support tree snag/fallen transforms,
- avoid rebuilding unique meshes for every corpse.

## 9.2 Fruit renderer

Use one or a small number of meshes per reproductive form/species with MultiMesh instances.

Two contexts:
- attached fruit on living plants,
- ground fruit derived from FruitMass.

Ground fruit can share the SoilDetailRenderer scan/cull infrastructure.

## 9.3 Groundcover renderer

Do not draw one mesh per 2 cm cell.

Use coverage tiles to generate:
- clustered blade cards/tubes for graminoids,
- small leaf clusters for broadleaf carpets,
- sparse repeated low forms for dry mats,
- density proportional to local biomass.

Batch by species/tile/LOD and use the same aggressive distance logic already learned from moss/soil-detail work.

---

# 10. Ecological interactions and succession

The target structural sequence becomes:

fresh disturbance / exposed substrate
→ ruderal pioneers and crusts
→ vascular carpet / moss establishment
→ herbs, shrubs, woody recruits
→ canopy and accumulated litter
→ shade carpet / ferns / fungi
→ death, gaps, fallen wood, renewed disturbance

The important point is that “mature” no longer means every cell is filled with a living plant. Mature shaded ground can legitimately be litter-dominant with moss and scattered shade flora.

## 10.1 Litter effects

First-pass effects should be modest and understandable:

- small moisture-retention bonus from fine litter,
- reduced exposed-soil evaporation if convenient to implement,
- heavy litter mildly suppresses tiny open-ground germination,
- some shade/humid groundcovers receive a litter-associated suitability bonus,
- fungi benefit indirectly because litter feeds detritus.

Avoid a large new soil-chemistry model in this campaign.

## 10.2 Deadwood effects

Persistent fallen woody material should:
- count as wood substrate,
- act as climber support,
- accept moss/lichen/fungus colonization,
- add coarse litter slowly,
- create local shelter/litter retention.

Reuse PropSet/log concepts where possible rather than inventing a second incompatible deadwood geometry system.

---

# 11. Suggested implementation campaigns

These campaigns are intentionally larger functional areas rather than one-file tasks. They are suitable for decomposition by the planning/Clanker workflow.

## Campaign A — diagnostics and baseline

Goal: measure the problem before changing it.

Add diagnostics/reference facts for:
- exposed soil area,
- moss/lichen covered area,
- vascular-groundcover area,
- fine litter mass/coverage,
- coarse litter mass,
- dead flora count/biomass,
- fruit mass,
- seed-bank amount.

Capture default and Rocky Rise at:
- fresh world,
- 15 biological days,
- 60 biological days,
- 180 biological days.

Do not tune toward one screenshot. Preserve these as baseline comparisons.

## Campaign B — authoritative litter field + renderer conversion

Implement:
- LitterSystem state,
- deposition API,
- deterministic decomposition into Detritus,
- persistence serialization,
- terrain litter blend,
- SoilDetailRenderer reading real litter state.

Initially route existing living SheddingRate into litter instead of Detritus.

Leave corpse death on the old path until this campaign is validated.

Acceptance:
- litter added in one cell is visible,
- wet/dry cells render distinct litter tone,
- litter mass decreases and Detritus increases,
- zero explicit decomposers still produces decay,
- save/load preserves it exactly.

## Campaign C — persistent dead flora

Implement:
- DeadPlant model/population,
- death transition,
- stage timers and content schema,
- DeadFlora rendering,
- continuous mass transfer to litter,
- corpse/litter tint convergence,
- final assimilation,
- large woody snag/fall path.

Migrate several contrasting species first:
- Prismstar,
- Frosttussock,
- Veilfern,
- Embercrown,
- one tree.

Then generalize.

Acceptance:
- no migrated plant visually vanishes on death,
- standing and collapsed forms are visibly different,
- corpse biomass reaches litter without duplication,
- dead plants stop living physiology immediately,
- final despawn occurs only after advanced decay,
- tree death leaves persistent woody structure.

## Campaign D — background and explicit decomposition coupling

Implement:
- material-specific decay profiles,
- moisture/exposure modifiers,
- explicit fungus/slime local bonuses,
- decomposition diagnostics.

Acceptance:
- fruit/leaf/wood materials decay at visibly different rates,
- wet/shaded vs dry/exposed treatment differs,
- explicit decomposer presence accelerates the appropriate material,
- no-decomposer control still completes cycling.

## Campaign E — reproduction and fruiting

Implement:
- vascular reproduction content block,
- per-plant reproductive reserve/load,
- attached fruit rendering,
- drop into FruitMass/litter,
- sparse SeedBank,
- germination,
- replace ordinary woody abstract propagation with fruit/seed path species by species.

Start with two deliberately different examples:
- Kiteleaf wind seed,
- Shadebell or Embercrown fleshy berry.

Then add the rest of the tree/shrub roster.

Acceptance:
- reproductive structures visibly appear and disappear,
- dropped fruit persists on the ground,
- uneaten fruit decays into litter/detritus,
- viable seed can wait and later germinate,
- wind vs gravity distributions are measurably different,
- parent reproduction is resource/pulse based rather than a constant timer.

## Campaign F — fauna seed movement

Implement:
- generic fruit resource query,
- fruit consumption,
- carried viable-seed payload,
- delayed deposition,
- waste/litter accounting.

Do not add complex specialist pollination yet.

Acceptance:
- fruit-eating fauna can move a plant’s viable seed beyond gravity-drop range,
- seeds remain deterministic across repeated runs,
- fruit mass and energy/matter are conserved through consumption.

## Campaign G — vascular groundcover core

Implement the spatial vascular layer and renderer.

First species:
- migrate Coinrunner as the proving case,
- add Silkneedle as the first genuinely new matrix species.

Acceptance:
- patches occupy continuous irregular area,
- spread is visibly frontier/rhizome based rather than a constellation of circles,
- entity count does not scale one-to-one with blades/leaves,
- groundcover responds to existing moisture/light/nutrients,
- reference/perf remains within budget.

## Campaign H — fill remaining ground niches

Add/tune:
- Gloamleaf,
- ruderal gap filler,
- dry creeping mat,
- optional humid clubmoss/litter creeper after visual evaluation.

Evaluate migration of Mooncoin and Trifold.

Acceptance:
- ordinary mesic soil, deep shade, dry open soil, and fresh disturbance each have at least one plausible matrix/gap-filling strategy,
- the species remain niche-distinct rather than becoming interchangeable green paint.

## Campaign I — Pilot Tree and event integration

Route:
- Pilot Tree litter fall,
- branch fall,
- fruit/cone fall,
- itinerant disturbance deposits

through the same authoritative systems.

Acceptance:
- event aftermath remains after the event object is gone,
- Pilot Tree fruit/branch/litter events visibly alter the ground and food/decomposition state,
- no parallel “event debris” bookkeeping exists.

---

# 12. Content/schema changes

Likely additions to FloraSpeciesDef:

- PostLifeDef? PostLife
- ReproductionDef? Reproduction
- VascularCoverDef? Groundcover

Possible new enums:
- DeadPlantStage
- CollapseStyle
- ReproductiveStage
- ReproductiveForm
- DispersalMode

Likely new world-owned state:
- LitterField / LitterSystem
- DeadPlantPopulation / DeadFloraSystem
- SeedBank
- vascular coverage layer/system

ContentLoader must validate:
- fractions sum sanely,
- times/rates are nonnegative,
- reproductive forms have valid colors/sizes,
- seed viability is 0..1,
- groundcover species use compatible substrate/render forms,
- deadwood-producing plants are woody or explicitly allowed,
- dispersal modes have the required parameters.

---

# 13. Persistence and determinism

Extend canonical WorldSerializer payloads rather than storing visual-only hidden state.

Suggested payload separation:
- litter
- dead_flora
- seed_bank

Vascular groundcover can live in CoveragePayload if it uses CoverageWorld.

Persist:
- litter mass/composition/humification,
- dead plant stage/progress/biomass/pose seed,
- reproductive state/load if stored on FloraIndividual,
- seed-bank contents,
- new coverage tiles,
- relevant subsystem step counters.

Do not persist generated close-up leaf/fruit instance transforms. Recreate them deterministically from authoritative state.

Digest all new authoritative state.

---

# 14. Validation and tests

## 14.1 Unit/integration tests

Required categories:

### Litter
- deposition conserves mass,
- decay transfers mass to Detritus,
- environmental modifiers remain finite/bounded,
- no spontaneous litter appears,
- save/load equality.

### Death
- biological death creates dead state instead of immediate deletion,
- dead plant cannot reproduce/grow,
- dead plant does not count toward living woody population cap,
- stage transitions happen at configured biological times,
- corpse mass transfer is monotonic,
- final assimilation preserves accounting,
- collapse heading is deterministic.

### Decomposition
- baseline microbes work with zero explicit decomposers,
- correct decomposer accelerates correct material,
- coarse wood persists longer than fruit/fine leaves.

### Reproduction
- immature plants do not fruit,
- resource-poor plants cannot generate infinite reproductive mass,
- release reduces attached load,
- seed-bank deposits match dispersal,
- viability declines,
- suitable dormant seed can germinate,
- deterministic replay produces identical offspring locations.

### Groundcover
- occupied area expands from a founder,
- unsuitable cells stop the front,
- competition prevents impossible double occupancy,
- dieback frees area,
- seed-bank founder creates a patch,
- area and biomass survive save/load.

## 14.2 Mass-balance tests

Create focused scenarios that start with a known amount of flora biomass and run through:
death
→ corpse
→ litter
→ detritus
→ nutrients.

Assert mass accounting within the intended respiration/mineralization losses.

A similar test should cover:
reproductive reserve
→ fruit
→ ground fruit
→ decay / seed
without material creation.

## 14.3 Visual reference scenes

Add deterministic reference scenes for:
- fresh vs old leaf litter,
- wet shaded vs dry exposed litter,
- standing-dead herb,
- collapsed fern,
- straw tussock,
- shrub skeleton,
- tree snag,
- fallen tree/deadwood,
- attached berry fruit,
- wind-seed fruit,
- ground fruit decay,
- Silkneedle mature sward,
- Gloamleaf under canopy,
- ruderal disturbance patch,
- dry mat.

Always inspect the generated images; numeric visibility assertions are not sufficient.

## 14.4 Performance

Watch:
- sparse litter/composition field memory,
- SoilDetailRenderer rebuild cost,
- dead-flora MultiMesh count,
- fruit instance count,
- vascular-groundcover tile geometry,
- extra texture uploads.

Prefer bounded camera-local procedural detail plus low-resolution/far shader representation.

Do not turn litter into one authoritative render object per leaf.

---

# 15. Ecological/visual acceptance targets

These are scene-level goals, not hard universal laws.

For a mature default mesic island:
- clean exposed mineral soil should be a minority of ordinary suitable soil,
- significant non-green coverage is allowed and desirable,
- shaded mature areas may be litter-dominant rather than plant-dominant,
- dry/rocky and recently disturbed areas may remain visibly open,
- every major patch should have a plausible reason for being green, littered, rocky, wet, or exposed.

A reasonable tuning target for ordinary mature soil is roughly 55–85% combined plan-view coverage from vascular carpet + moss + visible litter, with large variation by habitat. Do not force this target onto gravel bars, rock, water, freshly disturbed soil, or intentionally sparse dry niches.

The strongest visual test is simple:

A screenshot should no longer read as “interesting specimens placed on a mostly clean dirt platform.”

It should read as a small piece of ground that has been alive for a while.

---

# 16. Deferred work

Explicitly not required for the first implementation:

- full Earth-like seasons,
- pollination simulation,
- one-object-per-fruit or one-object-per-leaf physics,
- detailed bacterial species,
- full soil horizons,
- complex wind field,
- full litter transport by runoff,
- fire,
- disease/pathogen ecology,
- detailed deadwood insect succession,
- Pilot Tree mortality,
- exact botanical reproduction for every herb.

The architecture should leave room for these without making them prerequisites.

---

# 17. Recommended execution order

The order matters.

1. Build authoritative litter first and make the existing ground-detail renderer tell the truth.
2. Route ordinary shedding into it.
3. Add persistent dead plants and let death feed the litter system gradually.
4. Add background decomposition and explicit-decomposer acceleration.
5. Add fruiting/seed material and the seed bank.
6. Add the vascular groundcover spatial layer and migrate Coinrunner.
7. Add Silkneedle and the missing matrix/gap guilds.
8. Connect fauna seed transport.
9. Connect Pilot Tree/local-event deposits.
10. Tune only after long-run reference captures show how all layers interact.

This order ensures every later feature has a real material destination. Groundcover, corpses, fruit, branches, and events all end up participating in the same surface history rather than each inventing another decorative system.
