# Rendering overhaul integration â€” 2026-10-24

The implementation and native integration pass is complete and ready for the requested review/tweak pass. All 24 work packets have implementations in the local checkout. **Overall plan acceptance remains partial:** the mature reference fails the 30 FPS gate, several visual defects remain, and a valid historical whole-overhaul A/B is unavailable. This report does not declare photographic quality or the performance target achieved. The review package contains 11 successful native runs and 1,630 PNGs, plus 80 water motion JPGs.

## What changed

- Eight original ambientCG 4K surface sets, five channels per material, with source URLs, CC0 attribution, dimensions and SHA-256 hashes in `game/Textures/SURFACE_MASTERS.json`. Missing AO channels were derived from original displacement, not upscaled from low-resolution textures.
- Three authored 4096Ã—2048 float EXR enclosure radiance maps, blended with the existing procedural solar/canopy model. These are generated radiance assets, not photographed HDRIs or Cycles renders.
- Terrain/strata POM, original 4K surfaces, whole-domain floor detail built over loading frames, moisture-aware litter, gravel, fractured logs with real tunnels, and exact canonical rock support.
- Dynamic water impacts, coherent simulation clocks, capillary ripples, foam, meniscus, small rising plunge bubbles, and live water/field bindings for props.
- Rendered duckweed, benthic biofilm and filamentous algae layers, plus explicit slime optics.
- Closed botanical geometry, four rebuilt 2K shared leaf pools, dedicated pilot foliage material, layered bark, and three-dimensional fungal gills/pores.
- Fauna joint metadata and analytical articulation, folded isopod legs, thin-film chitin, wet/SSS tissues and refractive eye treatments.
- Guardian hair scattering approximation, directional coat relief, corneal/iris refraction, localized wetness/contact response, and preserved skeletal breathing/IK.

## Native and test evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Simulation and Godot client builds | Pass, zero errors; six existing warnings on full client rebuild | Per-run build logs |
| Integrated focused geometry/material tests | 113 passed, zero failed | `build/overhaul-checks/integrated-final-tests-02.log` |
| Forward+ spatial shader probe | 23 shaders rendered without shader errors | `build/overhaul-checks/shader-probe/` |
| Shared botanical texture bake | All four families report `FLORA_BAKE_OK`, actual 2K resources | `build/overhaul-checks/bake.*.log` |
| Final production world | 113 PNGs, stable source, zero engine errors; unchanged world digest | `build/photorealism/overhaul-release-world-01/run.json` |
| Night integration | 35 PNGs at light time 1020; three views plus motion; unchanged digest, stable source, zero engine errors | `build/photorealism/overhaul-release-night-01/` |
| Mature default reference | 81 scenes; 6.082 biological days; 380 flora / 86 fauna; unchanged digest, stable source, zero engine errors | `build/photorealism/overhaul-release-reference-02/` |
| Aquatic tissue fixture | 12 PNGs; all three uploaded layers; unchanged world | `build/photorealism/overhaul-aquatic-02/` |
| Guardian catalogue | Six archetypes, 180 PNGs with sharp detail shots, stable source, zero engine errors | `build/photorealism/overhaul-final-guardian-02/` |
| Sharp native fauna motion | Five families, 240 frames each; recorded no-DOF inspection attributes, gait phases and translation | `build/photorealism/overhaul-sharp-fauna-*-01/` |
| Dynamic water | Nine PNGs plus motion JPGs; impact history reaches eight active impacts | `build/photorealism/overhaul-final-water-01/` |

The aquatic fixture explicitly authors the three coverage layers in a disposable world. It proves renderer wiring and geometry; it does not prove natural six-day ecological establishment. The default six-day reference reports eight terrestrial coverage species and does not establish natural recruitment of those three aquatic layers. No ecology changes were made to force that result.

## Exclusions and limits

