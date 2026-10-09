# Living landscape rendering overhaul — implementation plan

**Status:** Design and implementation plan only; no engine or rendering code changed by this document.  
**Prepared:** 2026-10-08 against `main` at `1a17b2c06e28776a7f650f73b4cac9cd931c3059`.  
**Target:** Godot 4.7.1 Forward+ / C# / .NET 8; Windows x64; GTX 1080 (8 GiB) and first-generation Ryzen; **1600×900, 30 FPS floor**.  
**Scope:** Visual asset production, plant geometry and growth appearance, vegetation lighting, spatial rendering and update costs, and terrain/organism integration. **Out of scope:** changing the ecology model, gameplay rules, hydrology solver, replacing Godot, and opportunistic species additions.  
**Execution style:** Independently mergeable, measurable increments, with regression evidence and a rollback path for each. This is a full draft of the system, not an MVP or a sequence of disposable vertical slices.

---

## 0. Objective, observed architecture, and non-negotiable constraints

Make a small, biologically active, sculptable hexagonal island look like a photographed, inhabited piece of nature at macro, organism, and whole-island scales. Density must come from layered, correctly lit, physically attached organic structures—not oversaturated green textures, fog, anonymous circular plant clumps, or screen-filling trickery. The island remains roughly 10–20 m across; fidelity within this limited space is more important than features for kilometer-scale worlds.

### 0.1 What is already here; reuse it

| Existing subsystem | Current location | Keep or extend |
|---|---|---|
| Authoritative, deterministic ecosystem and heightfield | `src/Vivarium.Sim/` | **Keep as authority.** Changes to appearance cannot change its digest or scheduling. |
| Procedural plant geometry, forms, metadata | `src/Vivarium.Sim/Geometry/OrganismMeshes.cs`, `OrganismMeshes.VisualBreakFlora.cs`, `Geometry/Form/*`, `FloraVisualCompiler.cs` | Keep working forms; augment visual derivation and asset interchange. Never silently replace approved botanical anatomy. |
| Species/material visual recipes | `game/Render/FloraVisualProfile.cs`, `game/content/visuals/flora/*.json` | Extend using schema versions and a complete catalog/fallback model. |
| Leaf material bake and shader | `game/App/LeafMaterialBaker.cs`, `game/Shaders/leaf.gdshader` | Keep 1K four-variant texture-pool workflow until measured need says otherwise. |
| General vascular-plant shader | `game/Shaders/flora.gdshader`; surface bindings `game/Render/FloraSurfaceProfiles.cs` | Harmonize with dedicated leaf shader without losing existing species. |
| Instanced live/dead flora renderer | `game/Render/OrganismRenderers.cs` | Extend existing species × morphology × geometry-tier MultiMesh batches; do not create a Node3D per leaf. |
| Moss/lichen/plasmodium coverage | `game/Render/CoverageRenderer.cs` | Keep occupancy and tiled build semantics; optimize publication and contact shading. |
| Anonymous groundcover and litter | `game/Render/AmbientGroundCoverRenderer.cs`, `SoilDetailRenderer.cs` | Unify visual-density decisions, eliminate double-filled ground and stale camera-local buffers. |
| Terrain and ecological texture masks | `game/Render/IslandRenderer.cs`, `game/Shaders/terrain.gdshader` | Keep sculptable top/walls, existing field textures, soil/moisture/litter/substrate mappings. |
| Atmospheric lighting | `game/Render/EnvironmentRig.cs` | Calibrate existing 15/5-minute presentation-only daylight/night rig, with quality-tier experiments. |
| Diagnostics and visual comparison | `game/App/FrameProfiler.cs`, `SmokeRunner.Reference.cs`, `scripts/compare-reference.ps1`, `docs/photorealism/` | Extend and preserve historical reference reports; do not claim historical scenes are current benchmarks. |

**Specific existing facts that change the plan:**
- Dedicated foliage already separates stem/leaf surfaces and carries stable per-blade `CUSTOM0`/`CUSTOM1` metadata. Its current three geometry tiers preserve identity/attachment, with projected-size selection and hysteresis. Improve this, don't replace it with a completely new per-leaf object system.
- `FloraRenderer` builds meshes asynchronously where possible, refreshes instances on a main-thread budget, and uses frustum tests; `CoverageRenderer` has worker-generated tiles and budgeted GPU commits; `IslandRenderer` reuses an `ArrayMesh` during sculpting after a documented buffer-churn problem.
- `SoilDetailRenderer` is camera-local with radius-limited crumbs and debris and already avoids refreshing when camera/terrain/litter are unchanged. Keep this optimization.
- Visual overhaul logs include an isolated, paused-scene p99 of 10.71 ms and a running-simulation p99 of 20.63 ms, but later mixed-frame results around 33.5 mean FPS and p95 over 53 ms were not cleanly isolated. **The latest sustained 30 FPS floor is unproven.**
- `docs/flora_visual_review_log.md` reports some pre-existing geometry-suite failures. Record and triage them instead of reclassifying them as success or silently rewriting assertions.
- Several visual plans already exist, especially `docs/ground_continuity_lifecycle_plan.md` and `docs/photorealism/flora_visual_system.md`. This document governs **render-only quality, data interchange, performance, and integration**. Ecology or litter-accounting changes require a separate explicit decision; never manufacture simulation biomass to improve a screenshot.

### 0.2 Hard requirements

1. **Rendering does not participate in ecology.** All visual randomness is seeded from immutable world seed, stable entity ID, species ID, part ID, and/or spatial cell key. Camera position, frame rate, draw order, time of capture, and worker scheduling never alter ecological decisions, save content, biology RNG consumption, or authoritative IDs. Visual animation may use presentation time, but it never feeds back.
2. **No visible-distance cheating.** Keep convincing actual branch, leaf, grass, moss, and detritus silhouettes as long as individual parts remain perceptible. Never replace nearby recognizable vegetation with a flat green mass, billboard forest, or conspicuous impostor. An existing tessellation tier may be retained only if a matched reference proves silhouette, material identity, orientation, attachment, and perceived density remain stable. Reduction of genuinely subpixel *microgeometry* is allowed with evidence. Frustum/occlusion culling is encouraged; camera visibility must not change biology.
3. **No plant-node explosion.** Thousands of leaves/blades are materialized as cached meshes and/or batched GPU instances. Authoritative organism granularity and visible mesh granularity are intentionally different.
4. **No globally synchronous rebuilds for routine updates.** Planting, growth, aging, death, mowing/picking, camera motion, terrain sculpting, and moisture drift must not trigger whole-island geometry repacking unless a structural change actually requires it.
5. **Visuals remain habitat-derived.** Moss, dry litter, damp soil, grass, exposed rock, roots, and water-margin details must align with existing authoritative moisture/light/substrate/cover/litter/water fields. Decorative detail must not claim an ecological substance that isn't there.
6. **Existing species identity is preserved.** Keep distinctive approved forms (e.g. Ironlace's fluted split bole, Fenneedle's layered drooping needles, the climbing networks, carnivorous forms, and pilot trees). Any geometry change must be compared per-species, not only on an overview image.
7. **Offline and agent-friendly.** Source recipes, deterministic generators, interchange formats, build scripts, attribution, and diagnostics belong in the repo. Runtime has no cloud dependency. Blender/GIMP/Inkscape may be offline authoring/build-time tools; regular users should not need them installed.
8. **30 FPS floor on the specified hardware** is a release condition, not a reason to silently lower visual density. Quality controls may reduce genuinely nonessential effects; reference visual configuration must be explicit. Tests also track frame-time percentiles and 1-second windows, not average FPS alone.
9. **No engine migration implied.** Build this against the current client. Preserve engine-independent data and source meshes so Flax/Unity can be compared later if desired.

### 0.3 Definition of visual success

