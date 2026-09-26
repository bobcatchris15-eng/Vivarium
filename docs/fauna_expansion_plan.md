# Fauna expansion plan

## Goal

Expand the fictional ecosystem from seven fauna to seventeen while adding one horsetail-like wet-margin flora species. The expansion should fill missing food-web roles rather than merely increase catalog count.

## Added guilds

- **Moist grazers and decomposers:** Dewmantle (slug analogue), Rustcoil (millipede analogue), Loamthread (earthworm analogue).
- **Small surface predator:** Stiltclaw (harvestman analogue).
- **Flying biomass links:** Moonveil (moth analogue) and Duskflicker (midge analogue). These use the `flying` behaviour so water and terrain do not act as hard movement barriers.
- **Large damp-ground predators:** Stonebell (toad analogue) and Rainspine (salamander analogue).
- **Aquatic benthic grazer:** Glasscoil (freshwater-snail analogue).
- **Aquatic predator:** Reedjaw (predatory aquatic-insect larva analogue).
- **Wet-margin flora:** Ringreed, a jointed rhizomatous horsetail analogue.

## Simulation changes

1. Diet entries may target `fauna:<species-id>`.
2. Predation is deterministic: nearby prey are sorted by entity id before selection.
3. A consumed prey animal is removed after the metabolism pass so the fauna collection is never mutated during iteration.
4. Consumed biomass feeds the predator; uneaten organic mass enters the existing detritus pathway.
5. Flying fauna can cross otherwise impassable water while still preferring their configured moisture/substrate habitat.
6. Every new body plan has a dedicated procedural mesh model rather than using the generic ellipsoid fallback.

## Population structure

Keep upper trophic levels deliberately sparse. Detritivores, grazers and small flying insects should outnumber Stiltclaws, Reedjaws, Rainspines and Stonebells. Population caps remain per-species so individual guilds can fail or recover independently.

## Validation

- Content loader accepts and cross-validates fauna diet targets.
- Predator/prey medium mismatches fail content validation.
- Catalog/count tests update to 33 flora and 17 fauna.
- Species smoke tests seed prey for carnivores so each species can feed and reproduce in isolation.
- Add a direct predation regression test proving prey removal, predator energy gain and detritus return.
- All seventeen fauna species must produce a non-trivial procedural mesh.

## Deferred refinement

The first pass represents complete moth/midge life cycles as single populations. A later lifecycle campaign can split aquatic larvae, terrestrial adults, host-plant selection, metamorphosis and egg placement without changing the species identities introduced here.
