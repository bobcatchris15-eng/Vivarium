# VIVARIUM: FUNDAMENTAL REPRESENTATION & RENDERING REARCHITECTURE

**Execution plan for a Fable 5.2 orchestrator using predominantly Haiku workers and occasional Sonnet workers**  
**Prepared:** 2026-11-12  
**Research status:** primary project pages and documentation checked; citations and implementation leads at the end.  
**Repository:** https://github.com/bobcatchris15-eng/Vivarium  
**Current GitHub `main` reference when drafted:** `a193e6d5ca540614020544cd4ac0db4edd9dbefe` (2026-11-10). Re-inspect the actual checkout before acting.

---

## 0. This is the operative instruction, not a product proposal

You are the **Fable 5.2 research architect, orchestrator, integration lead, and judge of visual results**. Your task is to substantially reconsider and then rebuild Vivarium around representations better suited to a **small, self-evolving, visually beautiful ecological island**. Delegate implementation primarily to **Haiku** and sometimes to **Sonnet**. Do not delegate architectural judgment, experiment comparison, or integration ownership.

Vivarium is a **hobbyist, experimental, fun project**. It has no customers, release obligations, compatibility contract, stakeholder approvals, or production roadmap. **Backwards compatibility has no value**; existing saves, APIs, scene structures, rendering systems, engines, and languages may all be discarded. Rewriting is fine. Novel experiments with negative results are fine. Do not preserve current code merely because it exists. Do preserve or improve what looks and behaves good.

The aims, in order:

1. **Increase photorealism at ordinary viewing distances**, with believable organic structure, botanical identity, contact, transitions, microstructure, coherent materials and light. Avoid visible mesh intersections, floating leaves, discontinuous joints, thin or hollow distant canopies, tiled/noisy dirt, unrealistic water/shore transitions.
2. **Reduce total resource use for comparable or superior visual fidelity**: steady CPU/GPU time, spikes, RAM, VRAM, uploaded bytes, geometry and simulation work. Aim to do less work, not merely move the same work to GPU or increase RAM use.
3. **Make the ecology itself convincingly causative**. The same substrate, structure, growth and hydrology properties should explain both ecological behavior and appearance. Keep a productive, slowly changing, diverse habitat rather than freezing it or collapsing toward a single species.
4. **Have interesting, technically informative experiments**. New engines, languages, and computational representations are valid. Don't shy away from a decisive replacement.

**Not goals:** backwards-compatible saves; preserving Godot; preserving C#; keeping existing material/shader interfaces; commercial robustness; shipping speed; CI/CD, PRs, elaborate approvals; arbitrary absolute FPS targets; wholesale adoption of a paper without showing a benefit.

**Working style:** implement the full chosen redesign rather than treating a placeholder MVP as the destination. Use small *scientific comparison experiments* only to decide which architecture is worth implementing. If a prototype fails, explain why and try a different representation rather than endlessly tweaking parameters. Strong visual effects that initially look subtle should be exaggerated to prove they work, then returned to realistic values. Prefer main checkout and coordinated direct implementation; avoid worktrees, PR/CI machinery unless explicitly needed for a specific experiment.

## 1. Start by reading the REAL checkout, then measure it

The GitHub checkout inspected for this brief currently describes **Vivarium 0.1.4, Godot 4.7.1 .NET / Forward+, C# game with separate `src/Vivarium.Sim/`**. Source folders in this version include:

- `game/Render/OrganismRenderers.cs`: flora instancing, species/morph/LOD `MultiMesh`, periodic `Refresh()`, camera-based refresh, cached plant placement, per-species instance-buffer grouping. Review `FloraRenderer` around lines 17–115, 547–601, 732–986, 1440–1690. **A camera move can re-traverse and regroup the whole flora population**, even though placement and uploaded buffers are partly cached. Check the current code before treating this as definitive.
- `game/Render/RenderDirtyRegions.cs` and `RenderSnapshot.cs`: there are already region invalidation and snapshot mechanisms; some dirty-region data is not consumed by the full flora-instance rebuild.
- `game/Render/CoverageRenderer.cs`, `AmbientGroundCoverRenderer.cs`, `SoilDetailRenderer.cs`: coverage fields, tiled rebuilding and ground-cover/detail instancing exist. **Don't assume the current code is naïve**.
- `game/Render/EnvironmentRig.cs`, `game/project.godot`: the normal application uses *quality tier 1*: shadow atlas 4096, SDFGI and SSAO and volumetric fog enabled; SSIL and SSR disabled under that tier. Beware of treating the 8192 values in project defaults/high-quality tier as the actual normal runtime configuration.
- `game/Shaders/leaf.gdshader`, `flora.gdshader`, `terrain.gdshader`, `coverage_mat.gdshader`, `loam_ground.gdshader`: sophisticated shaders with procedural detail; prioritize *measured pixel cost* over counting shader lines.
- `src/Vivarium.Sim/World/VivariumWorld.cs`: domain, fields, water, individual flora, fauna, coverage, litter, roots and multi-cadence scheduler. Core update dispatch around lines 201–243. `src/Vivarium.Sim/Time/Scheduler.cs` has per-advance time budgeting/backlog. `game/App/GameSession.cs` advances the simulation in `_Process`.
- `src/Vivarium.Sim/Geometry/`: species-specific procedural geometry, botanical placement, and `FoliageAccommodation.cs` / `FoliageBladeAccommodation.cs`. Existing accommodation work may be useful as a **one-time growth/placement constraint**, not a permanent expensive physics loop.
- `game/App/FrameProfiler.cs`: useful CPU timers but not a substitute for genuine GPU timing.

