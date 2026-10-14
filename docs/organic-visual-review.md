# Glasscoil and organic foliage review — 2026-10-10

Changes are in the active `game/project.godot` project. Existing ecology, water, diagnostics and performance work was retained. No commit or push was made.

## Glasscoil

Glasscoil was classified correctly as a snail, but aquatic placement still used a water-column fraction and noisy swimmer pitch. It now occupies the submerged ground/prop support, with renderer normals sampled from that same support. It follows surface orientation even while idle and skips swimmer pitch. The shell is excluded from muscular deformation. A restrained tail-to-head pedal contraction wave moves the foot; tentacles retain independent probing movement. Foot/body wave strength, idle motion and bob are reduced. The compressed spiral shell now has the corresponding inverse-scale normal correction. Shell pigmentation stays within amber/brown rather than selecting the unrelated green palette.

Reference for the locomotion choice: [The mechanics of adhesive locomotion of terrestrial gastropods](https://pmc.ncbi.nlm.nih.gov/articles/PMC6514465/). This is a motion reference, not a claim that the fictional species reproduces every biomechanical detail.

Final native evidence: `build/organic-review/glasscoil-final/live-motion.json`, 240 frames through FaunaSystem + Scheduler + FaunaRenderer; `build/organic-review/glasscoil-live.gif` is the 8-second clip. This fixture does not render a water volume; its ground markers make surface contact and translation visible.

## Foliage geometry

Audited and rendered all 40 registered flora species, including nonvascular organisms. Preserved different botanical forms instead of replacing reeds, fern pinnules, succulent tissue or traps with one broad-leaf shape.

- Broadleaf: more near-view outline stations, rounded asymmetric margins, shallow seeded cup/twist, and lighter Shadebell folds. Shadebell uses an ovate outline and consistent leaf attachments across material detail levels.
- CurvedLeaf: more cross-blade curvature near the camera, restrained seeded asymmetry, collapsed root/tip positions with consistent texture coordinates, finite normals and correct winding.
- Embercrown: curved, tapered compound leaflets replace the four-triangle angular leaflets.
- FoliageBlade: additional near-view silhouette stations. Both this builder and Broadleaf adapt tessellation to the live geometry budget.
- Pilot Tree broad foliage: curved blade surfaces and varying normals replace the old constant-normal quad strips; Pilot Tree material markers are preserved.
- Collapsed blade triangles are skipped instead of emitting zero-area faces.

Full-roster captures: `build/organic-review/all-species-final/`; geometry receipt: `geometry-audit.json`. All 40 meshes have finite position/normal data. `indexedLeafCount` counts dedicated material metadata, not every botanical leaf: many ordinary species use the older LeafSpans path and nonvascular species do not have leaves.

## Material and light iteration

Adjusted the existing photographic texture pool, without adding an unlicensed asset: reduced vein/normal relief and exaggerated per-leaf brightness variation, increased thin-leaf roughness, reduced broad white cuticle highlights, restrained cyan/blue-green pigments, and retained paler rougher undersides with bounded transmission. The generic foliage shader gets the same restrained cuticle response. Shadebell, Lanternbrush and Embercrown have updated species recipes. [Godot shader color-space documentation](https://docs.godotengine.org/en/4.4/tutorials/shaders/shader_reference/shading_language.html) explains the source-color texture decode used by the existing materials.

The live environment uses a weaker grow-light multiplier and a less strongly green ambient tint, with more sky contribution. Auto-exposure target brightness is reduced from 0.40 to 0.16 and maximum sensitivity from 210 to 125 to avoid washing green pigment out against the dark backdrop. Specimen captures now allow 90 settling frames.

The twelve-species roster is Shadebell, Lanternbrush, Embercrown, Veilfern, Ringreed, Mooncoin, Sunstone Rosette, Pitcher Plant, Trifold, Veilblade, Glassfinger and Mirrorleaf. The review habitat adds repeat plantings and existing soil/stone albedo and normal textures. Front/side and backlit captures use fixed exposure for comparison.

Final bounded live-geometry scene: `build/organic-review/production-scene/garden.png` and `garden-backlit.png`. Earlier `shape-pass`, `material-pass`, `final`, and `habitat` directories preserve the iteration. Those earlier scenes and the single-specimen comparison use higher-detail specimen geometry; use production-scene for the geometry actually rendered in the island. The procedural capture scene is `game/App/BotanicalReview.cs` and uses the same FloraTier / FloraVisualProfile paths as the live renderer.

## Verification and limits

Build: 0 errors, 0 warnings. 25 flora/organic/LOD checks plus 7 Pilot Tree/gait checks passed, including submerged-surface placement during movement, deterministic organic contour, finite geometry for every species, stable material identities, winding/texture continuity, no degenerate detail-tier triangles, and live photographed-species geometry caps. Receipts are `build/organic-review/tests/organic-live-budgets.trx` and `organic-pilot-gait.trx`.

The 15-second frozen mature-island probe loaded 2,309 plants and 1,156 animals, reported no logged errors, and averaged 19.3 FPS with p95 frame time 89.99 ms on the GTX 1080. Startup layer work and other capture jobs overlapped part of this short probe, and the Godot editor remained open. These numbers do not establish a performance comparison or a passed frame-time gate. Existing triangle-budget checks pass; long-run performance remains unverified by this visual review.

The current result is still visibly procedural, particularly branch/leaf arrangement, simplified flowers and foliage thinning under the live cap. It is a material and morphology improvement, not photographic acceptance. The saved front/backlit images make those limitations reviewable.

## Reproduce

Build from the repository root:

```powershell
dotnet build game/Vivarium.csproj -v:q
. ./scripts/common.ps1
& (Get-GodotExe) --path game -- --botanical-review E:/Vivarium/build/organic-review/review-again
& (Get-GodotExe) --path game -- --botanical-audit E:/Vivarium/build/organic-review/audit-again
& (Get-GodotExe) --path game -- --fauna-preview glasscoil --live-motion --output E:/Vivarium/build/organic-review/glasscoil-again
```

These review modes exit after saving their captures. Normal editor debug launches use the changed live materials and animation directly.
