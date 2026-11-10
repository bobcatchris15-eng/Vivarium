# Audit: ecology / simulation library (src/Vivarium.Sim)

Read-only audit, 2026-11-10, HEAD a193e6d. Nothing was run (no Godot, no dotnet); all numbers below are derived by reading
code and `game/content` JSON. Items marked (inferred) need a `--soak` run to confirm. Paths are relative to `src/Vivarium.Sim`
unless they start with `game/`. Persisted-state impact is stated per item (save schema 5).

Scale facts used throughout: default preset diameter 8 m, cell 0.25 m, so about 665 domain cells (`game/content/presets/default.json`);
fine coverage raster 0.02 m (`Coverage/CoverageSpec.cs:7`). One tick = 10 sim-s (`Time/Clock.cs:13`). `BioAcceleration` default 60/7 = 8.57
(`World/WorldDescriptor.cs:57`), so 1 tick = 85.7 bio-s. At 1x speed the clock runs 168 sim-s per real s = 16.8 ticks/s (`Time/Clock.cs:16`).
Content: 125 flora files (106 individual species, 19 coverage species: 14 mat mosses incl. blue_sundew, 7 lichens; counts by archetype
are 90 plant / 14 fungus / 14 moss / 7 lichen), 17 fauna files, 19 presets.

---------------------------------------------------------------------------------------------------------------------------------
## 1. Tick structure (Q1)

Scheduler: fixed 10 s tick, systems sorted by (order, name), each with cadence and phase (`Time/Scheduler.cs:64-71`, `:92-126`).
Per Advance call max 400 ticks and a 12 ms wall budget (`:49-51`); heavy systems are `RegisterBatched` generators that yield mid-system.
Registration is `World/VivariumWorld.cs:201-243`; cadences `:87-89`.

| order | system | every N ticks (sim time / bio time) | unit of work | cost driver |
|---|---|---|---|---|
| 10 | fauna.behaviour | 4 (40 s / 5.7 bio-min) | per fauna | F x (locomotion + 1/6 of steps: 3-7 `Suitability` probes + `FoodAt` neighbour scans), `FaunaSystem.cs:147-291` |
| 12 | guardian.behaviour | 4 | 1 entity | out of scope |
| 20 | fauna.metabolism | 6 (60 s / 8.6 bio-min) | per fauna | F x (`Suitability` + `Feed` neighbour query) `:408-502`; bio dt = 514 s |
| 30 | fauna.lifecycle | 120 (20 min / 2.9 bio-h) | per fauna | F x (age, mortality hash, mate query `OrderBy`, births) `:613-698` |
| 40 | hydrology | 2 | EMPTY lambda `VivariumWorld.cs:211` | none; water is advanced by `SimHost.Advance` in real time (`Time/SimHost.cs:29`, `Water/SurfaceLiquid.cs:106-126`), not by ticks |
| 50 | environment | 30 (5 min / 43 bio-min) | per cell (665) | `CoupleMoisture` 665 x 24 neighbours (`Water/Hydrology.cs:301-368`); Pilot-tree moisture; `StepNutrients` + diffuse; light only if props changed |
| 60 | ecology.resources | 30 | per cell | plankton logistic + nutrient take, `Ecology/Ecology.cs:74-113` |
| 65 | aquatic | 60 (10 min / 1.4 bio-h) | fine raster, wet cells | Coverage/Aquatic |
| 66 | ecology.litter | 360 (1 h / 8.6 bio-h) | per cell + pilot litter | `LitterSystem.StepBatches` 665 x decomposer scan `LitterSystem.cs:93-132` |
| 70 | flora | 60 (10 min / 1.4 bio-h = 0.06 bio-day) | per flora individual | N (about 1600) x (`Suitability` + `Crowding`); spread/`CanEstablish`; `FloraSystem.cs:378-587` |
| 71-73 | flora.dead / reproduction / seedbank | 60 | per corpse / per plant / per seed lot | `DeadFlora.cs:102`, `Reproduction.cs:45,87` |
| 74 | flora.ambient | 60 | per cell | `AmbientGroundCover.TargetAt` 665 x (48 water probes + 1.35 m flora query), `AmbientGroundCover.cs:51-107` |
| 75/76 | coverage (mat) / coverage.lichen | 60 | fine raster tiles (about 100k fine cells possible) | `CoverageSystem.cs:330-363` |
| 90 | genetics.prune | 8640 (1 day) | genomes | `FaunaSystem.cs:815` |

Per-entity vs aggregate: flora and fauna are per-individual (flora about 1600 target, ceiling 2000, `FloraSystem.cs:53-54`; fauna budget 360 in default
preset); fields (moisture, nutrients, light, litter fine/coarse/fruit, plankton), ambient ground cover, and moss/lichen/algae rasters are aggregate.
Herb shading is not a field: tree/shrub shade is a derived grid (`FloraSystem.cs:84-139`), herb shade exists only inside coverage sampling
(`Coverage/CoverageEnvironment.cs:249-276`).

Dead/unused plumbing: `hydrology` slot (above); slime-mould `Creep` (`FloraSystem.cs:644-707`), colonial `ColonyStep` (`:1764-1805`), `AssignSlimeTint`,
`GlobalColonialCellBudget` have no content: no flora JSON has `creepSpeed` or `colony` (only `mat`/`lichen` coverage keys). Their per-tick
bookkeeping loop at `FloraSystem.cs:389-399` still runs over every individual. `ecology.json` `detritus.decayPerDay` and `biofilm.*` are parsed
(`Content/ContentLoader.cs:240`) but nothing reads `DetritusDecay` (litter uses hard-coded half-lives, `LitterSystem.cs:122,128,129`).

---------------------------------------------------------------------------------------------------------------------------------
## 2. Environmental drivers (Q2)

Every field is derived from terrain/hydrology/biology. There is no spatial noise driving habitat (the only noise is the 10 % per-cell patchiness
multiplier in ambient ground cover, `AmbientGroundCover.cs:91-92`, and terrain generation itself).