The repository's old `docs/` material appears archived outside the checkout; don't assume an old `AGENTS.md` or `docs/render_plan.md` exists. Search for those files; don't block if absent.

**Baseline capture (Haiku task):**

1. Check out / inspect actual main, available hardware and tools. Find current profiler, headless/batch hooks, render-review and capture commands. Do not assume an old path has survived.
2. Create **three reproducible scenes** or fixed world seeds: densely planted mature island; mixed damp/dry terrain with exposed rock and soil; close-range detailed plants, moss and fauna/guardian contact. Include idle and slowly evolving states.
3. Capture **six fixed camera positions** for repeatable comparisons: overhead, eye-level understory, macro soil, plant junction/leaf overlaps, shore/water transition, distant dense canopy. Include daylight and night where useful.
4. Measure 30–120 seconds steady-state and startup/world-generation separately. Report median and p95/p99 CPU frame time, GPU frame time if accessible, RAM/VRAM, draw calls, visible and culled triangles, instance-buffer bytes uploaded per second, mesh generation time, shader/material costs, number of organisms/colonies, ecological backlog and simulation seconds/sec. Separate CPU-bound from GPU-bound; note unmeasured metrics. Profiling must not silently change visual settings.
5. Store the captures, machine specs, command line, source commit and measurements under `experiments/representation_rebuild/baseline/` (or equivalent). Avoid creating CI or PRs.

**Comparison protocol:** same world seed, growth state, camera, time of day, image resolution, exposure and lighting intent; compare both *equal-quality resource use* and *equal-resource visual quality*. Report quality differences in plain words, not just numbers. Don't claim performance gains on unmeasured hardware.

## 2. Proposed replacement model: a world of state, not a world of meshes

This is the proposed architectural hypothesis. Fable may replace it if the experiments disagree, but must state why.

```
AUTHORITATIVE ECOLOGICAL STATE (deterministic, compact)
  world / terrain substrate horizons / water and nutrient fields
  structural plant organ graphs + colony and population distributions
  surface colonizers / litter / decomposers / fauna and guardians
                |
                | versioned changed-region records, material/organ IDs
                v
SCENE COMPILER / REPRESENTATION PLANNER
  decide visibility and projected-size detail
  maintain sparse geometry/material/light caches by region
  select geometry / instancing / aggregate / implicit representation
                |
                v
VISUAL PRESENTATION
  coherent near-field plant organs and substrate microgeometry
  mid-field assembly/cluster geometry and stochastic microstructure
  far-field aggregate foliage / canopy optical proxies
  cached directional illumination and selectively live lights
```

### Data contracts to agree on before parallel work

**World-space units:** metres, seconds for physical time, explicit bio-time multiplier for ecosystem; linear color values for lighting; fixed seed. Do not allow each experiment to invent incompatible coordinate conventions.

**Stable identity:** `world_seed + entity_or_patch_id + organ_or_feature_id` determines procedural variation. Appearance cannot reshuffle on camera motion, reload or representation transitions.

**Dirty region / event:** `{bounds, domain, component/version, reason, time}`. Domains: geometry, soil materials, vegetation, coverage, water, light. A change in a 20 cm patch must not invalidate a whole island unless physically necessary.

**Substrate sampler:** `SampleSubstrate(position, depth)` returns solid/void, material fractions, horizon identity, structural strength, porosity/aggregate scale, moisture (bulk, optional film/vapor), organic content, surface chemistry, colonization capacity. A companion `SurfaceSample(position)` returns height or nearest surface, normal, material and attached growth. Exposed soil sides/holes must query consistent volumetric state.

**Plant interface:** `PlantDescriptor` contains species traits and structural organ graph (parent ID, organ type, transform/tangent, radii, growth state, health, display flags); optionally compact envelope/leaf-area distributions for dense organs. World changes topology at biological timescales, render system chooses representation at visual timescales. A plant's renderer is not its ecology.

**Coverage/colony interface:** `SurfaceColonyPatch` has substrate association, area/occupancy, local thickness/density, species mixture, health, growth front, aggregate optics. No need for a separate full organism object for every tiny moss shoot.

**Geometry cache contract:** inputs `(world/feature IDs, state version, geometric detail parameters)` -> versioned mesh/assembly/volume/texture. Cached outputs are explicitly disposable. Geometry compilation may be CPU, compute, or built directly by shaders. **Generation every frame is not presumed cheaper than generation only when necessary**.

**Lighting contract:** geometry-derived occlusion/transport cached by changed region, direct light updated when sun/camera moves, dynamic highlights and selected bioluminescence treated separately. No cached lighting should become visibly stale when a plant grows, a rock is removed, or the sun moves.

**No cross-domain hidden writes:** renderer never mutates canonical plant shape, resource balance or terrain; ecology may consume shape/area/volume summaries but not require frame-dependent meshes. Workers must expose enough data to verify their layer independently.

## 3. WORKSTREAM P — structural plants, component instancing, and foliage aggregates

**Hypothesis:** storing a plant as topology + organ parameters + shared anatomical pieces produces **more believable joints and silhouettes for fewer stored bytes and fewer repeated rebuilds** than storing many complete species/morph LOD meshes.

