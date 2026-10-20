# Forest references and graphics experiments

User direction, October 15, 2026: compare the supplied photographs with the game, implement improvements, and repeat. Mission active; no convergence or final visual acceptance claimed.

## Largest visible gaps

| Priority | Observed gap | Candidate approach |
| --- | --- | --- |
| 1 | Angular leaves and repeated straight stems | Inspect actual detail tier and blade geometry before increasing texture frequency. Compare cambered blades, varied attachments, and species-specific droop. |
| 2 | Missing readable low vegetation between soil and larger plants | Replace 11 mm diamond-shaped anonymous leaves and 14–22 mm triangular grass with a varied, curved, petiolate low layer. Preserve ecological cover and exclusions; keep count unchanged for the first experiment. |
| 3 | Orange, repetitive trunk relief | Separate species anatomy, cutaway interior wood, pigment, scan coloration, and lighting. The Crowncoil's armor is intentional; do not erase species identity to imitate an unrelated tree. |
| 4 | Moss reads as surface coloration | Explore occupied-surface cushions and irregular tufted boundaries; preserve bare bark and authoritative occupancy. |
| 5 | Shadowed foliage/water loses readable form | Compare daylight with the previous dusk diagnostic; evaluate fill, backlighting, exposure, and water separately. |
| 6 | Repetition and weak depth at ordinary distance | Use deterministic variation in shape, scale, age, and attachment, then assess ecosystem views and motion. |

The source photos show overlapping scales of growth, bent stems, rounded and narrow leaf types, broken moss cushions, damp rough bark, debris, and readable shaded depth. Ignore watermarks when studying their visual structure; originals remain unchanged.

## Evidence and baseline correction

Historical orientation images inspected: `build/visual-lab/world-diagnostics/restrained-bed-light-20261014/{ground_vegetation,log_contact,macro_flora,mixed_depth,overview,dense_colony}.png`, and `scratch/2026-10-14 22_30_57-Window.png`. These are not fresh before captures. Several legacy views are blocked by the tree or leaves; the legacy world review uses light phase 420, close to dusk.

`scripts/visuals/forest-review.ps1` builds the actual game, authenticates eligible source/assets before and after rendering, preserves competing processes, holds the existing GPU capture lease, isolates user storage, and records explicit diagnostic limitations. The opt-in forest review uses daylight phase 60 and fixed exposure, refreshes camera-dependent ambient cover despite paused engine time, and supports camera receipt replay. Existing foliage time uniforms are locked; other shader TIME users and initial animal poses remain uncontrolled. No whole-frame identical-phase or isolated performance claim is made.

Protected source baseline: `build/forest-reference-20261015/baseline/`. Original dirty patch, Git status, and selected pre-harness source copies are alongside it. Unrelated dirty source and running sessions must survive.

## Iteration log

