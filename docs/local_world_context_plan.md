# Local World Context Expansion Plan

## Purpose

Expand Vivarium from a mostly self-contained floating specimen into a hyper-local ecological slice of a much larger surrounding world.

The simulated patch stays small, inspectable, deterministic, persistent, and non-loseable. The change is conceptual and structural: the specimen is no longer assumed to contain the entire ecosystem responsible for everything that happens inside it. World generation establishes what kind of local place this patch belongs to, and occasional outside influences cross the simulation boundary.

The first implementation should have three composable axes:

1. Island archetype — the landform, hydrology, boundaries, and large-scale habitat structure of the sampled patch.
2. Optional Pilot Tree — a massive old-growth organism partly intersecting one corner or edge and shaping much of the local microclimate.
3. Local event ecology — rare nearby happenings whose effects enter the patch, beginning with transient large-fauna visits and Pilot-Tree-linked events.

These should be designed as one system. A creek bank beneath a Pilot Tree should create different routes, habitat gradients, and eligible events than a dry slope with no large anchor.

## Core design rules

### The vivarium is a window, not the whole ecosystem

Resident mosses, herbs, springtails, amphibians, and small fauna may spend their entire simulated lives inside the patch. A raccoon-sized omnivore, a giant browser, a storm front, a nearby seed source, or a massive tree canopy does not need to.

The default interpretation of an event should be: something happened in the surrounding local ecosystem, and this patch intersects its consequences.

Every event should therefore have an implied physical source such as adjoining terrestrial habitat, upstream water, overhead canopy, subsurface activity, nearby animal territory, or the Pilot Tree.

### Local causality over arbitrary randomness

Scheduling can be pseudorandom, but eligibility and effects should be ecological.

Examples:

- Snake-ipede visits become eligible when suitable amphibian prey biomass exists.
- Landsquid visits become more likely in damp worlds rich in moss and fungi.
- Branch fall requires a Pilot Tree.
- Shoreline drift requires an aquatic boundary.
- Rockfall requires cliff or steep exposed-rock context.

The player should generally be able to inspect the aftermath and understand what sort of local process caused it.

### Events are processes, not percentage edits

Avoid event implementations whose primary action is “remove 70 percent of salamanders” or “reduce all herbs by 40 percent.”

Visitors and disturbances should have finite impact budgets and physical trajectories. A visitor enters, moves through the patch, chooses reachable targets, consumes or disturbs them through ordinary simulation interfaces, and leaves when its appetite, kill, time, route, or disturbance budget is exhausted.

The final effect should emerge from what actually existed and what the visitor could reach.

### Consequences persist naturally

The event object disappears when the outside cause has passed. Its ecological consequences remain in normal simulation state:

- browsed plant biomass,
- killed fauna and resulting detritus,
- depleted moss coverage,
- fallen branches or bark,
- fruit or cone deposits,
- exposed substrate,
- changed litter,
- displaced organic material,
- introduced seeds, spores, or eggs,
- temporary water changes where appropriate.

Do not create a parallel “event damage” layer when existing ecology can own the result directly.

### Events are rare enough to create history

The system should favor long quiet periods punctuated by distinctive changes. Variety should come from different disturbance shapes, not from constantly firing events.

## Pilot Tree terminology

Use Pilot Tree as the intended project term for the optional giant old-growth anchor.

The term is intentionally historical rather than newly invented. Large or conspicuous trees were used as navigational landmarks, and “pilot trees” is a documented navigation term; one surviving account describes lantern-lit pilot trees guiding vessels at the mouth of the Genesee River before a lighthouse was established.

Within Vivarium, Pilot Tree means a very large old-growth tree whose trunk/root system intersects the sampled patch while much of its canopy, root network, and ecological influence extend beyond it.

“King tree” and “anchor tree” can be useful explanatory phrases, but should not silently replace Pilot Tree in project terminology.

# System A: Island archetypes

## Goal

Replace the assumption that every world is essentially the same convex terrain treatment with a small set of strongly differentiated local landform archetypes.

An archetype controls large-scale topology, hydrology, boundary context, habitat gradients, and possible event origins. It should not directly hard-code the final species composition.

Keep the specimen presentation and regular-hex concept if desired; the archetype alters the terrain and water inside the specimen and records what each side is understood to connect to outside it.

## Initial archetype roster

Target roughly six strong archetypes.

### 1. Forest floor / neutral patch

The improved baseline.