### P1. Botanical structural generator

Implement a small independent plant schema and compiler supporting at least:
- `Axis` / stem segments: parent and branch node, start/end spline tangent, taper radii, cross-section, growth maturity, wood/bark tissue class.
- Nodes/meristems: internode spacing, phyllotaxy, attachment, dormancy, branching rules.
- Leaf organ: attachment/root tangent, curved midrib, outline coefficients, width profile, curl, thickness or two-sided optical parameters, venation class, chlorophyll/wax state.
- Flowers/fruits: compound structures connected to real pedicels; visible attachments and materials, not hovering decorative clumps.
- Species behavior/identity traits: distinct branching and crown architecture, not uniform procedural randomness with changed colors.

Start with **Glowbriar** (woody branching, leaf and junction test), **Moonfern** (repeated fronds/leaflets) and one complicated broadleaf or shrub, e.g. **Shadebells/Embercrown**. Use visible real-world botanical anatomy as a plausibility reference without turning fantasy species into ordinary Earth plants.

Generate **junction continuity**: sweep/taper connected stems and petioles into parent geometry; avoid exposed flat tube ends and ellipsoid-linkage anatomy. Use proper tangents/fillets/union at large joints and cheaper junction meshes at small ones. Leaves at growth time should consider nearby stems/leaves, using existing accommodation/occupancy research: wood > major stems > petioles > leaf blades, and stable first-claim occupancy within each tier. Deterministic, local, growth-time only; don't simulate collisions every rendered frame.

**Haiku:** schema, deterministic sampling, specimen generator, tests, screenshots, data serialization. **Sonnet:** topology/growth math and genuinely difficult junction/implicit blending if needed. **Fable:** judge whether results are convincingly botanical.

### P2. Rendering alternatives — compare, don't assume

Implement **three variants for the same organ description**:

1. `CACHE_ASSEMBLY`: CPU or offline-generated shared leaf/branch/frond primitives; compact per-organ transforms/parameters; cached assemblies and chunked MultiMesh (or equivalent); update only changed clusters.
2. `GPU_GENERATE`: compute-shader generation and/or GPU-oriented instance expansion from compact organ graphs; persistent GPU buffers; optional indirect draws only if supported. Compute generated meshes **on change** as the baseline. Separately test per-frame generation; measure if it genuinely saves memory without excessive ALU/bandwidth/sync cost.
3. `AGGREGATE_FOLIAGE`: from mid/far distance, represent leaf clusters using projected-density proxies (leaf normal distribution, occupancy/opacity, color/roughness/transmission distribution); preserve silhouette and canopy opacity instead of dropping triangles and leaving holes. Could be clustered crossed geometry, microcards, or small voxel/brick proxies; select by measured appearance, not novelty.

**Important GTX 1080 constraint:** the 2025 GPU tree paper uses GPU work graphs and mesh shaders, which aren't supported on Pascal/GTX 1080. **Do not port its shader API verbatim** or require hardware ray tracing. Port the *representational ideas* using Vulkan/D3D12 compute + ordinary instancing/draw or precomputed caches. A separate newer-GPU branch of the experiment is fine only if optional.

**Culling:** use modest world-space chunks with AABBs rather than one world-spanning MultiMesh per species; project size to pixels and use hysteresis/crossfades for LOD. Check per-cluster draw-call overhead. On a camera move, avoid `O(all plants)` rebuilding/uploading; only visibility/LOD selection should change unless a new cache level is needed.

**Required comparisons:** close silhouette, branch junctions, leaflet orientations, shadow stability, alpha/temporal shimmer, canopy coverage at 3 m and across the island, morph continuity after growth, CPU/GPU time, full mesh storage, GPU buffer upload and VRAM. Do not accept better FPS caused by noticeably thinner plant density or simpler species identity.

**P workstream done when:** one species set has identifiable close geometry, unchanged density/identity at mid/far, stable structural attachments, deterministic regeneration, and a measured better resource/fidelity tradeoff than conventional whole-plant mesh variants.

## 4. WORKSTREAM S — new soil, geology, and root substrate

**Hypothesis:** a sparse **layered material volume** with implicit/sampled microstructure will look more convincingly like actual soil and rock than surface textures + independent scattered props, while avoiding per-grain simulation.

### S1. Material volume, not one global dense voxel array

Model soil/rock over the island as:
- `HorizonDescriptor` / compact spatial layers: bedrock, weathered mineral layer, mineral soil, humus, litter. Layer thickness, composition and boundaries vary with actual drainage, slope, deposits and plant activity; avoid random independent color noise.
- A sparse **near-surface brick cache** or heightfield + column profiles for most horizontal territory; **local 3D implicit/SDF bricks only** for cliffs, crevices, exposed sides, holes, undercuts and close-up patches. Whole-world centimeter voxels are explicitly forbidden as default architecture.
- Fractions for coarse fragments, sand/silt/clay, organic matter; porosity, aggregate diameter distribution, compaction, moisture, mineral hue and strength. Sum/check fractions and expose them to rendering and ecology.
- `MaterialEvaluator(position, scale, seed)` builds correlated grains, aggregates, pores, fibers and layered strata. `scale` permits a filtered representation when grains are subpixel. Stable coordinate hashing, no temporal swimming or fake speckled noise.