| field | derived from | updated by | note |
|---|---|---|---|
| `Moisture` | water table vs bed height, terrain drainage, surface water depth and 5x5 neighbourhood | `Hydrology.CoupleMoisture` every 30 ticks (`Hydrology.cs:301-368`), Pilot-tree retention (`PilotTree.cs:419-437`) | floor `dryBaseline` 0.22 (`ecology.json`), so no true arid niche below 0.22; no rain, no transpiration, no vegetation feedback |
| `Light` | terrain horizon (8 azimuths, 3 m) x prop heights x Pilot canopy (`Fields/EnvironmentFields.cs:90-134`) | recomputed only when `Props.Version` changes (`:70`, `VivariumWorld.cs:227`) | static in time: no sun angle, no season; `DayNightClock` is presentation-only and must not be read by sim (`Time/DayNightClock.cs:19-27`). Woody shade subtracted in `EffectiveLight` (`FloraSystem.cs:141-180`); herb shade NOT applied to individuals, only to coverage (0.22 per caster) |
| `Nutrients` | mineral reservoir from retained topsoil and drainage (`EnvironmentFields.cs:146-151`) + litter mineralisation + decomposer + fauna waste | relax toward reservoir (0.06/bio-day) + diffusion 0.4/bio-day (`:163-176`) | typical normalised range 0.036-0.27 (see E10) |
| `RetainedTopsoilDepth`, `DrainagePotential` | slope, convexity (`Fields/RootZoneEnvironment.cs:36-60`) | only on terrain edit | never changes through erosion/litter/plants |
| substrate | rock/log/gravel props, water cover, base substrate from topsoil+drainage (`EnvironmentFields.cs:50-68`, `VivariumWorld.cs:272-280`) | props/tool edits | |
| litter fine/coarse/fruit | plant shedding, deaths, corpses, fruit, Pilot tree, fauna deaths, moss/lichen death | `LitterSystem` | feeds nutrients, decomposers, fauna |
| plankton, biofilm | wet cells + nutrients; biofilm is a projection of algae coverage | `StepResources` | plankton has no mortality or light term (`Ecology.cs:102-113`) |

Absent drivers: temperature, season, weather/rain, wind, day-length. Water physics runs on real time x speed multiplier, not ticks, so headless `Step`/
`RunFor` (`VivariumWorld.cs:318-319`) advances ecology on frozen water. Only the live game (`--soak`) exercises the water-ecology loop.

---------------------------------------------------------------------------------------------------------------------------------
## 3. Species niches (Q3)

Flora niche = substrate affinity x Gaussian preference on moisture, light, nutrients (+ optional oxygen, topsoil depth), geometric mean, times
(1 + proximity bonus) (`FloraSystem.cs:182-258`; Gaussian `Core/Vec.cs:65`). Three stage-specific tolerances (survival x1.65, establishment x0.85,
reproduction x0.72). Hard gates: refused substrate/tag, water depth, moisture hard min/max (new recruits only, `:196-197`), required feature (3 log fungi).
Only 10 species use `oxygen` and 9 use `topsoilDepth`, the two axes that come from terrain; for the rest the niche is moisture/light/nutrient only.
Fauna niche = substrate affinity x moisture preference, or water depth for aquatics (`FaunaSystem.cs:97-124`). No light, flora, food or temperature term.

Exact-duplicate niches (identical habitat, growth, spread numbers; differ only in visuals):
- dewbonnet = ghostbonnet (fungus, g 0.8, life 9 d)
- frosttussock = plumegrass = starmoss (starmoss is an individual-based "moss", not coverage)
- glowbriar = lanternbrush (woody)
- lanternsedge = ringreed (waterside)
- moonfern = veilfern (and swanfrond/sceptrefern/mirrorstrap_fern are within 0.25 niche distance, same growth block)
Near-duplicates (niche distance < 0.4): glasslily~mirrorleaf, candlecoral~cradlecup, nutglade~splitcrown, emberfan_fungus~glass_antlers, barbarrow~paddlerib,
burlmantle~hallowsting~heddlelace, cinderberry~hookthicket~trifold. Consequence: when both of a pair are in the island's species pool (up to 70 species,
`Ecology/FloraSpeciesPool.cs:127`) they compete 1.25 (same species) vs 0.5 (other species) so they coexist indefinitely on the same cells (homogenisation of look, not of
dynamics).

Nutrient axis is compressed: the field is normalised by `NutrientMax` = 2.0 (`FloraSystem.cs:216`) but the reservoir only ever yields 0.072-0.54 raw (0.036-0.27
normalised; `EnvironmentFields.cs:148-151`, baseline 0.4, retention clamp 0.18-1.35). 41 of 92 non-decomposer individual species have a nutrient optimum >= 0.5,
median 0.45. They are all uniformly "under-fed", so the nutrient axis ranks no species apart; only litter hotspots exceed 0.27. Establishment factor at typical
0.2: burlmantle 0.18, hallowsting 0.09 (best case 0.14 at 0.27, below its minSuitability 0.2: it cannot establish except on litter hotspots), heddlelace 0.34.

Interaction matrix: 29 relations (`game/content/flora_interactions.json`); 18 name a moss or lichen, which are coverage species with no `FloraIndividual`, and the only
reader is `FloraSystem` (`:205,283,331`), so those 18 (all 4 `refuse`, most `benefit`) are inert. 11 live relations remain (8 compete, 3 benefit) for 106 species.

Fauna: 17 species. Terrestrial detritivore/grazers (loamthread, rustcoil, marbleback, prismhopper, dewmantle, duskflicker, moonveil, coalback, ghostbristle) have
moisture optima 0.25-0.84, all feed on `detritus` with efficiency 5-12, so food is never a niche separator. Predators (rainspine, stiltclaw, stonebell, reedjaw)
have cap 8-18 and cooldown 25-70 d.

