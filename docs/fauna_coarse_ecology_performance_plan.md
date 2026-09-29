# Fauna coarse-ecology and performance plan

Status: implementation plan. Designed to be applied piecemeal; every numbered chunk should leave the build runnable and the existing species catalog usable.

## Intent

Keep the fauna visually diverse and behaviorally charming, but stop treating every species as a tightly balanced miniature food-web problem. Small fauna should usually be able to exist without player babysitting. Expensive biological accounting should become coarse and scheduled, while visible locomotion remains smooth enough to feel alive.

This plan supersedes the **population structure** direction in `docs/fauna_expansion_plan.md`: species identities and meshes remain, but per-species population caps and species-specific trophic dependencies should no longer be the primary population-control model.

## Target model

- Preserve the current fictional species, meshes, locomotion families, habitat preferences, and distinctive presentation.
- Collapse most feeding ecology into a few broad guilds: terrestrial grazer/detritivore, aquatic grazer/detritivore, flying grazer/scavenger, and predator.
- Let small fauna live on very small resource flows. Tune their metabolic demand downward before increasing detritus production.
- Make amphibian and other upper-level predators consume generic nearby prey biomass rather than requiring a particular named species.
- Keep an occasional concrete prey kill for visible ecological drama, but do not make predator survival depend on finding the correct species.
- Add a whole-vivarium fauna budget so adding species does not multiply the number of thinking animals without bound.
- Separate visible movement from expensive biology. Steering can run less often than rendering; metabolism/lifecycle can run much less often than steering.
- Ultimately settle biological accounts in staggered day/night accounting windows instead of continuously.

## Guardrails

1. Do not delete species merely to gain performance. Headcount and update frequency are the primary performance levers.
2. Do not increase global detritus production as the first fix for starvation. That also changes litter, fungi, decomposition, nutrients, and flora.
3. Every chunk below must have a focused test or diagnostic and be independently revertible.
4. Keep deterministic simulation behavior: keyed randomness, stable ordering, and save/load reproducibility must survive cadence changes.
5. Do not couple rendering to authoritative ecology. Render interpolation may hide coarse simulation steps, but must never become the source of state.
6. Existing saves do not need perfect balance continuity, but load/migration must not crash.

## Chunk 0 - Establish a repeatable fauna performance baseline

**Change**
- Add or expose a small deterministic benchmark scenario with representative low, medium, and high fauna populations.
- Record scheduler timing separately for `fauna.behaviour`, `fauna.metabolism`, and `fauna.lifecycle`.
- Record total fauna count, per-species counts, spatial-query counts if easy to expose, and simulation backlog.
- Include a no-fauna control with the same flora/water state.

**Acceptance**
- One command/test run produces comparable timing output before and after later chunks.
- Results are not committed as claims about every machine; they are a local regression baseline.

**Why first**
The scheduler already records system timings. Use that instrumentation before changing semantics so every later optimization can be measured instead of guessed.

## Chunk 1 - Add whole-vivarium fauna budgeting without changing diets

**Change**
- Add a world-level fauna budget in addition to existing species safety caps.
- Treat species caps as emergency/sanity limits, not the main carrying-capacity mechanism.
- Refuse reproduction/introduction that would exceed the world budget, with an explicit diagnostic reason.
- Make the budget configurable in the world descriptor/content rather than a magic constant in `FaunaSystem`.

**Acceptance**
- Existing species still reproduce below the world budget.
- Reproduction stops cleanly at the world budget.
- Manual introduction reports why it was refused.
- Save/load preserves the configured budget.


## Chunk 2 - Introduce ecology guild metadata

**Change**
- Add an explicit fauna ecology-guild field or equivalent stable tag.
- Initial guilds: `terrestrial_grazer_detritivore`, `aquatic_grazer_detritivore`, `flying_grazer_scavenger`, `predator`.
- Assign every current fauna species to exactly one primary guild.
- Keep existing species diet entries working during this chunk; guilds are metadata only.

