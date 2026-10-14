# Next candidate brief: adaptive water-volume graph with a continuous free-surface mesh

Status: design proposal only. No production integration or candidate selection is claimed by this document.

## Decision problem

The current particle prototype stores real parcel volume, but its measured cost is already too high at 200–1,000 particles and its pressure solver has no proven basin equilibrium. The current conservative `Hydrology` solver is inexpensive and accounts for spring input, retained water, and outflow, but its existing field-shaped presentation is explicitly rejected by the user. Reusing that solver and renderer unchanged does not meet the visible-fluid requirement.

The next bounded candidate should be a **conservative adaptive water-volume graph**. Water state is persistent volume and horizontal momentum attached to a sparse, connected set of terrain-conforming control volumes. A solver-owned free-surface extractor turns that state into a continuous water-body mesh. The grid or graph remains an internal numerical acceleration structure; it is never rendered as per-cell wet patches, and render geometry cannot create or route water.

This proposal uses finite volumes because source discharge is tiny: at 0.0005556 m³/s and 0.000015 m³ nominal particle volume, a particle representation creates about 37 parcels per simulated second. A 2,000-plus-parcel pool is reached quickly enough that the measured all-pairs neighborhood cost is already untenable. A volume graph advances conserved mass over wet adjacency edges, so cost follows the active graph and its boundary rather than all pairs of particles.

## State model

Each wet control volume owns:

- conserved water volume in cubic metres;
- a horizontal momentum vector, or an equivalent volume flux with a documented unit;
- a stable terrain support polygon and elevation samples;
- a stable node identity derived from terrain topology, not allocation order;
- optional source provenance for diagnostics only.

Each adjacency edge owns only solver data such as shared sill elevation, width, length, and transient flux. An edge never owns water. The complete water balance is the sum of node volumes plus explicit pending source remainder, minus named tool removal, boundary outflow, groundwater transfer, evaporation, and infiltration.

Dry candidate nodes have no water state. A source first adds `discharge * simulated_seconds` to a per-source remainder. It then transfers that exact quantity into one or more supported wet control volumes. Sub-parcel amounts remain as source remainder. The source never manufactures a visual jet, tracer count, lifetime, or hidden residual volume.

## Control volumes and adaptivity

Start from terrain triangles or clipped terrain polygons as the static support tessellation. Activate control volumes only where they contain water or are needed as immediate downhill receivers. Merge adjacent wet supports when their free-surface head differs below a chosen tolerance and refine them when any of these occurs:

- a wet/dry shoreline crosses the support;
- a sill or narrow channel connects two bodies;
- terrain curvature exceeds a declared error bound;
- head variation across the support exceeds the hydrostatic reconstruction tolerance.

Refinement and merge operations must conserve volume and momentum. Node identity and iteration order must be deterministic. Bound the active node count and per-tick work; if the cap is reached, retain unprocessed simulated time as explicit resumable backlog and keep spring remainder in the same accounting boundary.

## Hydrostatic pressure, retention, and spill

For each connected pool, solve a hydrostatic free-surface head from the node volumes and support areas. In the static limit, a closed basin's connected wet supports share one surface elevation, within the mesh tolerance. Pressure head across an edge is computed from upstream/downstream free-surface head and the edge sill. No flux is permitted while both water heads remain below the sill. Once upstream head exceeds the sill, a conservative flux transfers volume into the adjacent lower support; outgoing volume is limited so the source node cannot become negative.

This is the critical behavior the particle prototype lacks: volume changes the pool head, pool head controls the sill transition, and the same transported volume continues along the receiving terrain. Do not make the pressure solve a painted `Depth` interpolation or a direct spring-to-destination route. The face flux must come from the same volume state used to solve the free surface.

## Visible fluid surface

The visible water is a single continuous free-surface mesh for each connected body, clipped to the terrain and solids. Generate it from the graph's solved free-surface head and wetted footprint:

1. Extract the connected wet footprint from the conservative graph.
2. Intersect it with terrain-triangle support, walls, and sill geometry.
3. Build a smooth, continuous top surface whose elevations satisfy the hydrostatic head and shoreline boundary.
4. Add a boundary skirt or side wall only where the water volume physically contacts terrain or an obstacle; avoid a ground-level ribbon or a uniform-thickness sheet.
5. Derive normals and optional velocity shading from the mesh and graph flux. Shading cannot alter volume, fill the hollow, or create motion.

The mesh is a read-only view. It cannot feed heights, contact decisions, or volume back into the solver. There is no independent `Water.Depth` surface, tracer surface, shader-only pool, or duplicate groundwater sheet. Groundwater remains a separately named standing reservoir and is rendered separately.

## Candidate module boundaries

Build this in stages and keep it separate from live world ownership until the analytic fixtures pass.