Never establish / always dominate by construction:
- hallowsting: effectively never (above). burlmantle/heddlelace only on litter hotspots.
- Fungi never go extinct: `SporeRain` re-seeds every scarce fungus (count < 3) roughly once per bio-day on the best of 12 random cells, for all 14 fungus species,
  ignoring the species pool and the pool-less load case (`FloraSystem.cs:606-636`).
- Fastest recruiters hit the per-species cap of 130 (`:1881`): brooklace (spread 3 d, 2 propagules), carrion_bell, dewbonnet/ghostbonnet, prismstar (life 60 d),
  coinrunner. Cap 130 x any 12 species fills the 1600 soft capacity, so a dozen opportunists can crowd the island (inferred).
- Fauna: fastest breeders (duskflicker 7.5 d cooldown, prismhopper 10 d, siltshield asexual 7.5 d) fill the global budget first (E1).

---------------------------------------------------------------------------------------------------------------------------------
## 4. Life cycle and matter loop (Q4)

Flora: Establish (`:1976-1991`) -> logistic growth r = rate x suitability x 1/(1+sens x crowd), limited by nutrient take at the nearest cell only (`:412-440`) ->
crowding decline above `crowdingLimit` (`:443-451`) -> shedding to litter (`:464-473`) -> senescence at lifespan x [0.8,1.2] (`:475-482`) -> death (`:484-492`) ->
`Kill`: herbs/shrubs/trees become persistent `DeadPlant` corpses with fixed stage half-lives (`DeadFlora.cs:122-128`), others go straight to litter/nutrients (`:1994-2013`).
Fruit: `ReproductionSystem` (10 woody + cinderberry) -> fruit litter + `SeedBank` lots (540 d half-life, 5-day germination attempts, `Reproduction.cs:45-79`). Everything else
spreads by direct propagules (`:517-535`). Moss/lichen/algae live on rasters; moss death returns biomass to litter (`MatRules.cs:280,333`).
Decomposition: litter half-lives fine 20 d, coarse 140 d, fruit 6 d, x environment (0.35+1.15 moisture)(1.15-0.3 light), x decomposer bonus up to 4x
(`LitterSystem.cs:111,122-129,142-172`); mineralised mass x `nutrientYield` 0.6 goes to the nutrient cell (`:181`).

Is the loop closed? No, in three places:
1. Creation: nutrient return per biomass exceeds uptake. Uptake = `nutrientPerBiomass` (npb, 0.05-0.65, `FloraSystem.cs:419`); return on death = litterFraction x 0.6 + (1-lf) x npb
   (`:2009-2011` + `LitterSystem.cs:181`). Herb (lf 0.85, npb 0.3): 0.55 vs 0.3 = 1.85x; tree (0.9, 0.24): 2.35x; moss (0.8, 0.15): 3.4x; lichen (0.8, 0.05): ~10x (but coverage does not
   take nutrients at all, `MatRules.cs:289` uses nutrients only as a growth multiplier). Shedding returns 0.6 x litter without a prior uptake charge. Fungi eat `need = grow x npb` litter and make `grow`
   biomass (`:427-431`): 0.45 litter -> 1.0 biomass, so decomposers amplify litter mass. Fauna waste adds `consumed x wasteFraction` nutrients, even when the meal was detritus that carried no nutrient
   credit (`FaunaSystem.cs:501`). External source: Pilot tree litter (deliberate, capped, `PilotTree.cs:439-454`) and the reservoir relax term (deliberate).
2. Vanishing: nutrients clamp at `NutrientMax` and relax toward the reservoir, so any surplus created in (1) is bled away (this is the only thing keeping (1) bounded). Eaten biomass and
   decayed seed mass disappear; the unused `Tally.DetritusOverflow` is never written (`Ecology.cs:39`). Litter `Deposit` is unbounded (`LitterSystem.cs:32-68`).
3. Decay of corpses ignores moisture, light, decomposers: fixed half-lives (`DeadFlora.cs:122-128`), unlike litter (`LitterSystem.cs:111`).
Net effect: fertility maps are a smoothed copy of litter maps, with plants thriving where litter is deepest; no species can be nutrient-starved for long.

---------------------------------------------------------------------------------------------------------------------------------
## 5. Population regulation (Q5)

Flora:
- Soft capacity 1600 / hard ceiling 2000 on total individuals; between them crowding limit scales to x0.5 (`FloraSystem.cs:1866-1871,1966-1971`). Birth lists are processed in
  ascending individual id (`:539-545`), so at the ceiling the oldest lineages claim every freed slot (cliff).
- Per-species cap max(60, 130) (`:1881`); climbers `PopulationCap`; `WoodyPopulationCap` returns `int.MaxValue` (`:69-72`) so tree/shrub numbers are bounded only by `minSpacing`.
- Self-thinning by crowding (`:443-451`), intraspecific competition 1.25 vs neutral 0.5 (`flora_interactions.json:2-3`): stabilising, so monoculture is unlikely.
Fauna:
- Regulators actually active: per-species `populationCap` (labelled "safety", `FaunaSystem.cs:685,709`), `maxLocalDensity` (`:693,713`), world `faunaBudget` hard stop on every birth (`:80,686,710`),
  habitat moisture/substrate/depth, old age, `dailyMortality`, starvation.
- Food never limits. Detritus bite per metabolism pass is rate x dt = 0.003/h x 0.143 h = 0.0004 mass for 8 energy (`FaunaSystem.cs:477-483`), basal drain 0.0001 per pass; a fauna stays at
  about 0.8 energy wherever any litter exists, and consumption is far below litter turnover. Predation does not regulate prey (E2). So populations climb to the cap and stay (static equilibrium).
- Starter populations are 3-15 per species; sexual species need a mate within `mateRadius` 0.6 m and a cooled-down partner (`:663-667`): 3 founders can fail to meet and age out (extinction is only
  reversible by player introduction, `Ecology/Introduction.cs:14-16`).
