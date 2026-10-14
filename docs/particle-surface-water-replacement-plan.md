# Dynamic fluid surface water replacement

Authorized 2026-10-10. Status: first implementation wave dispatched; no fluid or visual acceptance yet.

Plan filename retains the initial candidate name for stable task pointers; the solver representation is open after the user clarification.

## Binding intent

User clarification: particles are optional; vector calculations or another representation are acceptable. Acceptance is physical and visible behavior, not a prescribed numerical method. The previously rejected field/grid accumulation must not survive as a cosmetic substitute.

Dynamic surface water must be actual simulated fluid. Spring discharge creates conserved moving volume that collides with solids, collects in hollows, raises a free surface, and overflows terrain sills toward the downhill water-table body. No decorative tracer lifetime or terrain-painted field may substitute for that behavior. The hydrostatic groundwater table remains a separate standing-water reservoir.

Candidate architecture below uses particles, but is provisional. W1 must compare a particle pressure solver against conservative vector/mesh methods (for example moving parcels or a Lagrangian fluid mesh), select based on basin/overflow/downhill evidence and target cost, and revise dependent contracts before integration. An Eulerian computational grid is not forbidden as an internal numerical aid after this clarification, but a spreading painted field without a coherent free surface and flow is unacceptable.

If particles are selected, they carry position, velocity, volume, and source identity. A spatial hash is allowed for neighbor lookup. A transient sampling lattice is allowed for extracting a surface from particles. For the provisional particle candidate, neither owns water volume, routes discharge, or prescribes fluid height. Alternative solver authority must be explicitly documented and pass the same gates. Ecological moisture fields are retained as derived consumers of actual fluid contact and infiltration.

## Baseline and preservation

Base revision: 8f65b553d0278b0fe543d5736671a2460d93c804, plus the current tracked and untracked working files. The checkout contains extensive unrelated changes and an active game. Snapshot file hashes and copy the exact dirty source into isolated worker worktrees. Do not reset, clean, stage unrelated files, replace the running binaries, restart the user's world, push, or install anything. Preserve prior Clanker records. Existing field tests passing is baseline evidence only.

## Removal inventory

| Existing path | Remove or replace | Retain |
|---|---|---|
| `Water/Hydrology.cs` | Dynamic `Depth`, shared face momentum, depth-grid spring injection, local-inertial surface transport, cell-based recharge decisions, authoritative depth digest | Spring definitions, named volume budgets, hydrostatic groundwater configuration and queries; expose particle-backed compatibility queries only where required |
| `Water/SpringFluidSystem.cs` | Cosmetic emission scaling, real-time authority split, 14-second rest retirement, age retirement, count-dependent volume loss, slope-only integration, presentation-only volume disclaimer | A coordinator name/API only if useful after it owns the real solver; no tracer fallback |
| `Water/ParticleFluid.cs` | Iterator-allocating neighbors, coincident point-source pileup, arbitrary upward launch, unsupported deep-contact deletion as infiltration, silent volume deletion | Audited particle state/export primitives and solver math that pass physical tests |
| `Geometry/WaterMesh.cs` | Dynamic surface reconstruction from depth and face discharge | Terrain-clipped hydrostatic water-table geometry |
| `Geometry/SpringWaterMesh.cs` | Ground-height plus fixed-thickness density ribbon as a purported free surface | Reusable mesh utilities after genuine 3D density reconstruction is proved |
| `Render/WaterRenderer.cs`, `WaterSurfaceRenderer.cs`, `WaterFluidRenderer.cs` | Simultaneous field/tracer surfaces and abandoned renderer wiring | One dynamic particle surface renderer, groundwater renderer, immutable snapshots, bounded background extraction |
| `App/GameSession.cs`, `World/VivariumWorld.cs`, scheduler | Per-frame real-time droplet advancement, grid prewarming and grid hydrology registration | Explicit simulation-time fluid service and deterministic initialization |
| Persistence serializer and digest | New saves omitting particle state or treating depth grids as new authoritative water | Explicit legacy migration, groundwater state, source/budget preservation |
| Water tools and observation/ecology clients | Pour/remove/edit operations that still mutate surface depth; stream/float queries based on face grids | Particle-backed adapters, spring slider, vent placement, groundwater semantics |
| Tests and docs | Tests asserting field spreading as proof of fluid; documentation prescribing shallow-water fields for this feature | Historical evidence, groundwater tests, conservation contracts rewritten for particles |

