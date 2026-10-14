# Smaller surface-water quanta and basin overflow

Implemented directly in the shared checkout on October 11, 2026. This refines the persistent liquid described in `surface-liquid-rebuild.md`. The user's requirement is a small depression that retains a pool, levels when resting, and overflows through whichever rim is lowest.

## Coordinated resolution change

| Component | Before | Now |
| --- | --- | --- |
| Nominal carried volume | 15 ml | 5 ml |
| Nominal rest spacing | 24.66 mm | 17.10 mm |
| Solid-contact radius | 10.36 mm | 7.18 mm |
| Density kernel support | 60 mm | 41.60 mm |
| Cached solid geometry spacing | 25 mm | 12.5 mm |
| Reconstructed surface spacing | 12.5 mm | 8.5 mm |
| Surface reconstruction support | 40 mm | 27.73 mm |

Changing volume alone would leave an oversized pressure neighbourhood and could make individual drops disappear under the old reconstruction threshold. The density support, correction limit, artificial pressure, cohesion distance and surface kernel were scaled with the cube root of carried volume. The spring still delivers exactly its configured cubic metres per second, retaining the sub-parcel remainder. The fixed physical step remains 1/60 second. Rendering still receives only actual three-dimensional parcels.

Solid height and its gradient now come from the same cached four terrain/prop samples. This resolves smaller lips while avoiding five independent height queries for every contacting parcel. The particle density and correction passes use independent Jacobi slots with bounded parallelism and unchanged per-particle neighbour order. Neighbour hash lookups are shared by all parcels in an occupied bin; individual distance filtering happens in reusable independent lists. Positions and constraint arithmetic use the existing float-vector precision; carried volume, source remainders and budgets retain double precision. Completed step diagnostics publish with the corresponding fluid state rather than reading a worker in progress.

## Existing saves

An old 15 ml parcel splits into three smaller parcels on its first physical tick. Subdivision retains volume, source identity, source emission phase and the original velocity. Its offsets preserve its centre of mass before the normal gravity/contact step. The loader initially restores the exact archived state, verifies the saved digest, and only then refines it during simulation. Loading a paused save therefore does not modify its verified state. Saving the finer state and loading it again does not repeatedly split particles.

Regression coverage includes an actual `.vivsave` containing the old coarse parcel, checksum/digest verification before refinement, and the first physical step after loading. The player's autosave was reviewed through an isolated copy with transient settings and autosave disabled. Its SHA-256 still matches the original: `E6070D64CE7459D32C7C4D67C512B2A711DCBB85A18FF837BFC16C1CF79DC172`.

## Behaviour evidence

The small analytical basin has a 24 by 20 cm flat floor, a 45 mm low sill and 80 mm high rims. Its regression is mirrored east/west; no flow direction is prescribed to the solver.

- With 1.5 litres added and the source off for eight physical seconds, neither low nor high rim loses water. Nine sampled surface heights differ by 0.259 mm in the east fixture and 0.185 mm in the west fixture. The actual upper reconstructed mesh differs by 0.449 mm and 0.429 mm respectively across the central resting pool.
- A further 3.888889 litres enters over 28 seconds at 500 L/h. The lower outlet receives 0.540 litres in the east fixture and 0.565 litres in the mirrored west fixture. The higher rims receive zero. Total retained plus departed volume equals tool input plus spring inflow to floating-point tolerance.
- The A/B probe runs the preserved 15 ml solver and its original renderer against the same basin and inflow. After 40 seconds, the coarse version has not delivered 50 ml beyond the low sill. The finer version crosses that threshold at 36 seconds and delivers 0.760 litres beyond the low sill by 40 seconds, with zero crossing a high rim. After shutoff, central reconstructed surface variation is 10.85 mm for the coarse state versus 4.18 mm for the finer state. These are finite-resolution game fluids; the probe does not establish exact continuum basin capacity or millimetre-accurate weir hydraulics.