Cliffs: flora ceiling 2000; fauna budget (default preset 360, others 247-430) vs sum of caps 512 for the 17 species; `FloraSystem.SporeBankThreshold` 3.

---------------------------------------------------------------------------------------------------------------------------------
## 6. Fauna-flora and decomposer coupling (Q6)

- Herbivory: `GrazeFlora` removes `fl.Biomass` within 0.12 m (`FaunaSystem.cs:519-538`). Bite = 0.0005-0.0035 mass/h x 0.143 h per pass, about 0.0003 against plant biomass ~1: the plant state changes
  but negligibly, and animals do not steer toward plants/lichens/fungi because `FoodAt` only knows detritus, fauna and field resources (`:373-395`; `Fields.Resource("flora:plant")` is null,
  `EnvironmentFields.cs:40-43`). Flora to fauna: none beyond the above; fauna habitat has no vegetation term.
- Pollination/dispersal: none. `rp.Dispersal` "animal" is only a distance distribution (`Reproduction.cs:151-156`); no fauna is ever consulted.
- Decomposition: real coupling. Fungi near a cell raise fine/coarse/fruit decay up to 4x (`LitterSystem.cs:142-172`) and consume litter into biomass (`FloraSystem.cs:424-432`); fauna eat the same fine/fruit pools
  (`LitterSystem.cs:209-217`) and leave waste nutrients; corpses and litter feed the nutrient field that plants read. But corpses decay independent of any of it (E9), and fauna are not part of any
  flora loop.
- Moss/lichen/algae couple only through light/moisture/nutrient samples (coarse fields) and by dropping litter on death; bogglass/blue_sundew add a moisture addend (E6).

---------------------------------------------------------------------------------------------------------------------------------
## 7. Long-run risks (Q7)

- Static equilibrium (most likely): every driver is time-invariant (light, topsoil, drainage), food is non-limiting, and fauna sit on caps. After a few hundred bio-days, population curves go flat.
- Predator lock-out and predator decline: predators breed at 25-70 d cooldown into a budget already consumed by prey (E1); with bites rarely lethal (E2) the system is more "frozen" than collapsing.
- Flora fills to 2000 and stays; turnover only through senescence; first-id bias (E5).
- Trait homogenisation: blending inheritance halves variance each generation (E7).
- Moss bubble drift: permanently saturated moisture addend (E6).
- Extinction spiral: not found for flora (fungi get spore rain; others re-seed). For fauna, only stochastic small-founder loss and pond-drying stranding (x6 energy stress, `FaunaSystem.cs:431`).
- Validation caveat: headless `Step` leaves water frozen (above), and the unit tests referenced in CLAUDE.md were deleted upstream (ledger D2), so use `--soak` (`game/App/Main.cs:114-141`) for all items below. Its autosaves
  and diagnostics land in `<soakDir>/saves` and `<soakDir>/diagnostics`; per-species counts and deaths-by-cause come from `Ecology/Stats.cs:5-14` and `EcologyTally` (`Ecology/Ecology.cs:16-39`).

---------------------------------------------------------------------------------------------------------------------------------
## 8. Efficiency (Q8) - findings (details in E12-E15)

- Flora tick re-evaluates every individual every 0.06 bio-day although inputs change at the 0.03 bio-day environment cadence and plant growth rates are about 0.2/day.
- `Suitability` per call: iterator-allocating `Props.HabitatTagsAt` (`FloraSystem.cs:189`, `World/Props.cs:252-258`), 3-4 linear prop scans via `SubstrateAt`, loop over all 29 relations in `ProximityBonus` (`:283`).
- `CanEstablish`: O(N) `WoodyPopulation` count whose result is unused because the cap is `int.MaxValue` (`:1894-1903`); a LINQ pass over 125 species per woody call (`:1908-1912`).
- Canopy shade rebuilt on any flora membership change (`:93-95,583-586`): each Establish bumps `Version`, so the next `Suitability` in the birth loop rebuilds the woody list (O(N)) and shade grid.
- `AmbientGroundCover.TargetAt`: 4 rings x 12 directions of water probes (`:63-79`) plus a 1.35 m flora query (about 17 % of the island) for each of 665 cells every flora tick, while the cover has a half-life of 0.8-4.5 bio-days.
- Allocations in loops: `new (double,double[])[]` + sort per decomposer take (`LitterSystem.cs:224-230`), `CellsInRadius().ToArray()` per deposit (`:49,75`), `new Dictionary` and `.Where().ToList()` per
  metabolism pass (`FaunaSystem.cs:419,452`), `_nb.ToArray()`/`OrderBy` per mating attempt (`:664-666`), `_nb.Count(x => ...)` closures in `FoodAt` (`:379,383`).
- Index rebuilt every behaviour step regardless of the `IndexRebuildPeriod` gate (`FaunaSystem.cs:155` vs unconditional `:290`); `FaunaPopulation.Remove` rebuilds the whole index on every death (`Fauna/Fauna.cs:79-84`).
- Sorted-list inserts: `DeadPlantPopulation.Add` sorts all corpses per death (`DeadFlora.cs:48`), `SeedBank.Deposit` linear-searches and sorts all lots per new lot (`Reproduction.cs:30,35`), `FloraPopulation.Remove` is `List.Remove` O(N) (`Flora.cs:254`).
- Per-individual tracking that could be aggregate: herbs at 1600 individuals is the main cost; the ambient ground cover pattern already shows the aggregate route, but replacing herbs by aggregates is a design change, not recommended here.

---------------------------------------------------------------------------------------------------------------------------------
## RANKED DEFECTS / OPPORTUNITIES

Ranking is by what a player watching the world sees, then by cost to fix.

### E1. World fauna budget is a first-come birth lock-out; caps are the only regulator
- Evidence: `FaunaSystem.cs:80` (`AtFaunaBudget`), `:686` and `:710` stop all births at budget; `:709,685` per-species cap is the real limiter; `game/content/presets/default.json` faunaBudget 360 vs sum of
  `populationCap` over the 17 species = 512 (19+29+58+51+29+26+29+38+29+32+83+8+14+22+19+18+8); predator caps 8-18 and cooldowns 25-70 d vs prey cooldown 7.5-30 d. Births iterate in id order (`:638`).
