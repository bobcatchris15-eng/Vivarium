# Dedicated flora visual system

Implemented in the live Godot project, 2026-09-27. The user accepted the overall Umbraheart, Kiteleaf, and Glassfinger pilot direction. Shadebell, Ironlace, Fenneedle, Lanternbrush, and Embercrown now also use the dedicated leaf system; their new finishes await visual review. Unmigrated flora retains its existing renderer. The approved procedural growth forms and simulation/save schema are preserved.

## Representation

`OrganismMeshes.Flora(..., visualDetail: tier)` records blade vertex/index ranges, stable identity, attachment, length, material variant, age, flexibility, and volume. These descriptions are render data, not world state. `FloraVisualCompiler` separates stem and blade triangles and writes floating-point attributes: CUSTOM0 = seed / variant / age / flexibility; CUSTOM1 = attachment XYZ / blade length. Existing mesh callers opt out by default. Fauna continues using its original signed CUSTOM0 convention.

`FloraVisualProfile` loads editable recipes from `game/content/visuals/flora`. Each cached ArrayMesh has separate stem and leaf surfaces. Leaves remain batched within MultiMesh instances; no per-leaf nodes are created. Thin broadleaves have one authored sheet with explicit front/underside pigment and roughness. Glassfinger keeps its fleshy volume and pale surface flecks.

Three cached geometry tiers use the same morphology seed, leaf count, attachment locations, and material identities. Only blade tessellation changes. Whole-plant projected height selects tiers at 320 and 120 pixels with 20% hysteresis. The normal renderer refresh budget and visibility rejection remain in effect. Camera movement changes instance assignment, never generates geometry.

## Asset pipeline

Thirty-two native-resolution imagegen masters live in `game/Textures/LeafPools/Masters`. `generation.json` and `woody-generation.json` retain exact prompts and provenance. Each of eight species has four subtle, evenly lit tissue images without major veins or leaf outlines. These are generated fictional tissues informed by botanical references, not measured scans.

The offline `LeafMaterialBaker` produces 1K runtime arrays with four layers, BC7 compression, and mipmaps. Color is normalized in linear space, with a narrow symmetric edge crossfade for continuous repetition. Deterministic per-leaf selection and smooth neighboring-layer blending operate continuously in blade coordinates. It never assigns independent textures to triangles.

Controlled periodic surface recipes author normals and physical maps independently of image brightness. Physical RGB channels store roughness variation, transmission, and relative thickness. Baked anatomy includes tapering midribs, secondary branches, tertiary forks/cross-links, and additional basal ribs for cordate Umbraheart blades; Glassfinger venation is restrained. Vein masks affect pigment and transmitted light while their slopes compose with tissue normals.

The twenty-four runtime arrays occupy about 128 MiB before the sixteen compressed anatomy maps. Masters are retained separately. Increase runtime resolution only after a matched capture demonstrates a visible benefit.

Rebuild:

```powershell
dotnet build game/Vivarium.csproj -c Debug
& tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --flora-bake
& tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --flora-bake --bake-species shadebell,ironlace,fenneedle,lanternbrush,embercrown
& tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --headless --path game --editor --import
```

The bake requires a real rendering device: Godot's headless dummy renderer cannot serialize populated GPU texture arrays. The baker rejects headless operation, and profiles validate array dimensions/layer counts. Anatomy PNG imports must retain VRAM compression and mipmaps; normal maps use the normal-map import setting.

## Lighting and motion

`leaf.gdshader` uses Godot's two-sided normal handling, roughness/specular response, and BACKLIGHT for a thin-leaf transmission approximation. It does not implement a measured leaf BSSRDF. Undersides have their own pigment and roughness; thickness and veins reduce transmission. Canopy occlusion is restrained and nondirectional. Normal and color detail contain no fixed sunlight shadows.

Each blade bends about its attachment with a tip-weighted angle and stable phase. The larger wind follows the existing stem wind; the leaf normal receives the corresponding rotations and bend correction. The leaf base follows its stem rather than sliding under independent wind. `--phase` freezes shader time for reproducible review.

## Review and verification

