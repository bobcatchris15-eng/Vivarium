# Saved-terrain fluid candidate probe

This is a read-only numerical probe of `user-autosave-reference.vivsave`, run from the fruit-lifecycle worktree. The save was opened through `SaveSystem.Load`; the candidate graph bed used `VivariumWorld.GroundHeight` at every domain cell center, so the bed includes saved edits, Pilot Tree roots, and prop tops. Candidate surface volume started empty. No save or live installation was changed.

The candidate source snapshot copied for this probe is SHA-256 `05A43A4F65E300767FD8A8962645067E72DBB0506E93FCDC52657958BABB0B83` (`WaterVolumeGraph.cs` in the solver worktree at probe time). It contains exterior `B=-1` edges and increments `BoundaryOutflow` on those transfers. The spring selected nearest the requested point was `spring-312` at `(-1.77531, 2.14902)`, discharge `0.0005555555555555556 m³/s`. The probe ran 1-second advances to 1000 seconds and recorded 10, 25, 125, and 1000 second checkpoints.

| Elapsed | Solver time / pending remainder | Integrated spring volume | Retained surface volume | Accounted boundary outflow | Residual (`in - retained - out`) | Wet nodes |
|---:|---:|---:|---:|---:|---:|---:|
| 10 s | 4.669 / 5.331082 s | 0.002593843 m³ | 0.002593843 m³ | 0 | 0 | 23 |
| 25 s | 25 / 0 s | 0.013888889 m³ | 0.013888889 m³ | 0 | 0 | 39 |
| 125 s | 125 / 0 s | 0.069444444 m³ | 0.067987329 m³ | 0.001457116 m³ | about `1e-12 m³` | 91 |
| 1000 s | 1000 / 0 s | 0.555555556 m³ | 0.342406992 m³ | 0.213148563 m³ | `1.345645816996921e-12 m³` | 104 |

The apparent 1000-second deficit in the earlier probe was boundary discharge, not unexplained loss. At 1000 seconds, retained plus reported boundary outflow is `0.5555555555542069 m³`, against `0.5555555555555525 m³` source input. The residual is roughly 2.4 parts per trillion of input. The minimum remaining node volume was `3.7594277137133964e-8 m³`; zero final node volumes were below `1e-12 m³`. The solver drops updated node values `<=1e-14 m³` (`WaterVolumeGraph.cs:294-295`); this probe does not expose a cumulative count of such threshold removals, so it cannot claim a bound on cumulative threshold-truncation across all internal steps. The measured aggregate residual is the bound for this run.

At 10 seconds, the adaptive solver had accumulated 5.331 seconds of remainder; by 25 seconds it caught up. This confirms the backlog is visible at a one-second caller cadence early in this low-discharge terrain case, though pending source schedule preserved the input accounting. At 25 seconds the local source neighborhood remained shallow (maximum node depth about 0.00056 m, head about 0.59212 m). The lowest ground sample within 3 m was at `(0.875,0.875)`, bed `-0.60541 m`; it first held water by 125 seconds (depth `0.098447 m`, surface `-0.5070 m`) and reached depth `0.450929 m`, surface `-0.1545 m` by 1000 seconds. This demonstrates actual low-ground accumulation, but this single probe does not establish a stable pond shoreline or visually acceptable pond area.

The four straight-line terrain samples between the spring and the low point were dry at the checkpoints even while the low point was wet. That is a diagnostic sampling line, not proof of a disconnected flow route; a full wet-path/contact analysis is still needed to characterize how water gets there. The graph retained 104 wet nodes at 1000 seconds and exported 0.213 m³ through domain edges, so domain boundary condition materially affects the saved terrain outcome.

Reproduction command:

```powershell
dotnet test tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj --no-restore --filter FullyQualifiedName~SavedTerrainFluidProbeTests --logger 'console;verbosity=detailed'
```

Result: passed 1/1 with the boundary-inclusive mass residual assertion. The probe is exploratory and intentionally lives only in the fruit-lifecycle worktree; it does not wire the candidate into production.
## Addendum: source-scale terrain and observed wet-path connectivity

The same frozen candidate snapshot was rerun after adding source-radius terrain and wet-path diagnostics. Snapshot SHA remains `05A43A4F65E300767FD8A8962645067E72DBB0506E93FCDC52657958BABB0B83`; the reference save and solver source were unchanged. The graph bed still samples `GroundHeight` at grid-cell centers (cell spacing/radius 0.25/0.125 m), while the new source profile samples `GroundHeight` directly at the saved spring position and around its radial rings. `GroundHeight` includes saved terrain edits plus the world’s root/prop surface contributions.

For `spring-312`, exact source ground is `0.476995 m`; the nearest graph support center `(-1.875, 2.125)` has bed `0.511015 m`, about `0.03402 m` higher than the exact source location. Ring bed minima/maxima were: r=0.05 m `[0.469847,0.494716]`, r=0.10 `[0.470825,0.524520]`, r=0.15 `[0.466457,0.558599]`, r=0.22 `[0.465519,0.578103]`, r=0.30 `[0.437428,0.599629]`. A 32-angle ray scan at 0.025 m increments through 0.8 m found the lowest directional maximum at `0.476995 m` (angle index 6, 67.5 degrees). That ray has no sampled terrain rise above the exact source height, while other rays climb substantially. These samples do not show a closed hollow around the spring; the actual source point is lower than its nearest finite-volume support, and there is at least one sampled open/downhill corridor. The 0.25 m graph can miss this point-scale relief, so the low-point-wetting result below must not be read as proof that this source hollow is resolved.