- Consequence: fast breeders (duskflicker, prismhopper, glintfin, emberglass) take the budget; predators and slow species (stonebell, rainspine, glasscoil) cannot replace losses and drift out; at the cap all curves are
  flat. Mix at day 300 depends on id order, not ecology.
- Remedy: in `CanReproduce` (`:701-716`) and the births loop (`:683-694`) replace the hard `AtFaunaBudget` stop with a density-dependent birth probability
  p = clamp((1 - total/budget) x 1.5, 0, 1) drawn with `Rng.HashUnit(seed, ReproStream, a.Id, tick)`, and exempt a species while `Count < reserve` where reserve = max(4, starter count). Raise per-species `populationCap` semantics to
  "safety only" by setting content caps about 2x the expected density. Add `Species.ReservedShare` to `FaunaSpeciesDef` only if content needs it (else derive).
- Persisted state: no (derived each lifecycle pass). Changes future trajectories only.
- Validate: soak at default preset, seeds 804584441 and two others: every starter species `Count >= 3` at day 400; `AtFaunaBudget` true for < 50 % of lifecycle passes; predator births > 0 per 100 days.
- Size: S-M. Files: `Fauna/FaunaSystem.cs`; optionally `Content/Definitions.cs`, `game/content/presets/*.json` (budgets).

### E2. Predation is an energy tax, not a death source
- Evidence: bite size = `ratePerHour/3600 x MassScale x dt` with dt = 514 bio-s (`FaunaSystem.cs:477`) = 0.0009 for rainspine on prismhopper; prey mass 0.004 (`prismhopper.json` body.massAtMid); the kill branch requires
  `take >= biomass` or `take >= prey.Energy` (`:591-599`), otherwise only `prey.Energy -= take` and the predator gains take x efficiency 11 = 0.0094 (more than 10x its 0.0007 basal drain). Same for stonebell/stiltclaw/reedjaw.
  Corpses from predation exist only if prey energy has already fallen below the bite (inferred: check `Tally.Species[*].DeathsByCause["predation:*"]`).
- Consequence: predators never go hungry when any prey is within 0.1-0.3 m, so they do not track prey numbers; prey are never top-down regulated; no visible hunting, chases or booms.
- Remedy: make `rate` a throughput of killed biomass: in `GrazeFauna` kill with probability min(1, want / biomass) per pass (keyed `Rng.HashUnit(seed, hash("fauna.kill"), hunter.Id, tick)`), award `biomass x efficiency` on a kill and nothing on a
  miss; keep the one-kill-per-pass budget (`:473,599`) and the `IsEdiblePrey` window (`:546-556`). Long-run kill rate then equals `want/biomass` per pass, preserving the intended throughput. Lower efficiencies (11-12 -> about 6) so predators
  starve when prey density is low. Add prey-scarcity to predator breeding via energy only.
- Persisted state: no.
- Validate: soak: predation deaths > 0 and < 30 % of all prey deaths; predator mean energy correlates with local prey count; no fauna species reaches zero within 600 bio-days; no predator-prey extinction before day 300.
- Size: M. Files: `Fauna/FaunaSystem.cs`, `game/content/fauna/{rainspine,stiltclaw,stonebell,reedjaw}.json`.

### E3. Matter/nutrient loop creates mass (1.9x-10x return per uptake) and hides it behind the relax sink
- Evidence: `ecology.json` `detritus.nutrientYield` 0.6; `LitterSystem.cs:181`; uptake `FloraSystem.cs:419-437`; death return `:2009-2011`, corpse return `DeadFlora.cs:151-152`, shedding `:464-473`; fungus yield `:427-431`;
  fauna waste `FaunaSystem.cs:501`; species npb range 0.05-0.65 (`game/content/flora/*.json`, `growth.nutrientPerBiomass`). Moss/lichen take no nutrients (`MatRules.cs:289`).
- Consequence: litter-rich cells over-fertilise; plants cluster where litter is, nutrient maps = litter maps; the reservoir relax term (`EnvironmentFields.cs:166-175`) silently deletes the surplus, so the ecology cannot be reasoned about
  or tuned by matter budget; `NutrientsFromDecay` in the tally exceeds `NutrientsUptake` by about 2x.
- Remedy A (content only): lower `detritus.nutrientYield` to about 0.25 (the lf-weighted mean npb), raise `nutrients.baseline` to about 0.6 to keep productivity. Remedy B (exact): carry a nutrient content with each litter cell
  (`FineNutrient`, `CoarseNutrient`, `FruitNutrient` arrays in `LitterSystem`) credited from `sp.NutrientPerBiomass` on shed/death and debited proportionally on decay/take; fauna waste returns what was taken.
- Persisted state: A no; B yes (new fields in `LitterPayload`, `WorldSerializer.cs:60-66`; optional with zero default, so saves stay loadable; recommend schema bump only if made mandatory).
- Validate: soak `EcologyTally` ratio (`NutrientsFromDecay + NutrientsDirectReturn + NutrientsFromWaste + NutrientsFromDecomposers`) / `NutrientsUptake` in [0.8, 1.3] after day 200 (currently about 2-3, inferred); no species drops to zero flora count from nutrient starvation.
- Size: S for A, L for B. Files: `game/content/ecology.json`; B also `Ecology/LitterSystem.cs`, `Flora/FloraSystem.cs`, `Flora/DeadFlora.cs`, `Fauna/FaunaSystem.cs`, `Persistence/WorldSerializer.cs`.

### E4. No temporal drivers: light, moisture, temperature, season are all static
- Evidence: `EnvironmentFields.cs:127-129` (light is a pure function of terrain/props/Pilot canopy, recomputed only on prop change); `DayNightClock.cs:19-27` (presentation only); `Hydrology.cs:333-337,358-365` (moisture targets fixed by the water table);
  no temperature/season/weather code anywhere in `src/Vivarium.Sim` (grep).
