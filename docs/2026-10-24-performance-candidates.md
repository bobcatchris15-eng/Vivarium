# Performance candidates — 2026-10-24

## Decision and scope

Prioritize GPU work that does not contribute visibly to the habitat: detailed shadow casters, small vegetation geometry, material work below pixel scale, and unnecessary resource residency. Presentation takes precedence over ecological accuracy. This document profiles candidates and defines experiments; it does not authorize a new visual overhaul or claim unmeasured savings.

The next action is a fresh baseline of the **current Medium configuration**, followed by selective shadow-caster experiments. Earlier measurements predate the water restoration, erosion retirement, ten-second terrain-texture cadence and some subsequent visual changes. They identify useful leads but are not current whole-game performance numbers.

## Presentation constraints

- Ambient movement belongs to fauna/guardian and surface water. Do not reintroduce plant wind, fungal breathing, slime streaming or aquatic-plant waving to hide simplification or LOD transitions. Growth and direct interactions are distinct from ambient animation.
- Bokeh/depth-of-field blur is removed by user direction. Production camera attributes initialize with both near/far blur disabled and zero blur amount; camera updates keep it disabled at every distance. Baseline the resulting sharper presentation before attributing any GPU saving to this change.
- Preserve the restored pre-overhaul water treatment from `150c1217`. Optimize its implementation before considering another aesthetic replacement.
- Terrain geometry stays player-authored: automatic erosion is disabled. Direct sculpt/prop edits must remain responsive. Habitat texture updates use a ten-second cadence; a thirty-second experiment is acceptable if the visual lag is unobtrusive.
- Preserve readable silhouettes, material identity, contact, habitat depth and attractive close views. More accurate simulation or physically elaborate shading is not independently valuable if the result looks worse.
- Preserve saves, settings and unrelated dirty work. Diagnostic rendering must not change the world state. Any deliberate future ecological approximation needs its own compatibility decision and checks.

## Evidence and its limits

The settled historical High-quality orbit on a GTX 1080 at 1600×900 averaged **58.617 ms wall time / 17.060 FPS**, with **58.038 ms viewport GPU time** and **5.339 ms viewport rendering CPU time**. This was a GPU bottleneck. A separate live sample showed 100% GPU utilization, 7125 MiB resident memory and 66°C; it was not a memory-breakdown or thermal-soak measurement.

The stronger intervention evidence comes from a single frozen-world overview. Each phase had five seconds of settling and five seconds of measurement. Original settings measured 42.357 ms GPU at the start and 42.217 ms at the end, a -0.33% drift.

| Intervention | GPU time | Difference from starting reference | Interpretation |
| --- | ---: | ---: | --- |
| MSAA off; TAA/FXAA retained | 33.935 ms | -8.422 ms | Strong measured lead; now the Medium AA policy |
| Sun shadows off | 35.946 ms | -6.411 ms | Shadow work is worth isolating; removal changes presentation |
| Fill shadows off | 37.626 ms | -4.731 ms | Another shadow lead; preserve grounding when approximating |
| SSIL off | 40.788 ms | -1.569 ms | Modest saving in this view |
| SDFGI off | 41.011 ms | -1.346 ms | Modest saving in this view |
| 4096 rather than 8192 shadow atlas | 41.976 ms | -0.381 ms | Weak lead for atlas resolution alone |
| SSR off | 42.025 ms | -0.332 ms | Weak in this overview; other views could differ |
| SSAO off | 42.249 ms | -0.108 ms | Weak in this overview |

These differences are neither additive nor guaranteed savings in another camera view. Disabling an entire feature supplies diagnostic context, not a guaranteed upper bound for every replacement algorithm.

The same-world gravel-mask experiment reduced `Island.FieldTexture` CPU per rendered frame from 0.371608 to 0.215725 ms (41.9%), with all 14,336 audited mask pixels equal. That result predates the slower refresh cadence. The two separate orbit runs had different prepared-world digests despite matching populations/ticks and assemblies; their small FPS difference is **not** a valid exact-world A/B gain.

Sources: [performance investigation](performance-2026-10-24.md), [water restoration](overhaul/2026-10-24-water-look-restoration.md), and the [raw within-world sweep](../build/performance-20261024/effect-sweep/20261024T231253Z-b7135c660f054e1b8f5d80a24cec3862/gpu-effect-sweep.json).

## Ranked candidate register

