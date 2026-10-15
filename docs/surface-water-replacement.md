# Surface-water replacement

Status: production migration implemented; full acceptance remains open because the normal populated-world frame-time target is not met. Backward save compatibility is explicitly out of scope at the user's direction. Current-format save/load remains covered.

## Ownership and architecture

`SurfaceFluidDynamics` owns all mobile water: supported-cell volume, shared face discharge, reconstruction slopes, airborne volume and physical time. The sparse 25 mm control volumes integrate eight triangles of actual solid support. Their free surface comes from conserved volume using a safeguarded Newton inversion of geometric capacity. Local-inertial face flow, donor limiting, wet/dry pressure and depth-based CFL substeps handle supported motion. Unsupported flow transfers into volume-carrying ballistic parcels; impact transfers it back, with a small conserved splash fraction. Parcels have no lifetime or camera-distance deletion.

`SurfaceLiquid` owns immutable terrain/prop/root snapshots, bounded jobs, immutable published views and queued input history. It integrates source rate changes within each physical step, preserving both intervals. Queued input survives saves and pause. Physics uses actual wall elapsed time in the client, including long rendering frames; calendar acceleration does not multiply the flow. A short, accounted five-second warmup uses this same owner for new presets. Explicit calendar-only `World.Step` remains an ecology operation; `SimHost` is the live physical-time entry point.

Persistent GPU tiles consume previous/current float textures. Their shared topology is subdivided for subcell support, not rebuilt as a CPU isosurface. Supported free surfaces respect ledges, solid boundaries and wet/dry coverage. CPU surface queries use the same reconstruction for habitat, contact and camera medium. GPU trajectory rings reconstruct connected falling sheets from real parcel volume, face aperture width and motion. Airborne refractors write depth so instanced sheet overlap cannot overwrite a nearer surface with a farther background copy. Disconnected short runs become compact volume-preserving droplets. Refraction, absorption, capillary normals and dark-enclosure light response present the liquid.

The ecology scheduler has no advancing mobile-water solver. Hydrology's depth and current arrays are derived samples for ecology and overlays, plus historical import buffers. Tools, moisture, fauna habitat/current, observation, camera, presets and terrain editing use the new owner. Terrain/prop edits join outstanding work and invalidate solid support without deleting volume. Erosion still stops at its finite 3 cm / 600 second exposure budget; sediment deposition is not modeled.

## Preserved standing water

`WaterMesh.BuildWaterTable` is text-identical to the starting revision. `WaterRenderer.cs` and `water_stream.gdshader` retain their original hashes. Table height/depth queries, moisture coupling and pond topology remain intact. Contact with the fixed-head reservoir is accounted once as `GroundwaterRecharge`; mobile water does not present a second pond surface.

User saves were never overwritten. The original Streamside Hollow archive hash still matches the initial receipt. Every unrelated initially dirty/untracked path matches its baseline hash. Main.cs contains only an additive review route over the user's existing fern/material routes. Existing user Godot processes were left running.

## Removed production paths

Removed the old advancing Hydrology finite-volume solver, `LiquidDynamics`, `ParticleFluid`, `SpringFluidSystem`, its ground snapshot, CPU `LiquidSurfaceMesh`, spring surface/outlet mesh builders, `WaterFluidRenderer`, and obsolete mobile-water shaders. Historical DTO/read paths may remain, but they never advance or render during normal play. There is one production mobile-water authority.

Manifest schema 4 stores cells, shared momentum, surface slopes, airborne position/velocity/volume/shape metadata, queued source rates, fractions, time and erosion history. Render meshes/textures are disposable and absent from saves. Transactional saves, payload checksums and staged-load validation remain. The save fault-injection test hook uses AsyncLocal to avoid making parallel tests fail unrelated saves. Newly-added v1 species/payload migration work was removed after the user waived compatibility; no additional historical migration is promised.

## Executed verification

Generated receipts are under `build/surface-water-replacement/` and are not committed.

| Receipt | Verified scope |
| --- | --- |
| `refined.trx` | 70 passing hydrology, persistence, tools, erosion, replacement and night-flora tests at that checkpoint. |
| `newton.trx` | 30 passing focused physics/erosion/hydrology tests after geometric-capacity optimization. |
| `cadence.trx` | Both clock/cadence checks pass: equal physical input across irregular rendering and read-only observation at matching ecology ticks. |
| `tests-20261014-205617/fluid.trx` | 78 passed: hydrology, current persistence, tools, camera, integration, erosion, replacement, all preset warmups and night flora. Three independently reproduced baseline integration failures are explicitly excluded; long ecology soaks are not part of this fast run. |
| `baseline-integration-fast.trx` | All three excluded integration failures also fail on the untouched baseline DLL: missing Brooklace starters, aquatic-habitat expectation, and unrelated dead_flora tool-state changes. |
| `baseline-bioclock.trx` | BioClock continued-world digest failure reproduced on the untouched baseline DLL. Full long ecology continuation is not certified by the fluid tests. |
| `final-boot.log` | Headless Godot: save/load/roundtrip true, 0 invariants, `VIVARIUM_BOOT_OK`. |
| `verified-A` through `verified-F` | Production-session slope, depression/overflow, obstacles/removal, drop, pond junction, and terrain edits. Each completed 45 physical seconds, reload, snapshots and motion samples. Maximum conservation error across these runs: 1.25e-16 m3. |
| `D-native-verified` | Real 1600x900 Desktop Duplication video, 30 FPS, 449 frames. Decoded image is nonblank and matches the owned viewport (mean absolute RGB error 1.88/255). Actual frame samples were inspected. GDI capture attempts were blank and are rejected evidence. |
| `populated-H-v2` | 90 seconds of active ecology, 92 flora/148 fauna, props, two springs, erosion, bounded fluid backlog and conservation. Synthetic terrain, not normal-preset acceptance. |