Pass at **three viewing regimes**, with deliberately different failure modes:
- **Macro (roughly 5–50 cm from subject):** leaf curl/thickness/veins, bark, soil granules, moss tips, root collars, water margins, genuine contact and self-shadowing. No floating attachments, faceted hero contours, repeated identical leaf/tissue shapes, or texture swimming.
- **Normal interaction (roughly 0.5–3 m):** dense but legible layers, believable understory spacing, leaf clusters interrupting light, plant-floor integration, low-angle near-field terrain relief. No "individual green sticks over painted dirt" impression.
- **Whole island (roughly 5–25 m):** readable tree/shrub silhouettes, ecological gradients, patchiness, shadowed understory, viable cutaway stratigraphy. No repeated concentric scatter patterns, obvious geometry-tier pop, floating plants, or homogeneous color fields.

An acceptance review examines the still image **and** orbit/pan, rotating sun/day-night light, species aging and dying, sculpt/redeposit, and changing wetness. Screenshots alone are insufficient.

---

## 1. Baseline, measurement rig, and change control (must be first)

### 1.1 Freeze a reproducible reference, not the user's saved world

Create `docs/photorealism/living-landscape/baseline.md` with:
- Source commit SHA, Godot .NET editor build, GPU/driver, CPU, RAM, OS, display resolution, window/display scaling, VSync state, anti-aliasing method, render quality tier, flora tier configuration, test seed/preset, simulation time and camera trajectory.
- Warmed and cold-start runs; distinguish shader compile/import/startup from steady-state timings.
- Explicit statement that no other Godot instance, editor preview, GPU-intensive application, or competing benchmark is running.
- Number of visible organism instances, actual leaf/branch triangles, drawn primitive count, draw calls, GPU memory reported by renderer, process working set, instance-upload bytes, dirty tile counts, build queue length, sculpt updates and simulation ticks.
- Do **not** overwrite `docs/baseline/` or the September reference data. New report schema/version and output directory.

Extend `--perf-test` / reference capture with independent modes:
1. **Frozen world, moving camera:** isolates rendering and instance publication.
2. **Live world, moving camera:** captures ecology scheduler and presentation contention.
3. **Close under-canopy, stationary camera:** isolates foliage shading and pixel fill/overdraw.
4. **Dense colony floor grazing angle:** moss, litter and contact.
5. **Fast camera traversal/rotation:** invalidation and buffer churn.
6. **Sculpting near dense planting:** terrain update and re-projection.
7. **Growth/death/litter event burst:** dirty tile and batching behavior.
8. **Wet margin / moving daylight phase:** shader and lighting stability.
9. **Worst populated preset/stress state:** a saved, deterministic high-density scenario; do not use a hand-picked easy seed to declare success.

Use existing `--reference`, `--species-view`, `--specimen`, `VIVARIUM_PERF_FREEZE`, `VIVARIUM_PERF_LOW`, `VIVARIUM_PERF_SCULPT`; extend rather than duplicate them. Fixed-camera screenshots must include overview, sparse, moist, wet edge, undercanopy, moss/lichen close, broadleaf front/back, woody trunk/ground, fungus and dead wood. Add 10–20 second moving-video or frame sequence captures where stills conceal issues.

### 1.2 Performance gates

At the **quality preset declared as visual target** and 1600×900, after warm-up:
- No 1-second FPS window below 30 during normal navigation, sculpting or live world evolution in required scenarios.
- p99 frame time <= 33.3 ms in a >=60-second steady-state measurement. Record p50/p95/p99/p99.9/worst; a good average with periodic 60–100 ms stalls is **not** a pass.
- Frame hitches >50 ms: zero unexplained *recurring* hitches; one-off OS noise must be called out and re-tested, not cropped without disclosure.
- Frame-time breakdown: simulation CPU, render-state gather, geometry build CPU, main-thread commit, GPU draw/lighting, and synchronization. Use available Godot timing APIs or an external capture; don't label CPU process timings "GPU ms."
- Resource hygiene: peak VRAM below 6 GiB as a working budget on the 8 GiB GTX 1080, unless justified by measurement; zero unbounded growth across 20 minutes of sculpting/growth.
- Quality work should not regress a matched scenario's p99 by more than 5% without a documented corresponding image-quality gain and a **passed** absolute 30 FPS gate. A/B both before and after in the same clean session.
- Do not declare a pass from a reference report whose camera/seed/organism counts or renderer flags differ.

Make a machine-readable `render_report.json` with sections `provenance`, `world`, `scenario`, `render_settings`, `cpu_timing`, `gpu_timing` (nullable), `frame_times`, `counters`, `digest_before`, `digest_after`, `artifacts`, `warnings`. Provide a validator that fails for missing provenance, malformed timings, mismatched captures, digest changes, memory growth or missed frame gates. The comparison script should generate human-readable deltas and **links to image evidence**.

### 1.3 Behavioral and visual invariants

Add tests for:
- Same render recipe and input keys -> byte/semantic-identical geometry metadata and asset manifest.
- Changing renderer quality, camera, lighting, wind phase or paint layer -> identical simulation digest after identical tick count.
- A saved world renders identically after reload at frozen presentation time / equal renderer version.
- Asset metadata includes valid triangles, finite normals/tangents, normalized weights, consistent winding, rooted-to-ground anchors, valid indices, nonzero UV extents, atlas layer numbers, convex/nondegenerate bounding boxes.
- All authored recipes refer to a known species and mesh/profile version; unknown IDs fail with useful validation rather than substituting another species silently.
- No new test failures; separately enumerate already-failing geometry tests and repair them under their own tracked change if they become blockers.

**Deliverable:** baseline report, contact sheet, benchmark runner/validator, and a short list of the **measured top three** CPU/GPU visual costs. Without this, don't attempt a wholesale mesh/shader rewrite.

---

## 2. Asset pipeline: botanically convincing assets that remain procedural

### 2.1 Preserve procedural identity, upgrade the representation

Do not interpret "asset pipeline" as replacing every generated plant with one static glTF. Build **parameterized botanical assemblies** with a reusable deterministic geometry source, plus optional offline-baked variants.

Proposed presentation-only model:

```text
Species physiology / world entity   (Vivarium.Sim authoritative)
    id, species, seed, age/stage, size, health, fruiting, pose, habitat
             | read-only adapter
             v
VisualRecipe(species, recipeVersion, anatomy profile, material profile)
    + VisualSeed(world seed, entity id, species, stable part ids)
             |
             v
VisualAssembly (hierarchical skeleton and part descriptors)
    axes/root collar/scaffolds/twigs/petioles/blade sheets/fruit/rootlets
    stable anchors, attachment frames, local bounds, tissue/material slot, flex
             |
             +--> procedural geometry generator (near / deformable / unusual)
             +--> offline authored/baked part library (repeat motifs)
             |
             v
Cached variant meshes + texture arrays + per-instance growth parameters
             |
             v
Tile/species batches, shaders, deterministic deformation and draw
```

Existing `OrganismMeshes.*` and `FloraVisualCompiler` already provide much of the core. Extend their **part metadata**, not organism simulation. For new presentation-only classes, prefer `src/Vivarium.Sim/Geometry/` initially where existing type contracts reside and remain engine-agnostic. If visual-only infrastructure grows too large, extract into a new `Vivarium.Visuals` class library with a one-way dependency on core sim types; **the sim must never depend on Godot or the new runtime renderer**. This separation is optional and must be preceded by a dependency audit.

### 2.2 Define versioned visual recipe format

Add versioned optional recipe fields (proposed `game/content/visuals/flora/<species>.json`; keep current `FloraVisualProfile` fields working during migration):
- `schemaVersion`, `species`, `geometryMode` (procedural | hybrid | baked), `authoringVersion`, stable `recipeHash`.
- `architecture`: root collar height/flare, number/order of axes, branching family, phyllotaxis, apical dominance, canopy asymmetry, droop, internode length curves, attachment orientation, avoidance/competition shaping, trunk ovality, branch mortality/scars, leaf distribution.
- `stageCurves`: juvenile/mature/senescent stages, branching/count/size/curvature from real growth parameters; no abrupt object swap; dead collapse uses existing death state.
- `materialSlots`: leaf upper/underside, petiole, young stem, bark, fruit/cone, scar/deadwood; maps/palette/roughness/normal/transmission and sensible per-species bounds.
- `deformation`: wind base stiffness, branch flex, per-leaf edge flex, attachment locations, maximum deformed AABB expansion, stable per-part phase.
- `variantPolicy`: bounded shape variants and deterministic selection; favor **continuous** parametric variation over a dozen exact duplicates.
- `performance`: upper vertex/triangle/texture budget by plant stage, expected max visible instances, maximum unique materials, caching/rebuild keys, asset scale.
- `provenance`: authored by/tool/script version/seed, source file paths, licenses, edit notes, anatomical references; copyrighted scans are not silently bundled.

