# Surface fluid handoff

Plan: docs/particle-surface-water-replacement-plan.md. User explicitly allows particles, vector methods or other representations; acceptance is real visible fluid behavior. Three built-in gpt-6-luna workers dispatched: solver medium, audit low, tests low, in isolated exact dirty worktrees. Dispatch records under tasks/surface-fluid-*/dispatch.json. All dependent integration/persistence/render/runtime stages remain pending. No integrated fix or actual fluid acceptance. Preserve live main and active world. Original hashes verified unchanged, excluding coordinator-authored plan update. Continue by consuming receipts, reviewing candidate and tests, revising architecture choice, then sequence W3-W7. Do not infer background continuation beyond actual worker status.


## Progress update — October 10, 2026 (Night Flora & Fluid Solver)

1. **Night Flora Lighting & Organ Masks (Integrated & Validated)**:
   - Implemented exact organ-specific glow masks:
     * `ghostbonnet` (bonnet fungus): gills only (UV2.x=1.0 vs cap/stems 0.0)
     * `lanternsedge`: seed heads only (UV2.x in 1.3..1.5 vs stems 0.0)
     * `siltstars`: broad luminous center stripe down leaves (|UV.y - 0.5| <= 0.18, runners dark)
     * `tidebell`: luminous perimeter strip around leaf edges (|UV.y - 0.5| >= 0.35, midrib/stems dark)
     * `glowbriar`: underside mint nodules (UV2.x in 1.3..1.5)
     * `starmoss`: full colonial body surface
     * `moonfern`: foliage blades
   - Implemented real visible colored cast light: all 7 luminous species register shadowless `OmniLight3D` nodes smoothly modulated by the night factor.
   - Live game support added in `FloraRenderer` via a pooled cluster of shadowless lights for camera-proximate luminous plants (0 overhead during day).
   - Validated: 11/11 tests passing (`NightFloraContentTests` + `NightFloraGeometryTests`), 0-warning game build, and isolated Godot 4.7.1 Forward+ review exited 0 with `NIGHT_FLORA_REVIEW_OK` and visual captures saved in `.clanker/tasks/night-flora-light/captures/`. Integrated into main.

2. **Surface Fluid Solver (Validated Candidate Suite)**:
   - The unvalidated face-flux experiment with relaxed CFL depth stepping (`limit = Math.Min(limit, Cfl * edge.Distance / (waveSpeed + velocity))` using full sill-relative depth) in `WaterVolumeGraph.cs` was tested against the full test suite.
   - Suite passed 20/20 tests in 32.5s wall time (no timeout or catastrophic slowdown). Hydrostatic bowl equilibrium achieved within 0.66 mm spread, downhill acceleration from rest conserved, and open domain boundary outflow accounted.
   - Next fluid stage: continuous conservative free-surface mesh reconstruction across support boundaries to eliminate adjacent-cell head seams before production runtime wiring.

