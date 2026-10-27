# Vivarium — Deterministic Foliage Spatial Accommodation
## Serialized clanker implementation plan | 2026-10-24

**Mission:** Eliminate conspicuous interpenetration of stems, petioles, leaves and neighboring plant foliage by negotiating static *render geometry* against nearby occupied space at build/growth-refresh time. Preserve realistic contact, overlapping canopies, visual density, and species silhouettes. No runtime physics or ecological state mutation.

**Execution mode:** One implementing agent, serialized checkpoints. Implement the complete feature, not a throwaway demo; commit at verified boundaries. Coordinate with the active performance worker before touching renderer hot paths. Do not overwrite unrelated dirty files. Read `AGENTS.md`, this document, `docs/overhaul/PHOTOREALISTIC_RENDERING_PLAN.md`, and the current rendering/performance work before editing.

### Navigation
- [0. Non-negotiable contracts](#0-non-negotiable-contracts)
- [1. Codebase anchors and integration problem](#1-codebase-anchors-and-integration-problem)
- [2. Geometry and occupancy data model](#2-geometry-and-occupancy-data-model)
- [3. Priority and negotiation rules](#3-priority-and-negotiation-rules)
- [4. Deterministic solver](#4-deterministic-solver)
- [5. Shared mesh / MultiMesh strategy](#5-shared-mesh--multimesh-strategy)
- [6. Incremental rebuild and concurrency](#6-incremental-rebuild-and-concurrency)
- [7. Serialized implementation packets](#7-serialized-implementation-packets)
- [8. Validation and acceptance](#8-validation-and-acceptance)
- [9. Failure modes and safeguards](#9-failure-modes-and-safeguards)
- [10. Completion handoff](#10-completion-handoff)

## 0. Non-negotiable contracts

1. **Render-only:** Do not change `VivariumWorld`, plant competition, growth rates, ecology, entity identities, or simulation digests. Render accommodation is reproducible from the current world snapshot, stable IDs, visual parameters and geometry.
2. **Static between relevant changes:** Never perform collision/relaxation every frame. Recompute only when an affected visual plant/region is generated or refreshed. Do not install Godot physics collision bodies for individual leaves.
3. **Keep the scene lush:** Never solve conflicts by deleting leaves, indiscriminately shrinking crowns, flattening all foliage, or systematically spacing plants farther apart. Real leaves may touch, overlap in projection and rest against one another; avoid *visible surface penetration*.
4. **Deterministic:** Stable entity/part IDs and a defined total order; independent of thread timing, hash-map enumeration or camera position. No random reseeding during accommodation.
5. **Biological plausibility:** Anchors remain attached; stiff veins/midribs deform less than lamina margins; bend radii, twist and pitch stay species-appropriate; preserve broad rhubarb-like, narrow grass-like and fern silhouettes.
6. **Bounded work:** Fixed neighbor radius, finite candidate counts, bounded relax iterations and dirty-region propagation; retain the last accepted mesh if a rebuild exceeds its budget.
7. **Performance:** Preserve batching, existing visual tiers, camera controls, and current optimization work. Record CPU time, allocation, mesh/variant counts, GPU memory and frame time before/after.
8. **No false acceptance:** A successful `dotnet build` alone is not acceptance. Native Godot screenshots, motion/orbit inspection, populated-scene profiling and world digest comparison are required.

## 1. Codebase anchors and integration problem

Verified on local desktop at plan authoring:
- `src/Vivarium.Sim/Geometry/MeshData.cs`: `Positions`, `Normals`, `Indices`, `Structural`, `LeafSpans`, `Leaves`, `RecordLeaf`, `RecomputeNormals`. `RecordLeaf` records `LeafSpans` even when `FloraDetailLevel == null`, but `Leaves` metadata is conditional; account for this explicitly.
- `src/Vivarium.Sim/Geometry/FloraVisualCompiler.cs`: `FloraPartMetadata` (part/parent IDs, tissue slot, attachment frame, length/radius/flex/bounds), `RecordPart`, `GetParts`, `Split`. Inspect how widely this metadata is actually populated before assuming complete coverage.
- `src/Vivarium.Sim/Geometry/Form/Broadleaf.cs`: `Broadleaf.BuildLeaf`, parametric outline, camber, petiole, curl. Also inspect `Form/LeafBlade.cs`, `Form/FoliageBlade.cs`, `Form/PinnateFrond.cs`, `Form/Sheet.cs`, and species-specific builders.
- `game/Render/OrganismRenderers.cs`: `FloraRenderer` renders shared species/morph/detail `MultiMesh` variants and refreshes from snapshots with a limited per-frame budget. **One shared vertex buffer cannot hold unique deformation for each individual plant.**
- `game/Render/RenderDirtyRegions.cs` (locate actual path/name before use), existing snapshot and plant-state tracking, and `src/Vivarium.Sim/Geometry/OrganismMeshes*.cs` are likely integration points.

**First task:** Trace exact mesh creation, variant selection, per-plant transform, GPU upload, growth refresh and disposal. Document the real call graph in the execution log. Do not insert a solver at `FloraVisualCompiler.Split` merely because it looks centralized: if split happens on shared prototypes, world-space neighbor information is absent.

## 2. Geometry and occupancy data model

Create render-only structures (names illustrative; use idiomatic project names):
- `AccommodationPart`: stable `EntityId` or visual owner key; stable part ID; parent ID; tissue kind (trunk/wood, structural stem, petiole, blade, frond, flexible shoot); world-space attachment and rest frame; stiffness/flexibility; effective diameter; growth/maturity priority; local/world bounds; compact control points or vertex span; species-specific deformation limits.
- `OccupancyPrimitive`: stem tapered capsules/short segment chains; blade sampled oriented surface patches or coarse triangles with thickness; optional convex envelope for broad query only. Record owning part and priority. **Do not use AABBs as final collision tests.**
- `SpatialHash`: world-space cells of configurable size (start ~0.15–0.30 m, profile actual foliage dimensions); map cell to compact occupancy handles. Oversized primitives span multiple cells; deduplicate handles in queries. Expand queries by thickness and intended deformation range.
- `AccommodationResult`: final part control deformation; collision before/after; displacement/twist/bend; unsolved count; cost and diagnostics; deterministic fallback flag.
- `AccommodationCache`: final per-plant render geometry/recipe, affected bounds, snapshot version, visual-seed identity, occupancy handles and rebuild dependencies. Cache lifetime tied to rendered instances, not simulation.

Use world coordinates only after applying plant instance transforms. Account for nonuniform scales, mesh winding, double-sided leaves and leaf thickness. Treat leaf surfaces as two-sided for intersection; thickness is a small configurable clearance, not an exaggerated invisible balloon. Structural capsule radius should match visible stem thickness plus a tiny clearance.

### Contact representation

For leaf blades, sample a small stable grid of points (e.g. midrib stations x lateral stations, including edges), along with local normals and neighboring triangles. Detect:
- signed penetration of blade samples through another blade surface;
- stem-capsule versus blade surface penetration;
- stem versus stem overlap where at least one stem is flexible.
Broad phase = spatial hash + inflated AABBs; narrow phase = point/triangle proximity with signed side continuity and edge/triangle checks where required. Two-sided proximity alone must not repel legitimately adjacent parallel leaves on opposite sides. Include previous accepted/rest configuration and local normals to distinguish contact from pass-through.

Avoid naive nearest-point repulsion that makes leaf tips vibrate or creates zig-zag artifacts. For ambiguous exact coplanarity use stable ID-based side choice and an epsilon.

## 3. Priority and negotiation rules

Process **structural occupancy before flexible foliage**; priority is not equivalent to arbitrary generation order:
1. Terrain, rock, dead logs and established major woody axes: immovable obstacles when included in the occupancy index.
2. Trunks and thick structural stems: effectively immovable; thicker/stiffer wins. Use radius/stiffness, not species names.
3. Smaller stems, petioles and flexible shoots: yield to stronger structure; preserve attachment.
4. Mature blades/fronds: claim settled space after structural negotiation.
5. Newly generated blades/fronds: yield to already settled blades of equal priority (stable deterministic first-claim).
6. Tendrils, very fine foliage: yield last, with broader flexibility.

Within a tier use stable ordering `(-stiffness, -radius, -maturity, entityId, partId)` or an explicitly documented equivalent. Use quantized comparisons if floating-point ties threaten determinism. **Do not let hash enumeration or job completion decide first claim.**

A stronger later-arriving stem may require local renegotiation of earlier flexible foliage. Do not force it through a mature blade just because the blade was registered first. Cap the neighborhood of eviction/re-solve and preserve stable prior shapes where possible.

Self-collision is necessary: leaves from the same plant should avoid visibly piercing siblings and their own supporting stems. Exclude only intentional attachment/adjacent surface pairs, not the entire owner plant.

## 4. Deterministic solver

**Solve controls, not raw triangle soup.** Parameterize each flexible part by a small vector:
- petiole yaw/pitch and smooth multi-segment curvature;
- blade roll/twist about midrib and limited camber/edge curl;
- frond rachis curvature and pinna splay for pinnate forms;
- grass/strap blade base pitch, distributed arc bend and tip roll.

Given rest pose `q0`, minimize a bounded objective:
`E(q) = wP * penetration(q)^2 + wR * ||q-q0||^2 + wB * bendEnergy(q) + wS * silhouettePenalty(q) + wC * continuityPenalty(q, previousAccepted)`.
Prioritize clearing significant penetration over tiny aesthetic contact. Use deterministic finite candidate poses followed by bounded local relaxation, not an unbounded optimizer. Suggested starting budget: 8–16 candidate poses, 3–6 refinement passes per conflicting leaf; calibrate using profiling. Evaluate samples against only nearby occupancy.

Algorithm per affected region:
1. Gather immutable candidate plant geometries and stable metadata; record baseline overlaps.
2. Insert fixed terrain/structural obstacles in deterministic order.
3. Resolve flexible stems against stronger stems; update final capsules.
4. Resolve blades against final stems and accepted blades in stable order.
5. For each conflict, try petiole orientation/curvature first, then blade twist/curl; accept minimum-cost feasible candidate, or least-penetrating plausible candidate when no feasible solution exists.
6. Recompute affected vertices/normals/tangents/bounds from control geometry, preserve UV/UV2/material custom channels and triangle topology; rebuild GPU meshes only when necessary.
7. Register final occupancy, emit diagnostics, and commit the accepted region atomically.

Prefer bending around obstacles over displacing whole plant crowns. Respect species-specific maximum bend, twist, pitch and curl. Never create inverted triangles, degenerate surfaces, discontinuous petioles or inverted leaf normals. Include robust fallback: original geometry or best valid deformation with a logged unresolved conflict; do not hang or delete foliage.

## 5. Shared mesh / MultiMesh strategy

**This is the key architectural decision.** `FloraRenderer` currently reuses meshes for many individual plants. A uniquely accommodated leaf cannot be represented by modifying one shared mesh without changing every instance.

Implement a **hybrid**, rather than globally converting every leaf to a Godot node:
- Keep existing `MultiMesh` prototype batching for plants with no meaningful conflicts, distant/low-salience vegetation and any instances whose existing shared variant already fits.
- For visible conflict clusters, generate/cache **accommodated instance-specific mesh variants** or deterministic deformation recipes. Group identical/quantized recipes where feasible; batch by variant, material and tier. If uniqueness makes batches too small, use a bounded separate mesh-instance path only for near-field high-salience exceptions.
- Prefer CPU one-time deformation at generation/rebuild, then static GPU geometry. Shader-only deformation is acceptable only if the required per-instance controls and occupancy guarantees can be represented and tested; do not claim a shared-mesh deformation has solved individual contacts.
- Implement explicit policy for far-field accommodation: stable simpler pose/occupancy or graceful fallback; no camera-driven reshuffling when changing LOD.
- Track additional mesh variants, vertices, draw calls, allocations and GPU memory. Add a cap with graceful deterministic fallback, never an unbounded per-leaf material/mesh explosion.

Before committing to the exact implementation, prototype *the batching decision* on one dense mixed-species patch and measure against current baseline. A good target is unchanged or near-unchanged draw-call scaling outside the accommodation hotspots. If the proposed architecture scales linearly in draw calls with all leaves, reject it and redesign the cache/batching boundary.

## 6. Incremental rebuild and concurrency

- Query existing `FloraRenderer.DirtyRegions`, `RenderSnapshot`, `_prevPlantStates`, `_refresh`, and existing mesh-build iterator semantics. Reuse the actual dirty region mechanism where possible.
- Maintain a versioned occupancy index over **accepted render geometry**; changes enqueue a bounded affected-neighborhood region. On plant removal, free its claims and consider releasing neighboring bends on the next relevant refresh.
- Preserve last accepted visuals while a replacement region builds; atomically swap meshes and occupancy claims. Never expose a half-updated region or make one-frame popping worse.
- Region halo must include maximum leaf reach plus bend distance. Ownership across region boundaries must be stable (one canonical owner for pairwise negotiation). Cap cascading invalidations; defer and batch, never recursively rebuild the entire island.
- Candidate generation can be parallel, but final conflicting claims must commit in deterministic order. A single serialized solver is explicitly acceptable.
- Separate camera-dependent visual LOD from occupancy ownership: camera movement must not change the canonical accommodated arrangement.
- Schedule expensive CPU generation off the render hot path when safe; if Godot objects must be created on main thread, perform CPU geometry on workers and bounded GPU upload on main thread. Respect existing per-frame refresh budget and instrument individual oversized iterator steps.
- Persistent cache keys: world snapshot identity + stable entity/part IDs + geometry/morph variant + relevant neighborhood revision + accommodation algorithm version. Do not cache solely by camera or hash table iteration order.

## 7. Serialized implementation packets

### AC-00 — Baseline, ownership and call graph
**Inspect:** `AGENTS.md`, current git status, active perf work, `FloraRenderer` build/refresh code, `MeshData`, `FloraVisualCompiler`, `OrganismMeshes`, form builders, existing test/capture harness.
**Deliver:** `docs/overhaul/FOLIAGE_ACCOMMODATION_EXECUTION.md` containing exact call graph, files/entrypoints, baseline screenshot IDs, populated-scene CPU/GPU/frame-time stats, draw calls, mesh count, flora refresh spikes, simulation digest. Define test scenes: crowded broadleaf rosette, grass tuft, fern, mixed woody shrub, water lily pads, cross-species understory.
**Gate:** Reproducible baseline captured before any modifications. No assumptions about metadata completeness.

### AC-01 — Metadata and pure occupancy library
**Build:** Render-only part extraction/metadata registration, world-transform conversion, stable ordering, spatial hash, tapered capsule and blade-sample proximity. Add unit tests for self collision, adjacent contact, edge crossing, rotation/scaling, overlapping bounds without contact, negative coordinates, and deterministic insertion/query.
**Gate:** Exact test cases pass; hash query scales with nearby occupants rather than all world parts. No Godot scene required.

### AC-02 — Controlled deformation primitives
**Build:** Shared leaf/petiole control-pose representation and mesh regeneration/deformation adapters for broadleaf, strap/grass and pinnate frond; preserve UVs, custom data, leaf attachment and material; recompute normals/tangents correctly. Where source builders lack part spans/attachments, instrument them rather than guessing from global mesh topology.
**Gate:** Isolated before/after render of each form, fixed root, no tears/degenerate triangles, shape limits tested. No collision solver yet.

### AC-03 — Priority solver and deterministic claim ledger
**Build:** Fixed/structural insertion, flexible-stem negotiation, then leaf settlement, candidate evaluation, bounded refinement, conflict statistics, stable tie-breaking, last-good fallback.
**Gate:** Synthetic arrangements show thick stem displacing leaf, thin stem yielding, first-claim leaves, self-overlap correction, later stronger-stem eviction and bounded unresolved cases. Repeated run produces identical accommodated mesh digest.

### AC-04 — One populated patch integration / batching decision
**Build:** World-space plant-instance extraction and a patch-level accommodated result path without changing ecological simulation. Integrate the hybrid batching/caching policy; retain current shared MultiMesh path for unaffected instances. Profile near-field conflict patch versus baseline.
**Gate:** Real mixed-species patch shows materially fewer visible penetrations; no one-node-per-leaf architecture; no runaway draw-call or memory increase. Record tradeoffs and revise before broader rollout.

### AC-05 — Broad integration and lifecycle
**Build:** Apply accommodation to all relevant broadleaf, frond, grass/strap and species-specific leaf families; cover within-plant and between-plant collisions; integrate region dirtying, growth, removal, refresh, LOD, water lily surface positioning and near-water constraints. Add exclusions for intended intersecting anatomical joints and intentional leaf contacts. Do not displace lily pads arbitrarily above/below the water plane.
**Gate:** All six baseline scenes improve; repeated grow/die/regrow cycles have no occupancy leaks, no visible global rearrangement, no orphaned geometry.

### AC-06 — Performance, stability and final visual acceptance
**Build:** Profile and optimize allocations, occupancy cell size, sample counts, candidate counts, dirty-region halo, batch fragmentation, upload budget and cache churn. Add debug toggles/overlays (occupied capsules, blade samples, conflict pairs, displacement heatmap), telemetry and a runtime disable switch for A/B comparisons.
**Gate:** See Section 8. Capture identical seeded worlds with accommodation on/off. Document measured costs and any remaining exceptions.

**Packet discipline:** Finish each packet's tests, capture evidence, update execution log and commit before moving on. If a packet requires new files or architectural changes, record why; do not silently substitute a narrower feature. No forced six-file ceiling on this serialized job.

## 8. Validation and acceptance

**Functional tests**
- Same world/visual seed and snapshot yields identical geometry digests across repeated runs, different scheduling and camera paths.
- Authoritative world digest unchanged with feature on/off.
- Dense multi-species overlap and same-plant self-collision both handled.
- Strong stem arriving later can push flexible earlier foliage aside; weak stem yields.
- Removal allows space to be reclaimed without an island-wide rebuild.
- No petiole detachments, inverted winding, broken UVs, disappearing leaves or species-silhouette homogenization.

**Quantitative visual metric:** Define a stable set of leaf surface samples and exact conflict pairs from identical scene states. Report baseline vs after: count of penetrations above a visible tolerance, total penetration depth, 95th-percentile depth, number of deformed parts, mean/max angular displacement, unresolved conflicts and contact preservation. Establish numeric acceptance thresholds from baseline; suggested goal is **>=80% reduction in clearly visible penetrations in six target scenes**, with no mass foliage removal. Do not fake precision where ground-truth surface classification is ambiguous.

**Performance:** Measure full startup, scene refresh, individual max iterator step, 95th/99th percentile frame times, CPU allocation, cache footprint, GPU memory, draw calls, triangle count and geometry upload count on actual desktop hardware. Record quality settings and hardware. Target no measurable ongoing per-frame solver cost; bounded incremental rebuild spikes; no sustained frame-rate regression that breaks the existing >=30 FPS target. If that target conflicts with current overhaul quality, report the exact measured baseline and delta instead of silently lowering graphics settings.

**Visual evidence:** Matched camera/lighting captures from at least six scenes, close-ups from multiple angles, and a slow camera orbit to reveal penetrations hidden in one view. Review in motion, at mature and growing states. Check water lilies separately: leaf pads can overlap in projection but should not pierce each other or float off the water.

**Acceptance record:** Source commit, world seed, capture paths, native Godot console errors, shader compilation status, test results, world digest, FPS/frame times, draw calls and before/after conflict metrics. C# builds alone are insufficient.

## 9. Failure modes and safeguards

| Failure | Required mitigation |
| --- | --- |
| Shared mesh deformation bends every plant identically | Instance-specific accommodated variant/recipe with batching policy |
| All leaves repel into sparse unnatural crowns | Preserve near-contact, constrain pose, never cull to solve |
| Solver repeatedly rearranges foliage | Stable IDs, canonical ordering, continuity penalty, hysteresis |
| Growth triggers endless regional cascades | Fixed halo, capped propagation, versioned claims, deferred batching |
| Huge mesh count/draw calls | Hybrid unaffected-instance MultiMesh + bounded accommodated cache |
| Leaves bend through their own stems | Self collision with narrow attachment exclusions |
| Paper-thin surfaces intersect at edges | Two-sided samples plus edge/triangle checks, thickness clearance |
| Camera movement changes leaf layout | Camera-independent canonical occupancy |
| Normal maps/lighting look wrong after deformation | Correct geometric normals/tangents; preserve material data |
| Performance optimization undoes fidelity | Matched captures and perf gates; preserve prior visual result |
| Floating pads get lifted above water | Water-plane constraint and lily-specific permitted pose space |
| Agent assumes metadata exists for all species | Audit coverage, instrument missing builders, report exceptions |

## 10. Completion handoff

Write final status to `docs/overhaul/FOLIAGE_ACCOMMODATION_EXECUTION.md`:
- exact modified files, commits and architectural decisions;
- metadata coverage per plant family and remaining exceptions;
- occupancy/solver algorithm, limits and cache strategy;
- deterministic and native test commands/results;
- before/after images, measured overlap reduction and hardware performance;
- outstanding defects, deliberately unsolved cases and recommended next work.

**Done means integrated and visually verified on a populated world, not merely a functional geometry helper.** The goal is foliage that appears to have physically grown together while remaining a mostly static, efficient render presentation.
