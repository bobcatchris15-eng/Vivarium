# Digital Desktop Vivarium — Product Contract

Status: **authoritative, immutable for the first-draft effort.** Downstream work implements this
contract; it does not reinterpret it. Source: `docs/VivariumSpec.txt` (plan `vivarium-first-draft`).

## C1. Nature of the deliverable
- C1.1 A **native standalone Windows desktop application** (x86_64), distributed as a portable bundle.
- C1.2 **Offline operation**: no network access is required or attempted at runtime, for any feature.
- C1.3 This is a **first draft, not a prototype**. Every feature family below is implemented to a
  usable, verified level. Feature families may **not** be silently removed, stubbed, or deferred for
  expedience. A genuine blocker is recorded as an explicit human decision, never an omission.

## C2. World
- C2.1 A **persistent world**: the vivarium continues from where it was left, via saves.
- C2.2 **Versioned saves**: every save records a format version and the application version;
  older formats migrate explicitly, newer unsupported formats are refused before touching live state.
- C2.3 The world is a **floating island** whose footprint is a **regular hexagon 10–20 m across**
  (corner to corner; configurable, default 16 m).
- C2.4 The six sides are clean vertical **geological cross-sections** (soil-sample cut): readable
  strata — at least topsoil, subsoil, base material — presented as a deliberate specimen cut.
- C2.5 Water that reaches the boundary ends on the same clean cut plane, so the underwater cross
  section is visible from outside.

## C3. Observation
- C3.1 **Free-fly camera**, no avatar, usable **above and under water**, with adjustable speed from
  close microfauna inspection to fast island traversal, and focus/orbit on a selected target.
- C3.2 Crossing the water surface visibly changes the medium treatment, while water stays clear
  enough to inspect aquatic life.

## C4. Time
- C4.1 Default rate: **roughly one wall-clock hour per in-game week** (168 simulated hours per real hour).
- C4.2 Pause and bounded speed controls. Simulation is deterministic and independent of render
  frame rate, camera, and quality settings.

## C5. Design stance
- C5.1 **Non-loseable**: there is no fail state. Extinction or player experimentation can always be
  undone through ordinary UI (species catalog + reintroduction) without restart or file editing.
- C5.2 **Vibrant sanitized realism**: realistic forms and plausible ecology, presented with clean,
  saturated, well-lit color; no grime, gore, or crushed shadows. Organisms receive a readable visual
  scale-up relative to true scale (art direction, not simulation truth).

## C6. Simulation feature families (all required)
- C6.1 **Terrain dressing**: seeded organic terrain, rocks, fallen logs, gravel patches; placeable,
  movable, removable by the player; substrates independent from visuals.
- C6.2 **Hydrology**: fixed water table, spring sources, downhill surface flow, ponding, boundary
  outflow, soil-moisture coupling.
- C6.3 **Flora**: data-driven moss, lichen and small plant archetypes (initial library of 8) with
  habitat suitability, growth, nutrient uptake, propagation, hard substrate refusals, beneficial
  proximity, competition, death and litter return.
- C6.4 **Microfauna**: data-driven terrestrial and aquatic fauna (initial library: springtail,
  freshwater shrimp, triops, microminnow) with habitat, locomotion, diet, metabolism, lifespan,
  reproduction, schooling for microminnows.
- C6.5 **Genetics**: inheritable size and ornamentation traits, parental averaging, ~10 % mutation
  probability, bounded traits, visible phenotype, persistent lineage.
- C6.6 **Ecology**: unified detritus/nutrient cycle, diets linked to resources, resource-driven
  population pressure, reintroduction, statistics.

## C7. Player tools (all required)
Nutrient application; pick/remove plant; grab and release critter (with habitat validation);
pokin'-stick (fauna and flora responses); place rock, log, gravel; introduce flora and fauna from
the species catalog. Every tool is reachable from normal UI, shows target validity before commit,
and reports success or rejection.

## C8. UI
Clock HUD with pause/speed; tool palette; selection inspector with flora, fauna, genome/phenotype
and lineage detail; environment probe; species catalog; ecosystem statistics; world creation
(seed + parameters); settings (camera, quality); save/load with autosave; controls/help overlay;
optional debug overlays that never change simulation state.

## C9. Quality attributes
- Deterministic: same seed + same inputs + same simulated duration ⇒ identical authoritative digest.
- Render state is never authoritative simulation state.
- Low/medium/high quality presets that do not alter simulation.
- Structured logging to a user-writable location; mutable data only in user-writable locations.
- Automated verification of every family, plus a release smoke run against the packaged build.
- Third-party license notices shipped with the release.