Visual surface alternatives to compare:
1. **Hybrid height/displacement** with cached procedural albedo/normal/roughness and small exposed aggregate meshes.
2. **Implicit microstructure + sphere tracing** in bounded close patches, based on the 2025 multiscale geometry work.
3. **Generated close-range surface mesh** from implicit samples, with cached lower-detail meshes/normal fields farther away.

The entire world must not be sphere-traced per pixel by default. The implicit geometry research proves representational possibility, **not a GTX1080 real-time performance guarantee**.

### S2. Rock is a geological component of substrate

Rock outcrops, buried stones, strata and fractures share geological material with surrounding terrain. Allow natural coherent exposed edges, fissures, undersides and soil-filled pockets. Use material hardness and bedding directions to determine fracture/erosion patterns; borrow *concepts* from Arenite instead of implementing a full destructive sandstone solver unless it measures worthwhile. Keep ordinary rocks cheap through cached mesh surfaces.

### S3. Soil-water-plant coupling

Conserve liquid water across **surface runoff, percolation, storage, roots and evaporation**. Track a minimal physically informed pore/moisture state, not just a painted wetness scalar. Suggest initial fields: volumetric water content, capillary retention tendency, permeability, water table/head at coarse resolution; optional film-water and vapor exchange only in relevant near-surface/dry conditions. Link visible wetness, surface roughness, puddles, moss habitability and root uptake to that state.

The 2026 Virtual Soil Simulator paper motivates separating capillary, film and vapor transport; **do not replicate its heavy OpenFOAM numerical solver wholesale**. Implement stable conservative finite-volume fluxes where useful; check nonnegative water, stability at variable time steps and closed-domain conservation. If biological time acceleration differs from physical time, define the exchange explicitly.

### S4. Roots

Use major structural roots as sparse connected geometry and fine roots as density per soil region/horizon, as in functional–structural plant modeling. Nutrient uptake, moisture drawdown, root competition and local soil structure use fine-root density, not millions of individual cylinders. Near a cut/dig site, generate visible fine rootlets from the stable density field.

**S done when:** a 1 m³ excavated substrate sample has convincing composition, natural layers/pores/aggregates, different dry/saturated appearance, root interactions, coherent exposed rock and soil, stable details under camera movement, no large per-grain simulation burden, and verified water/material conservation.

## 5. WORKSTREAM C — moss, lichen, litter and ground cover as attached growing materials

**Hypothesis:** aggregate colonies attached to a substrate can preserve visible lushness while tracking far fewer independent organisms.

### C1. Substrate-attached growth fronts

Build `SurfaceColonyPatch` over the shared substrate interface. Each patch tracks species, local coverage, thickness, biomass, age/health, nutrient draw, moisture retention and advancing perimeter. Establish at a point, expand over hospitable surface, slow at unsuitable edges, merge compatible neighbors without seams and compete with others. For steep rocks/logs, use a surface parameterization or triangle/surface adjacency mapping rather than planar island grid only.

Moss, lichen and fine ground cover must be **distinguishable**:
- Moss: tiny branching leafy shoots, cushion/mat density, fuzzy but not uniformly furry, damp edge growth.
- Crustose lichen: mineral-following thin crust and cracked/color-banded margin.
- Foliose lichen: raised lobes/sheets with realistic overlap and substrate attachment.
- Fruticose lichen: sparse branching microstructure.
- Litter: deposited mass from local plants that changes soil composition and visual color/geometry as it decomposes; don't independently simulate every dead leaf indefinitely.

### C2. Fiber/mat representation LOD

Close: instanced shoots/leaflets and real lichen lobes, surface-aligned; mid: compressed strand/clump families with collective scattering and structure-aware normal/roughness; far: coverage/volume proxy. The 2025 strand-based LOD research is a **transfer of method from hair/fibers to moss**, not a documented moss renderer. Measure it. All LOD transitions must preserve coverage, total mass/biomass read, irregular silhouettes, optical density and distribution of shades.

**C done when:** colonization visibly spreads and merges on soil/rock/log with no edge seams or swimming, remains biologically consistent, and costs materially less than individually modeled microflora at equivalent visual density.

## 6. WORKSTREAM L — time-coherent, ecology-aware illumination

**Hypothesis:** an ecosystem that changes slowly should cache most of its **geometric light transport**, using modest real-time lighting for the sun, motion and selected emissive organisms.

Build a **lighting experiment** with consistent reference captures:

- Baseline: normal-tier Godot lighting (SDFGI + SSAO + volume fog + dynamic directional shadow; SSIL/SSR normally disabled) and existing leaf material optics.
- Candidate A: low-resolution **cached sky-visibility and directional occlusion field** computed from canopy/terrain/rocks and updated by dirty region; direct sun sampled dynamically against field/near shadow maps; local baked-in structural AO for joints/interiors.
- Candidate B: **sparse spherical-harmonic irradiance probes** or directional ambient basis per spatial region, updated only for meaningful geometry changes, relit for time-of-day without rebaking whole geometry; high-value close shadows live.
- Candidate C: current high-quality approach at lowered settings plus targeted material improvements as an honest competitive baseline.

**Requirements:** leaf underside transmission/backlight, local foliage contact, damp substrate specular response, rock crevice shading, sun moving over a day, night bioluminescence, no sudden illumination pop on plant growth. Careful with double-occlusion (environmental field *and* SSAO *and* baked material). Luminous organism lights should continue to be budgeted spatially; not every glowing leaf requires a dynamic light.