Write a JSON schema or equivalent C# validator, migration path for the four existing dedicated recipes, and fallback for all unmigrated flora. `game/content/index.json` remains the authoritative species catalog; visuals cannot create ecology species. Explicitly avoid mixing presentation schema version with `.vivsave` migration.

### 2.3 Toolchain and offline baking

Proposed `tools/visual_authoring/` (build-time only) and `scripts/visuals/`:
- Python + Blender in **background mode** for repeatable mesh generation/export when genuinely higher quality than current `OrganismMeshes`; reuse C# primitives where they perform well.
- Small generated source assets rather than elaborate manually edited scene files. Use procedural curves, swept splines, leaf blade outlines/camber, branch grafts, bark scar geometry and stable attachment IDs.
- glTF 2.0 interchange when useful, with explicit handedness, units/metres, Y-up conversion, coordinate-frame rules, vertex normals/tangents, UV0/UV1, color-space and material slot mapping. **Do not assume glTF custom attributes survive every importer unchanged.** Validate custom part metadata in a separate JSON sidecar or baked binary manifest.
- For unusual geometry (climbing support network, traps, plasmodium), use the existing runtime geometry path if asset baking would lose responsiveness.
- `scripts/visuals/build-assets.ps1` detects Blender or optional toolchain, builds/rebakes selected species, compares hashes, and validates outputs. Normal game builds consume committed approved generated assets and **must not require Blender**.
- Clearly distinguish source files (`tools/` or `assets/source/`), baked interchange (`assets/generated/` if appropriate), compressed runtime textures (`game/Textures/`), and manifest. No duplicated, stale full-resolution texture pools in the release package.
- Record source and derived licenses; keep CC0 ambientCG and any permitted sources credited. A provenance record must distinguish generated fictional tissue from real photographed tissue.

Asset-validation CLI requirements: `--validate`, `--build-species <id>`, `--all`, `--rebuild`, `--hash-only`; CI can validate hashes and schemas without a live rendering device. The existing leaf baker still needs a real rendering device for its texture arrays: run that in the graphics verification lane, not headless-only CI.

### 2.4 Morphology rules for believable plants

Implement or expose, species by species:
- **Axes and taper:** spline frames, curvature continuity, asymmetric secondary branching, taper by branch order, buttress/root collar connection, wood scars and dead twig portions.
- **Leaves:** stable non-overlapping petiole junctions, lamina with real outline/shoulder/tip differences, controllable camber/curl/twist, serration where botanically appropriate, veins aligned to actual blade coordinates rather than projected world axes, paler/rougher undersides.
- **Distribution:** light-driven orientation as **visual response to the existing field**, crown gaps, shaded interior leaf dropout, clustered flushes, branch hierarchy, fractional phyllotaxis phase and local asymmetry. Do not multiply all leaf placements independently at random; keep growth-organ logic.
- **Life stages:** juvenile forms distinguishable from adults, variation during growth without morph teleportation, seed/flower/fruit visibility from authoritative phenology flags, senescence and fallen structures aligned to current dead-flora data.
- **Distinct guilds:** broadleaf woody, needled woody, compound shrubs, ferns, creeping runners, grass/graminoid, moss, wet/aquatic, fleshy succulents, fungi, carnivorous plants. No single generic "foliage bush" generator across all of them.
- **Contact:** support-bound vines wrap/contact logs or host stems; root collars intersect topsoil properly; fallen parts lie on local surface rather than world-height-only Y. Preserve the Hookthicket ground-return and Kinkcane bend specifications already approved.

When triangles are expensive, optimize **the hidden or redundant anatomy** (internal duplicated faces, coincident sheet backs, over-tessellated cylinders). Do not remove the outline-defining leaf silhouette, root collars, fungus cap contours, or readable branchlets.

### 2.5 Pilot set and visual review gates

Implement **three pilot families**, not 33 simultaneous migrations:
1. **Ironlace** — branching/crown, trunk/fluted root collar, leaf top/back, cross-branched shade; compare at macro and entire tree.
2. **Veilfern or Lanternbrush** — compound/partially translucent understory, stem/leaf attachments, high density without identical fan repeats.
3. **Coinrunner or Trifold** — ground-level stems/runners, leaf-floor contact, repeated small meshes without clumping.

Then apply the same contract by guild; expand to Fenneedle needles, Kiteleaf undersides, Shadebell/Embercrown shrubs, climbers, pitcher/sundew/latchjaw, wetland species, fungi and pilot trees. Existing `docs/overhaul/flora_species_art_plan.md`, `docs/flora_visual_review_log.md`, and `docs/photorealism/flora_visual_system.md` are authoritative for already-agreed aesthetics. Each conversion needs paired: isolated specimen under front/back/side lighting, juvenile/adult/dead states, one integrated world, animation phase, and profile/perf comparison.

**Do not merge all species from a batch if one fails anatomy, reference parity, or performance review.** Nonmigrated species continue on the known-good renderer.

---

## 3. Vegetation lighting: natural depth without expensive gimmicks

### 3.1 Establish an explicit material-space contract

Evolve existing `leaf.gdshader`, `flora.gdshader`, `surface.gdshaderinc` and `FloraSurfaceProfiles` rather than introducing a totally separate lighting framework.

Specify:
- Input base color is interpreted in the correct texture color space (sRGB for albedo; linear for physical masks, normals, thickness).
- `UV0` leaf/stem geometry space, `UV2` existing tissue modes, stable per-blade metadata `CUSTOM0` and `CUSTOM1`, per-instance `INSTANCE_CUSTOM` remain documented and tested. Only add a new channel after verifying Godot's mesh format and shader compatibility.
- Consistent world-vs-local coordinates for normal mapping, baked tangent handedness, double-sided normal flipping, and wind-deformed normals.
- Explicit physical-ish ranges for albedo, roughness, anisotropy if supported, normal strength, backlight/transmission and opacity. Avoid compensating for poor lighting by multiplying albedo >1 or adding glow.
- Backside pigment, chlorophyll transmission and thickness control are distinct: `BACKLIGHT` alone is a cheap response, **not** real volumetric subsurface scattering.
- Leaf veins affect normal, pigment, *and thickness/transmission* consistently, without "painted-on black veins" or baked illumination direction.
- Use `cull_disabled` appropriately on thin leaves; prefer opaque thin-sheet geometry over general-purpose alpha blending. If a leaf needs a cutout, test alpha-scissor/hashed techniques, z-prepass behavior and overdraw before accepting it.
- All material presets must render the same organism plausibly in sun, overcast/ambient, shade, backlight, dusk and night. Material IDs must remain stable through variation and growth.

### 3.2 Avoid indiscriminate global illumination

Godot Forward+ provides SSAO, SSIL and SDFGI, but they are not interchangeable:
- Start with correctly calibrated directional sun and skylight/ambient, contact shadows and modest scene AO.
- Add light probes or a **small-scale, inexpensive, render-only canopy/ground irradiance approximation** only if test captures show a real missing bounce-light cue.
- Test SSIL separately at low resolution/quality as a **local bounce accent**, not a full GI replacement; recognize screen-space disappearance artifacts.
- SDFGI is expensive on GTX 1080 and does not track dynamic occluders like growing foliage correctly. Treat it as a **controlled optional experiment**, not the default architecture.
- No hardware-ray-traced rendering dependency on a Pascal GPU.
- Keep the 15/5 minute presentation-only light cycle; biology never reads it. Expose a deterministic fixed-sun/fixed-environment capture mode and time-of-cycle markers for comparisons.
- AO should darken contact and true cavity regions, not turn foliage gray/black. Avoid duplicating baked canopy AO + SSAO + contact shader darkening.
- Preserve the cutaway's clarity and nighttime bioluminescent accents; never wash out the island with global fog for a "cinematic" look.