- Local reference preservation, initial comparison, protected dirty-source snapshot and daytime capture harness completed.
- Canopy irradiance: integrated a cubemap-only canopy sky with a black camera pass, local bounded mist, zero global volumetric density, high-tier soft shadows/SSIL and quality-dependent SSAO. An earlier blind review preferred the shade-readability direction in all four comparisons, while flagging a chalkier floor. Daytime quality-2 empty-void probes, including the forced underwater screen effect, measured all 1,440,000 pixels as RGB(0,0,0). This is bounded evidence, not universal camera/quality acceptance.
- Floor materials: masked the black backing in the isolated-litter photograph, tempered damp litter colors, and corrected the winding/normal contract in all five SoilDetail custom mesh builders. Actual renders exposed and then removed the previously black-facing litter pieces.
- Moss: replaced additive relief that could exceed thin species' thickness with a bounded pillow profile and tapered edges, preserving authoritative coverage and species budgets. Dedicated habitat review remains necessary; a procedural log's moss is not evidence for the separate coverage renderer.
- Foliage: reduced vein albedo contrast and added normal-variance roughness filtering in leaf/flora shaders. Preserve their existing UV2/CUSTOM0 anatomy and animation contracts.
- Groundcover: integrated four bounded batches for grass, creeping leaves, arching rosettes and runners. The first tall-tier implementation produced zero rosettes in the real initial world; removing an unjustified upper-light cutoff produced two fully cleared rosettes. A subsequent fallback to later existing slots when the chosen slot is blocked produces six, still at most one per cell, with unchanged accepted instance totals, random draws and full-footprint exclusions. Seven parabolic, folded leaves replace the tall tier's five inflated micro leaves; the low leaves also have smoother outlines. The matched floor view keeps 839 instances and increases ambient geometry from 33,307 to 44,235 triangles. This is a visible shape/volume improvement, not achieved forest lushness or a performance pass. UV2.x stores crown-height occlusion independently of pigment and the runner-age flag. An overly dark first shader was revised to preserve readable leaf crowns.
- Water: integrated the low-flow ripple reduction, slower standing capillary motion, rougher submerged sediment, reduced submerged-bed specular response and quieter bed caustics. Matched phase-zero captures show reduced frosted/silver bed patches. Preserve depth reconstruction, shoreline clipping, Beer absorption and the actual water surface's reflection. Synthetic reflection controls remain a follow-up.
- Bark: integrated ornament-local shape/spacing variation and shallower Crowncoil keels, keeping the shared structural RNG, support geometry and triangle counts. Fresh matched Crowncoil captures show the irregularity. Palehollow shelf variation has numerical geometry coverage; dedicated species imagery remains pending.
- Small stone: replaced the render-only gravel pebble's universal boulder profile with a mixture of irregular cleavage chips and weathered fragments, using thinner vertical proportions and smaller tilt. The first uniform flat-top version looked like paving tiles and was rejected. The retained second version varies side count, outline and proportions. Large-rock geometry, analytic contact surfaces, placement and ecology are unchanged. Matched gravel views use the same cameras and ecology digest; new chip geometry is 30–42 triangles, with retained weathered variants at the previous 80. Dedicated tests cover finite geometry, winding, broad cleavage planes, deterministic scatter and flat-world burial; slope contact still needs review.
- Creatures: integrated opt-in cuticle/moth profiles using verified UV/region semantics, plus named Guardian keratin/quill/fur/plumage material profiles. Fresh forced-recompile comparisons now show the intended material differences. Runtime audits confirm actual material names, bindings and values. Existing eye materials, authored atlases and unrelated species retain their previous contracts; no emissive fur or unsupported anisotropy was added. This does not fill undercoat gaps or establish all-pose readability.
- Further passes must follow observed defects. Do not equate a build, instance counts or an attractive closeup with ecosystem, motion, mature-world or 30 FPS acceptance.

## Capture correction and current evidence

Copying candidates between worktrees preserves their modification timestamps. This allowed an incremental build to reuse an older C# binary even while the source manifests correctly changed. Pixel-identical Guardian/bark comparisons exposed the problem. The capture scripts now use `dotnet build --no-incremental`, record both produced managed binary hashes, preserve the caller's working directory, and still check source stability. Earlier captures without `forced_recompile: true` must not establish acceptance of copied C# variants.

Manifest scope version 2 also includes the three runtime C# files in `src/Vivarium.Sim/Tools`, which the general visual-lab directory-name exclusion omitted. Historical manifests remain preserved with their original scope.

`verified-baseline` / `verified-v1` and `guardian-verified-baseline` / `guardian-verified-v1` are the replacement comparison runs. Each receipt records source identity and compiled binary hashes. Runtime material receipts authenticate actual imported Guardian names, texture bindings and surface properties; the fauna audit records uploaded UV region ranges and material-family values. The separate import-cache audit found consistent GLB/imported-scene hashes for all four Guardians.

Integrated main-checkout evidence: `build/forest-reference-20261015/integrated-main-v2/`, followed by `integrated-main-v3/` with the revised groundcover. Their forced builds passed with zero warnings/errors, complete source scopes remained stable, native review checks passed, and paused world digests were unchanged. The default preset was updated independently during this work; the main images use its 126 flora / 134 fauna initial world. Comparisons use a frozen preset and record their actual digest. The earlier `integrated-main` and later `integrated-main-v4` attempts failed source stability when that shared preset changed and are not authenticated acceptance runs, even though v4's native checks passed. Preserve these failed guards. A line-ending conversion artifact in the groundcover candidate was corrected after v3; `groundcover-next/newline-normalization.json` authenticates that formatting-only change. The normalized source passed the geometry checks and native quality-1 capture.

