# Soil surface checkpoint — 2026-11-15

## Current representation

The v1 soil surface is a connected, seeded, terrain-aligned triangular sheet. Its canonical lattice spacing is at most 12.5 mm (the configured target); the default relief amplitude is 20 mm for newly configured or upgraded worlds. Height and triangle normal come from the same piecewise-planar surface. This replaces the rejected detached-ped approach: no individually separated soil blocks are used to suggest granularity. The physical signal is continuous, deterministic relief integrated with the terrain.

`Heightfield.Height` and `Heightfield.Normal` route through this surface when soil version 1 is active. Hydrology bed capture, surface-liquid support, erosion, terrain mesh generation, and ground-contact queries use those terrain queries. Thus the render mesh's detail selection cannot change physical height or water support. `SoilTileMesher` uses fine, medium, and coarse render meshes; it keeps every fine lattice sample at tile boundaries so adjacent detail levels can meet without cracks. Wet tiles stay fine. This is render LOD only, not a lower-resolution physical surface.

## Migration and integrity

Upgrade is applied after saved-state integrity checks. It preserves macro/sculpt height storage, records soil version/seed/amplitude, refreshes the water bed and terrain dependents, reseats placement, and checks that mobile and subsurface stored water are unchanged by the upgrade. The upgrade fixture reported `SOIL_UPGRADE_OK`: macro unchanged, trunk base 0.0646592 m, mobile water unchanged at 0.0001 m³, and subsurface water unchanged at 4.9876398 m³. Legacy load, write/read, and world validation logs also completed successfully (`LEGACY_LOAD ok=true`; `SOIL_WORLD_VALIDATION_OK`).

An initial regenerated-descriptor diagnostic measured a trunk-anchor shift of -0.0009274 m. The implemented migration consequently persists the original trunk anchor. A frozen pre-soil simulation assembly subsequently wrote a real tree-bearing legacy save; SoilUpgradeTest loads that file, upgrades it, and verifies the original trunk anchor, macro heights, stored water, new save/load digest, composed ground, and idempotency. The older missing-fixture diagnostic is superseded. A sculpt diagnostic records stale RootSample.GroundY values, but rendered root rings sample the current composed ground; the stored sample is used for outward orientation, so this alone does not establish detached root geometry.

## Native candidate/control sample

`perf-candidate/report.json` and `perf-legacy/report.json` are native captures from the same assembly (`a4c52304-fbe4-4e9b-83f6-88a4294431cb`), diameter 17, on the soil foundation experiment. The candidate used 1,224,178 terrain triangles across 369 tiles; the legacy control reported 1,621,888 triangles and no tile/build counters. Candidate versus legacy by matched view:

| View | GPU mean candidate / legacy (ms) | Draws candidate / legacy | Frame p95 candidate / legacy (ms) |
| --- | ---: | ---: | ---: |
| soil macro | 6.13 / 7.50 | 90 / 131 | 33.36 / 33.36 |
| soil oblique | 6.23 / 8.60 | 92 / 140 | 33.37 / 33.36 |
| litter macro | 6.66 / 7.85 | 83 / 121 | 33.35 / 33.35 |
| surface overview | 6.05 / 7.40 | 54 / 56 | 33.35 / 33.96 |

These are a narrow candidate/control observation, not performance acceptance. The report itself says no broad fidelity or performance acceptance; it is also a foundation experiment, not a user save or full-world acceptance run.

## Water transition and outstanding review

In `final-water.log`, the upgraded case's maximum sampled surface first-step delta is 0.0114416 m (about 11.44 mm), versus 0.0000893 m (about 0.089 mm) for legacy. At the settled 20-second comparison, the maximum is 0.0001382 m (about 0.138 mm) upgraded versus 0.0001004 m (about 0.100 mm) legacy. Mass-conservation errors were near machine precision in both. The first-step upgrade transient remains unresolved; conservation does not make that transient visually acceptable. No visual water acceptance is claimed.

The native actual-world captures are bare-soil shots only. They do not establish full-world surface fidelity, and the current soil visual review remains open. The completed material/mesh diagnosis and final actual-world-correct-gravel capture are described below; full planted-world and water-transition review remains separate. Flora/import working files are outside this checkpoint.


## Final material and mesh diagnosis

The real-save review uses a copy of the user's Terraced Cascades world (seed545553165), transient settings and separate review data. `actual-world-correct-gravel` is the final native pair. Soil relief and curled litter are visible; detached soil peds are omitted. The gravel material retains a soil matrix between exposed mineral crowns rather than replacing whole patches with a pale scan.

Sharp black rings persisted with litter hidden, sun shadows disabled, terrain albedo displayed as emission, and water hidden; hiding placed props removed them. Inspection then found that `PropMeshes.Pebble` used the outward right-handed cross-product winding from TriangleFacing, while Godot front faces are clockwise. Reversing that mesh's indices restores solid stone crowns in the matched native capture. All 32 variants are checked for outward Godot crown faces and the correct signed shell orientation. The trial replacement pebble shader was rejected and removed; original rock shading remains. This correction applies to gravel in legacy and integrated worlds. It does not alter unrelated flora geometry or the general TriangleFacing helper.

## Repeated performance observations

The first candidate run after its build is warmup evidence and is excluded from the repeated comparison. Retained interleaved runs are `perf-legacy`, `perf-candidate-2`, `perf-legacy-2`, and `perf-candidate-3`, all module a4c52304-fbe4-4e9b-83f6-88a4294431cb. Across the four views, candidate GPU means range 5.99-7.34 ms; legacy ranges 7.40-11.87 ms. Legacy between-run variance is substantial. Candidate transition maxima reach 68.15 ms (the other candidate maximum is46.45ms); worker construction for the overview is roughly300ms and the grouped vertex/index payload is29MB. P95 frame times are capped near33.3ms by30FPS, so they do not establish uncapped throughput. These bare fixture results support lower bounded rendering cost, not whole-world CPU, memory, tail-latency, or Radeon860M acceptance. The gravel winding fix followed these measurements; the fixture has no placed gravel.

## Reproduction

From the repository root, use the bundled Godot4.7.1 mono console binary with `--path E:/Vivarium/game --` before these application arguments:

```text
--soil-review E:/Vivarium/build/soil-review-new --soil-large
--soil-review E:/Vivarium/build/soil-review-old --soil-large --soil-legacy
--ground-review E:/Vivarium/build/soil-actual --load <COPY-OF-SAVE> --integrated-soil --ground-quick --ground-bare
```

Diagnostic-only flags are `--ground-no-litter`, `--ground-no-water`, `--ground-no-props`, `--ground-no-shadows`, and `--ground-unlit`. They record their state in the review JSON. Do not use a user save as a writable test destination. Physics and legacy fixture recipes are in `experiments/soil_world_validation/README.md`.

Final build/boot and isolated surface, tile, world, upgrade, and mature-water logs are in `build/soil-surface-20261115/`. This checkpoint establishes the connected ground architecture and the specific gravel-face repair. Full planted-scene visual acceptance, the migration's one-time physical pool transient, and whole-world performance remain open.