### 3.3 Contact and vegetation occlusion

Implement in ascending expense order:
1. **Grounded geometry:** fix placement and embed root collars, rhizoids, fallen bark/logs and rock contacts. Geometry must do most of the work.
2. **Local precomputed/baked plant AO:** per-vertex or texture metadata from each authored assembly for major cavities (branch intersections, clustered bases, inside canopy). It moves with the plant and need not be generated every frame.
3. **Contact decal/mask field:** optional small footprint mask per large plant/root/rock at the soil surface; blends with moisture/soil material rather than a fake black disk. Requires terrain edits and changed plant footprints to invalidate locally.
4. **Screen-space AO / contact shadow tuning:** quality-tiered, with side-by-side references; do not crank radius to hide missing geometry.
5. **Selective shadow casting:** major trunk/canopy shapes and a bounded proportion of important leaves cast directional shadows. Small floor debris usually receives but need not cast. Shadows should be stable while camera or foliage shifts; disable on subpixel-only detail with evidence.

Provide an inspectable lighting debug mode: diffuse-only, normal, roughness, transmission/thickness, world-space normal, canopy occlusion, shadow contribution, ground contact mask, and AO intensity; each view works on the current island without modifying state.

### 3.4 Wind, growth and lighting continuity

Keep existing hierarchy-aware per-blade deformation. Use stable attachment frame and part-phase, distinguish branch bending from leaf-edge flutter, rotate normals with deformation, and ensure the leaf never detaches from a moving twig. Animation strength depends on species stiffness and presentation conditions, not random per-frame seeds. A frozen-wind render must be identical across runs. Check motion blur/TAA or anti-aliasing only if needed; avoid costly post effects to mask aliasing.

Lighting gates: backlit underside looks plausible without glowing neon; internal canopy is shaded but readable; tree/trunk contacts anchor to soil; no inverted normals, shadow acne, detached shadow silhouettes, foliage popping or reflection flicker in moving camera and 20-minute day/night captures.

---

## 4. Spatial batching, update lifetime and CPU/GPU cost

### 4.1 First fix ownership and invalidation, then optimize draw calls

New proposed *presentation-only* infrastructure:
- `game/Render/VisualInstanceKey.cs`: stable `(worldSeed, entityId or cellKey, species, partId, recipeVersion, stateCategory)`.
- `game/Render/RenderSnapshot.cs`: immutable or copied snapshot of **render-needed** fields, captured with version tokens; no background thread walks mutable world collections while sim advances.
- `game/Render/RenderDirtyRegions.cs`: reason-coded spatial dirty regions (flora instance transform, stage geometry, terrain version, coverage tile, litter, moisture, waterline, props, material/lighting).
- `game/Render/RenderUpdateScheduler.cs`: shared work budget, priorities and diagnostics for renderer refresh/upload tasks. **Do not duplicate the authoritative sim scheduler.**
- `game/Render/VisualBatchKey.cs`: recipe/material/geometry variant/tile/quality tier/visibility class; separates geometry cache from instance buffers.
- `game/Render/RenderResourceCache.cs`: explicit lifetime/disposal/refcount or validated ownership for GPU meshes and textures, with stable keys and bounded cache.

These are proposed files/classes, not claims about the current tree. Implement only after a profiling gate demonstrates sufficient benefit; prefer extending existing code where abstraction would add more scheduling overhead than it removes.

### 4.2 Spatial culling strategy suited to a 16 m island

A single `MultiMesh` is culled as a whole, not each instance. A giant species-wide batch spanning the entire island can waste vertex processing when only a corner is visible. But splitting each organism into dozens of tiny batches can increase draw calls. Find the crossover empirically.

- Measure present per-species variant batches with near/mid/overhead camera, visible fraction, draw calls and primitive count.
- Introduce **coarse spatial tiles only where beneficial** (initial candidate tile edge 1.5–3 m, then benchmark 1 m, 2 m, 4 m and unsplit); tile IDs based on world coordinates independent of camera.
- Keep large tree/pilot-tree meshes in separate large-bounds batches or organism-level submissions as needed; do not tile-slice one trunk unless an actual visibility win follows.
- For low groundcover/litter, use stable world tiles so camera motion **changes visibility**, not generated identity or geometry. Keep camera-local detail buffer as a view-dependent cache of deterministic tile content when required.
- Set/verify correct custom AABB for instances with wind sway, living growth and falling/dead poses. Too-tight AABBs cause vanishing foliage; too-large bounds defeat culling.
- Perform CPU coarse frustum tests with bounds; use engine occlusion if supported and demonstrably beneficial, but never rely on unverified per-instance occlusion from a single MultiMesh.
- GPU-driven per-instance culling/indirect draws are a *later experiment*, gated by instrumentation and binding limitations on Godot 4.7.1; do not assume a custom compute path integrates with Godot's normal shadow, GI, scene picking, or resource lifetime for free.

### 4.3 Stop rebuilding stable buffers

Per entity/cache key, distinguish:
1. **Recipe/mesh changed** -> rebuild geometry, not on every age tick.
2. **Transform or size changed** -> update instance record/batch membership only.
3. **Color/health/fruit parameter changed** -> update custom instance data or the relevant fruit bucket; don't regenerate leaves.
4. **Visibility/camera changed** -> cull or select an already cached representation, not a mesh build.
5. **Coverage occupancy changed** -> rebuild only affected tile(s) and edge neighbors necessary for continuity.
6. **Terrain sculpt** -> update terrain geometry and only rooted object placements / grounded detail in edited spatial region.
7. **Moisture/light/litter changed** -> update ecological field textures and targeted visual scatters; don't rebuild trunks.
8. **Material/shader/wind phase changed** -> uniform/material updates; no geometry churn.
9. **Death/collapse** -> move a plant through existing live/dead representation; reuse anatomy when possible and update pose, no reroll of unrelated plants.

Build immutable content off-thread and **only publish Godot resources on the main thread**. Use bounded queues, cancellations for obsolete versions, and latest-wins coalescing. Separate completion of visual uploads from authoritative sim ticks; renderer lag cannot delay simulation or modify its output. Never block the main thread awaiting a worker `Task.Result` unless already completed and validated. Avoid iterator/task allocations in per-frame hot loops.

### 4.4 Instance buffer policy

- Reuse pooled arrays / `PackedFloat32Array` or the supported buffer API; compare bulk `MultiMesh` buffer upload against individual `SetInstanceTransform` updates on the real GPU.
- Cache per-batch capacity, live count, dirty ranges and upload bytes. Avoid resetting `InstanceCount` merely because a few organisms changed.
- Stable, deterministic packing order, or maintain explicit key->slot mapping with a free-list/tombstone strategy; tests ensure no stale slots appear during deletion or migration. If compaction is needed, bounded and explicitly measured.
- Separate opaque leaves, bark and fruits when material slots force different passes, but avoid creating an extra draw per leaf.
- Share texture arrays/materials across variant meshes; watch sampler pressure and shader permutation count.
- Reserve budget for tree canopy shadows and macro moss before spending it on leaf-litter microtriangles invisible from the current camera.
- Track update intervals per system; eliminate whole-world snapshots and LINQ allocations in hot paths where profiler evidence supports it.
- Retain the existing budgeted `CoverageRenderer` and `FloraRenderer` behavior until direct A/B tests show a better version.

### 4.5 Geometry detail policy and no-popping tests