- `TerrainStaysSoilEvenAtMaximumRockExposure` fails on 281 of 1044 cells in the clean archived starting revision `e7d65714` as well as the current checkout. It was explicitly excluded from the focused test set. Ecology was not changed to hide it. Evidence: `build/overhaul-checks/baseline-terrain-test.log`.
- The first world diagnostic (`overhaul-world-01`) changed sources during capture and is excluded from acceptance. Aquatic01 exposed incorrect model-space POM rays; aquatic02 verifies that correction.
- Native inspection found sparse dark foliage flecks that remain visually unaccepted. Independent effects/lighting/SSS/AA probes did not establish a fix. Constant ALBEDO alone suppresses the flora-associated flecks; EMISSION/ROUGHNESS overrides do not. Validity masks find finite, nonnegative, nonzero ALBEDO in inspected flora. Range masks expose some bright plume tips above one; final diffuse reflectance is capped at .98, but this is not a claimed fleck fix. The precise interaction of dark pigment, shading and subpixel coverage remains unresolved. Separate flecks on a blue coverage patch persist with flora hidden. Evidence: `overhaul-constant-color-probe-01`, `overhaul-color-validity-probe-01`, `overhaul-reflectance-range-probe-01`.
- Standalone preview cameras inherited unfocused DOF. Guardian framing and preview-only DOF were corrected; guardian02 and all five sharp-fauna runs verify sharp material inspection. Production camera settings were preserved.
- Rocks retain the canonical support profile exactly. Their additional triangles subdivide planar fracture faces; texture/POM supplies fine relief. This is not a new high-detail scanned silhouette.
- Hair scattering is a bounded R/TT/TRT-inspired model, not a full spectral hair renderer. Caustics and screen-space/reflection effects retain their practical renderer limits.
- Pilot foliage is conspicuously pale in the overview and obscures some habitat views. Dense mature foliage also blocks the six-day water-margin view. Material/lighting balance and camera composition require human review; an occluded screenshot cannot establish water quality.
- Guardian material inspection is now sharp, but the coati still shows model seams and a smooth coat. Material implementation does not establish anatomical or photographic acceptance.
- Native runs emit a shutdown warning for seven leaked texture RIDs. No shader errors or runtime exceptions were recorded in the completed gates. The warning remains unresolved.
- **The mature reference fails the 30 FPS performance gate on the GTX 1080 at 1600×900, quality 1.** Across 7,290 measured frames: mean 20.0 FPS, minimum one-second window 14.0 FPS, p95 64.35 ms, p99 72.29 ms. Scene means range 13.8–33.1 FPS (median 19.9). These are capture measurements, not a complete gameplay-route benchmark. Performance acceptance remains open; no 30 FPS claim is made.
- The legacy `render_report.json` embeds stale assembly commit metadata (`1a17b2c…`) and its human warning strings lose decimal punctuation. Use the wrapper `run.json` actual checkout revision, before/after source SHA-256 manifests and DLL hashes for provenance, and numeric frame-time fields for measurements. The source was intentionally dirty; HEAD alone does not identify the rendered content.

## Review package and comparison limits

The filterable native gallery is `build/photorealism/review-2026-10-24/index.html`. The side-by-side comparison is `comparison.html` in that directory, with a JSON matching proof. It includes 81 matched scenes and zero exclusions, checking seed/tick/digest, quality/light, resolution and actual cameras. Its scope is the earlier successful late integration run versus the final world run. Raw shader TIME and initial CPU poses remain uncontrolled, so it does not establish exact whole-frame repeatability.

The final six-day reference fixes daylight at 420 seconds, waits for flora publication and locks available review material clocks. It preserves production exposure adaptation. `overhaul-release-reference-01` was stopped after discovering wall-clock lighting drift and is excluded.

 The historical named `baseline-v0.1.2` directory is absent. A byte-verified clean archive of the recorded starting revision `e7d65714` was imported, built and launched. Native gate `overhaul-baseline-world-02` exited0 but reported1007 engine-error lines (including unsupported canvas depth-texture use), so its34 PNGs are excluded from visual comparison. This clean Git revision is not a snapshot of any pre-existing uncommitted work. Use the earlier successful integration capture for a narrowly labeled local iteration comparison; no valid historical whole-overhaul A/B baseline is available.

The review order is canopy color/lighting and obstructed views, sparse foliage flecks, guardian surface/anatomy, then performance profiling and optimization. Further visual changes should follow the user review rather than additional speculative shader tuning. The full plan remains partially accepted until those issues and any explicit deviations are resolved.

All edits remain local and uncommitted. Existing editor sessions, unrelated dirty work, and historical worktrees are preserved.

Automatic approval review blocked removal of two completed review-worker lock directories. They remain untouched; all worker implementation scopes have been reconciled by the parent. This does not block native verification.

## User review adjustment: still terrarium air

The user rejected ambient plant motion as distracting in an enclosed terrarium. Production flora stem sway and leaf wind strength are now zero, and pilot-tree foliage uses its nonanimated material path. Authored curvature, growth and interaction/contact wobble remain. Existing gallery captures predate this review adjustment; they are retained as historical evidence. Client build verification is recorded in the task progress.