File names in this table are relative to `src/Vivarium.Sim` or `game` as appropriate. Audit serializer and all query callers before deleting an API. A transitional adapter must derive data from particles and must never become a second owner.

## Architecture to build

1. **Authoritative particle solver.** Start from the existing position-based incompressible solver as an untrusted candidate. Fixed steps; gravity; volume-weighted density/pressure constraints; viscosity; stable deterministic neighbor ordering; finite-state validation; no cosmetic deletion. Tune particle spacing, support radius, rest density and solver iterations together using physical fixtures. Validate hydrostatic stability and downhill motion before selecting constants.
2. **Source discretization.** Accumulate discharge times simulated seconds in a per-source volume remainder. Emit noncoincident parcels in the physical vent/depression with bounded placement, exact accounted volume, and no artificial jet unless requested. Pending emission remains owned volume. A full particle budget retains source volume or slows progress with explicit backlog; it never drops volume.
3. **Solid contact.** Cache terrain/solid collision geometry with invalidation on terrain, props, and roots. Resolve penetration and contact-normal velocity without erasing tangential downhill movement. Handle terrain edits by conservative relocation and report unresolved contacts. Do not reinterpret collision failure as infiltration.
4. **Water boundaries and physical losses.** Transfer particle volume to groundwater only at a verified contact/intersection with the exposed standing body, not because a coarse cell center is low. Track perimeter outflow. Evaporation acts on exposed fluid surface; infiltration acts on wetted ground contact with substrate rates, units, and moisture coupling. No per-particle area sum that overcounts a buried pool's exposed area.
5. **Simulation timing and budget.** Connect fixed fluid steps to simulation advancement and pause. The existing ten-second ecology tick cannot trigger an unbounded batch of 1/120-second fluid work. Introduce bounded resumable work with a persisted time remainder/backlog and deterministic publication boundary. Speed settings cannot silently skip physical time or lose volume. Benchmark feasibility on the target machine before claiming the full workload is affordable.
6. **Queries and tools.** Derive point depth, surface elevation, wet contact, current, and buoyancy samples from local particles/free-surface data. Keep groundwater composition explicit. Pouring adds particles; soaking removes exact volume; spring edits change future discharge. Ecology reads derived samples, never an independent water storage field.
7. **Persistence and migration.** Persist particles, velocity, volume, source remainders, integrator time/remainder, budgets, spring rates, and any deterministic ordering state. Digest authoritative state. Legacy grid volumes convert once into bounded particle distributions with conserved totals and a migration receipt. If a save exceeds a resource limit, preserve the original and report the limit; no silent draining. Reload evolution must match uninterrupted evolution at the same simulation boundary.
8. **Fluid surface rendering.** Extract a 3D isosurface from volume-weighted particle density with physical rest-density calibration, terrain/solid clipping and meaningful normals/velocity. Use sparse transient samples and bounded worker snapshots. Pool height comes from particle positions and pressure. Reuse mesh resources; keep a single renderer; refresh on state version. Render geometry cannot feed water state back into the solver.

## Dependency order and worker ownership