**Acceptance**
- Content validation rejects missing/unknown guilds.
- Existing species catalog count and meshes are unchanged.
- A catalog test proves every fauna species has a valid guild.

## Chunk 3 - Make small-fauna energy budgets forgiving

**Change**
- Tune basal energy drain, hunger thresholds, and feeding rates for the three non-predator guilds.
- Start by reducing required intake substantially rather than adding more food to the world.
- Preserve relative differences where they create character, but target survival on sparse background resources.
- Add a long-run "benign vivarium" test in which representative small fauna can persist without player intervention.

**Acceptance**
- Small fauna no longer collapse rapidly in an otherwise healthy habitat.
- The tuning does not create runaway reproduction because population control is handled separately.
- Detritus generation, fungal food supply, and nutrient production remain unchanged in this chunk.

## Chunk 4 - Give grazer/detritivore guilds broad fallback diets

**Change**
- Allow terrestrial grazer/detritivores to treat detritus as their common fallback resource.
- Allow aquatic grazer/detritivores to use aquatic biofilm/detrital resources as common fallback food.
- Allow flying grazer/scavengers to use a cheap broad resource contract rather than a narrow named-species dependency.
- Preserve species-specific preferred resources as bonuses or steering attractors, not hard survival requirements.

**Acceptance**
- A species with no preferred resource nearby can still survive on its guild fallback when appropriate habitat resources exist.
- Preferred foods remain behaviorally meaningful.
- Resource consumption remains bounded and deterministic.

## Chunk 5 - Decouple predator diets from named prey species

**Change**
- Replace predator survival dependence on `fauna:<species-id>` with a generic prey-guild query.
- Define eligible prey primarily as nearby non-predator fauna within a sensible size/mass range.
- Compute local prey availability from existing spatial buckets; do not scan the whole fauna population.
- Keep the old named-prey parser temporarily for migration/backward compatibility, but stop using it in normal predator content.

**Acceptance**
- Stonebell/Rainspine/etc. can feed in a mixed small-fauna community without requiring a particular prey species.
- Predators do not consume other predators unless explicitly allowed.
- Aquatic/terrestrial medium constraints still apply.

## Chunk 6 - Preserve visible concrete predation as an optional event

**Change**
- When generic prey biomass is consumed, optionally select one eligible nearby concrete prey individual using deterministic ordering/randomness.
- Kill/remove that individual only when doing so is consistent with the amount of biomass consumed.
- Return uneaten organic mass to detritus exactly as the current predation path does.
- If no concrete victim is appropriate, permit abstract feeding against local prey availability rather than starving the predator.

**Acceptance**
- Predation can still visibly remove real animals.
- Predator survival is no longer brittle when the exact target species is absent.
- Biomass accounting cannot double-count abstract and concrete consumption.


## Chunk 7 - Lower fauna steering cadence without changing biology

**Change**
- Increase `Cadence.FaunaBehaviour` conservatively so animals reconsider direction less often.
- Keep authoritative positions advancing deterministically from the coarser steering decisions.
- Do not change metabolism/lifecycle cadence in this chunk.
- Add/adjust render interpolation so a lower steering cadence does not make movement visibly stepwise.

**Acceptance**
- Slow terrestrial fauna remain visually smooth.
- Fast/flying fauna do not tunnel through obvious habitat boundaries.
- Behavior timing in the baseline benchmark decreases materially.
- Deterministic tests produce the same result for repeated runs with the same seed.

## Chunk 8 - Split "movement" from "expensive decision"

**Change**
- Separate cheap locomotion integration from expensive habitat/food/schooling decision work.
- Cache a desired heading/behavior intent per individual.
- Refresh intent at a slower cadence; integrate movement from cached intent at a cadence appropriate for visible motion.
- Disturbance/stranding may force an immediate intent refresh so interaction remains responsive.

**Acceptance**
- Normal wandering does not perform food/habitat probe fan-outs on every movement update.
- Poking/grabbing/flooding an animal still produces prompt reaction.
- The benchmark reports decision work separately enough to verify the reduction.