The existing three leaf tessellation tiers use stable attachment and identity. Maintain that invariant. At 1600×900, preserve visible silhouettes; optimize only curvature subdivisions not resolving into visible pixels. **Prohibit** reducing a recognizable leaf/fern/flower to a flat color patch or a distant plane purely to reach the frame target. Use hysteresis and fixed specimen/orbit tests to check flicker, shadow changes, and texture scale. Where leaves are genuinely subpixel, prefer merged foliage **only if** parallax and material response are indistinguishable in moving captures; otherwise retain original meshes and find another optimization.

### 4.6 Dirty-region algorithm (normative outline)

```text
on simulation tick/visual snapshot:
  sample authoritative versions and changed bounds into RenderSnapshot
  map each changed spatial footprint to affected visual tiles
  merge overlapping work by tile + reason + latest source version
  calculate priority: camera-visible > near camera > recently altered > background
  enqueue immutable CPU tasks (mesh/scatter/material-data generation)
on presentation frame:
  advance environment uniforms (bounded cost)
  apply completed validated work until publication budget expires
  reject work if source version is stale; preserve last valid buffer
  upload changed instance/texture data, reuse unchanged cached meshes
  publish counters: queue depth, longest age, stale drops, upload bytes, rebuild ms
  draw with tile/species batches
```

Fairness guarantee: off-screen changes eventually publish if they can enter view. Priority affects *latency*, never output semantics. A camera move cannot lose an update permanently. Invalidation propagation tests cover tile borders and cases where terrain, plants and litter are modified simultaneously.

---

## 5. Terrain and vegetation must read as one physical scene

### 5.1 Composition model: which layer owns what?

| Scale / mechanism | Source of truth | Render representation | Required continuity |
|---|---|---|---|
| Island slope, shape and cut faces | Terrain heightfield/strata | Existing `TerrainMesh`, `IslandRenderer`, terrain/strata shaders | No holes or misaligned strata after sculpt |
| Broad wetness/substrate/light/cover | Existing per-cell fields, cover, water depth | Filtered shader textures and masks | Stable across cell borders; no checkerboard or literal square patch edges |
| Continuous soil/moss/lichen/algae | Field + coverage tile occupancy | Tile/terrain material blends and coverage geometry | No clipping, z-fighting, black halos, texture seams |
| Plant roots/base contacts | Living/dead flora or world props | Geometry plus local soil/leaf/debris imprint | No floating stems or conspicuous generic disks |
| Named grass/herbs/ground flora | Explicit flora IDs/coverage | Stable individual/rhizome meshes, local species batches | Species remains visibly distinct and selectable |
| Anonymous fine turf/filler | Existing `AmbientGroundCover`/habitat, render-only | Dense, cheap small blades or clustered plants | Filler follows light/moisture and avoids named plant collisions |
| Litter/twigs/fruits/detritus | Existing litter/fruit/dead plant systems | Scattered instanced visible material | Amount and tint match authoritative mass/composition; no invented food |
| Rocks/logs/deadfall | Props/death data | Existing mesh + contact dressing + underside occlusion | Intersection and ground contact remain believable |
| Surface/standing/flowing water | Existing hydrology | Existing water shaders and mesh | Shoreline graded from dry soil to saturate to submerged silt |

**Rendering must not implement new species, new ecological production rates or alternative physics to fill a visual gap.** If a desired meadow needs more actual occupied plants or litter, file it as an ecology/content task under `docs/ground_continuity_lifecycle_plan.md`; do not silently make the scene biology-inconsistent.

### 5.2 Shared sampling and soil/material masks

Propose a reusable **read-only** terrain visual sample, potentially `game/Render/TerrainVisualSample.cs`, returning world-space height, slope/normal, curvature/hollow score, substrate category, moisture/saturation, plant cover/light, litter abundance/composition, water depth and nearest prop/plant exclusion radius. It wraps existing queries and cached field maps; never becomes a second authoritative field.

- Use continuous bilinear/multiscale interpolation where appropriate, not sharp cell-edge steps for shading.
- Preserve exact categorical boundaries where necessary (water or solid substrate) and apply an explicitly bounded transition band only to **appearance**.
- Provide stable world-space tri-planar or distorted planar texture coordinates shared by soil, moss, wet bank and rock. Geometric slope scaling must be in metres, not accidental UV tiling.
- Define roughness/normal intensity by wetness and organic cover: wet surfaces darker and often smoother, but not mirrorlike black; saturated moss has a different sheen than wet stone.
- Use a consistent material variation seed across terrain and scatter to avoid repeating circular dots and striped noise. Variation must be scale appropriate (millimetres, centimetres, decimetres) and not a visible grid.
- Preserve `terrain.gdshader` existing masks and `IslandRenderer.UpdateFieldTexture` semantics. A new packed texture is justified only if it avoids repeated CPU traversals or provides necessary precision.
- Profile field texture update cadence, region dirty updates, format bytes and GPU upload cost. Do not upload the entire field every frame when it changes locally.

### 5.3 Plant-floor physical integration

Implement **by contact class**, not by painting generic dark decals under every plant:
- Tree: root flare intersects soil; soil rises or mounds subtly at root collar only where physically plausible; leaf litter collects at downhill/lee side; fine roots may appear near erosion.
- Shrub: multiple basal canes share rooted stool; immediate ground within actual stem footprint has displaced turf and accumulated fine litter; dense interior is shaded, not a featureless black circle.
- Fern/graminoid/herb: basal crown sits on slope-normal with no hovering or planar soil clipping; stalks emerge from the same anchored region; fine surrounding grass bends/avoids it.
- Climber: runner contacts ground and support; consistent thin cast shadow; crossing plant surfaces must not accidentally z-fight.
- Moss/lichen: material blend meets stems/rock/log bark through low profile growth, tiny elevated tips and ragged edge; no painted flat hard boundary.
- Fungus: caps/stems emerge from litter, decaying wood or soil contact as appropriate; group variation and litter base continuity.
- Deadwood: bark side, branch undersides, contact dampness, surrounding accumulated detritus and fungi align with the real deadwood record.
- Water margin: fine reeds/rhizoids at waterline, sediment/moss thresholds, damp-to-dry soil, shore-normal continuity; **do not** draw dense grass below open water.

Define penetration/contact tolerances in metres and automate per-specimen checks (root below/on soil, visible stem above soil, no suspended deadfall, rock contact not floating). Recompute local placements when sculpt changes; guard against terrain-cell-edge snapping.

### 5.4 Anonymous groundcover: density without ecological lies

Refactor the relationship between `AmbientGroundCoverRenderer` and `SoilDetailRenderer`:
- Inventory which one currently draws small blades, broadleaf filler, stalks and litter; identify overlapping patches, z-fighting and redundant CPU scans.
- Centralize **density and exclusion decisions** into a lightweight shared tile source. Render species-agnostic filler from existing anonymous cover; preserve named groundcover identity and its ecological placement.
- Scatter deterministically from tile/world key and stable blue-noise/low-discrepancy point distribution where useful; remove obvious regular-grid or repeated circular tuft outlines.
- Use **clusters of varied blades/stems** rather than many identical tri-prongs. Cluster orientation responds to local slope/light and bend; meshes vary leaf count, width, length, droop, senescence and shade.
- Render finite-height grass blades when perceptible; at subpixel scale apply conservative fade/coverage only if an animated visual comparison shows no collapse into tinted ground. Honor the no-visible-LOD requirement.
- Sample local named-flora silhouettes to create exclusion/occlusion and fringe transition; don't put filler in the middle of a pitcher trap, under dense woody stems, or over rock/water.
- Scatter leaves/twigs preferentially in hollows and shelter using existing deposition logic, but scale visible amount from actual litter composition/mass. Keep color convergence under decay/soil saturation.
- Preserve biological speed decoupling: background grass is presentation only; do not give it independent growth/eating/reproduction rules.

### 5.5 Sculpting and local reconstruction