- broadly traversable rolling ground,
- no dominant hydrological barrier,
- mixed moisture,
- ordinary soil, rock, log, and gravel opportunities,
- terrestrial external context on most boundaries.

This is the reference environment and the cleanest place to compare Pilot Tree effects.

### 2. Shoreline

One broad side meets persistent open water beyond the sampled patch.

- inland side grades toward a wet margin,
- shallow shelves and saturated soil,
- one or two aquatic/wetland boundaries,
- drift and deposition zones,
- mudflat, gravel, root-tangled shore, or tiny beach variation.

This creates extensive wet-margin habitat and makes waterborne arrivals and wetland itinerants naturally eligible.

### 3. Creek / river split

A deep persistent channel crosses the specimen and divides it into two banks.

- continuous channel from one boundary to another,
- deep enough that ordinary terrestrial fauna cannot casually cross,
- directional upstream/downstream identity,
- optional shallow bars, log bridges, or giant roots,
- bank-specific shade, moisture, and substrate differences.

The channel must be a first-class world-generation feature created before ordinary props and starter populations. It should produce meaningful spatial isolation and dispersal constraints.

### 4. Steep slope

The specimen samples part of a larger hillside.

- strong elevation gradient,
- one side high and another low,
- runoff and litter movement biased downslope,
- exposed mineral soil and erosion-prone patches,
- optional seep where groundwater intersects the slope.

This should naturally place dry and wet niches close together.

### 5. Dell / cliff-side

A sheltered patch constrained by rock walls on one or more sides.

- steep exposed rock faces,
- shaded interior,
- ledges and talus,
- cracks and seep lines,
- strong moss and lichen surfaces,
- fewer legal terrestrial entry edges.

This archetype supports rockfall, seep, and wall-derived detritus events.

### 6. Wet hollow / seep

A local depression receiving groundwater or runoff from beyond the slice.

- shallow saturated basin rather than a clean shoreline,
- irregular standing water,
- one or more seep sources,
- dry hummocks or raised roots,
- strong detritus retention.

This is ideal for mosses, fungi, gastropods, amphibians, and Landsquid visits.

## Archetype data model

Add a WorldArchetypeDef or equivalent containing:

- id, name, description,
- terrain profile,
- hydrology profile,
- six boundary-context descriptors,
- allowed Pilot Tree anchors,
- event tags,
- prop and substrate biases,
- entry-edge weights,
- optional channel, cliff, shore, slope, or seep parameters.

Persist boundary intent explicitly instead of attempting to infer it later from terrain height.

Useful boundary contexts include:

- terrestrial forest,
- open water,
- upstream water,
- downstream water,
- cliff/wall,
- higher slope,
- lower slope,
- wetland,
- blocked/impassable.

This metadata becomes shared infrastructure for visitor routing and future local events.

# System B: Pilot Tree

Implementation direction, 2026-09-28: all six forms are required. The user tightened placement to **corners only**, with the two primary root buttresses extending along the neighboring island sides. This supersedes edge-or-corner alternatives elsewhere in this plan. Real anatomy references and the fictional form roster are recorded in [pilot_tree_references.md](pilot_tree_references.md).

## Goal

Allow zero or one enormous old-growth tree to intersect a world at a corner. It is landscape infrastructure, not an ordinary woody flora individual scaled up.

## Placement and generation

- choose a vertex anchor,
- place most of the trunk outside the specimen,
- allow only part of the trunk flare and root system to enter,
- reserve trunk/root volume before rocks, logs, ordinary woody flora, or starter populations,
- orient roots according to terrain and archetype rather than perfect radial symmetry,
- extend the two primary buttresses along both sides adjoining the selected corner,
- let roots alter local terrain, water, and traversal.

Centering the whole giant tree inside the island would undermine the intended scale.

## Initial Pilot Tree roster

Target about six ecologically distinct forms; final fictional names can come during individual visual work.

### Giant hemlock analogue

- dense shade,
- cool humid root zone,
- slow acidic litter,
- strong moss and fungal association,
- broad root shelves and bark crevices.

### Giant spruce analogue

- heavy needle litter,
- pronounced radial roots,
- alternating dry raised-root and wet root-crotch pockets,
- frequent cone debris.

### Giant cycad analogue

- huge armored trunk,
- massive fronds mostly outside/above the world,
- strongly dappled light,
- fleshy cone or fruit events,
- visually alien but biologically legible.

### Sequoia/redwood analogue

- enormous fibrous trunk,
- high canopy and somewhat more open ground light,
- huge buttress roots,
- bark slough,
- persistent deadwood after branch events.

