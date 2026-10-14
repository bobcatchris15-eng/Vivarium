# Night flora catalog

This content expansion adds Plumestep grass and seven low-light, bioluminescent species. Glow is optional visual metadata under each flora `visual` object: `bioluminescenceColor` is linear RGB in [0,1] and `bioluminescenceStrength` is 0..8. Omitted values default to black and zero, preserving existing content behavior. The seven new night species all carry positive strength; Plumestep does not glow.

| ID | Ecological role | Shape | Habitat notes |
| --- | --- | --- | --- |
| `plumegrass` | Meadow grass with multiple plume tufts spaced down each stalk | `pampas` | Terrestrial; ordinary, non-luminous flora. |
| `glowbriar` | Low shrub for shaded banks and loam | `shrub_glowbriar` | Spreading arching canes, narrow alternating leaves, and small mint nodules beneath the canes. |
| `lanternsedge` | Narrow sedge fringe for saturated margins and very shallow water | `reed` | Wetland / riparian herb; shallow tolerance up to 9 cm. |
| `tidebell` | Emergent herb on shallow pond shelves | `pairedleaf` | Explicit 0.5–18 cm water band; warm bell-colored glow. |
| `siltstars` | Fully aquatic, bottom-anchored low carpet spreading across submerged soil | `carpet` | Requires at least 0.5 cm water and has no maximum depth. `unlimitedWaterDepth: true` is an explicit habitat rule; `maxWaterDepth: 0` is retained as an unused neutral value, not a depth sentinel. The existing generic soil substrate remains the floor. |
| `ghostbonnet` | Detritus-feeding fungus of damp shaded litter | `mushroom_cluster` | Cool blue glow. |
| `starmoss` | Low moss cushion for moist ground | `carpet` | Green glow. |
| `moonfern` | Fern for humid, shaded woodland | `fern` | Cool white-blue glow. |

The depth rule is consumed by flora suitability, which is shared by initial establishment, continued habitat evaluation during growth, and ordinary propagule establishment. A deep-water test covers establishment, biomass gain, and propagation at 7 m, plus rejection of dry ground. The render contract places Siltstars at the terrain/floor rather than floating at the surface; no dedicated silt substrate was introduced.

The shape keys `pampas` and any emission masks are implemented in the separate geometry/render work. Content loading and placement tests do not claim a rendered visual review.