| ID | Priority | Candidate | Evidence strength | Expected benefit / principal risk |
| --- | --- | --- | --- | --- |
| P00 | First | Measure current Medium baseline | Required evidence gap | Prevents optimizing an obsolete configuration |
| P01 | High | Selective shadow casters and simpler shadow geometry | Feature cost measured; replacement unmeasured | GPU reduction / detached objects or weaker canopy shade |
| P02 | High | Vegetation and coverage geometry LOD | Source-supported hypothesis | Geometry, memory and shadow reduction / popping or lost silhouettes |
| P03 | High | Cheaper fill-shadow representation | Fill-shadow cost measured; replacement unmeasured | GPU reduction / flattened or floating habitat |
| P04 | Medium | Distance-aware material complexity | Expensive shader paths verified; cost unisolated | Pixel-shading reduction / relief or material transitions |
| P05 | Medium | Selective texture residency reduction | Memory pressure observed; attribution unmeasured | Memory headroom and possibly smoother traversal / close-view blur |
| P06 | Medium | Cache stationary presentation data and avoid refresh work | Terrain cache measured; other caches unmeasured | CPU spikes, uploads and allocation / stale presentation |
| P07 | Later | Detail budgets for fauna/guardian contact | CPU scopes measured; dominant GPU limit remains | CPU headroom / visible foot sliding or penetration |
| P08 | Later | Coarser ecological scheduling and representative populations | Product direction; no isolated timing yet | Scaling and CPU reduction / discontinuous growth or population changes |
| P09 | Low initially | Trim post effects or lower internal resolution | Effects weak in overview; resolution untested | Potential GPU reduction / loss of lighting depth or image clarity |

### P00 — Current baseline

Use a retained save and record its digest, actual camera route, Medium tier, resolution, source manifest and executing DLL identity. Wait for flora publication and other visible startup layers to settle. Record GPU time, wall p50/p95/p99/worst, rendering CPU, scope totals, draw/primitive counters, VRAM, working set and simulation backlog.

Cover overview, ground-level vegetation, a close specimen, the guardian/nearby fauna and restored surface water. Use a sixty-second route plus short fixed-view probes. Do not run the whole scenario matrix merely to screen one local change. The existing landscape runner forces quality 2: add an explicit recorded quality selection before using it to claim a Medium baseline. Do not relabel its High run as Medium.

### P01 — Shadow geometry and caster selection

Entry points: [EnvironmentRig](../game/Render/EnvironmentRig.cs), [flora renderer](../game/Render/OrganismRenderers.cs), [coverage renderer](../game/Render/CoverageRenderer.cs), [pilot trees](../game/Render/PilotTreeRenderer.cs), and [loam detail](../game/Render/LoamSurfaceDetailRenderer.cs).

Flora has per-variant caster flags; coverage tiles cast shadows; loam detail recursively enables shadows. Isolate these families one at a time before rewriting meshes. Start with tiny loam aggregates and low coverage, then leaf clusters and dense crowns. Keep visible geometry unchanged in the diagnostic.

If a family is expensive, test simplified shadow silhouettes, reduced distant caster density, or disabling only subpixel casters while retaining contact darkness. The rendering mechanism must first be checked against available Godot APIs; do not assume a built-in automatic shadow-proxy path. Stationary plants do **not** by themselves prove the engine caches shadow maps or stops resubmitting them.

Keep only changes that preserve broad canopy shadow, readable branch/leaf silhouettes and grounding in close, grazing-angle and overhead views. Reject visible light leaks, floating litter and abrupt shadow changes during camera movement. Screen with baseline → one family changed → baseline restored, using GPU time and submitted primitives.

### P02 — Geometry LOD without ambient motion

Inspect `FloraRenderer.Refresh`, variant geometry creation, `CoverageRenderer.BuildTileMeshCore`, pilot foliage construction and loam patch density. Existing visibility/projected-size behavior is not proof that every costly mesh has an effective reduced-geometry LOD.

For one expensive family, preserve its nearest representation and reduce distant internal leaf subdivisions, hidden fungal structures, tiny tendrils or coverage shoots. Preserve crown outline, patch extent, density impression and color distribution. Prefer a few convincing clumps over thousands of imperceptible structures. Prototype one family first; retain the same world/population and camera poses.

Track visible and shadow-submitted geometry, draw calls, GPU time, mesh memory and startup publication cost. Test slow approach/retreat for popping and dither shimmer. Do not add wind or movement to conceal transitions. Full-species catalog work comes only after the experiment earns its cost.

### P03 — Fill shadow approximation

The measured fill-shadow intervention saved 4.731 ms in the overview. Test the existing fill with shadows disabled, then compare a softer/broader contact approximation or selectively retained casters. Avoid a global brightness rebalance during the timing experiment: it would obscure whether the shadow replacement is acceptable.

Review under foliage, rock/log contact, guardian feet and day/dusk/night lighting. Reject a flatter or detached-looking habitat even if faster. Preserve the principal sun shadow. Do not add the measured sun/fill savings together.

### P04 — Material cost below visible scale

Entry points: [terrain shader](../game/Shaders/terrain.gdshader), [strata shader](../game/Shaders/strata.gdshader), and [shared surface helpers](../game/Shaders/surface.gdshaderinc). The POM helper performs a linear search, refinement and self-shadow sampling; terrain/strata may invoke it for multiple material layers.

Use a diagnostic toggle to isolate relief cost in ground and cut-face views. Test fewer steps or a smooth transition to normal mapping when the relief becomes subpixel. Keep full relief for close inspection. Preserve material blending, wetness and coarse normals; do not flatten every material into the same response. Inspect grazing angles and transitions during camera movement. Avoid a shared-helper rewrite before identifying which callers actually cost time.

