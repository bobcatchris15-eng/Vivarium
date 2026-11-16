# Plant identity and readable foliage — 2026-11-16

The owner reversed the earlier identity-free redesign direction. Keep the graph architecture and improvements, but approach the previous recognizable species forms. The old renderer is a silhouette and characteristic-structure reference, not a requirement to reproduce its vague or missing foliage. Fenforge, Mirespire and Moonbark were explicit examples of that foliage problem, not the entire affected roster. Distinctiveness, lifelike foliage and anatomical whimsy are welcome. No new bioluminescence. Performance improvements must retain visual richness.

The scheduled continuation used fresh Terra agents for each materially new implementation task, with root coordinating shared files, actual Godot builds/captures and integration. Earlier uncommitted identity work was preserved. No unrelated legacy geometry, imports, project settings or user data were replaced.

## Authored forms and actual iterations

The 125-entry graph catalog remains the production visual path for 106 individual species and bounded microstructure for 19 coverage species. Species now use substantially more specialized construction instead of only the initial family templates.

- Fenforge: raised root cage and staggered leafy shelves. Mirespire: a tapered leader with overlapping cyan needle sprays in three-dimensional volume. Moonbark: pale crooked stems, arching limbs and pendant leafy shoots. Root rejected a sparse intermediate Mirespire made of thin horizontal planes and had it rebuilt with overlapping sprays rather than accepting lower richness.
- Hollowcrown: a real hollow bole with inward-facing bark, an open rim and an organically tapered side mouth; roots and branch scaffolds attach to its exterior. A dedicated ray test checks both openings and the interior wall at all LODs. Root rejected the sealed column approximation, then the rectangular-looking intermediate mouth.
- Greywatch: low spreading pale needle foliage. Its first enlarged needles looked like pebbles, so their proportions were corrected while retaining their placement/count. Witchberry regains a thick splayed fork and compound crown; Cragparasol regains a single crooked trunk and terminal parasol. Other woody forms retain specialized canes, palmate fans, pinnate sprays, croziers and distinct scaffold construction.
- Moonfern: two or three connected lobed, pierced frond sheets. Metric dimensions and early leaf emergence were corrected after the first native capture revealed oversized blades and bare juveniles. Shadebell: three crooked stems with broad leaf decks and pendant bell racemes. Glowbriar: low runners, arching thorny canes and decussate foliage, retaining its existing chalices.
- Herbs: distinct cotton heads, spikes, umbels, burrs, lantern heads, nodding bells, red bracts, compound runners, aquatic plumes, violet clumps and continuous strap fronds. Veilfern's initially flat bowl was replaced with connected rising and drooping rachises; Fencomb was filled, tapered and warmed without deleting its foliage.
- Cryptogams: morel-like pitted Ashcomb caps, cups and eggs, stinkhorn veils, jelly folds, coral forks, sphagnum heads and broader thallus lobes. Ghostbonnet's reflected cap/stalk colors were restored toward a pale icy palette; its emission settings were not increased.

The existing content remains fictional. Botanical counterpart names in authoring tables are construction references, not a replacement for the owner’s species identities or a claim of taxonomic accuracy.

## Reproduce and inspect

Evidence root: `build/plant-graph-tuning-20261115/`.

`identity-legacy/` contains 125 captures through the actual previous geometry/material path. `identity-current/` is the first full graph capture; `identity-second/`, `identity-third/`, `identity-final/`, `identity-cavity/` and `identity-window/` and `identity-taper/` record successive inspections. `identity-scales.json` fixes per-species display scale from the legacy capture log. Cameras, seed and presentation settings are fixed; the right-hand mature specimens are the useful identity comparison. Legacy juveniles do not correspond to the same percentage ages as graph growth.

```powershell
$godot = 'E:/Vivarium/tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
& $godot --path E:/Vivarium/game -- --repr-review E:/Vivarium/build/plant-graph-tuning-20261115/identity-final --repr-demo plant_catalog --plant-catalog-scale-reference E:/Vivarium/build/plant-graph-tuning-20261115/identity-scales.json
```

Use `--plant-catalog-species fenforge,mirespire,moonbark` for a bounded iteration, and `--plant-catalog-macro` for foliage inspection. Hollowcrown also gets a dedicated cavity camera. `--plant-catalog-legacy` uses the previous renderer. The new `identity-v1.json` records the active dedicated-form settings; older organic configurations are historical experiments and can override the restored forms.

## Verification and limits

The catalog sweep covers all 125 species, three identities, four ages and all three LODs. It checks catalog membership, stable attachment/organ identity through growth, mature completion, finite geometry and usable normals, indices and bounded counts. Five intentionally dense species—Fenforge, Moonbark, Mirespire, Fencomb and Deepquill—have an explicit 10,000-organ bound; the other entries retain the 4,000 bound. The old Moonfern count test was updated to require two or three pierced whole fronds instead of fifty or more separate organs. There are now 13 focused plant checks including real hollow opening/interior tests.

The live fixture consumes actual world flora. A nearby woody host is supplied for each vine; its simulation-authored path is refined without changing its course to exercise more than 96 support edges. The bridge now copies all recorded edges instead of truncating them at 96. This is a deliberately refined integration fixture, not evidence that natural growth produced over 96 original edges. The fixture verifies each copied start/end pair and a node-150 update in the presentation path, plus population publication, growth/health, movement, birth/removal, coverage prototypes and warm camera behavior. See the latest live report for actual counts and outcome; failed intermediate fixtures remain as diagnostic evidence.