Do not simply bake a static screenshot into the world; cache **geometry response** separately from changing light direction/energy.

**L done when:** equal or better close/mid lighting with measured GPU resource savings, stable temporal behavior and responsive ecology changes. If the actual savings are marginal, retain simpler stock lighting.

## 7. WORKSTREAM E — ecological representation and scheduling

**Hypothesis:** ecological significance should determine the number of independently updated entities and update frequency.

Create a common **individual → colony → population patch → environmental field** hierarchy. Keep exceptional trees, distinctive plants, guardians and important animals as individuals; represent dense background small flora, rootlets, decomposer spread, moss and competing populations as aggregate state where appropriate. Allow promotion/demotion when a patch becomes a large distinctive specimen, **conserving biomass, population counts, energy/resource budgets and reproducible random state**. Maintain ecological gradients (wet to dry, illuminated to shaded, deep to shallow substrate) that arise from geometry and hydrology, not arbitrary random fields. Prevent one species trivially dominating each niche; let tolerances overlap and differentiation arise from actual resource use, shade, architecture and dispersal.

Separate biologically slow update cadence from visually frequent interpolation. If the simulation runs in a separate worker/CPU thread, use immutable versioned snapshots and bounded queues; **moving CPU work off the render thread is a latency tactic, not proof of lower total CPU work**. Test variable timesteps against equivalent elapsed biological time and mass/conservation; use event-driven or region dirty updates only when they don't change the ecology incorrectly. Avoid per-frame re-evaluation of plant growth.

**E done when:** the island becomes denser and more diverse over time with stable ecological throughput, low update costs, no growing simulation backlog and no hidden freeze of off-camera ecology.

## 8. ENGINE / LANGUAGE DECISION: choose by experiments, not migration anxiety

Do **not** presume Godot is the final host. Make a short, comparable render harness showing a plant cluster and a soil patch with a fixed camera and material setup:

| Candidate | Why test it | Requirement to pass |
|---|---|---|
| Current Godot Forward+ / C# | Existing procedural ecology/UI, RenderingDevice compute, quick experiments | Can retain most renderer quality while avoiding population-wide instance uploads and costly screen-space passes |
| Godot + C++ GDExtension/RenderingDevice | Native geometry/field generation and compact SoA storage, custom GPU buffers | Clearly improves a measured bottleneck, not just a language rewrite |
| Custom `wgpu`/Rust or Vulkan/C++ renderer | Full control of caching, culling, indirect draws, compact material fields | Must produce an actually better *visual + resource* result on the target GPU without spending all effort on plumbing |
| Unreal 5.8 experimental Nanite Foliage | Reference implementation of assemblies, voxel aggregates and foliage animation | Only use as full engine if feature/hardware tests pass; GTX 1080 may not support required modern Nanite features. Otherwise borrow the conceptual model |

**GTX1080/Pascal constraints:** lacks modern mesh shaders/work graphs and hardware ray-tracing acceleration. Compute shaders, standard rasterization and GPU instancing are realistic candidates. No planned critical path may require unsupported hardware features. Check exact device/driver support on the real machine; do not infer every Vulkan extension. Unreal's documented Nanite requirements include SM 6.6 atomics or Vulkan 64-bit atomics; test rather than assume.

Make a **decision record** after initial prototypes: visual result, per-frame CPU/GPU, memory, draw/upload cost, implementation complexity, hardware support, ease of botanical iteration. Complexity is relevant as a *hobby enjoyment and experimentation criterion*, not a release/disruption cost. Fable may decide to replace the engine decisively and delete legacy integration layers.

## 9. EXECUTION: Fable delegations and dependency map

Fable owns architectural interface agreements, main checkout, visual assessment, final engine choice, integration, experiment comparison, stopping unsuccessful branches and the final write-up. Use **Haiku for most implementation**. Use **Sonnet sparingly for deep cross-module design, numerical stability, new GPU pipeline code, difficult mesh junctions, or rescuing a worker stuck on a genuinely hard problem**. Don't consume Sonnet for simple edits, harness scripts, tests or straightforward plumbing.

**Parallelism rule:** run 2–3 workers maximum when ownership doesn't conflict. Give each worker **a precise file/directory ownership scope, interface, test/capture instructions, definition of done, and short report format**. No two workers edit a shared file simultaneously. Work directly in one coordinated checkout; use independent `experiments/...` modules for parallel trials, not parallel Git worktrees. Fable integrates before a worker touches another worker's interface. Existing local changes are work-in-progress to inspect, not automatically trash. Keep the history simple; no PR/CI setup.

### Dispatch table