### P05 — Texture memory, not indiscriminate downscaling

Inventory runtime textures by actual GPU format, dimensions, array layers, mip levels and duplication. Separate texture storage from shadow maps, render targets and mesh buffers. Audit imported compression and the leaf pools; do not assume every `.res` follows ordinary image-import settings or that compression is missing.

Retain original source assets. Test 2K runtime variants for one small/repeated asset family, keeping prominent close-view surfaces detailed. At the same format, 4K → 2K reduces a texture's texel count to one quarter; that is **not** a prediction that total VRAM falls by 75%.

Measure memory, GPU time and traversal frame tails after settling. Review the closest intended camera distance for blur and unstable normals. A useful result may be extra memory headroom without a major average-FPS change. Leave the restored water appearance intact.

### P06 — Stationary presentation and slower refresh

Entry points: [IslandRenderer](../game/Render/IslandRenderer.cs), flora refresh, and `LoamSurfaceDetailRenderer.PatchStamp`/`RebuildOnePatch`. The retained terrain cache and ten-second cadence already address this family; do not count their gains again.

Profile dirty checks, repeated transform preparation, material setters, mesh rebuilds and texture uploads separately. Camera movement requires visibility/LOD evaluation but need not invalidate stationary geometry or unchanged material data. Cache by the inputs actually read. Test a thirty-second habitat-texture cadence only if the remaining refresh cost matters; compare visual lag rather than exact ecological fidelity.

Player sculpting, prop edits and returning to previously unseen regions must update promptly. New-world/load invalidation is mandatory. Preserve water and fauna motion rates; slowing ground shading does not authorize slowing their animation.

### P07 — Fauna/guardian contact budgets

Historical orbit CPU scopes include guardian vegetation, skin and leg/contact work. Those scopes overlap; do not sum them as independent costs. Inspect the existing guardian contact caches and [fauna renderer](../game/Render/OrganismRenderers.cs) before proposing another cache.

For distant or offscreen bodies, test less frequent contact solving, simpler ground samples and reduced diagnostic/audit work while retaining detailed near-camera solves. Keep animal animation smooth through interpolation where appropriate. Inspect actual foot/body contact, transitions into close view and nearby vegetation interactions. Do not trade visible sliding, penetration or broken behavior for a scope-timer improvement.

### P08 — Ecology and population approximations

Entry points: [scheduler](../src/Vivarium.Sim/Time/Scheduler.cs), [flora system](../src/Vivarium.Sim/Flora/FloraSystem.cs), [fauna system](../src/Vivarium.Sim/Fauna/FaunaSystem.cs) and coverage systems. First profile a live/mature world: paused-render evidence cannot establish their cost.

Batch slow growth, nutrients, litter and recruitment work using accumulated elapsed time. Consider bounded representative render clumps independently of authoritative population counts. Rendering fewer representatives is a different change from discarding simulation entities; test them separately. Avoid an ecology rewrite while GPU cost dominates. Preserve convincing establishment, persistence and observable animal behavior; exact fine-scale ecological depth may be simplified under the user's direction.

### P09 — Effects and render scale

Keep SSAO/SSR and shadow-resolution reductions below the stronger candidates for now; their measured overview savings were small. Different close-water or ground views could change this ranking, especially after water restoration, so isolate those views before concluding they never matter.

Lower internal resolution remains an available explicit performance setting, not an assumed default. Test it against native resolution with the same scene and AA policy. Inspect fine foliage, text/UI compositing, water edges and camera movement. Prefer removing invisible work before softening the whole presentation.

## Experiment and acceptance contract

1. Start from the current visual state and one retained world. Fix camera, quality, resolution and lighting for probes. Record all intentional differences.
2. Hold `.clanker/visual-lab-gpu.lease`; do not overlap another capture or terminate unrelated processes. Use disposable user storage.
3. Freeze source/binary changes during a run. Verify executing assembly identity, source provenance and world digest. Use one frozen world for render-only interventions, or reload the identical retained snapshot; equal population counts alone are insufficient.
4. Change one cost family. Use short fixed-view screens, a restored reference to check drift, and only then a sixty-second route for promising candidates. GPU timestamps may be delayed and are not frame-aligned with wall samples.
5. Capture matched stills and camera traversal. Animate only fauna and surface water. Preserve intentional lighting variation when testing day/night presentation.
6. Retain a change only if its saving is repeatable beyond reference drift and its visual cost fits the presentation priorities. Define a candidate-specific worthwhile saving after the baseline; do not invent a universal FPS floor or forecast unsupported percentage gains.
7. Keep failed attempts and disclose shader errors, incomplete population, mismatched digests, startup contamination, leaks and untested views. Build success or a passing process does not establish visual or performance acceptance.

## Recommended next sequence

**P00 → P01 → P02**, with P03 if selective fill casters show promise. Run P04 or P05 when the preceding measurements indicate pixel-shading or residency pressure. Use P06 to remove measured spikes. Defer P07/P08 broad changes until live-world profiling establishes a CPU or scaling problem. Reassess after each retained improvement rather than implementing the entire register blindly.