When a brush changes terrain:
1. Determine changed heightfield vertex bounds plus margin equal to tallest nearby root footprint/coverage tile overlap.
2. Update the affected `IslandRenderer` geometry using existing reuse strategy.
3. Invalidate field texture sample region and local prop/plant contact transformations.
4. Reproject previously seeded scatter points to the new surface; **do not reroll their positions** merely because height changed.
5. Update slope-aligned normals and local contact masks; clip/sink objects that would otherwise float or disappear.
6. Rebuild only affected moss/litter/ground-detail tiles; preserve unaffected GPU buffer identity.
7. Ensure waterline material follows latest authoritative hydrology at its tick cadence; no animated visual water claiming a changed flow before the solver does.
8. Expose backlog/invalidation counters so an agent can quantify sculpt latency and staleness.

Performance goal: sustained interactive sculpting on populated islands without second-long mesh rebuild stalls, growing memory, or repeated shader recompiles.

---

## 6. Implementation slices and dependency graph

Each item below is intentionally small enough for an agent to implement, test and report in a bounded worktree. **Do not run agents that modify the same shader or renderer source at the same time.** Independent recipe art, tests, diagnostics and source-model generation can be parallelized.

Legend: `G#` is a gate; `P#` is an implementation task. "Gate" requires a checked-in report with a concrete pass/fail determination; it cannot be marked passed by an agent saying "looks good." File paths marked **new** are proposed.

### Phase A — measurement and guardrails (must precede major edits)

**P00 — Commit scope + invariant contract**  
Files: this plan; `docs/photorealism/living-landscape/README.md` (new).  
Work: inventory current quality modes and assets, choose canonical visual target (1600×900 Forward+, intended high quality), list fixed seeds and scenes, enumerate pre-existing failing geometry tests.  
Acceptance: frozen baseline definitions and explicit "not part of this work" ecology behavior list.

**P01 — Perf report versioning and provenance**  
Files: `game/App/FrameProfiler.cs`, `SmokeRunner.cs`, `SmokeRunner.Reference.cs`, `scripts/compare-reference.ps1`.  
Work: structured counters, hardware/source/settings provenance, CPU-vs-GPU timing distinction, trace of render version/source digest; ensure reports have unique run IDs and don't overwrite historical captures.  
Tests: report JSON schema validation, same-run compatibility check, wrong-hardware comparison rejected.

**P02 — Deterministic visual scenario library**  
Files: `SmokeRunner.Reference.cs`, `game/App/SpecimenPreview.cs`, new `docs/photorealism/living-landscape/scenarios.md`.  
Work: macro/understory/shore/colony/sculpt/live density scenes, fixed camera poses, captured light states; include high-density case and at least 60 s traversal.  
Tests: identical world digests, same scene query results for a pinned revision, no writes to real user saves.

**P03 — Benchmark harness and report validator**  
Files: `scripts/visuals/benchmark.ps1` (new), `scripts/visuals/validate-report.py` (new), existing perf harness.  
Work: clean process launch, warm-up, capture frame times and sample windows, memory checks, comparison HTML/contact sheet; runnable with portable release executable.  
Tests: fake report with dropped frames, memory growth, digest mismatch, incompatible conditions must fail.  
**G0:** publish a real GTX 1080 baseline and measured bottlenecks, not inferred ones.

### Phase B — authoring contracts and three pilot families

**P04 — Visual-recipe schema and migration**  
Files: `game/Render/FloraVisualProfile.cs`, `game/content/visuals/flora/*.json`, schema/validator (new).  
Work: recipeVersion, architecture/deformation/material/provenance section, compatibility with current 4 profiles and fallback.  
Tests: valid/unmigrated/invalid IDs, range checks, no world-save schema change.

**P05 — Stable part and attachment representation**  
Files: `src/Vivarium.Sim/Geometry/OrganismMeshes.cs`, `Geometry/Form/*`, `FloraVisualCompiler.cs`, tests.  
Work: explicit part IDs, parent/root frames, attachment normals, tissue slot, flex, bounding volumes, material identity. Keep original `CUSTOM0/1` conventions; add any sidecar metadata safely.  
Tests: same seed => same topology and part IDs; no dangling attachments, NaNs or triangle budget surprises.

**P06 — Offline asset authoring scripts**  
Files: `tools/visual_authoring/` (new), `scripts/visuals/build-assets.ps1` (new), manifest.  
Work: deterministic Blender procedural output, glTF import tests, sidecar material/part identity, source license records and asset hashing; optional Blender path, no runtime tool dependency.  
Tests: clean source->generated hash reproducibility; CI manifest verification, offline launch.

**P07 — Pilot A: Ironlace geometry and rooting**  
Files: `OrganismMeshes.*` and/or authored asset, recipe, `FloraVisualTests.cs`, reference images.  
Work: root flare, irregular bole, branch order, branchlet and leaf orientation, juvenile/mature/dead comparison.  
Acceptance: improved photographic silhouette at macro and overview without anatomy or growth regression; benchmarked.

**P08 — Pilot B: understory specimen**  
Work: Veilfern or Lanternbrush, correct compound leaves/petioles, varying foliage density, readable backlight and self-occlusion; preserve approved form.  
Acceptance: independent material slots/stiffness and stable attachment through wind; documented matched image.

**P09 — Pilot C: groundcover specimen**  
Work: Coinrunner or Trifold, distinct stolons, clustered nonuniform leaves, slope/root attachments, local scatter cache in dense cover.  
Acceptance: recognizable under normal camera distance, not a repeated tuft stamp; separate habitat source.  
**G1:** three visual families implemented with before/after images, deterministic bake hashes and GPU/CPU measurements. Nonpilot species unchanged unless a shared bug fix is demonstrated.

### Phase C — lighting and materials

**P10 — Material coordinate and color-space audit**  
Files: `leaf.gdshader`, `flora.gdshader`, `surface.gdshaderinc`, `FloraSurfaceProfiles.cs`, `LeafMaterialBaker.cs`.  
Work: audit all UV/CUSTOM bindings and map import flags; ensure matching tangent frames, double-sided normal handling, roughness/transmission map semantics; eliminate accidental lighting baked into albedo.  
Tests: colored normal/tangent/roughness debug captures, 1K texture array correctness, stable front/back.

**P11 — Species-specific transmission, thickness and leaf-face tuning**  
Work: adjust visual profiles by physical leaf class, vein/thickness masking and cuticular gloss, with bounds; preserve light response under every time of day.  
Tests: fixed sun-angle capture matrix; no neon edges, no darkened inverted backside.

**P12 — Canopy and contact occlusion**  
Files: `EnvironmentRig.cs`, `leaf.gdshader`, terrain shader; new contact helpers if needed.  
Work: anchored geometry first, restrained per-part canopy AO, selective casting, contact mask experiments.  
Tests: roots, understory, dense canopy; track extra shadows draws and fill cost.

**P13 — Indirect-light experiment matrix**  
Work: baseline ambient vs tuned ambient vs SSIL-low vs carefully constrained SDFGI optional; screen-space artifact assessment, GPU time, memory.  
Acceptance: select only effects with clear visible win and compliant 30 FPS cost; do not ship SDFGI because "photorealistic" is written on a checkbox.

**P14 — Wind-light integration**  
Work: branch vs leaf animation, moving normals, stable phase, shadow AABB expansion, frozen-phase reproducibility.  
Tests: both orbit directions, dusk/dawn, front/back leaf, no detachment or flicker.  
**G2:** material/lighting matrix approved across 3 pilots and full island, with documented chosen GPU effects and cost.

### Phase D — render ownership and batching

**P15 — Instrument current batch inefficiency**  
Files: `OrganismRenderers.cs`, `CoverageRenderer.cs`, `SoilDetailRenderer.cs`.  
Work: per-batch bounds, drawn triangle count, visible fraction, GPU resource allocation events, upload bytes, dirty reasons.  
Acceptance: profile verifies whether global batches or CPU refreshing actually cost more; do not assume.

**P16 — Render snapshot/dirty event design**  
Files: new `RenderSnapshot.cs`, `RenderDirtyRegions.cs` or minimal existing-class extension.  
Work: versioned render-only read snapshot, local spatial invalidation for flora/coverage/terrain/litter/props.  
Tests: simultaneous edits, stable digest, no worker race over mutable state.