| ID | Model | Depends | Deliverable / bounds | Acceptance |
|---|---|---|---|---|
| B0 | Haiku | none | Baseline capture+performance harness, standalone scripts, fixture manifest | Reproducible six views and machine metrics |
| R0 | Haiku | none | Repo feature and GPU capability audit (quick, exact refs) | Matrix of supported/unsupported features; no imaginary GPU APIs |
| I0 | Sonnet or Fable | B0/R0 | Define common state/geometry/lighting/material contracts | Shared types, coordinates, dirty/version semantics, no cross-workstream ambiguity |
| P1 | Haiku | I0 | Structural plant schema + 3 specimen generators | Same seed gives same joints/leaves across captures |
| P2 | Sonnet | P1 | Difficult junction/implicit sweep and attachment geometry | No visible seams, no floating petioles; stress specimen captures |
| P3 | Haiku | P1 | Cached shared-component rendering + chunked spatial instancing | Measured bytes/upload and no population-wide rebuild on camera motion |
| P4 | Sonnet | P1/P3 | GPU generation **or** aggregate foliage renderer, based on actual bottleneck | Equal-density visual comparison, GTX1080 path |
| S1 | Haiku | I0 | Soil horizon/material field and deterministic sampler | Layer and composition consistency, stable coordinate sampling |
| S2 | Sonnet | S1 | Implicit/agglomerated close-soil trial and cached hybrid mesh trial | Different wet/dry substrates, controlled performance, actual pores/aggregate structure |
| S3 | Haiku | S1 | Rock strata and exposed face mesh generation | Bedrock/soil joints continuous, no floating rock decals |
| C1 | Haiku | I0/S1 | Moss+lichen colonization fronts and surface attachment | Merge/seam tests, surface-specific habitat reaction |
| C2 | Haiku, Sonnet if needed | C1 | Multi-detail moss/lichen geometry + optical proxy | Stable clump density and close appearance across LOD |
| L1 | Sonnet | I0/P3/S1 | Sparse occlusion/irradiance cache with dirty invalidation | Lower measured lighting cost at equal image quality |
| E1 | Haiku | I0/S1 | Ecology aggregation/field migration in isolated fixtures | Conservation, diversity and off-camera consistency |
| E2 | Sonnet | E1/S3 | Coupled water/root/soil update with stability tests | Water/biomass budgets and wet/dry behavior |
| INT | Fable + Haiku | chosen trials | Replace old presentation/ecology interfaces, remove redundant systems | End-to-end world works; no double-rendering or duplicate ecology |
| REVIEW | Fable | INT | Side-by-side screenshots, counters, decisions, next experiments | Better visuals at comparable or lower resource use |

**Initial wave (parallel):** B0 baseline + R0 capabilities + one simple independent research spike (P1 or S1 *after* provisional contracts). Fable then publishes I0 so remaining work is not built against incompatible assumptions.

**Second wave:** P1, S1, and C1 or a separate lighting capture can proceed on different folders. Do not have P2/P3/P4 start before topology/data shape is accepted.

**Third wave:** P3 and S2 and C2. Fable pauses or respecifies branches that cause measurable regression. Use Sonnet only at technical choke points.

**Fourth wave:** L1, E1/E2, integration as dependencies permit. Select winning rendering platform based on P3/P4/S2 evidence, and **port only the winning representations**. Do not keep three renderers permanently as “compatibility options.”

**Last wave:** remove obsolete geometry/rebuild/lighting paths wholesale once replaced. Use Fable for final visual direction and architecture simplification, Haiku for deletion/cleanup/test updates.

### Mandatory worker task template

```
WORKER: [Haiku unless this is a named Sonnet-level problem]
TASK ID: [P1 / S2 / ...]
OWNED PATHS: [explicit paths; other workers own everything else]
INPUT CONTRACT: [types, units, seed/version semantics]
OBJECTIVE: [visible result and resource question, not 'refactor']
SPECIFICATION: [precise algorithm, cases, invariants]
BUILD/CAPTURE: [commands + fixture/cameras]
DONE WHEN: [artifact + visual + performance + correctness thresholds]
REPORT: 1) files changed; 2) what was built; 3) screenshots/metric links;
        4) invariants/tests; 5) what failed; 6) further research needed.
DO NOT: touch shared files/other workers' paths; add CI/PR; claim benchmark
        without GPU measurement; replace ecological mechanisms with random noise.
```

Fable reviews worker reports by *looking at captures and metrics*, not accepting agent self-assessment. If captures cannot be generated due to environment limitation, report that openly and record the executable reproduction command instead of pretending visuals were inspected.

## 10. Pass/fail: results must be visually better AND computationally leaner

At each candidate decision point, preserve a **paired visual/perf report**:

- **Botanical:** believable whole-species silhouette; continuous internode/stem/petiole/leaf connections; characteristic organ arrangement; sensible mature/juvenile variants; no conspicuous repeated clones; collisions handled by growth placement; dense canopy not hollow from LOD; no unnatural shimmer under movement.
- **Materials:** real-looking soil crumbs, bedding, roots, fissures, wet/dry surface response; no wallpaper or high-frequency noise pretending to be structure; no floating rocks or moss; close substrate and far surface agree.
- **Ecology:** actual habitat gradients and overlap of species; water, nutrients, biomass and litter make sense; no population-collapse shortcut; no endless backlog; off-camera still changes.
- **Renderer:** 30–120 sec capture showing CPU and GPU frame timing (median and tail), VRAM/RAM, draw calls, upload volume, cached bytes and world generation; repeat with a denser mature world and a slow camera orbit. Report CPU vs GPU, and absolute numbers rather than invented percentage savings.
- **Continuity:** same seed and state give same visual detail after reload/rebuild; changing camera doesn't regenerate leaves in different orientations; changing a local growth state dirties only the appropriate region; visible motion/interpolation remains smooth.