### Broadleaf buttress giant

- large buttress roots forming small walls and basins,
- episodic leaf-litter pulses,
- large fruit or nut drops,
- richer and faster-decaying litter.

### Pale fungal old-growth conifer

- pale plated bark,
- hollows and rotten-wood habitat,
- heavy fungal colonization,
- broken crown and irregular sunflecks,
- elevated bark and deadwood events.

## Pilot Tree ecology

PilotTreeDef should provide spatial parameters for:

- canopy shade,
- root competition,
- moisture retention,
- litter deposition,
- detritus contribution,
- bark/wood substrate,
- moss/fungal affinity,
- root geometry,
- trunk geometry,
- fruit/cone resource type,
- event table and weights.

Do not apply one global modifier. Generate or query spatial fields for canopy occlusion, root density, litter bias, moisture influence, and root/trunk proximity.

Roots should be ecological terrain, not decoration. They can form ridges, sheltered crotches, under-root cavities, barriers, corridors, bark surfaces, detritus traps, and local water deflection.

The Pilot Tree can be effectively immortal on the simulation horizon.

# System C: Local ecological events

## Goal

Build a deterministic content-driven framework for rare outside influences crossing into the specimen.

First event classes:

1. transient itinerant fauna,
2. Pilot-Tree-linked events.

The architecture should also support later runoff, drift, rockfall, seed rain, mass emergence, nearby carcass attraction, burrowing, and similar events.

## Event origin classes

Each event should declare an origin such as:

- outside boundary,
- upstream,
- overhead,
- subsurface,
- Pilot Tree,
- within patch.

Within-patch events should be the exception rather than the default for this system.

## Lifecycle

Use an explicit lifecycle:

1. ineligible,
2. eligible,
3. pending or approaching when relevant,
4. active,
5. departing or settling,
6. complete.

Simple tree events may skip some visible phases but should still use the same runtime contract.

## Scheduler

Use a dedicated deterministic RNG stream such as local-events so event selection cannot perturb resident flora/fauna random sequences.

Inputs should include:

- world seed,
- simulated time,
- archetype tags,
- boundary contexts,
- Pilot Tree type,
- resident populations and resources,
- per-event cooldown,
- global quiet period,
- recent event history.

Use a low hazard rate and strong cooldowns rather than a fixed “one event every N days” cadence.

Persist a small rolling history containing event id, timing, origin, visitors, and coarse impact summary.

# Event class 1: Itinerant fauna

## Runtime model

Itinerants are transient VisitorAgent objects, not resident FaunaIndividuals.

They do not:

- reproduce,
- join resident population caps,
- participate in resident genetics/lineage,
- remain permanently because food is abundant.

They do:

- visibly enter the patch,
- follow a legal route,
- sense and choose local targets,
- consume or disturb actual simulation state,
- produce waste/detritus/deposits where appropriate,
- visibly leave.

Because only a handful exist at once, render them as individual scene objects rather than forcing them through resident MultiMesh batching.

## Impact budgets

Visitors receive finite budgets such as:

- food biomass appetite,
- prey kill count or prey biomass,
- browsing bites,
- visit duration,
- travel distance,
- trampling/disturbance,
- deposition.

If food is absent, the visitor searches briefly and leaves instead of forcing a scripted impact.

## Initial itinerant roster

### Sleipnir doe and fawns

Large browser group.

- one doe plus zero or more fawns,
- enters from terrestrial context,
- follows a broad corridor,
- browses herbs, succulents, tender shrubs, and reachable climbers,
- usually reduces biomass rather than deleting plants,
- minor trampling,
- leaves a patchy browse trail.

### Snake-ipede

Solitary upper predator.

- strongly targets Stonebell/Rainspine-class amphibian analogues,
- senses prey concentrations,
- moves between prey patches,
- kills until satiated or its kill budget is spent,
- exits through another legal terrestrial boundary.

It may wipe out a tiny remaining population. Do not secretly preserve a breeding pair.

### Platypus Fox

Raccoon-sized wet-margin omnivore.

- patrols shoreline, creek edge, seep, or shallow water,
- strongly targets Glasscoil/snail analogues,
- eats palatable wet vegetation,
- opportunistically takes larvae or other easy prey,
- remains spatially concentrated around wet habitat.

### Landsquid

Slow damp-habitat moss/fungal grazer.

- requires suitable wet/moist context,
- crops moss mats and fungal bodies with feeding arms,
- leaves a visibly low-cropped trail,
- may redistribute spores through skin contact or waste.

