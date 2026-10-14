# Independent fluid behavior contract

These probes exercise `ParticleFluid` directly. They use analytic, bounded fixtures rather than a depth grid: a 3 m hexagonal domain, flat or planar ground, no losses unless stated, and at most 20 initially-added nominal parcels per test. One nominal parcel is `ParticleFluid.NominalVolume` (15 ml in the candidate's declared cubic-metre units). The suite advances at most 15 simulated seconds, or 1,800 fixed 1/120 s solver steps. Run cost should be recorded from the focused command; no test is a target-hardware performance claim.

The contact fixture clamps parcel centers to `ground(x,z) + ParticleFluid.Radius(volume)`. This isolates the particle solver's response at a simple solid boundary. It does not model real terrain geometry, a hollow's wall/lip collision mesh, vegetation, exposed groundwater, or renderer output. Groundwater remains a separate standing-water appearance and is not treated as dynamic-fluid evidence.

Contract probes:

- With evaporation and infiltration disabled, particle volume plus source pending volume remains equal to initial volume plus spring inflow minus tool removals, within 1e-10 m3; any boundary outflow must be explicitly counted in `WaterBudget.BoundaryOutflow`.
- Water retained against a closed contact surface must still exist after 15 seconds, exceeding the old cosmetic 14-second retirement window. A separate 300-second gate remains for a later bounded/optimized soak test.
- A released pool must travel down a planar slope while contacting the solid. The current probe gives it a small initial +X release velocity; it tests transport/contact continuity, not whether pressure alone selects downhill flow from rest.
- Export/restore must preserve the fixed-step remainder and particle state sufficiently for identical future evolution at the same simulation boundary.
- Spring discharge remains represented by particles or source remainder, within 1e-10 m3, with no loss rates.

The current API exposes no surface-height or basin overflow query. A true rising-pool/overtopping test needs a solver-owned free-surface measurement and a closed 3D analytic bowl/lip contact fixture; inferring a pool from particle count or a depth-grid field would not prove that requirement. The source-discharge probe is the closest available conservation check, and an explicit overflow gate remains open for integration.

These tests are designed to expose failures honestly. Passing this suite does not prove a GPU/mesh cost, game integration, water-table transfer, groundwater separation, visualization, time-batching equivalence across all schedulers, or target runtime acceptance.
