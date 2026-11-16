# Organic growth and recognizable species identities — 2026-11-16

The owner requested another full-roster iteration: closer previous identities, organic curves and variance instead of regular straight scaffolds, and substantially differentiated leaf shapes. Barbarrow was the explicit example of a lost leaf identity. The previous fictional renderer and actual previous captures are the references; botanical counterpart labels do not overrule them. No new bioluminescence; visual richness takes priority over performance reductions.

## Completed cycle

Fresh Terra workers handled woody leaf profiles, herb profiles/Barbarrow, cryptogam growth, the three dedicated generators, a second architecture pass per catalog, native visual triage, and targeted identity repairs. Root integrated compiler geometry and checked native captures; fresh Luna workers reviewed source correctness. These are bounded tasks with fresh agents, not repeated reuse across unrelated implementation work.

- Woody leaves now have per-species width profiles and margins: tapered willow blades, broader shield forms, incised outlines, narrow cane blades, needles and distinct leaflets. Curved stems use explicit endpoints and continuous tangents; branch tiers/spokes have uneven attachment heights, reach and sweep. Recent Fenforge low roots, Banksorrow curtains, Kinkcane angular bamboo and Embercrown sinuous staggered crowns were preserved.
- Herb families receive bounded, seeded spacing/curvature variation while retaining upright reed, runner, rosette, fern and aquatic habits. The dedicated Moonfern, Shadebell and Glowbriar generators retain their defining anatomy with varied frond spread, deck attachment and leaf profiles.
- Cryptogams have curved shoots/forks, varied colony bearings, distinct profiles and broad unequal cap/cup shoulders. This does not replace the continuous production coverage base with isolated catalog shoots.

The initial full native review found important remaining identity regressions. Additional repairs restored overlapping Rainshield leaves, more divergent Moonbark stems and pendant shoots, Vaultfern's tapered base and arching fronds, Witchberry crown elevations, delicate Fenbead branches, pale airy Ashwire/Frosttussock blades, pink Widowspire spires, cool low Tidebell tubes, stout Barrowbole, squat Duskpuff, grounded Starsplit rays, Carrion Bell's shorter stem and Reekhorn's actual open lace veil.

Barbarrow's first separate-barb construction still looked crowded and oval in the actual render and was rejected. It now has nine long individual petioles bearing connected sagittate blades with a central notch and two real backward basal lobes, plus white three-petal flowers. This restores the open old silhouette instead of preserving the erroneous dense leaf count. The pitcher plant now uses individually proportioned curved red/cream vessels and lids attached to their curved rims; root rejected and corrected the first hood alignment inside the vessel.

## Shared geometry

`BasalLobeLength` produces connected backward basal lobes in lamina meshes. `RadialAsymmetry` introduces broad unequal cap/cup shoulders. `RadialLattice` removes real cells from both shell walls and bridges their thickness for the Reekhorn veil. Radial `MidribCurl` bends vessels; `LengthToWidth` supplies the curved radial aspect ratio so lateral displacement scales with physical length. Pitcher shapes have fixed per-species/per-slot dimensions, matching the renderer's species/shape-slot cache; stochastic placement remains in instance transforms.

No materials, light counts, emission strengths or new bioluminescent organs were added. Shape variation is anatomical geometry, not universal shader wobble.

## Evidence

All paths below are under `build/plant-graph-tuning-20261115/`.

- `organic-roster/`: all 125 species captured in the real Godot renderer at the shared identity scales. `organic-roster-review/`: paired old/current contact sheets covering 29 woody/climbers, 61 herbs and 35 cryptogams. Root reviewed cryptogams and fresh visual reviewers covered the woody/herb sheets, enlarging priority species.
- `organic-refined/`: 17 targeted follow-up species with foliage macros. `organic-pitcher/`: corrected curved-vessel/hood capture. `organic-tidebell/`: final lower tube placement. `identity-reviewed/index.html` combines current captures with the previous-renderer references; `capture_sources.json` retains per-image report and module provenance. Reused images are explicitly from earlier iteration binaries.
- `organic-final-validation.log/json`: latest 125-species numerical sweep, three identities, four ages, three LODs; zero errors and zero exact duplicate structural fingerprints.
- `organic-final-selftests.log`: 15 focused tests pass. New tests check backward arrow lobes and notch, real open lattice cells against a solid cup control, and finite curved-vessel geometry at every LOD.
- `organic-final-live/report.json`: actual population adapter fixture, 118 individuals spanning 106 species plus all 19 coverage prototypes; complete refined climber paths, node invalidation, growth/contact/birth/removal and warmed camera behavior. This fixture refines recorded support paths and does not establish natural growth past 96 original edges.
- `organic-final-boot.log`: native save/load boot receipt. Sequential solution/game build logs are `organic-final-build.log` and `organic-final-game.log`.

The initial variation pass retained mature organ counts for every species except the added Barbarrow barbs. The later identity repairs deliberately changed organ counts where the wrong generic anatomy was the problem: the true arrow leaf replaces overlapping oval parts, the lace veil replaces sticks, and sparse pink spires replace a generic bush. `organic-final-count-comparison.json` records those changes. Counts are not a proof of visual richness, and this art pass is not a performance optimization claim.

## Acceptance boundary

This is another completed full-roster review/repair cycle, not a claim that all 125 species have final photorealistic art approval. Contact sheets establish broad silhouettes; only selected forms received close macro inspection. Some juvenile timing, regular vein/mesh detail, foliage intersections and flower/material treatment remain approximate. The updated foliage needs owner playtesting in ecosystem context. Existing corpse and ecology rules remain in use. Native catalog exit still reports the historical Texture RID/ObjectDB warnings; captures complete successfully. No Radeon 860M or uncapped mature-world performance acceptance is claimed.