**Decision rule:** adopt an experiment if it improves the realistic visual read without a substantial resource increase, or decreases measured resource use without a visible loss; exceptional quality increases at small cost may also win if explicitly justified. Reject novelty-only changes. Do not lower world density, shorten simulation time, or change capture exposure to hide regressions. Run an exaggerated parameter test before declaring a subtle visual effect ineffective.

**Report the tradeoff openly**: a method may reduce VRAM but consume more GPU ALU, or reduce CPU spikes but increase average CPU. A single FPS number cannot answer whether it is better.

## 11. Concrete acceptance demonstrations

1. **“Three plants, no seams”**: Glowbriar branch/leaf junction, Moonfern compound frond, a broadleaf shrub. Macro stationary camera and slow orbit. Growth from juvenile to adult without popping organ connections. All recognizable and distinctive.
2. **“Living soil cube”**: one cubic metre of rocky, root-filled, variable-moisture soil with top, vertical cut and cutaway. Zoom 5 cm to 3 m, scrape/dig and watch detail remain stable and physically attached. No all-volume per-grain simulation.
3. **“Colonizing stone”**: wet porous stone and adjacent dry mineral. Moss establishes in a crevice, expands along damp route, merges with another colony. Lichen follows a different surface niche. No seam, no mobile texture.
4. **“Dense canopy orbit”**: mature crowded vegetation shown at close, middle, and far distances. Visual volume and light transmission remain credible. Camera movement does not trigger population-scale mesh rebuilding or uploads.
5. **“Day to dusk”**: daylight, shadowed understory, wet soil, then bioluminescent night. Cached lighting responds to sun/time and changes in geometry without artifacts.
6. **“Ecology stays alive”**: longer growth run; flora/cover increase and differentiate spatially, fauna remain plausible, resource budgets stay stable, no escalating backlog. Save compatibility irrelevant.

## 12. Research dossier — use these links ONLY when implementing a gap

Below are confirmed primary papers, project pages and engine documentation. Each contains **what is established**, **what to borrow**, and **what NOT to infer**. A worker should read the relevant subsection here first, only opening the original when encountering a missing method, equation, algorithm or hardware detail.

### R1. GPU-procedural trees (primary inspiration for P1/P2/P4)

- **Real-Time GPU Tree Generation** (Kuth et al., HPG 2025): https://diglib.cgv.tugraz.at/items/93fc78c0-71fa-4511-8564-a7e5268bf27a ; paper mirror: https://gpuopen.com/download/Real-Time_GPU_Tree_Generation.pdf
- **Established:** compact parametric tree descriptions can drive GPU-generated geometry with per-frame LOD, animation and details; reported 3.13 ms for a demonstration scene on **RX 7900 XTX**, **not GTX1080**.
- **Borrow:** plant topology/parameters, geometry-on-demand, screen-space tessellation/LOD, shared procedural organs and animation.
- **Do NOT infer:** mesh shaders or work graphs work on Pascal; regenerating everything every frame is always optimal; benchmark numbers transfer to our scene.

### R2. Aggregate foliage (primary inspiration for P3/P4)

- **Nanite Foliage / Assemblies / Voxels**, Epic Engine 5.8 documentation: https://dev.epicgames.com/documentation/unreal-engine/nanite-foliage ; assemblies: https://dev.epicgames.com/documentation/unreal-engine/nanite-assemblies
- **Established:** repeated botanical components can be micro-instanced inside a tree; near-pixel-scale aggregate voxel representations preserve dense foliage silhouette and use distributions of normals for optical behavior. Feature remains **experimental**.
- **Borrow:** component assembly, aggregate cluster representation and statistical normal/coverage when triangles become smaller than pixels; deterministic LOD transition.
- **Do NOT infer:** current Vivarium can directly run Unreal's implementation on GTX1080, or that Nanite's storage/performance figures imply comparable gains here.

### R3. Structural plant science (P1/E1/S4)

- **Elements of a functional–structural plant model** (*in silico Plants*, 2026): https://academic.oup.com/insilicoplants/article/8/2/diag023/8770950
- **Established:** anatomy can couple topology, geometry, physiological function and environment; major organs can be explicit, while canopy fine detail and fine roots can be represented as voxel/density distributions.
- **Borrow:** organ graph as a coherent shared source of shape and resource interaction; mixed spatial resolutions.
- **Do NOT infer:** a scientifically complete crop model is appropriate for a fantasy tabletop ecosystem.

### R4. Implicit soil microstructure (S1/S2/S3)

- **Procedural Multiscale Geometry Modeling using Implicit Surfaces** (Venu et al., CGF/PG 2025): https://doi.org/10.1111/cgf.70233 ; project page with code/supplement: https://graphics.unizar.es/projects/MultiscaleGeometry_2025/ ; preprint (earlier title “Implicit Functions”): https://arxiv.org/abs/2504.09553
- **Established:** controlled particulate, porous, fibrous and layered structures can be generated from procedural implicit functions and sphere tracing; particle agglomeration/piling without explicit individual-grain collision.
- **Borrow:** bounded, local implicit microstructure and correlated aggregate generation with scale-aware representation; geometry derived from composition and structure.
- **Do NOT infer:** whole-island per-pixel raymarching is cheap, or that the paper simulates actual soil ecology. Check project-code reuse terms before copying it; the project site indicates non-commercial availability.

### R5. Aggregate sand mechanics (S1/E2)