Final native main-checkout confirmation: `integrated-main-v5/` captures the integrated gravel and water views against the then-current 128 flora / 134 fauna preset. It passed with stable source, forced recompilation, unchanged paused ecology, zero build warnings/errors and successful native checks. Its source tree is `a95eda88cd3328e7877c369e69243f8277dbf9b8af8762c3f63a364c6dfc4a4a`. This is separate from the deliberately frozen before/after worlds. The viewer now contains 11 matched panels and five original reference photographs.

The first six full empty-void images in the quality-2 daylight, quality-0 daylight and quality-1 night captures measured exact RGB(0,0,0), including each forced underwater screen effect. The updated check combines quality-2 main v3, quality-1 groundcover daylight, quality-0 daylight and quality-1 night: all eight full frames pass. See `void-verification.json` and `void-verification-v2.json` for pixel counts, image hashes, cameras, lighting, source and binary provenance. The checker is `scripts/visuals/check_forest_void.py`; this is sampled camera/quality evidence, not a universal boundary proof.

The local `build/forest-reference-20261015/review.html` viewer includes matched before/after panels plus the five original photo references, including the groundcover medium and closeup pairs. Latest main material-clock/camera-arc sequence: `integrated-main-v3/motion.mp4` (32 rendered frames at 16 fps playback; not a runtime frame-rate measurement). Representative stills were inspected; continuous playback and temporal acceptance remain open.

Two failures in the 29-test focused suite were reproduced against an isolated reconstruction of protected pre-graphics Sim source and content: `SeepageSaturatesGroundWithin3Cm` and `CoverageContinuesIdenticallyAfterSaveLoad`. They predate these graphics edits. The source provenance, exact assertions and isolated output are in `build/forest-reference-20261015/test-provenance-audit.{md,json}`. Their underlying simulation defects remain unresolved.

After the later groundcover and pebble integration, 19 focused coverage, tree, rock, pebble and gravel contract tests passed (`final-v2-contract-tests.log`). Four extracted groundcover builders also passed finite-attribute, unit-normal, nondegenerate-triangle, crown-occlusion and Godot clockwise-winding checks; arching mesh vertices fit the conservative placement envelope. No isolated runtime-performance gate has passed; other live Godot processes were preserved.

## Latest retained visual passes

See [LATEST.md](LATEST.md) for the current integrated result and evidence. Subsequent native passes replaced the pale substrate wash, increased low cover and arching rosettes, filled below elevated shrub crowns, curved Shadebell canes, loosened terminal leaf arrangements, and refined the litter texture and curled leaf geometry. Latest integrated run: `lushness3-integrated-main`; 33 focused tests and four latest sampled full-frame black-void probes pass. Performance work is deferred under the latest user instruction.

## Remaining visible gaps

Large-rock strata and support-aware shoreline dressing remain pending. Some species still show planar foliage; prop moss, Crowncoil ornament repetition and wider water scenes need further visual work. Mature-world and temporal review remain open. These are continuing visual priorities, not evidence of convergence.


## October 16 water checkpoint

See [WATER.md](WATER.md) and `build/forest-reference-20261015/water-impact/review.html` for the retained shared table/film optics, visible advected reflections, matched receiver views and corrected actual-flow motion. The current main source exactly matches the corrected native receipt. Eight deliberately empty above/forced-underwater void frames are exactly RGB(0,0,0). Forty-one focused physics/drainage contracts pass, and no Sim source changed. Startup5s flow and shallow bayou scenes supplement the matched100s creek comparison. Original motion clips with a stale forced underwater overlay are rejected and preserved; only corrected above-water clips are acceptance evidence. Performance remains deferred. The broader reference-photo realism task remains open.