### Velvet Anteater analogue

Litter-fauna specialist.

- searches leaf litter and deadwood margins,
- targets springtails, beetles, millipedes, larvae, and similar small fauna,
- disturbs litter and creates small exposed patches,
- suppresses a broad guild rather than one species.

### Glass Heron analogue

Wetland ambush hunter.

- enters from water/shore context,
- stalks shallows and wet banks,
- takes Reedjaw, swimmers, tiny fish, or juvenile amphibian analogues,
- leaves after a small number of successful captures.

## Routing

Visitor routing should:

1. query boundary context for legal entry edges,
2. choose an entry point deterministically from weighted options,
3. generate a broad route toward a compatible exit or habitat objective,
4. allow attractive local targets to bend the route,
5. prevent small food targets from trapping the visitor,
6. leave through a legal boundary when done.

Cliff and blocked edges should be excluded by metadata, not discovered through collision failure.

# Event class 2: Pilot Tree events

Target roughly six first-pass events.

### Fruit / cone fall

- large fruit, cone, seed cluster, or equivalent falls in,
- creates food and/or propagules,
- can attract fauna,
- decays into detritus if uneaten,
- product varies by tree type.

### Branch fall

- branch enters from above/tree side,
- becomes persistent deadwood,
- may locally crush or shade a small area,
- creates climbing structure, fungal substrate, and future detritus.

Prefer converting the result into ordinary log/deadwood ecology after impact.

### Bark slough

- bark plates or fibrous slabs detach,
- create small woody cover and decomposer substrate,
- may temporarily smother tiny flora.

### Litter pulse

- episodic needles, leaves, frond fragments, scales, or similar canopy litter,
- increases detritus beneath or downslope of the tree,
- may shade low flora until decomposition.

### Root heave / exposure

- rare long-horizon structural event,
- cracks or raises a small soil area,
- exposes root/wood/mineral substrate,
- may slightly redirect surface water.

### Sap leak / fungal flush

- local exudate, wound, or old scar creates a temporary rich resource,
- attracts decomposers and small fauna,
- may trigger a fungal fruiting flush,
- gradually returns to normal substrate state.

# Future archetype-linked events

The generic framework should later support, without redesign:

- shoreline drift,
- upstream runoff/silt pulses,
- temporary flooding,
- seep strengthening or weakening,
- cliff rockfall,
- nearby treefall,
- seed/spore rain,
- egg/larval deposition,
- nearby carrion attracting scavengers,
- mass emergence,
- overhead predator scare,
- burrowing visitors.

These are not required for the first implementation campaign.

# World generation order

The generation order should be explicit.

## Phase 1: Base specimen

- seed,
- diameter/cell size,
- strata and cut geometry.

## Phase 2: Archetype

Generate channel, shore, slope, cliff, hollow, and boundary context before ordinary dressing.

## Phase 3: Pilot Tree

If enabled:

- choose legal anchor,
- reserve trunk/root volume,
- generate major roots and root-modified terrain,
- establish Pilot Tree influence fields.

## Phase 4: Hydrology settle

Water responds to actual archetype topology and roots.

## Phase 5: Structural props

Place rocks, ordinary logs, gravel, and dressing around protected channel/root/cliff geometry.

## Phase 6: Starter ecology

Seed flora/fauna according to the resulting habitat.

## Phase 7: Event context

Build the potential event pool from archetype, boundaries, Pilot Tree, and world settings. Runtime ecological state further gates eligibility.

# Runtime architecture

Suggested content/runtime concepts:

## WorldArchetypeDef

Owns large generation intent, boundary context, event tags, and archetype-specific geometry.

## PilotTreeDef

Owns static ecology, root/trunk form, and tree event table.

## LocalEventDef

Common metadata:

- id/name/description,
- event class,
- origin,
- required and forbidden tags,
- base weight,
- cooldown,
- minimum quiet period,
- eligibility thresholds,
- optional time constraints.

## FaunaVisitDef

Visitor composition, target preferences, movement style, budgets, legal entry contexts, duration, and visual/animation reference.

## LocalEventSystem

Owns:

- deterministic eligibility,
- weighted scheduling,
- cooldowns,
- lifecycle,
- active-event persistence,
- event history,
- origin and entry-edge resolution.

It should not contain every event-specific effect.

## VisitorAgent

Transient state:

- position/orientation,
- route progress,
- impact budgets,
- target,
- animation state,
- entry/exit,
- group id where needed.