```powershell
# Append --legacy-flora for the previous path, --close for detail, or --underside.
Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --specimen umbraheart --lighting back --detail 0 --output build/flora-system/review/umbraheart/back
# Wind: compare both angles and several phases.
Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --specimen kiteleaf --phase 1.5 --reverse --output build/flora-system/review/kiteleaf/wind-reverse
# Integrated game lighting and scale.
Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --species-view glassfinger --output build/flora-system/review/glassfinger/world
```

Focused geometry tests verify stable identities/attachments across tiers, deterministic regeneration, preserved triangle counts after material separation, one authored broadleaf side, volumetric succulent leaves, and hysteresis. Persistence tests round-trip every subsystem and compare the running ecology digest after save/load. The build and all eleven focused tests pass.

Review gallery: `game/build/flora-system/review/index.html`. All three static specimen renders are byte-identical when regenerated in a fresh process (`verification.json`). Front/back/shade comparisons, detail and underside views, two wind phases from both angles, and integrated world screenshots are saved in the gallery tree. The wind comparison shows gentle motion without visible leaf-base separation. All three integrated runs exit without resource/shader errors; their screenshots were inspected to confirm the staged plants actually appear. The capture harness now waits one second for the budgeted flora refresh instead of assuming thirty frames are enough at high FPS.

Performance reports live under `game/build/flora-system`. `VIVARIUM_LEGACY_FLORA=1` selects the previous pilot path for A/B measurement. `VIVARIUM_PERF_FREEZE=1` pauses the initial fixed-seed world before its first tick and isolates render costs; normal dynamic performance testing remains available. Run paired comparisons with the same environment and no second game instance sharing the GPU. The acceptance gate is p99 within 5% of baseline and no added recurring stalls. Initial comparisons were rejected because of overlapping verification and a second running game.

### Isolated dense-scene rendering measurement

GTX 1080, default fixed seed, 166 flora / 267 fauna, 30-second moving-camera runs with the initial world paused; first three seconds excluded from percentiles. Reports: `isolated-before/perf_report.json` and `isolated-after/perf_report.json`.

| Metric | Previous pilot | Dedicated pilot |
|---|---:|---:|
| p50 frame time | 8.09 ms | 7.79 ms |
| p95 frame time | 8.90 ms | 8.63 ms |
| p99 frame time | 11.89 ms | 10.71 ms |
| Worst measured frame | 28.56 ms | 22.26 ms |
| Frames over 33.4 ms | 0 | 0 |

The render-only gate passes (-9.9% p99). This result applies to the tested GPU and scene; it does not establish performance on the Radeon 860M. Dynamic-world results are recorded separately.

The last render-only samples report 414 → 432 draw calls, 4,224,357 → 4,131,030 primitives, and 506 → 550 MiB video memory. These are whole-scene counters at the corresponding orbit point, not leaf-only texture measurements.

### Running-simulation measurement

Same default seed, 30-second moving-camera runs, simulation enabled; both end at 176 flora / 217 fauna. Reports: `dynamic-before/perf_report.json` and `dynamic-after/perf_report.json`.

| Metric | Previous pilot | Dedicated pilot |
|---|---:|---:|
| p50 frame time | 8.17 ms | 8.03 ms |
| p95 frame time | 13.17 ms | 11.75 ms |
| p99 frame time | 23.25 ms | 20.63 ms |
| Worst measured frame | 64.33 ms | 60.27 ms |
| Frames over 33.4 ms | 9 | 5 |

The dynamic gate passes (-11.3% p99), with fewer long frames. Existing simulation/coverage/terrain work can still stall; this pilot does not claim to eliminate those unrelated costs. Both isolated pairs loaded resources without shader errors and exited cleanly.

## Remaining woody species pass

The user authorized continuing through the remaining trees and shrubs on 2026-09-27. Profiles are integrated directly in the live project. Each receives four generated tissue variants, separate upper/underside settings, independently authored normal/physical maps, and cached species anatomy. Existing silhouettes, leaf placement, bark references, fruit, and flower clusters are retained.