- **Numerical Homogenization of Sand from Grain-level Simulations** (Chen, Ly, Wojtan, SIGGRAPH Asia 2025): https://visualcomputing.ist.ac.at/publications/2025/HomogenizedSand/ ; DOI https://doi.org/10.1145/3763344
- **Established:** continuum properties can be derived from grain-scale behavior, avoiding tracking every grain for large-scale material dynamics.
- **Borrow:** aggregate porosity/strength/conductivity representations rather than per-grain dynamics.
- **Do NOT infer:** the complete material-point simulation is necessary; it is mainly a *model-selection* reference for Vivarium.

### R6. Soil moisture films and vapor (S3/E2)

- **Virtual Soil Simulator** (Kozyra et al., JAMES, July 2026): https://doi.org/10.1029/2025MS005421
- **Established:** capillary, film and vapor transport can significantly differ during soil drying; detailed Richards-equation/OpenFOAM work validates this distinction.
- **Borrow:** tiered moisture storage and transport; visible wetness and root uptake tied to actual substrate properties.
- **Do NOT infer:** full OpenFOAM/Richards numerical complexity is justified; preserve conservation with simpler models first.

### R7. Strand aggregation as moss analogy (C2)

- **Real-time Level-of-detail Strand-based Rendering** (Huang et al., EGSR 2025): https://doi.org/10.1111/cgf.70181 ; authors' paper: https://sites.cs.ucsb.edu/~lingqi/publications/paper_egsr25_hairlod.pdf
- **Established:** clusters of hairs/fibers can be replaced by thicker aggregate strands while preserving scattering; study reports up to 13x speedup in its test cases.
- **Borrow:** LOD optical-density conservation, aggregate scattering and strand-cluster representations for fine moss shoots/lichen branches.
- **Do NOT infer:** this has already been tested on moss or gives 13x speedup for our materials.

### R8. Geology/erosion-derived rock (S3)

- **Arenite: A Physics-based Sandstone Simulator** (Yang et al., SIGGRAPH 2025): https://jango6324.github.io/arenite/
- **Established:** structural stress, erosion, deposition and different material hardness produce coherent geological forms.
- **Borrow:** stratification, fracture and eroded-rock logic for shapes and surface material; optional offline authoring.
- **Do NOT infer:** we need continuous GPU sediment physics to render a believable rock.

### R9. Coupled terrain/water/vegetation fields (S3/E1/E2)

- **Oasis: A Real-Time Hydraulic and Aeolian Erosion Simulation with Dynamic Vegetation** (Nilles et al., GRAPP 2025): https://www.scitepress.org/PublicationsDetail.aspx?ID=JWwDcg51MFQ%3D&t=1 ; DOI https://doi.org/10.5220/0013112100003912
- **Established:** layered terrain, moisture, vegetation density and erosion can be coupled in a real-time experiment; authors report 2048² terrain and two million plant entities in the tested setup.
- **Borrow:** efficient map of vegetation influence on fields, coupled conservative state, patch-level environmental feedback.
- **Do NOT infer:** the result renders two million individually high-fidelity models, or that the test speed transfers to GTX1080.

### R10. Real engine capabilities and constraints (P3/P4/platform decision)

- **Godot 4 RenderingDevice compute shaders:** https://docs.godotengine.org/en/stable/tutorials/shaders/compute_shaders.html
- **Godot MultiMesh limitations:** https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html
- **Godot renderer capabilities:** https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html
- **Unreal 5.8 rendering hardware requirements:** https://dev.epicgames.com/documentation/unreal-engine/hardware-and-software-specifications-for-unreal-engine
- **NVIDIA mesh shaders introduced with Turing:** https://developer.nvidia.com/blog/introduction-turing-mesh-shaders/
- **Established:** Godot Forward+ supports compute; Godot MultiMesh culls as an entire mesh; Unreal Nanite requires modern shader/atomic capabilities; hardware feature support must be checked directly.
- **Borrow:** practical API constraints and fallback-free design for the actual available GPU.
- **Do NOT infer:** support for a newer GPU feature from support for Vulkan/DX12 generally. Avoid explicit blocking `RenderingDevice.Sync()` every frame; latency, stalls and Windows GPU timeout are real concerns.

## 13. Integration, cleanup, final artifacts

At the end Fable must deliver:

- A functioning Vivarium using the selected architecture, on whatever engine/language was demonstrated best; **no requirement to support existing save schema or previous API**.
- A concise technical map of **what is authoritative world state** and **which geometry/material/lighting is disposable/cached**.
- Screenshots/video of the six acceptance demos at fixed angles and equal settings; explicit failed experiments and remaining rough edges.
- A reproducible performance report showing resource changes and image fidelity tradeoffs. Identify which measures were not obtainable.
- A research decision log listing which ideas were used, modified, or rejected and why.
- Removal of redundant render/model pipelines and dead glue code once the replacement wins.
- Commit/push to `main` once integrated and verified if the connected environment and user authorization permit; no PR/CI machinery.

### Final direct instruction

**Do the research as a guide, not a pretext to defer.** Inspect the checkout, establish a baseline, create and compare real visual experiments, choose the strongest representations, then redesign Vivarium accordingly. Treat the entire technical stack as replaceable, but never compromise the organic visual quality or ecological coherence simply to post faster FPS numbers. Delegate actual implementation mainly to Haiku, escalate difficult GPU/numerical/geometry work to Sonnet selectively, and keep Fable in charge of architecture, visual judgment and integration.