The separate attachment-index optimization preserves ordered child traversal while avoiding a full-organ scan for every axis. Its frozen before/after comparison matched all mesh/instance/shape fields for 125 mature graphs at three LODs. In single CPU runs, Fenforge compile time fell from 69.166 to 17.381 ms and Moonbark from 34.349 to 12.228 ms, with a small allocation increase and no geometry reduction. Those measurements precede some later art edits; they establish the compiler change, not that the final richer roster has identical overall performance. See `experiments/plant_compile_cost/README.md`.

This is a source/renderer checkpoint for owner review, not final per-species visual approval or Radeon 860M/mature-world performance acceptance. Several fine details remain approximations; the hollow exterior is relatively regular, some growth stages expose structure before foliage fills, and isolated coverage micro-colonies are not a substitute for their continuous production coverage layer. Ecological growth, woody support profiles and corpses still use their existing simulation/presentation rules; all physiology is not derived from the visual graphs. The catalog harness still reports the historical seven Texture RID/one ObjectDB warnings at process exit.

## Final checkpoint receipts

- `identity-release-validation.log`: 125 catalog/content entries, 125 completed, zero errors and zero exact structural fingerprint collisions.
- `identity-release-selftests.log`: 13 focused plant tests passed, including all-LOD hollow opening/interior checks after the tapered mouth change.
- `identity-live-complete/report.json`: 118 living individuals across 106 species, all 19 coverage prototypes, 156 refined support edges for each of six climbers, late-node invalidation, exactly one old chunk changed by growth, and zero warm camera descriptor builds/uploads. The strengthened start-and-end pair assertion also passed in `identity-release-live/report.json`; its native log records `GRAPH_PLANT_LIVE_OK`, no pending work and no error.
- `identity-reviewed/index.html`: all 125 graph/previous-renderer pairs with 15 macro links, shared framing scales and per-image provenance in `capture_sources.json`. The sources span several successful native iteration builds; unchanged images are reused explicitly.
- `identity-taper/plant_catalog/hollowcrown_cavity_growth.png`: actual native view confirms the open tapered side mouth, bark thickness and interior wall. The regular bole contour remains an approximation.

The latest autosave was copied into `user-save-fixture/`, with its original SHA-256 and modification time recorded. The native `identity-user-save/` review loads only that copy, uses transient settings and disables autosave. User data is preserved.

The copied-save run completed nine native views with all 766 flora and 68 fauna published (`identity-user-save/review.json`). Root inspected overview, foliage/terrain and understory views: recognizable substantial foliage is present in the actual save. This does not establish every species/age is resolved. The run was paused and capped at 30 FPS; per-view GPU medians ranged 8.3–26.7 ms on the GTX 1080, and capped frame intervals cannot establish an uncapped performance gain. `capture-receipt.json` records local binary hashes and save receipt references.

Final native headless boot passed: `identity-release-boot.log` records save/load roundtrip true, invariants=0 and `VIVARIUM_BOOT_OK`. The original autosave SHA-256 was rechecked after all runs and still matches the copy receipt.

## Owner corrections: Fenforge, Embercrown, Banksorrow and Kinkcane

The next owner review identified four specific silhouette/material regressions. The correction uses fresh Terra workers for Embercrown/Banksorrow and Kinkcane, root for Fenforge and compiler integration, and a fresh Luna read-only source review.

- Fenforge's eight roots now attach at .10–.14 of the bole rather than .34–.40, and their main endpoints enter the soil. Its foliage construction/count was retained. RootBark was missing from the compiler's woody shader tag: it now receives UV2.x=2 and the bole's basal wood tint, so bark-covered roots use the same material path. The plant.compiler test checks that tag.
- Embercrown retains seven stems and its compound foliage/fruit. Three upward, laterally alternating Hermite sections produce visible sinuous stems; their lengths are staggered .52–.97 of authored height. Crown branches curve over two connected sections. The first revision was too restrained; a stronger tangent-only version collapsed the silhouette and was rejected. Explicit endpoints restore positive height advancement without losing the curves.
- Banksorrow has a leaning low fork and seven arching boughs carrying 56 long leafy hanging shoots, returning to the old curtain-like form. The mature native capture shows the hanging foliage reaching toward the ground.
- Kinkcane uses six straight, angular internodes per cane, with pronounced alternating elbows on lower canes and gentler joints on the taller sentinels. Explicit continuation radii retain a bamboo-like thickness rather than compounding the lateral-branch radius clamp. Foliage is retained.

Current native evidence is `four-species-reviewed/plant_catalog/`, with the same shared identity scales and report/module provenance. `mature-detail.png` is a labeled crop/rescale of those actual captures. `identity-reviewed/index.html` now uses these four captures and their latest macros; older rejected captures remain for diagnostic history. The earlier copied-save review predates these four corrections.

Sequential solution/game builds passed without warnings. The focused 13 plant tests passed after the root-bark fix. The latest numerical catalog passed 125/125 with zero errors/duplicate fingerprints (`four-species-reviewed-validation.log`); boot passed save/load roundtrip and invariants=0 (`four-species-reviewed-boot.log`). This bounded art correction adds no bioluminescence and makes no new performance acceptance claim.