Wet connectivity was computed over actual 4-neighbor graph edges, requiring positive mobile depth, and independently requiring depth above `1e-5 m`. The spring-to-low-receiver route exists at both 125 s and 1000 s for both thresholds. Per one-second checkpoint, connectivity to the receiver was present for 972/1000 samples at positive depth and 961/1000 at `>1e-5 m`, with one connected/disconnected transition in each series. Thus the earlier straight-line samples were a poor representation: the candidate route bends around the terrain.

The shortest route at 125 s, listing each support as `x,z / bed / mobile depth / surface head` in metres, was:

`(-1.625,2.375 / .4654 / .000194 / .4656) -> (-1.375,2.375 / .4426 / .000101 / .4427) -> (-1.375,2.625 / .3837 / .000068 / .3838) -> (-1.125,2.625 / .3508 / .000035 / .3508) -> (-.875,2.625 / .3171 / .000243 / .3173) -> (-.625,2.625 / .3036 / .010424 / .3140) -> (-.375,2.625 / .3129 / .000057 / .3129) -> (-.375,2.375 / .3039 / .000149 / .3041) -> (-.125,2.375 / .3006 / .000139 / .3007) -> (.125,2.375 / .2848 / .008822 / .2937) -> (.375,2.375 / .2864 / .006113 / .2925) -> (.625,2.375 / .2834 / .007013 / .2904) -> (.625,2.125 / .2903 / .000004 / .2903) -> (.875,2.125 / .2793 / .003217 / .2825) -> (.875,1.875 / .2785 / .000660 / .2792) -> (.875,1.625 / .2712 / .000032 / .2713) -> (.875,1.375 / .1185 / .000011 / .1185) -> (.875,1.125 / -.2407 / .000011 / -.2407) -> (.875,.875 / -.6054 / .098447 / -.5070)`.

The first support along this route below the configured water table (`0.0 m`) is `(0.875,1.125)`: its bed is `-0.2407 m` and mobile depth is about `1.1e-5 m` at 125 seconds. At 1000 seconds this same support holds `0.086243 m` mobile depth and has surface head `-0.1544 m`; the basin receiver's head is `-0.1545 m`. The candidate has entered the below-table bed region but has not raised this pool to the table elevation. The signed instantaneous flux crossing the bed `< WaterTable` contour was `0.0005554871 m³/s` at 125 s and `0.0005555556 m³/s` at 1000 s. Those are end-of-step edge-discharge diagnostics, not cumulative transfer measurements. This is the relevant integration boundary: transfer only volume that actually contacts/overlaps the exposed hydrostatic body; do not clear all graph cells whose bed is below the table.

The focused probe still passes 1/1. Connectivity and terrain sampling do not establish a visually acceptable waterline, exact point-scale hollow containment, or production coupling. The saved source-to-basin test resolves its numerical route only at the graph's support resolution.

## Addendum: localized point-source comparison

The original source probe used `SupportNodesAt(position, 0.3 m)`. That footprint is larger than the authored `CarveSpringHole` radius of 0.2 m (depth 0.08 m), so its earlier checkpoints are broad-source results and must not be presented as a localized vent test. The comparison below reruns the same saved spring discharge with exactly one support: `graph.NodeAt(spring.Position)`. It uses the same frozen solver SHA above and leaves the earlier broad-source evidence intact.

The mapped single support is cell center `(-1.875,2.125)`, bed `0.511015 m`; the exact source position ground is `0.476995 m`. That 3.4 cm mismatch remains a limitation of the frozen cell-center graph. The point source was not distributed beyond this one cell.

| Elapsed | Point source injected | Retained | Boundary outflow | Residual | Source-node depth | Wet nodes | Path to receiver |
|---:|---:|---:|---:|---:|---:|---:|---|
| 10 s | 0.002478397 m³ | 0.002478397 m³ | 0 | `2.2e-14 m³` | 0.001203 m | 18 | no |
| 25 s | 0.012919707 m³ | 0.012919707 m³ | 0 | `1.3e-12 m³` | 0.001312 m | 32 | no |
| 125 s | 0.069444444 m³ | 0.068005754 m³ | 0.001438691 m³ | `6.8e-12 m³` | 0.001345 m | 81 | yes at positive and `>1e-5 m` depth |
| 1000 s | 0.555555556 m³ | 0.342542485 m³ | 0.213013071 m³ | `6.9e-12 m³` | 0.001314 m | 94 | yes at positive and `>1e-5 m` depth |

At the 10- and 25-second caller checkpoints, the solver had not yet consumed the entire scheduled source duration (input volume corresponds to approximately 4.46 s and 23.26 s respectively); it had caught up by 125 seconds. The localized run still develops a bent connected path to the receiver, but the checkpoint path is absent at 10 and 25 seconds. Compared with the broad-disc run, the single source cell remains deeper near the vent (`~0.0013 m` versus broad local maxima `~0.0006 m` at 125-1000s); the downstream basin receiver depth is very similar (0.098077 m vs 0.098447 m at 125 s; both 0.450929 m at 1000 s). Mass closes with named domain outflow in both runs. These results establish a localized *one-support* source test only; they do not resolve the authored 0.2 m carved hollow with the frozen 0.25 m cell-center bed representation.

The same focused test was rerun and passed 1/1 with both source variants. Exact source fixture and point-source result are retained in `SavedTerrainFluidProbeTests` output. The broader-disc and one-cell source variants are explicitly separate to avoid implying that the broad injection validates vent-scale pooling.