Raw evidence: `artifacts/fluid-resolution/pool-east-diagnostic.json`, `pool-west-diagnostic.json`, `comparison.json`, and the reproducible comparison project under `comparison/`. Backups of the five initial implementation/review files are retained under `before/`.

The first refinement test runs reused the earlier helper and updated the legacy `fluid-rebuild/bowl.txt` and `bowl-diagnostic.json` output paths. The final helper writes those rerun outputs under `fluid-resolution/`; the earlier rebuild document's bowl figures are historical, while the original 56-check TRX and visual capture directories remain separate.

The production Godot basin review uses valid game terrain resolution and a wider sloping basin, a lower east spillway and a descending channel into the standing pond. At 60 physical seconds, source input is 8.333333 litres, retained liquid is 6.193333 litres, pond transfer is 2.14 litres and boundary loss is zero. Pond transfer begins between the 42- and 44-second captures. The final bare-earth view disables only the fixture's decorative ground-cover renderers and fixes its initial soil moisture so the water surface is readable; the copied player-world review retains real plants, terrain, props and habitat.

Raw fixture evidence: `artifacts/fluid-resolution/pool-clear/report.json` and `fluid-00.png` through `fluid-60.png`. The preceding vegetated/bare-earth captures and invalid descriptor attempt remain separate historical runs; they are not overwritten.

## Live check and limits

The initial refinement preserved about 64 FPS but advanced only 8.33 physical seconds during a 20.16-second review. It was rejected as an acceptable live-clock result. Float-vector constraint arithmetic, independent Jacobi work, shared neighbour candidates and single-pass solid gradients restore real-time progress.

The final serial surface reconstruction review of the copied player world records 19.983333 physical seconds in 20.078272 elapsed seconds, 1250 drawn frames (62.26 FPS), 313 captures, 108 surface uploads and 60,176 final triangles. The fluid catches up after the old saved water initially settles. Video playback uses capture count divided by elapsed recording time. Raw evidence: `player-final/live-report.json` and `spring-fine-quanta.mp4`.

Surface geometry updates average 5.38 Hz in this close-source review, lower than the earlier coarser build's roughly 13 Hz. Physics keeps up, but finer mesh generation remains a rendering cost. A bounded parallel deposition experiment did not improve that cadence and was removed. Its separate report is retained under `player-parallel-surface/`; it does not describe an enabled feature. This short capture holds biological progression still to isolate water and does not establish a mature-world, GPU or whole-scene frame-time gate.

The current game/editor build succeeds with zero warnings/errors. All 60 final focused checks pass; the receipt is `artifacts/fluid-resolution/test-results/resolution-complete.trx`, covering liquid retention/overflow, both low-rim directions, coarse save migration, closed surfaces, pause/publication, existing water tools, luminous flora geometry/content and fruit recruitment regressions. No lighting implementation was changed in this refinement.

The source changes are in `LiquidDynamics.cs`, `SurfaceLiquid.cs` and `LiquidSurfaceMesh.cs`; the fixture and entry point are `LiquidReview.cs` and `Main.cs`. The running game must be stopped and run again to load the rebuilt C# assembly. No player process was forcibly restarted, no player save was overwritten by the review, and no release or remote branch was published.

Reproduce the visual basin with Godot user arguments `--liquid-review OUTDIR --pool`, or the copied player scene with `--liquid-review OUTDIR --save ISOLATED_COPY --live --motion`. Set `VIVARIUM_LANDSCAPE_SCENARIO=liquid-correction` and `VIVARIUM_BENCHMARK_USER_DIR` to an isolated output directory for separate logs as well.

The density neighbourhood and resolution reasoning follows the mass-weighted density constraints in [Macklin and Müller's Position Based Fluids](https://mmacklin.com/pbf_sig_preprint.pdf). The observations above are measurements of this implementation, not results claimed by that paper.

The subsequent terrain-feedback implementation is documented in [surface-water-bounded-erosion.md](surface-water-bounded-erosion.md). It adds mild, persistent, capped erosion from moving liquid contact; the fluid and pool representation remain the same.