## Chunk 9 - Stop rebuilding the whole fauna spatial index every behavior pass

**Change**
- Replace unconditional `Fauna.RebuildIndex()` at the start of every behavior step.
- Choose the simplest correct alternative: dirty-bucket updates for moved animals, or a lower-frequency rebuild if incremental maintenance would overcomplicate the index.
- Preserve deterministic neighbor-query ordering.
- Keep explicit full rebuilds after bulk mutations/load where they remain useful.

**Acceptance**
- Neighbor queries return current-enough positions for schooling, predation, and local density.
- Population/index invariant diagnostics remain clean.
- Behavior cost scales better with fauna count than before this chunk.

## Chunk 10 - Coarsen metabolism accounting

**Change**
- Move energy drain and food consumption off the current high-frequency metabolism cadence.
- Accumulate the inputs needed between accounting events: elapsed biological time, habitat stress exposure, and any cheap resource/opportunity summaries needed by the selected model.
- On accounting, apply the integrated energy change once.
- Keep starvation/death deterministic at the accounting boundary.

**Acceptance**
- Equivalent elapsed biological time produces approximately equivalent energy outcomes across old/new cadence in controlled tests.
- Feeding is no longer a whole-population operation several times per real second at default speed.
- Animals do not gain or lose energy from render/frame rate.

## Chunk 11 - Coarsen lifecycle separately

**Change**
- Move ageing, maturity, reproduction eligibility, cooldowns, and natural mortality to a coarse lifecycle accounting cadence.
- Do not combine this with metabolism implementation work unless the previous chunk is already stable.
- Use elapsed biological time explicitly; never assume one accounting event equals one fixed day.

**Acceptance**
- Maturity and cooldown tests remain correct under large elapsed-time steps.
- Population caps/budget are respected when many individuals become eligible in the same accounting window.
- Save/load in the middle of a cycle does not duplicate reproduction.

## Chunk 12 - Add an explicit biological accounting queue

**Change**
- Introduce a scheduler-facing queue/bucket mechanism for coarse biological work.
- Assign fauna deterministically to accounting slots by stable entity/species hash rather than random frame-time distribution.
- Give each slot a bounded amount of work so a single accounting boundary cannot create one giant hitch.
- Initially use the queue only for fauna metabolism/lifecycle; do not migrate flora yet.

**Acceptance**
- All living fauna are accounted exactly once per intended biological interval.
- Work is spread across slots with no large one-tick population spike.
- Pausing/saving/loading preserves or reconstructs slot assignment deterministically.


## Chunk 13 - Introduce the 15/5 minute presentation day

**Change**
- Add an explicit presentation day/night clock targeting 15 real minutes of day and 5 real minutes of night at normal speed.
- Keep this concept separate from render frame time and from authoritative biological elapsed time.
- Expose stable phases such as morning, afternoon, dusk, night, and dawn.
- Do not yet change flora or fauna behavior merely because the visual clock exists.

**Acceptance**
- The cycle is deterministic and inspectable.
- Pause/speed behavior is defined and tested.
- Existing simulation can run with the day/night clock present but otherwise inert.

## Chunk 14 - Bind accounting cohorts to day/night windows

**Change**
- Map deterministic accounting slots onto broad ecological windows instead of letting every organism settle at one instant.
- Permit some guilds/species to settle in late afternoon, some at dusk, and the remainder across the five-minute night.
- Respect dependency ordering where it matters: environmental/resource state before consumers; prey availability before predator settlement.
- Treat accounting time as scheduling, not as a claim that all biological change literally happens at night.

**Acceptance**
- No dawn "thundering herd" where the entire ecosystem updates on one tick.
- Dependencies are documented and tested.
- Every cohort has completed its required accounting before the next biological day begins.

## Chunk 15 - Let completed cohorts drive dusk/night spectacle

**Change**
- Add a presentation hook that marks a cohort/species as settled for the current cycle.
- Bioluminescent fungi/fauna or other nocturnal displays may activate after their relevant accounting is complete.
- Use the visual behavior to pull attention toward already-settled areas/species, but never expose scheduler state directly in the UI.
- This is optional presentation polish: the simulation must remain correct if the visual hook is disabled.