Numerical tests cover conservation, positivity, triangle capacity, resting level/velocity, fill-before-spill, persistent sub-resolution input, obstacle removal, airborne impact, exactly-once pond transfer, terrain displacement, pause, current-format roundtrip, saved rate-change history, physical cadence and all preset warmups. The observation fixture holds ecology at matching integer ticks and compares the same physical duration; it does not treat different elapsed wall times as equal fluid states.

A dry-versus-flow comparison identified the broad green ledge curtain in early images as terrain moss already present before water input. Bare fixtures now use dry lit soil for visibility, including after reload; populated reviews retain ordinary materials. This was a review correction, not a change to the user's terrain shader.

## Performance and remaining acceptance gap

Hardware: GTX 1080, Ryzen 5 1600. Medium quality, native 1600x900 unless explicitly recorded otherwise. VSync is now disabled in review benchmarks, consistent with the repository's established performance runner. Earlier VSync-enabled receipts remain diagnostic.

- `D-native-verified/performance.json`: 141.5 FPS while recording externally, p99 17.18 ms, worst 32.99 ms, 13.8 ms final fluid backlog. Recording is clearly identified; it is not an uncaptured benchmark.
- `D-final-benchmark/performance.json`: final uncaptured waterfall, 148.45 FPS / p99 12.61 ms / worst 20.87 ms / 11.7 ms backlog. Medium quality, native render scale, VSync disabled.
- `populated-H-v2/performance.json`: synthetic populated fixture, 63.1 FPS / p99 16.80 ms / 1.95 ms backlog over 90 seconds.
- `H-no-vsync/performance.json`: actual Streamside Hollow, 127 flora / 158 fauna / props / two springs / active ecology, 18.23 FPS / p99 82.53 ms / 9.2 ms backlog over 30 seconds. **Does not meet the target.**
- `default-H-drawing-off/performance.json`: diagnostic with mobile-water drawing disabled still runs at 21.17 FPS / p99 69 ms. It does not certify a baseline performance pass.
- `default-profile/performance.json`: measured CPU means about 3.27 ms/session and 1.97 ms/mobile-water presentation per frame. These are CPU scopes, not GPU timings. The remaining native/rendering cost has not been fully attributed.

Managed live memory after normal-world probes stayed around 12-18 MB, with process working set near 1.2 GB after assets loaded. Allocation traffic remains measurable, roughly 35 MB/s in the last normal probe. The five-minute ground-view soak (`H-ground-soak`) advanced 299.97 physical seconds, ending with 193 flora/311 fauna at 7.38 biological days, 11.96 ms backlog and 1.71e-15 m3 accounting error. Managed-memory sample means were 27.8 MB early and 24.4 MB late; working-set means were 1208 MB and 1212 MB. Its 11.86 FPS / p99 122.35 ms result fails the frame-time target too. No multi-hour memory claim is made. Input backlog remains retained and bounded in the executed runs; no volume or elapsed time was removed to force performance.

The full normal-world 30 FPS/frame-pacing gate remains open. Wide-scene performance cannot be represented by the successful small fluid fixtures. No complete photorealism or full-project acceptance claim is made. Next work should profile and resolve that normal-world cost, then repeat the populated-world soak and final visual review at acceptable frame pacing.

Approximation limits: one supported free surface per horizontal location, solid tops rather than cave/overhang hydraulics, shallow local-inertial flow rather than full turbulent Navier-Stokes, finite control-volume resolution, ballistic sheets/splash fractions rather than resolved surface-tension breakup or air entrainment, screen-space refraction and enclosure-reflection approximation. Sub-nanolitre numerical wet/dry reserves remain stored and persisted until more water arrives.

## Reproduction

Run the game from the repo with the bundled Godot console executable and `--path game`. Build with `dotnet build game/Vivarium.csproj -v:q` first.

`./scripts/review-surface-water.ps1 -Scenario A -Mode Motion` runs a complete numerical/reload fixture and 8 Hz viewport samples. `-Scenario D -Mode Video` records an actual 30 FPS Desktop Duplication video of the owned foreground review window; requires FFmpeg and Python/Pillow for validation. `-Scenario H -Mode Benchmark -Preset default` tests the ordinary populated preset and deliberately fails the runner if the current performance target is missed. `-DurationSeconds` extends a live run. Evidence directories must be new; existing evidence is never overwritten by the runner.

The runner rejects missing receipts, failure markers, shader errors, conservation loss and sustained physical backlog. Benchmark mode also rejects average FPS below 30 or p99 above 33.333 ms. Video validation checks dimensions, frame rate, frame count, nonblank image and owned-viewport match. Visual quality still requires inspecting screenshots and actual decoded motion.

`./scripts/test-surface-water.ps1` runs the targeted fast regression, including the known baseline failures by default. `-ExcludeKnownBaselineFailures` explicitly excludes the three independently reproduced integration failures and reproduces the targeted passing run. The script rejects missing/empty receipts and any failed test.

Isolated test artifacts avoid an unrelated, pre-existing testhost that holds normal Debug outputs. See the exact filter and test receipts in the generated directory; exclude Speed=Slow for ordinary regression. No push or release was performed.