- Consequence: after the initial transient nothing cycles; no flush of spring fungi (`ashcomb` is tagged `spring_flush` but nothing to flush it), no dry-season die-back, no blooms. The world reads as a still life at 1x for hours.
- Remedy: add a deterministic seasonal phase `s = BioDays / YearBioDays` (e.g. 90 bio-days, a constant in `EcologyConfig`, content key `season.lengthDays`). Apply (a) moisture: lower `groundwaterTarget` floor and wetting by +/-0.12 in `CoupleMoisture` (`Hydrology.cs:337,358`);
  (b) light x (0.85 + 0.15 sin) in `EffectiveLight` (`FloraSystem.cs:141-180`); (c) growth rate x (1 + amplitude x cos(2pi(s - center))) with optional per-species `growth.seasonCenter`/`seasonAmplitude` (`:414`), amplitude default 0 so existing species stay unchanged
  until authored; (d) spring-flush fungi (tag `spring_flush`) get +amplitude. State derives from `Clock.BioSeconds` (already saved), no new persisted field.
- Persisted state: none. Digests change.
- Validate: soak: autocorrelation of `MeanMoisture` and total flora biomass at lag = year length > 0.3; per-species flora count never below the pre-season minimum by more than 40 %; `CheckInvariants` clean.
- Size: M. Files: `Fields/EnvironmentFields.cs`, `Water/Hydrology.cs`, `Flora/FloraSystem.cs`, `Content/ContentLoader.cs`, `Content/Definitions.cs`, `game/content/ecology.json`.

### E5. Flora ceiling/cap cliffs and id-ordered first-come recruitment
- Evidence: `FloraSystem.cs:53-54` (1600/2000), `:1869-1877` hard stop, `:1881-1886` per-species cap 130, `:1966-1971` density scaling; birth loop in id order `:539-545`; seed bank also routed through `CanEstablish` (`Reproduction.cs:67`); `SporeRain` bypasses pool (`:606-636`).
- Consequence: when the ceiling is reached the oldest plants' offspring take every freed slot; recruitment of everyone else stops; richness slowly narrows to early-id lineages; abrupt on/off population behaviour instead of smooth density dependence.
- Remedy: (1) in `CanEstablish` replace the `>= ceiling` return with acceptance probability 1 - SmoothStep(capacity, ceiling, total) drawn from `Rng.HashUnit(seed, "flora.establish.gate", Hash(q), tick)`; (2) order `births`/`_buds` by `Hash(seed, tick, parentId)` instead of id (`:539`); (3) per-species cap = max(25, 0.65 x ceiling / pool size) using `w.SpeciesPool.Ids.Count` (null-safe).
- Persisted state: no.
- Validate: soak: share of flora ticks with `total >= 2000` < 10 %; species richness (species with >= 1 individual) at day 400 >= 80 % of day 50; Berger-Parker (max species share) < 0.12.
- Size: S-M. Files: `Flora/FloraSystem.cs`, `Flora/Reproduction.cs`.

### E6. Coverage moisture addend never decays and is persisted
- Evidence: `MatRules.cs:309-310` adds `moistureFeedback x B x dt` per fine cell per step (bogglass_moss 0.015, blue_sundew 0.006: `game/content/flora/*.json` mat block); `CoverageSystem.cs:241-245` and
  `CoverageEnvironment.cs:55-59` `Values[c] += amount` with no decay or clamp; read at `CoverageEnvironment.cs:178`; persisted `WorldSerializer.cs:105,193,341`. A 0.25 m cell holds 156 fine cells: about 0.07-0.1 per step when covered, i.e. reaches >1 in about 10 steps (0.6 bio-day) (inferred).
- Consequence: wherever a bog moss or sundew ever grew, all moss/lichen sampling that cell sees moisture 1.0 forever (even after the host dies), so mosses creep onto drier neighbours and persist in places that never support them.
- Remedy: in `CoverageSystem.RunStepPart` (`:330`) multiply `MoistureBonusOf(_w).Values` by exp(-dtDays/3.0) once per mat step; clamp `Add` to <= 0.35; divide `amount` by the fine-cells-per-coarse-cell (156) in `AddMoistureBonus`. Add a `CheckInvariants` line (`VivariumWorld.cs:322`): max bonus <= 0.5.
- Persisted state: same format, but old saves contain saturated values that will decay after load (no schema change; note in release notes).
- Validate: soak: max(MoistureBonus) <= 0.35 and mean falls after moss removal; coverage cells of bog moss vs `Moisture > 0.75` overlap > 0.9.
- Size: S. Files: `Coverage/CoverageSystem.cs`, `Coverage/CoverageEnvironment.cs`, `World/VivariumWorld.cs` (invariant).

### E7. Blending inheritance collapses trait variance; selection is absent
- Evidence: `Genetics/Genetics.cs:74-79` midpoint of the parents; mutation: one event with p=0.10, one trait of six, sigma = 0.12 (`:84-94`, `genetics.json`); founders 0.07 variance (`fauna/*.json` `initialVariance`). Equilibrium sd about sqrt(2 x 0.0144 x 0.1/6) = 0.022 versus 0.07 at start; 5 generations reach it. Food is non-limiting (section 5), so `size` and `metabolic_efficiency` carry no fitness.
- Consequence: within a couple of in-game months every beetle of a species looks identical (hue, ornament, pattern, appendage) - the visible variation the genetics trait list exists to provide disappears.
- Remedy: `Inheritance.CreateOffspring`: per-trait pick from parent a or b (`rng.NextDouble() < 0.5`) then add small Gaussian (sigma 0.02) to every trait; raise `mutationProbability` to 0.25 with a magnitude 0.08. Make `size`/`metabolic_efficiency` matter (via E1/E2 food limitation).
- Persisted state: no (same `Genome.Traits` array; existing genomes untouched).
- Validate: soak: per-species trait standard deviation (add `StdTraits` to `FaunaSpeciesStats`, `Ecology/Stats.cs:6`) stays >= 0.05 after `MaxGeneration >= 10`.
- Size: S. Files: `Genetics/Genetics.cs`, `game/content/genetics.json`, `Ecology/Stats.cs`.

