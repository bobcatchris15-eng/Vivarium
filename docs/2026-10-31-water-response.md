# Water response investigation, 2026-10-31

Status: placement and shallow-current fixes validated; large-world real-time throughput still fails.

## Player-save evidence

Read-only copy: `build/water-response/player-snapshot.vivsave`, Terraced Cascades #1268,
seed 462591268. Original and copied autosave SHA-256 both
`875fc9eb9510ed68e66f2ae9d3927dc6ea4636ee2cc18471903cd01b091ea837`.
The live save was not edited or advanced.

- Calendar: 22.63988 days; physical fluid time: 101.98333 seconds.
- Pending input history: 1256.86855 seconds, containing 1276.42352 litres of spring input.
- Emitted input: 12.74792 litres; stored mobile water: 12.68590 litres.
- Infiltration: 0.05038 litres; evaporation: 0.01164 litres.
- Boundary outflow and groundwater recharge: both zero.
- Current two springs: 2000 L/hour each. The queued history preserves earlier rates and removed springs.

This snapshot establishes a solver backlog, rather than infiltration as the main cause of the
apparently stalled stream. The displayed calendar and mobile-water clock also run at different
rates; calendar weeks are not weeks of fluid integration.

## Changes

- Spring placement preserves the existing terrain instead of carving a 20 cm radius, 8 cm deep
  reservoir. The vent marker, source identity, rate editing and removal remain supported.
- Published shallow-water velocity uses the solved wet face area instead of a minimum 12 mm
  depth. This corrects optical advection and consumers of the published current.
- Solver transfers retain cell references and donor scales instead of repeatedly looking up
  endpoints and per-donor dictionary entries. Cached centre support and domain membership
  replace repeated immutable geometry queries. Infiltration factors cache until a geometry or
  provider change. Coordinate hashing and optional stage timing support further investigation.
- Simulation spacing remains 1.25 cm. No input history or stored volume is discarded.

## Validation

- 56 focused water, terrain-tool, resolution, response and persistence checks passed.
- Solution build: zero warnings/errors. Game build: zero errors, three existing warnings.
- Matched isolated solver probes use the copied save and its oldest queued source rates, 30
  steps of 1/60 second. Discarding the first 10 timings: baseline median 431.525 ms/step,
  candidate median 213.370 ms/step. These are bounded CPU measurements on the shared host,
  not whole-game frame-time or GPU acceptance. Final exported fluid states compare exactly
  equal, including cell volumes, fluxes, slopes, sections, clock, remainder and source fractions.
- Probe sources, logs and states are in `build/water-response/`.
- Production native tool/slider check: `build/water-response/native-placement-clear/`.
  A new 50 L/hour spring reaches 0.282 m at fluid time 1.55 s, 0.696 m at 4.033 s,
  and 1.383 m at 8.05 s. Backlog at the last checkpoint is 0.016 s. Slider editing from
  50 to 500 L/hour and source removal pass. No shader/runtime errors; existing reflection
  probe and shutdown texture warnings remain. The fixture suppresses surface dressing for
  inspection; screenshots show segmented thin-water presentation and are not visual acceptance.

## Remaining failure

The copied large world still needs approximately 213 ms for a 16.667 ms physical step,
roughly 13 times its real-time budget. The local optimizations do not clear the player's queued
history or prove island traversal at 1x. More work on solver data layout and execution is needed;
raising source rates or reducing infiltration does not repair this throughput failure.

Existing placed springs retain any terrain depressions already stored in the player's save.
The placement fix applies to future placement; no speculative terrain repair was applied to saves.