**P17 — Cached mesh/instance ownership and invalidation**  
Work: resource cache keyed by recipe/stage/topology, separate transform/custom-data updates, pooled buffers, bounded geometry builds.  
Tests: 1,000 mixed lifecycle edits, bounded allocation and no stale instances; same image/digest before/after.

**P18 — Spatial batch size experiment**  
Work: compare existing species-wide batches vs 1/2/4 m tile groupings, different policies for tree and ground species; scene traversal.  
Acceptance: retain only partitions that improve p95/p99 and do not create excessive draw calls or duplicate large plant meshes.

**P19 — Bounded publication scheduler**  
Work: apply latest-wins tile commits, visible priority, stale cancellation, overflow telemetry; no main-thread blocking.  
Tests: 20-minute camera/sculpt/growth churn, queue drains with camera still, no permanently missing details.

**P20 — Tessellation and overdraw cleanup**  
Work: eliminate invisible triangles/duplicate faces, measure no-pop tessellation tiers, improve transparent cutout only if required, verify AABBs/shadows.  
Tests: animated comparisons at all projected-size thresholds; no visible simplification.  
**G3:** live density and sculpt test passes 30 FPS floor, no unexplained recurring hitches and no memory growth. Keep baseline fallback switch for new batching path until sustained QA completes.

### Phase E — physical terrain/plant integration

**P21 — Shared render-only terrain sampling**  
Files: `IslandRenderer.cs`, `SoilDetailRenderer.cs`, `AmbientGroundCoverRenderer.cs`, new sampling helper if useful.  
Work: height/slope/curvature/wetness/cover/litter/water query, world-space coordinates, stable cached field versions.  
Tests: exact plane/slope fixtures, no changed biological query results.

**P22 — Material field blending**  
Files: `terrain.gdshader`, `strata.gdshader` if necessary, field binding code.  
Work: triplanar/world projection consistency, wet soil/rock/moss boundaries, coherent micro/meso/macro noise.  
Tests: color and normals continuous across noncategorical cell edges, distinct underwater silt vs damp earth.

**P23 — Root/prop/ground contact library**  
Work: per-class root collar/decal/mound/soil displacement and detritus blend, slope alignment; prefer geometry over dark circles.  
Tests: base penetration tolerance, moving/sculpted terrain, log-rock-tree juxtaposition.

**P24 — Anonymous filler and litter ownership**  
Work: map duplicate grass/stalk/leaf coverage between existing renderers, deduplicate scatter policy, stable tile point generation, biologically sourced litter quantities, named plant exclusion.  
Tests: no duplicate visible clumps, no changes to ecology mass, no filler floating over water/rock.

**P25 — Moss/lichen/fungi edge quality**  
Work: tile-edge normals, ragged mat/micro-stem continuity, support-surface hugging, protrusion into appropriate crevices.  
Tests: low-angle macro, close camera and wet/dry scene; no tile seams, strange repeated caps or unit-square patterns.

**P26 — Sculpt and shore reattachment**  
Work: region-limited terrain edit invalidation, stable sampled scatter keys, rooted object reposition, waterline/coast material update.  
Tests: repeated brush over dense planting for 10 minutes, no random reseeding, no floating plants, no large upload stall.  
**G4:** cohesive whole-island and macro floor visual review across seasons/lifecycle and waterlines; perf on worst terrain interactions.

### Phase F — integrate all species and ship with evidence

**P27 — Guild conversion matrix**  
Work: convert remaining species in batches by shared anatomy (woody, fern, grass/herb, creeping, aquatic, fungi, climbers, carnivorous, pilot trees). Preserve individual approved forms. Each species has recipe/schema/tests/photo references/material front/back/life-stage/perf record.  
Acceptance: no anonymous generic-plant fallback for a named, explicitly modeled species unless it was already an intentional accepted behavior.

**P28 — Whole-world coherency and growth transitions**  
Work: long-lived sim with changing wetness, reproduction, senescence, falling logs, decomposer activity, multiple day/night cycles.  
Tests: capture fixed scene at several sim ages; no world-pop-in, LOD silhouette swaps, visible scattering rerolls or material identity changes unexplained by biology.

**P29 — Full benchmark and resource soak**  
Work: all Phase-A scenarios run on GTX 1080/first-gen Ryzen, plus long 20-minute memory and dynamic workload, separate cold/warm runs, compare GPU/CPU.  
Acceptance: all gates from §1.2, after-versus-before visual artifacts and accepted budget deltas.

**P30 — Clean documentation, remove dead paths and packaging verification**  
Files: `docs/photorealism/README.md`, `README.md`, `THIRD_PARTY_NOTICES.md`, build/export scripts, schema/manifest.  
Work: retire flags/workarounds only after comparative tests, update authoring and asset regeneration guide, asset provenance, LOD restrictions, quality tiers, offline package and smoke suite.  
Tests: fresh offline-compatible checkout builds/runs without Blender, export boots/shaders compile, save/reload digest is stable.  
**G5:** final acceptance review, tagged baseline, visual and perf reports, reproducible assets. Do not mark complete if 30 FPS fails or if reference photos are less believable.

### Dependency ordering

```text
P00 -> P01 -> P02 -> P03 -> G0
                   |
            +------+--------+
            |               |
          P04 -> P05 -> P06   P10
            |               |
       P07 / P08 / P09     P11 -> P12 -> P13 -> P14
            |               |
           G1               G2
            \               /
             +-----+-------+
                   |
           P15 -> P16 -> P17 -> P18 -> P19 -> P20 -> G3
                   |
           P21 -> P22 -> P23 -> P24 -> P25 -> P26 -> G4
                   |
               P27 -> P28 -> P29 -> P30 -> G5
```

Phase D profiling P15 can start immediately after G0. Phase E sampling design P21 can also begin early as a standalone prototype, but **integration of new batches, materials and scatter must wait for compatible visual data contracts**. Pilot art P07/P08/P09 can proceed in separate files/worktrees; shader/material edits P10–P14 need one coordinated owner because they share bindings.

---

## 7. Concrete per-task handoff and agent operating protocol

For every P-task, the implementing agent must provide a task packet before making edits:

```text
ID/title:
Baseline commit SHA:
Owned paths / forbidden paths:
Observed current implementation:
Intended behavior (visually and mechanically):
Inputs from authoritative sim:
Outputs to visual renderer / caches:
Dependencies and compatibility mode:
Implementation steps (max 3–6 small modifications):
Unit/static tests:
GPU capture / performance scenarios:
Reference views and expected differences:
Rollback switch or revert boundary:
Known uncertainty / experiment before assuming:
Completion artifacts and comparison links:
```

**One task = one bounded, reviewable change.** If a task proves too large, split it into substeps such as `P17a` buffer capacity, `P17b` slot mapping, `P17c` invalidation, `P17d` compaction perf. Do not conflate renderer fixes with ecological rebalance. The human primarily judges final aesthetics, but the agent must still run deterministic and structural tests before requesting that judgment.

Standard task completion:
1. Fetch current head and relevant docs/code; inspect previous task's artifacts and failing tests.
2. Write expected state transitions and invalidation scope in plain language before coding.
3. Change only owned files or coordinate explicit shared-file lock with other agents.
4. Build; run scoped xUnit and Godot boot/render smoke; use the existing `scripts/verify.ps1` entrypoint and the new P03 benchmark.
5. Capture before/after same-seed fixed views, including at least one moving camera and one real growth/event transition; report **actual** timings, not selected successes.
6. Compare digest, geometry/material validation, FPS percentile gates, recurring hitches, VRAM and memory trends.
7. Record what was tried, what failed, photos and reports, and any visual limitations.
8. Commit with task ID and precise description; keep rollback possible. Merge only after tests and the applicable gate.

**Parallelism:** source-profile generation for different species is safe in separate worktrees; performance capture must run isolated on the machine and **never concurrently with another GPU test or Godot editor**. Integration of changes to `OrganismRenderers.cs`, `EnvironmentRig.cs`, shared shaders and `IslandRenderer.cs` must be serialized to avoid conflicting attribute and resource-lifetime contracts.