### E8. Interaction matrix is mostly inert (18 of 29 relations name coverage species)
- Evidence: `game/content/flora_interactions.json`; readers only in `FloraSystem.cs:205,283,331`; coverage species have no `FloraIndividual` (`CanEstablish` refuses them, `:1863-1867`). Live: 8 compete, 3 benefit.
- Consequence: moss versus moss, lichen versus moss exclusion (all 4 `refuse`) never happen through this file; interspecific competition is uniform 0.5 for 100+ species.
- Remedy: (content, S) retarget the 18 relations to species with individuals or delete them; add 25+ relations among individuals (shade tolerators vs sun species, nitrogen-rich shrubs vs ruderals). (code, M) implement `refuse`/`compete` for coverage in `MatRules`/`LichenRules` by reading `CoverageSystem` occupancy and the same JSON.
- Persisted state: no.
- Validate: `HabitatSurvey.Snapshot.Overlaps` and soak richness; unit count of live relations >= 40.
- Size: S content / M code. Files: `game/content/flora_interactions.json`; optionally `Coverage/CoverageSystem.cs`, `Coverage/Rules/MatRules.cs`, `Coverage/Rules/LichenRules.cs`.

### E9. Corpses decay on a fixed clock, independent of environment and decomposers
- Evidence: `Flora/DeadFlora.cs:122-130` half-lives in bio-days, no moisture/light/fungus term; litter has them (`LitterSystem.cs:111,119,142-172`).
- Consequence: a dead tree on a dry ridge rots at the same pace as one in a hollow with bracket fungi; fungi spread around corpses with no effect on how fast they vanish.
- Remedy: expose `LitterSystem.DecayFactor(int idx)` (the `environment x DecomposerBonus.Coarse` product, `:111-119`) and multiply `lost` in `DeadFloraSystem.Step` by `Math.Clamp(factor, 0.3, 3)` using the corpse cell.
- Persisted state: no (`DeadPlant` fields unchanged).
- Validate: soak: corpse mean lifetime in cells with fungus is shorter than in cells without by 20-50 %.
- Size: S. Files: `Ecology/LitterSystem.cs`, `Flora/DeadFlora.cs`.

### E10. Nutrient axis compressed; hallowsting cannot establish; nutrient preferences do not discriminate
- Evidence: normalisation `FloraSystem.cs:216` (divide by `NutrientMax` 2.0) versus reservoir 0.072-0.54 raw (`EnvironmentFields.cs:146-151`); 41 of 92 species have optimum >= 0.5 (median 0.45); hallowsting best-case establishment 0.14 < minSuitability 0.2 (hard fail); same divisor in `FloraSpeciesPool.cs:55` and `AmbientGroundCover.cs:73`.
- Consequence: the pool and starters never show hallowsting; most species are equally "hungry"; the only nutrient variation that matters is litter hotspots.
- Remedy: normalise by `NutrientBaseline x 1.5` (0.6) and clamp to [0,1] at the three call sites (single helper `EcologyConfig.NutrientScale`); re-derive hallowsting's optimum to <= 0.55 if still marginal. Verify with `HabitatSurvey` establishment maps before/after for the 20 widest species.
- Persisted state: no.
- Validate: soak richness unchanged or higher; `HabitatSurvey` favourable-cells for hallowsting > 5 % of the island; distribution of nutrient factor across species has stdev > 0.2.
- Size: S-M (risk: shifts every species; re-seed the preset starters). Files: `Flora/FloraSystem.cs`, `Ecology/FloraSpeciesPool.cs`, `Flora/AmbientGroundCover.cs`, `Content/Definitions.cs`.