| Stage | Work | Dependency | Routing |
|---|---|---|---|
| W0 | Exact dirty snapshot, removal audit, API/caller inventory, behavioral fixture design | None | Coordinator plus Luna low audit |
| W1 | Compare representations; repair or prototype chosen source, contacts, pressure and conservative storage; benchmark | W0 contracts | Luna medium bounded implementation |
| W2 | Independent basin/slope/conservation probes; reject fake pooling and tracer behavior | W1 candidate | Luna low test author, then medium review if needed |
| W3 | Authoritative world/scheduler/query/tool integration; disable field ownership | W1 and W2 pass | Luna medium sequential worker |
| W4 | Particle serialization/digest and explicit legacy conversion | W3 state contract | Luna medium separate bounded worker |
| W5 | Particle-derived 3D surface, single renderer, remove ribbons and field mesh | W3 query/snapshot contract | Luna medium worker |
| W6 | Remove obsolete dynamic APIs, rewrite dependent tests/docs, reconcile graph | W3-W5 | Luna low cleanup plus medium review |
| W7 | Target-machine runtime, user's hollow or copied save, visuals, performance, final adversarial review | W6 | Serialized capture and Luna medium review |

First wave is limited to three bounded workers: solver foundation, read-only integration audit, and independent fixture tests. All use `gpt-6-luna`, low or medium effort, with explicit file ownership and isolated worktrees. No recursive delegation. Dependent stages are not launched against invented APIs. Each dispatch has a bounded turn; no unattended recurring wake loop is authorized or configured.

## Acceptance gates

- **Authority:** Spring input enters the selected conservative fluid solver; no independent cosmetic depth/face field remains. Exactly one selected fluid solver owns dynamic water. No cosmetic lifetime or count-cap deletion. Static search plus source inspection verifies this.
- **Conservation:** With losses disabled, particles plus pending source volume plus named transfers equal initial volume plus spring/tool inflow minus tool removal, within a declared numerical tolerance. Repeat with enabled losses and log every budget component.
- **Hollow:** A closed analytic bowl below its lip retains fluid for longer than the old 14-second and 300-second retirements. Added volume raises pool height. No material leaves across the lip before overtopping; sustained input produces spill after overtopping.
- **Downhill:** A spring and released pool accelerate along a sloped solid, follow a bend/channel, cross a sill only at sufficient head, and reach the receiving water body. Ground contact does not pin horizontal motion.
- **Groundwater/perimeter:** Only actual receiving-body contact transfers volume. A hollow above groundwater continues filling. Edge loss is accounted exactly once. Dry terrain under a low neighboring cell does not retire particles.
- **Pause/timing:** Pause preserves particle state/digest. Equivalent fixed-step simulated time gives equivalent results across frame batching. High speed has explicit finite backlog behavior and no water/time loss.
- **Terrain edits/tools:** Raise/lower ground, place/remove obstacles, pour and soak; no unexplained volume loss or buried fluid. Prop/root invalidation is covered.
- **Persistence:** Save/reload preserves volume, momentum, pending emission, time remainder and future evolution. Legacy conversion preserves volume without reintroducing the grid owner.
- **Visual:** Video/time sequence shows emission, filling hollow, rising level, overtopping, and downhill connection. One still image or a shader ripple is insufficient. No slope plates, dry-ground triangles, disconnected ribbons, or duplicated water surfaces.
- **Performance:** Report real target-hardware fluid step and mesh costs, allocation rate, particle count, backlog, total frame percentiles and memory over sustained max-discharge operation. Target is the existing 30 FPS floor at 1600x900 on GTX 1080/Ryzen 5 1600. Failed performance remains an open gate; never substitute a field to obtain a pass.

## Evidence and completion

Store baseline hashes, dispatch briefs, task state, receipts, tests, benchmarks, migration receipts, and runtime sequences under `.clanker/tasks/surface-fluid-*`. Keep W1 solver feasibility separate from integrated or visually accepted completion. Preserve raw failure evidence. Integrate only reviewed changed paths against the exact dirty snapshot, checking overlap before applying changes. Do not close the overall work until W7 is met, or claim background progress without active workers or an explicitly configured continuation.