**Acceptance**
- A species cannot visually depend on uncommitted biological state.
- Bioluminescence/nocturnal activity can be tested independently from scheduler correctness.
- No simulation state is derived from glow/render state.

## Chunk 16 - Add weighted population pressure and predator sparsity

**Change**
- Extend the whole-vivarium budget with simple cost/weight classes only if raw-count budgeting proves too blunt.
- Small detritivores/grazers should be cheap; large amphibian predators should be expensive and therefore sparse.
- Prefer soft reproduction pressure near the budget plus a hard absolute ceiling over per-species micromanagement.
- Keep enough representation safeguards that one prolific species does not trivially erase the rest of the visible catalog.

**Acceptance**
- Mixed communities stabilize below the hard ceiling.
- Predators remain visibly uncommon without requiring fragile prey-specific tuning.
- Adding a new species cannot silently multiply the total fauna workload.

## Chunk 17 - Full ecological tuning pass

Run this only after the structural changes above are measurable and stable.

**Tune in this order**
1. Whole-vivarium target fauna headcount and hard ceiling.
2. Steering/decision cadence and visual smoothness.
3. Small-fauna basal demand and fallback feeding.
4. Reproduction pressure/cooldowns.
5. Predator demand and density.
6. Detritus supply only if the decomposition system itself is resource-starved after fauna demand is reasonable.
7. Species-specific preference bonuses for flavor.

**Target behavior**
- A planted/seeded healthy vivarium can support small fauna indefinitely without routine user feeding.
- Local crashes can happen, but they should arise from real habitat/resource changes rather than razor-thin default tuning.
- Predators can persist at low density over a generic small-fauna base.
- The user can mostly watch rather than service the food web.

## Chunk 18 - Remove compatibility scaffolding and document the final contracts

**Change**
- Once saves/content have migrated, remove unused named-prey assumptions and obsolete per-species balancing code.
- Update `docs/fauna_expansion_plan.md` with a short note pointing to this plan/final architecture.
- Document guild semantics, population-budget semantics, accounting cadence, and predator biomass rules near their authoritative types.
- Keep diagnostic counters for fauna population and scheduler cost; they remain useful after the refactor.

**Acceptance**
- No dead dual-path ecology remains.
- Content authors can add a fauna species by choosing a guild and presentation traits without constructing a bespoke food web.
- Tests describe the intended coarse-ecology contract rather than historical implementation details.

## Recommended application order

Apply chunks strictly in this order unless a measured result gives a reason not to:

`0 -> 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9 -> 10 -> 11 -> 12 -> 13 -> 14 -> 15 -> 16 -> 17 -> 18`

Natural stopping points are after chunks **1**, **4**, **6**, **9**, **12**, and **15**. Each of those leaves a coherent improvement even if later work is deferred.

## Measurement gates

After chunks 1, 6, 9, 12, and 17, rerun the Chunk 0 baseline and record:
- total fauna count;
- `fauna.behaviour` mean/max/total time;
- `fauna.metabolism` mean/max/total time;
- `fauna.lifecycle` mean/max/total time;
- overall scheduler backlog at normal speed;
- visible motion quality and any obvious hitches;
- 30+ biological-day survival/reproduction summary for representative guilds.

Do not declare a performance win from reduced fauna count alone. Report both **cost per individual** and **total cost at the chosen population budget**.

## Expected architectural end state

Fauna species remain visually and behaviorally distinct, but their ecology rests on broad, robust guild contracts. Most small fauna are inexpensive residents supported by generic detrital/grazing resources. Predators sit sparsely over that generic prey base. Movement remains visually continuous, expensive decisions are cached and refreshed less often, the fauna spatial index is not rebuilt needlessly, and biological bookkeeping is amortized across deterministic afternoon/dusk/night accounting windows.

The optimization goal is not to make the ecosystem less alive. It is to spend CPU on the parts the player can actually notice.