### E11. Duplicate niches: 5 exact pairs plus a triple
- Evidence: section 3 (dewbonnet = ghostbonnet; frosttussock = plumegrass = starmoss; glowbriar = lanternbrush; lanternsedge = ringreed; moonfern = veilfern).
- Consequence: identical growth/competition numbers, so the visually different species act as colour variants of one species and always co-occur.
- Remedy (content only): offset each member's moisture optimum by 0.10-0.15, light by 0.1-0.15, change substrate affinities (e.g. starmoss rock 0.8, plumegrass gravel 0.9), and vary `growth.ratePerDay`/`lifespanDays` by 10-25 %. Keep geometry untouched (per ledger D5).
- Persisted state: no (species ids unchanged).
- Validate: rerun the pairwise niche script (this audit's method: moisture/light/nutrient optimum distance divided by mean tolerance + substrate vector) to reach distance > 0.5 for every pair; soak shows non-identical counts per pair.
- Size: S. Files: `game/content/flora/{dewbonnet,ghostbonnet,frosttussock,plumegrass,starmoss,glowbriar,lanternbrush,lanternsedge,ringreed,moonfern,veilfern}.json`.

### E12. Flora tick cost: full re-evaluation of every individual every 0.06 bio-day, with avoidable allocations
- Evidence: section 8; `FloraSystem.cs:69-72,1894-1903` (O(N) unused count), `:1908-1912` (LINQ per call), `:389-399` (dead colony bookkeeping), `:84-97,583-586` (shade rebuilt on any membership change; each Establish bumps `Flora.Version`, `Flora.cs:248`),
  `:189` (iterator allocation), `World/VivariumWorld.cs:282-311` already shows the per-cell cache pattern keyed on `Props.Version`.
- Consequence: the dominant per-tick cost scales with flora count (target 1600+); this is the item behind long batches and frame spikes at 8x-16x speed.
- Remedy (in order): (1) delete `WoodyPopulation`/`WoodyPopulationCap` use in `CanEstablish`; precompute `maxSpacingByLayer` once at construction; (2) bump a separate `Flora.WoodyVersion` only when a woody individual is added/removed/changes `BiomassFraction` bucket (quantise to 1/16) and key shade on it;
  (3) cache `HabitatTagsAt` and `SubstrateAt` per environment cell keyed on `Props.Version` (reuse `_propSubstrateCells` approach); (4) cohort slicing: each flora tick processes individuals with `(id + tick/60) % 4 == 0` using dt x 4 (spread/shed cadence is already interval-based);
  (5) drop the colony/creep/slime code paths from the hot loop or guard by `sp.Colony != null || sp.CreepSpeed > 0` precomputed per species.
- Persisted state: none (per-individual `Age` still advances by the right dt; digest changes).
- Validate: soak `WorkloadBudget` warnings for `flora` (`Diagnostics/Diagnostics.cs:75-94`) and per-system mean/max ms (`SubsystemTimer`); target flora mean tick < 50 % of today, no new `Tally` drift (births/deaths per bio-day within 10 %).
- Size: M. Files: `Flora/FloraSystem.cs`, `Flora/Flora.cs`, `World/VivariumWorld.cs`, `World/Props.cs`.

### E13. Ambient ground cover recomputes expensive targets far more often than it changes
- Evidence: `AmbientGroundCover.cs:51-107` (48 `OpenWaterDepth` probes, 4 `new[]` per cell, 1.35 m flora query with `FloraOrThrow` per hit), run for all 665 cells every flora tick (`VivariumWorld.cs:239`), `halfLife` 0.8-4.5 bio-days (`:34`).
- Consequence: about 665 x (48 + 280 neighbour) = 220k operations per flora tick of pure background, none of it visible.
- Remedy: cache the water-margin boolean per cell keyed on a hydrology version counter (add `Hydrology.Version`, bumped on spring/tool/terrain change and every 0.25 bio-day quantised depth change), and process cells in 8 interleaved cohorts per flora tick with dt x 8; reduce the flora query radius to the actual max exclusion (0.69 m).
- Persisted state: `Cover[]` values are persisted (`WorldSerializer.cs:152`) but format unchanged; trajectory only.
- Validate: soak `flora.ambient` mean ms down by >= 5x; visual diff of cover (reference report) < 2/255 on static scenes.
- Size: S-M. Files: `Flora/AmbientGroundCover.cs`, `Water/Hydrology.cs`.

### E14. Smaller hot-path costs and dead plumbing (batch together)
- Evidence: unconditional `fauna.RebuildIndex()` (`FaunaSystem.cs:290`) defeats the 4-step gate (`:155`); `FaunaPopulation.Remove` rebuilds per death (`Fauna/Fauna.cs:79-84`); per-pass allocations (`FaunaSystem.cs:419,452,664-666,379,383`; `LitterSystem.cs:49,75,224-230`);
  `DeadPlantPopulation.Add` full sort per death (`DeadFlora.cs:48`) and `Remove` O(D); `SeedBank.Deposit` linear+sort (`Reproduction.cs:30,35`); empty `hydrology` slot (`VivariumWorld.cs:211`); unused `Tally.DetritusOverflow`, `ecology.json` decay/biofilm keys (`ContentLoader.cs:240`).
- Consequence: GC churn on the sim thread; no player-visible effect individually.
- Remedy: keep the line-290 rebuild only when `fauna.Version` changed since the last index (comment says it protects reload parity; version counter keeps parity); batch removals via `RemoveMany`; reuse `List<>`/`Dictionary<>` fields; binary-insert in `DeadPlantPopulation` and `SeedBank`; delete the no-op `hydrology` registration and unused config; remove `DetritusOverflow`.
- Persisted state: `Tally.DetritusOverflow` is serialised via `Tally`; removing it is safe for JSON deserialisation only if unknown members are ignored (check `WorldSerializer.Json`); else leave the property and stop reading it. Everything else none.
- Validate: soak: `fauna` category total ms down; Gen0 GC count per sim-hour lower; unchanged digests where the change claims to be behaviour-neutral (`Diagnostics`).
- Size: S. Files: `Fauna/FaunaSystem.cs`, `Fauna/Fauna.cs`, `Ecology/LitterSystem.cs`, `Flora/DeadFlora.cs`, `Flora/Reproduction.cs`, `World/VivariumWorld.cs`, `Ecology/Ecology.cs`.

### E15. Plankton and biofilm have no mortality or light/flow term (aquatic food is unlimited)
- Evidence: `Ecology.cs:83-86,102-113`: logistic growth to `capacity x depthFactor`, a permanent 2 % seed population (`b0 = max(field, cap x 0.02)`), no decay, no light; glintfin eats it at 0.005/h (`game/content/fauna/glintfin.json`) which logistic regrowth (1/day) easily exceeds.
- Consequence: pond fauna are cap-limited like land fauna, with no response to shading, algae blooms, or flow.
- Remedy: add `plankton.mortalityPerDay` (about 0.3) returning mass to litter in that cell and a light factor using `Fields.Light`; remove the permanent 2 % seed (use inflow from neighbours via diffusion).
- Persisted state: none (plankton field is already persisted; values will relax).
- Validate: soak: plankton mean and variance across wet cells; glintfin count varies with plankton > 0.5 correlation.
- Size: S. Files: `Ecology/Ecology.cs`, `game/content/ecology.json`, `Content/ContentLoader.cs`.

---------------------------------------------------------------------------------------------------------------------------------
## Suggested order
1. E6 (S, bug), E1, E2 (visible dynamics), E7 (S, visible variety).
2. E12/E13/E14 efficiency batch (no behaviour change claimed except digest).
3. E3A, E10, E5 (retune ecology; re-seed presets and rerun soak at 3 seeds).
4. E4 (seasons) after the equilibrium is no longer frozen by caps.
5. E8, E9, E11, E15 content/small code.
Re-run `--soak` with seeds 804584441 + two others at the default preset before and after each group; every item above has a soak metric listed.