Keep this separate from resident fauna genetics and persistence fields.

# Save requirements

Saving during an active event must be deterministic and resumable.

Persist:

- archetype id and generated parameters,
- boundary contexts,
- Pilot Tree id, anchor, seed/form state,
- non-regenerable Pilot Tree geometry/state,
- event RNG or deterministic scheduler state,
- cooldowns and event history,
- active event instances,
- active visitor agents.

Ordinary event consequences stay in their ordinary systems and should not be duplicated in event save data.

Breaking old saves is acceptable for this campaign unless that decision changes before implementation.

# Player-facing world creation

Keep controls simple:

- Island archetype: Random / Forest floor / Shoreline / Creek / Steep slope / Dell-cliff / Wet hollow.
- Pilot Tree: Random / None / specific tree.
- Local events: On / Off initially.

Do not expose every scheduler coefficient or tree parameter in the normal UI.

The same seed and options must produce the same archetype variation, Pilot Tree, and event stream.

# Rendering and animation

## Pilot Tree

At most one exists, so it can justify more geometry than ordinary trees.

Priorities:

1. convincing scale,
2. root/trunk continuity,
3. strong bark depth and silhouette,
4. believable canopy shadow influence,
5. convincing integration with terrain.

The full crown does not need to fit inside the specimen. Partial trunk, roots, and overhead limbs/fronds should imply a much larger organism outside the viewed slice.

## Itinerants

Use individual render nodes. Reuse the animation-family philosophy from resident fauna where useful, but do not constrain visitors to the resident batching implementation.

Requirements:

- visible arrival and departure,
- believable contact-aware locomotion,
- readable feeding/hunting actions,
- appropriate shadows and ground contact,
- group coherence for Sleipnir visits.

Do not block mechanics on final materials; itinerant appearance should be refined creature-by-creature.

# Ecology integration

## Flora browsing

Use existing biomass/growth state.

- reduce biomass,
- remove tiny individuals only if fully consumed,
- allow survivors to regrow,
- return waste/detritus through ordinary cycles.

## Fauna predation

Visitor predation should:

- target a real resident individual,
- remove it authoritatively,
- satisfy visitor appetite/kill budget,
- return uneaten mass to detritus where appropriate.

Visitors do not need full resident metabolism.

## Moss/coverage grazing

Landsquid requires a real interface to coverage-layer biomass. Add a bounded “consume coverage biomass in area” operation if needed rather than pretending mats are FloraIndividuals.

## Disturbance

Prefer real substrate/biomass effects over cosmetic decals. First pass may limit trampling/rummaging to biomass loss and detritus redistribution if generalized soil compaction expands scope too far.

# Implementation campaigns

## Campaign 1: World-context data model

Deliver:

- WorldArchetypeDef,
- boundary contexts,
- PilotTreeDef,
- LocalEventDef,
- content validation,
- descriptor/save fields,
- deterministic generation contract.

Acceptance:

- round-trip preserves context,
- invalid content produces useful errors,
- same seed/options produce identical context.

## Campaign 2: Island archetypes

Recommended order:

1. refactor existing forest-floor world into archetype system,
2. steep slope,
3. shoreline,
4. creek split,
5. dell/cliff-side,
6. wet hollow/seep.

Acceptance:

- each is recognizable from terrain alone,
- hydrology matches topology,
- starter entities avoid invalid channel/cliff geometry,
- boundary metadata matches the generated world.

## Campaign 3: Pilot Tree infrastructure

Implement:

- selection and legal anchoring,
- trunk/root procedural geometry,
- collision and terrain reservation,
- shade/root/litter fields,
- tree-specific substrate opportunities.

Implement all six forms: Gloomspire, Needlevault, Crowncoil, Emberpillar, Basinwarden, and Palehollow. Their real-world references guide ecological and anatomical structure without copying a species.

Acceptance:

- tree occupies a corner while implying much larger size, with buttresses extending along both adjoining sides,
- prop placement respects roots,
- shade/moisture/litter gradients measurably alter habitat,
- effects are spatial rather than global multipliers.

## Campaign 4: Generic local-event runtime

Implement:

- dedicated RNG stream,
- eligibility evaluation,
- weighted scheduling,
- lifecycle,
- quiet-period and cooldown logic,
- persistence,
- history/logging,
- entry-edge resolution.

Use one trivial debug event before building real visitors.

Acceptance:

- same state yields same event decisions,
- rendering cannot affect outcomes,
- save/load mid-event resumes correctly,
- ineligible events never enter the pool.

