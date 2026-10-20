# Shoreline footprint correction - October 16

The retained correction is in `src/Vivarium.Sim/Geometry/WaterMesh.cs`. Table geometry now clips the authoritative heightfield faces instead of sampling and early-culling on the separately centered coarse water grid. Intersections stay on the exact table plane and actual terrain face. Fluid solver, terrain shape, ecological queries, save schema and water level are unchanged.

The corrected regression uses supported5m/6m worlds. On the protected implementation, the6m isolated depression has no rendered water despite being submerged;5m and the sampled dry-terrain invariant already pass. The first attempt used unsupported3m/4m sizes and is explicitly invalid as bug evidence. Candidate and retained ROOT pass all three corrected regressions; root's broader focused set passes47/47 (shoreline, terrain materials, surface fluid and map drainage).

Native main runs: `shore-aligned-main` and `shore-aligned-bayou`. Both use saved100s creek/bayou fixtures and exact replayed cameras, force recompilation and authenticate unchanged runtime sources. The previous and new creek water snapshots and canonical world digest match exactly. The stream motion sequence has48frames over2physical seconds with camera/environment above water. Four new empty-void probes are exactly RGB(0,0,0). Representative main motion and bayou images were inspected. Validation: `build/forest-reference-20261015/shoreline-20261016/validation.json`; comparison page: `review.html` in the same directory.

This is a real narrow disappearing-water fix, not a dramatic visual shoreline improvement. The large creek zigzags remain because those banks are angular in the authoritative terrain itself. Do not call this geometry fix forest convergence or a performance result. Existing saved worlds can use the corrected presentation after relaunch; no new map is required.

The active hourly reminder reads `NEXT_WAKE.md`. Next target: dry-bank anchored arching foliage and fibrous root fringe to break up exposed bank planes while preserving fluid support, water exclusions and a readable flow corridor.