1. `Water/AdaptiveWaterState.cs`: node volumes, edge topology, stable IDs, source remainders, deterministic time remainder, and named budget export.
2. `Water/AdaptiveWaterTopology.cs`: static terrain support triangulation, clipped domain boundary, sill/obstacle adjacency, wet component discovery, and deterministic refinement/merge.
3. `Water/AdaptiveWaterSolver.cs`: fixed-time conservative flux, hydrostatic head solve, CFL/positivity bounds, and bounded resumable work. No rendering types or game framework dependencies.
4. `Water/AdaptiveWaterQueries.cs`: derived point depth, surface elevation, wetness, current, and buoyancy samples. It reads the same volume graph and cannot store another water quantity.
5. `Geometry/AdaptiveWaterSurfaceBuilder.cs`: immutable input snapshot and one continuous mesh per connected water body; no solver mutation.
6. `docs/adaptive-water-volume-mesh-brief.md` plus new direct tests and numeric/performance receipts. Do not switch `VivariumWorld`, scheduler, serializer, tools, or renderers in this stage.

## Removals required before any integration claim

When a later integration task is explicitly authorized, inventory and remove or adapt all competing dynamic-water authorities:

- Remove `Hydrology.Depth` and face-flow arrays as visible water ownership. Replace them with adapters that derive queries from the conservative graph, or delete callers after migration.
- Remove point-cell spring injection and any old depth-field refill path. Spring discharge must enter the graph source remainder exactly once.
- Remove decorative droplet count caps and age retirement as substitutes for volume transport. Presentation particles may be added only as a derived visual effect and may never own, route, or discard volume.
- Remove `WaterMesh.Build` output that exposes raster-cell water patches as the surface. A single connected free-surface mesh consumes immutable graph snapshots.
- Remove `SpringWaterMesh` or ribbon geometry if it claims to be the free surface. A small contact/foam effect may remain only as secondary rendering.
- Update save, digest, tools, ecological moisture, swimming, and floating queries in separate contracts. No serializer or live world changes are part of this proposal.

## Validation fixtures and gates

All numeric gates run directly against the new solver and actual volume graph, with no old `Hydrology` state present. All physical-loss rates are disabled unless a test explicitly exercises them.

1. **Source and conservation:** inject 0.0005556 m³/s for 10, 25, and 125 simulated seconds. At each boundary, `stored volume + source remainder + named transfers = source inflow` within 1e-9 m³. Include fractional-tick input and save/restore at a partial fixed step.
2. **Small closed hollow:** use a 10 m domain with an analytic 0.8 m radius, 0.05 m depression and a level outer sill. At 25 seconds, central head must rise measurably from its 10-second value and no volume may escape the basin before the solved head reaches the sill. At 125 seconds, the connected spill path must contain water. A separate 3,125-second case verifies retained pool plus named perimeter outflow balances all 1.73625 m³ input.
3. **Receiving channel:** continue the same water after the sill into a downhill channel with one bend and a receiving depression. Measure volume crossing named gate sections and confirm it reaches the receiver. Ground contact must not pin horizontal movement.
4. **Static equilibrium:** stop the source after filling a closed bowl. The solved surface across the connected pool must be level within 5 mm; no support may drain while all connecting heads are below their sills.
5. **Mesh correspondence:** sample the output mesh in the hollow, at the sill, and along the channel. Every mesh vertex must correspond to a positive graph volume footprint, and mesh-integrated volume must match graph volume within a declared geometric tolerance. Confirm no duplicate field, ribbon, or groundwater surface is present.
6. **Determinism and timing:** identical simulated time gives identical node state and digest across different frame batches. Pause preserves digest. Work caps retain explicit time backlog without losing source volume.
7. **Performance sample:** report active wet supports, graph edges, mesh vertices, allocations, wall time per fixed step, backlog, and memory at the actual maximum source rate. Include the 10 m basin until overflow and a sustained 300-second case. Run on the target GTX 1080/Ryzen 5 1600 at 1600x900 only after a standalone solver is credible. A CPU-only fixture is feasibility evidence, not final performance acceptance.
8. **Player-facing review:** capture a time sequence from the actual running game showing spring emission, the hollow filling, a rising pool, sill overtopping, downhill channel connection, and receiving-basin filling. A flat cell surface, isolated triangles, a still image, or debug overlay does not pass.

## Decision and stop conditions

The adaptive water-volume graph is a proposed next candidate, not a claim that a mesh makes a field physically meaningful. It is acceptable only if conservative graph volume drives head and flux, while a continuous mesh displays that same stored water as a coherent body. If the active graph still produces a field-like surface, if mesh volume cannot be reconciled to stored volume, or if required refinement exceeds the target budget, reject the candidate and report the evidence before integration.

Current evidence does not select an implementation: `Hydrology` is numeric comparison evidence only and remains visually rejected; `ParticleFluid` is computationally prohibitive and lacks basin pressure evidence. The following worker should implement only state/topology/solver foundations and the small-hollow tests. Renderer and world integration must wait for all numeric gates above to pass.