## Campaign 5: Itinerants

Recommended implementation order:

1. Sleipnir — route + group + browsing.
2. Snake-ipede — directed predation.
3. Platypus Fox — mixed wet-margin targets.
4. Landsquid — coverage and fungal browsing.
5. Velvet Anteater — broad guild targeting/litter disturbance.
6. Glass Heron — aquatic/shoreline hunting.

Acceptance:

- legal entry/exit,
- saveable mid-visit,
- effects derive from encountered targets,
- no leakage into resident population/genetics,
- no hidden percentage edits.

## Campaign 6: Pilot Tree events

Implement:

1. fruit/cone fall,
2. branch fall,
3. bark slough,
4. litter pulse,
5. root heave/exposure,
6. sap leak/fungal flush.

Where possible, outputs become ordinary persistent world objects/resources after the event.

## Campaign 7: Composition and tuning

Required scenarios:

- forest floor + no Pilot Tree,
- forest floor + hemlock,
- shoreline + cycad,
- creek + major root near one bank,
- steep slope + conifer,
- cliff/dell + no tree,
- wet hollow + Landsquid,
- wet margin + Platypus Fox/Glass Heron.

Tune:

- event frequency,
- appetite/kill budgets,
- Pilot Tree influence radius,
- starter population biases,
- route selection,
- cooldowns,
- recovery times.

The target is visible disturbance followed by interesting recovery and succession, not constant ecological equilibrium under assault.

# Testing

## Determinism

Verify deterministic:

- archetype generation,
- Pilot Tree anchor/form,
- event selection,
- visitor authoritative routing/target choice,
- save/load continuation.

## Simulation regression tests

At minimum:

- Sleipnir browsing reduces reachable flora biomass only.
- Snake-ipede removes actual prey and returns uneaten biomass to detritus.
- Landsquid can consume coverage-layer biomass.
- Creek water blocks ordinary terrestrial crossing except valid routes.
- Cliff boundaries cannot be visitor entry points.
- Pilot Tree shade/root fields alter habitat spatially.
- Branch fall creates persistent deadwood.
- Event save/load reaches the same digest as uninterrupted execution.
- Global cooldown prevents immediate repeated spectacle events.

## Soak tests

Run accelerated long worlds for every archetype with events enabled and watch for:

- accidental inevitable population collapse,
- event starvation from over-restrictive eligibility,
- visitors that fail to leave,
- pathological repeated entry edges,
- hydrology failures around roots,
- event-object accumulation,
- unrecoverable creek-bank isolation,
- runaway litter/detritus under Pilot Trees.

Extinction by itself is not a test failure. Systemic accidental inevitability is.

# UI and observability

Normal UI should remain light:

- unobtrusive notification for notable events,
- selection support for visible itinerants,
- optional recent-events view,
- world inspector showing archetype and Pilot Tree.

Debug tooling should expose:

- eligible event pool and weights,
- cooldowns,
- event RNG/next evaluation,
- visitor budgets and targets,
- legal entry/exit edges,
- Pilot Tree influence fields,
- boundary contexts.

# Explicit non-goals for first pass

- simulating the whole offscreen ecosystem,
- making itinerants permanent residents,
- full seasonal migration,
- growing a Pilot Tree from seed,
- routine Pilot Tree death/toppling,
- hundreds of event variants,
- dynamic weather as a prerequisite,
- full soil compaction chemistry,
- final photoreal materials for all visitors before mechanics work,
- old-save compatibility unless explicitly reinstated.

# Definition of done

This expansion is complete when:

1. World creation offers at least six materially distinct island archetypes.
2. A world may contain zero or one Pilot Tree selected from roughly six distinct types.
3. Pilot Trees produce spatially meaningful shade, roots, litter, and terrain structure.
4. The local-event system schedules only causally eligible events deterministically.
5. Roughly six itinerant fauna variants visibly enter, affect real ecology, and leave.
6. Roughly six Pilot-Tree-linked event variants create persistent normal-world consequences.
7. Archetype and Pilot Tree context jointly determine legal event sources and routes.
8. Saving during an event is deterministic and resumable.
9. Events remain rare enough that the patch spends substantial time simply being an ecosystem.
10. A player inspecting an aftermath can usually infer a plausible local cause.

The intended result is not “a floating island with random events.” It is a small, intensely observed piece of a larger fictional ecosystem, where surrounding geography and organisms occasionally intrude in physically legible ways.