### 7.1 Suggested test inventory

Pure .NET:
- `tests/Vivarium.Sim.Tests/FloraVisualTests.cs`: metadata and tier stable identity; add botanical anchor and variant hashes.
- `FloraRefinementTests.cs`, `FormTests.cs`, `GeometryLodTests.cs`: geometry continuity, triangle degeneracy budget, no visible differences, explicit pre-existing failure disposition.
- `TerrainSurfaceMaterialTests.cs`, `WorldTests.cs`, `TerrainWaterToolTests.cs`: height, soil/water boundary and field invariants.
- `PersistencePerfTests.cs`, `IntegrationTests.cs`: digest and save/load under renderer changes.

Windowed Godot:
- `--specimen`: front/back/side/above/below, three stages, fixed lighting and wind.
- `--species-view`: actual island context with ecology fields intact.
- `--reference`: full named scenes and original-shader comparison.
- `--perf-test`: dynamic and frozen, close/grazing/sculpting, stress populations.
- `--flora-bake`: texture/manifest validation (requires rendering device), no silent headless fallback.

Automated image checks can flag byte-level reproducibility, silhouette masks, gross color-space change, grid repetition and large temporal discontinuities; **they do not replace human visual approval** of believable morphology. Benchmark failures are not fixed by hiding difficult shots.

### 7.2 Rollback and feature flags

Keep all newly invasive implementations behind a single small family of presentation-only toggles while being proven:
- `VIVARIUM_VISUAL_PIPELINE=legacy|hybrid` (existing legacy flora flag remains supported until transition), and explicit recipe opt-in per species;
- `VIVARIUM_RENDER_BATCHING=legacy|tiled`;
- `VIVARIUM_RENDER_LIGHTING=baseline|candidate` for lighting A/B;
- `VIVARIUM_GROUND_COMPOSITION=baseline|candidate`.

These flags are **temporary migration scaffolding**: document each flag's removal criteria and delete its dead branch when the target path is accepted. Do not proliferate permanently supported modes or silently change a release user's quality preset. All flags must leave save digests untouched. Retain source-control rollback even after flags are removed.

---

## 8. Risk register and fallback decisions

| Risk | Early signal | Mitigation / fallback |
|---|---|---|
| Beautiful single plant but ugly planted world | macro specimen great; interaction/overview repetitive | Include per-guild world scene, clustered morphology, distribution and ecology-gradient review before rollout |
| Asset generator duplicates anatomy or removes approved forms | species compare reveals missing unique signature | pin accepted recipe/source; require targeted approval before replacing its topology |
| One mesh per leaf or material per instance | draw-call count or CPU memory balloons | cached species variants, shared arrays and stable per-instance metadata |
| Batch splitting creates too many draw calls | p95 worsens as tile size decreases | measure 1/2/4 m vs global; choose hybrid size per guild, not universal tiling |
| Dynamic growth invalidates render resource every tick | repeated CPU mesh builds and upload spikes | versioned topology vs transform/health, latest-wins dirty-region updates, bounded queues |
| Background workers race mutable simulation | nondeterministic geometry or intermittent crashes | copy immutable RenderSnapshot on main thread; version-check before commit |
| Contact AO looks like black halos | base of all plants is a dark circle | fix geometry first; local physical contact mask and low AO intensity |
| SSIL/SDFGI costs exceed benefit | high fill/GPU ms or temporal swimming | remove; calibrate sun/ambient and local AO before GI |
| Terrain sculpt rolls grass/litter again | visual randomization when brush crosses cell | seed by stable world tile; reproject original scatter anchors |
| Species grows but visuals pop between tiers | silhouette discontinuity in orbit video | stable anatomy and curvature-only tessellation changes, hysteresis, or eliminate offending tier |
| 30 FPS is met only on frozen simulation | p99 degrades in live world | optimize snapshot/invalidation and existing ecology cadence separately; never claim GPU-only pass |
| VRAM/memory creep | long sculpt/growth soak rises steadily | reuse buffers/ArrayMesh, explicit disposal; add cache limits |
| Conflicting unfinished plans | agent changes litter accounting while doing visuals | ecology work explicitly lives in existing continuity/lifecycle plan; presentation work cannot alter mass |
| "Optimizer" deletes needed organic detail | foliage looks sparse or terrain becomes tinted mush | compare macro/normal/overview and reject visual sacrifice unless user approves |
| Leaf orientation transforms distort baked normals | backside appears dark/bright inconsistently | mandatory local/world tangent-frame debug test and double-sided front/back capture |

**Stop-work conditions:** simulation digest changes under renderer-only operation; reproducible output differs for same inputs; GPU test isn't isolated; any data-loss/export regression; repeated severe frame stalls; a rendering shortcut conspicuously simplifies visible biological structure; or new renderer ownership races. Revert the offending slice, document cause and continue on independent work.

---

## 9. What "done" looks like

The final build:
- Presents an island in which plants, moss, fungi, rock, soil, water margins, and dead organic material share coherent scale, surface properties and contact. Each species has recognizable anatomy, natural asymmetry, stage variation and meaningful habitat placement.
- Maintains the current deterministic ecosystem, save format, tools, sculpting and hydrology behaviors. No renderer state changes the sim digest.
- Uses a documented repeatable source-to-recipe-to-runtime asset path: agents can improve one species without touching a dozen unrelated systems or hand-editing a Godot scene; committed runtime outputs rebuild deterministically.
- Has actual tile/snapshot/buffer metrics showing why its batching and refresh scheme fits a small dynamic island; no visual flicker or stalls during growth, deaths, camera motion and sculpting.
- Meets the GTX 1080 / first-gen Ryzen **30 FPS** benchmark contract with measured frame percentiles, clean A/B captures, bounded video/process memory and reproducible profiling artifacts.
- Ships an offline release with valid attribution, no Blender requirement, no new paid service and no unnecessary engine migration.

### Minimal first agent assignment

**Implement P00–P03 only.** The immediate goal is a credible, automated, fixed-scene performance/visual baseline on the actual machine, including honest failure results. Once G0 reports real bottlenecks, assign **P04–P09** to one material/geometry owner with separate pilot recipe workers. Do not start an expensive GI system or write a bespoke GPU renderer before the profiling and pilot gates justify either.

---

## 10. References and existing project instructions

Project:
- [Vivarium architecture](../architecture/architecture.md)
- [Engine and renderer selection](../architecture/adr-001-stack.md)
- [Dedicated flora representation and GTX 1080 measurements](flora_visual_system.md)
- [Existing photorealism benchmark and materials](README.md)
- [Material surface pass and observed visual limits](material_surface_pass.md)
- [Approved species visual changes and outstanding perf questions](../flora_visual_review_log.md)
- [Ground continuity, litter and decomposition — authoritative ecological scope](../ground_continuity_lifecycle_plan.md)
- [Coarse fauna ecology/perf plan — distinct subsystem](../fauna_coarse_ecology_performance_plan.md)

Technical:
- [Godot 4.7 spatial shader reference: BACKLIGHT, transmission, double-sided surfaces](https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/spatial_shader.html).
- [Godot MultiMesh documentation and culling tradeoffs](https://docs.godotengine.org/en/stable/classes/class_multimesh.html).
- [Godot Forward+ renderers and feature availability](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).
- [Godot global illumination techniques and caveats](https://docs.godotengine.org/en/stable/tutorials/3d/global_illumination/introduction_to_global_illumination.html).
- [Godot SDFGI limits: dynamic occluders and runtime cost](https://docs.godotengine.org/en/stable/tutorials/3d/global_illumination/using_sdfgi.html).
- [NVIDIA GPU Gems 3, Ch. 16: hierarchical plant motion and vegetation shading](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis).
- [glTF 2.0 specification and format compatibility](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html).

**Implementation note:** references describe techniques, not blanket endorsements. Verify APIs against the installed Godot 4.7.1 .NET bindings and generated resource behavior before coding. All hard performance numbers must come from repeatable tests on the target hardware, not published engine claims.