| Species | Surface direction | Anatomy |
|---|---|---|
| Shadebell | Leathery emerald tops, matte paler undersides | Restrained pinnate branches without conspicuous cross-links |
| Ironlace | Thin matte green leaves, papery undersides | Fine pinnate branches and cross-links |
| Fenneedle | Waxy blue-green longitudinal grain | Midrib and two restrained longitudinal ribs |
| Lanternbrush | Satin teal compound leaflets | Fine slender-leaf branching |
| Embercrown | Warm green compound foliage | Branching veins with continuous coordinates across every leaflet facet |

Fenneedle retains broad, long sweeping needles, following the user's preference for attractive fictional proportions over matching Earth analogues. Its generated pigment is rotated during baking to follow the blade's longitudinal UV axis. The native master is unchanged.

`enabled` permits staging a profile until assets are baked. `microScale`, `microRelief`, and `striation` control physical surface recipes independently of generated color. `rotateTissue` rotates only the baked pigment. Bake selection is explicit with `--bake-species`; the default remains the original three pilots.

Lanternbrush records each curved leaflet separately and uses baked midrib relief instead of a rigid decorative midrib tube that would remain behind during leaf wind. Embercrown now shares continuous blade coordinates across its facets and uses correct clockwise front winding. Thin sheets use a single authored side. The original geometry remains available through the legacy path for comparisons.

Build: zero warnings/errors. Focused flora geometry checks: 25 passed, including stable identities/attachments across tiers, single-sheet construction, triangle-preserving splitting, and Embercrown facet UV continuity/front winding. Comparisons, undersides, wind phases, and integrated captures are under `game/build/flora-system/review/woody-final`; `review/woody.html` is the review gallery. These finishes are candidates pending user judgment.

All five fresh-process static captures are byte-identical across reloads. The five integrated-world runs loaded and exited cleanly, and screenshots were inspected to confirm the staged species appear; Lanternbrush also has a new close world view. Wind was captured at 0 and 1.5 seconds from both sides, with leaf roots remaining attached in the reviewed views. Both subsystem and running-ecology save round-trip checks passed (27 focused checks total).

### Five-species performance gate

GTX 1080, default fixed seed, 30-second moving-camera runs, first three seconds excluded. The baseline keeps the original three dedicated pilots active and these five profiles inactive. No other running game shared the GPU. Reports are under `game/build/flora-system/woody-{baseline,after}-{frozen,dynamic}`; the additional dynamic check is `woody-after-dynamic-repeat`.

| Metric | Paused baseline | Paused after | Dynamic baseline | Dynamic after | Dynamic repeat |
|---|---:|---:|---:|---:|---:|
| p50 ms | 7.76 | 7.30 | 7.81 | 7.38 | 7.21 |
| p95 ms | 8.55 | 7.98 | 10.84 | 9.91 | 9.38 |
| p99 ms | 16.19 | 14.62 | 18.87 | 17.39 | 16.04 |
| Worst ms | 59.69 | 35.45 | 43.46 | 152.54 | 41.83 |
| Frames over 33.4 ms | 7 | 1 | 2 | 1 | 2 |

The p99 gate passes in both modes. One 152.54 ms dynamic outlier prompted the repeat; it did not recur. These short runs establish no added recurring stall in the tested slice, not elimination of all possible hitches. Existing startup simulation/coverage work remains outside the steady-state comparison.

At the last paused orbit sample, whole-scene counters changed from 432 to 484 draw calls, 4,132,188 to 3,874,284 primitives, and 550 to 756 MiB video memory. The additional memory includes five texture pools plus three cached geometry tiers per morphology. This is evidence for the GTX 1080 and tested scene only; Radeon 860M remains unmeasured.

## Research sources

- [Leaf reflectance and transmission](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/leaf.pdf): distinct reflection/transmission, sidedness, and thickness motivate the surface model.
- [Runions et al. venation](https://algorithmicbotany.org/papers/venation.sig2005.pdf): branching anatomy motivates the cached vein graphs; this pilot is a bounded authored graph recipe, not a complete implementation of their growth algorithm.
- [Crytek vegetation animation](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis): separate plant and blade motion.
- [Godot spatial shaders](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html): current engine surface outputs and two-sided rendering.
