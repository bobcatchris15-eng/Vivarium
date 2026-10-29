# Pilot tree structure rework — 2026-10-28

Implemented in the production `PilotTreeMeshes` generator. Direction: distinct, mildly fantastical species with internally consistent growth habits, using real tree structure as inspiration.

[Before/after contact sheet](../build/pilot-structure/comparison-20261028.png)

| Species | Structural treatment |
|---|---|
| Gloomspire | Tapered evergreen crown with broad drooping sprays, fewer overlapping tiers and visible lower bole. |
| Needlevault | Narrower foliage, stronger crown taper and a dominant upright leader with subordinate tips. |
| Crowncoil | Fewer fronds, coherent age-dependent arches, tapering pinnae and a hanging older skirt. Curvature is sampled once per frond instead of jittering at every rachis vertex. |
| Emberpillar | Three unequal replacement leaders, fewer high shelves, thicker load-bearing boughs and retained old lower stubs. |
| Basinwarden | Fewer stronger spreading arms, distal leaf-bearing branches, calmer leaf pitch and a bole terminating within the crown. |
| Palehollow | More exposed dead framework, reduced surviving crown and narrow drooping foliage around the retained broken snag. |
| Cisternvault | Unequal crooked boughs emerging at varied heights, spaced leaves along terminal branchlets and hanging capsules. |
| Cinnabarcrest | Tapered bole ending at its main fork, unequal repeated forks and radial terminal rosettes. |
| Rootcloister | Broader lateral leaf canopy above irregular aerial props with bends and thickened feet. Bole ends within the crown. |
| Silkcascade | Sweeping boughs supporting more variable pendant axes, each with an outward shoulder and hanging tip. Bole ends at the crown. |

Shared correction: secondary branches, foliage and ornaments now sample the actual woody polylines instead of chords between curved branch endpoints. Main boughs have modest lateral sweep, a short attachment collar, and continuous taper. Uneven primary azimuths replace the common evenly spaced fan in the newer species.

Authoritative ground-root samples, ecological descriptors, persistent species IDs and save schema are unchanged. Existing saves reconstruct the revised presentation. The clipped specimen boundary presentation is retained.

## Verification

- `dotnet build Vivarium.sln --no-restore -v:q`: passed.
- `dotnet build game/Vivarium.csproj --no-restore -v:q`: passed; existing warnings in LoamGroundReview, SmokeRunner and GuardianSkinContact.
- Focused pilot classes: 146 tests, 144 passed, 2 failed. All 8 mesh tests and 11 radius-query tests passed, including ten deterministic finite distinct forms, roots across all six corners, and exact root/static recombination.
- The two failures are `GenuineSchemaTwoSaveMigratesWithoutAddingTree` (invalid legacy ID) and `PilotWoodSupportsLogHostsAndShrubsHaveNoCarryingCap` (16 versus expected int.MaxValue). Both reproduce in an untouched archive of HEAD under `build/pilot-structure/baseline-tests`.
- An initial broader name filter also selected the existing guardian test expecting six associations; that failure likewise reproduced in untouched HEAD. It is unrelated to the geometry change.
- Native Forward+ review uses production PilotTreeRenderer, IslandRenderer and EnvironmentRig, isolated user data and exclusive GPU lease. Existing Godot editors are preserved. The review checks unchanged world digest and renderer replacement/root refresh contracts for every species.
- Sharp baseline: `build/pilot-structure/baseline-sharp-20261028`. Geometry-final capture: `build/pilot-structure/final-20261028`. Final runner metadata correction: `build/pilot-structure/verified-20261028`. Each complete run contains 52 images, camera poses, triangle counts, source/binary hashes and a run receipt. See the verified run receipt for terminal success.
- The comparison sheet uses baseline-sharp and geometry-final images; the verified run changes only the review metadata, not tree geometry. Images use identical lighting and view directions, with each tree automatically fitted to its bounds. This is a structural comparison, not a pixel-matched material/performance experiment.
- No populated gameplay or FPS acceptance is claimed. The review omits flora and fauna. Final subjective art acceptance remains with the user.

## Native tree triangles

Counts below exclude the separate authoritative root mesh. These are uploaded mesh counts, not a performance benchmark. Most large crowns became lighter; Rootcloister and Silkcascade gained foliage to support their changed silhouettes.

| Species | Before | Reworked |
|---|---:|---:|
| gloomspire | 169,228 | 100,596 |
| needlevault | 116,106 | 76,628 |
| crowncoil | 2,025,680 | 1,475,564 |
| emberpillar | 125,796 | 85,554 |
| basinwarden | 1,729,208 | 1,157,266 |
| palehollow | 179,572 | 191,664 |
| cisternvault | 181,572 | 181,538 |
| cinnabarcrest | 195,896 | 195,924 |
| rootcloister | 287,352 | 582,238 |
| silkcascade | 222,092 | 380,546 |

The old review's 180k ceiling predated the existing solid leaf geometry (the unchanged Crowncoil baseline already exceeds two million triangles). The review now uses the current 2.6-million ceiling from PilotTreeMeshTests and checks all mesh surfaces when fitting foliage bounds. All ten species now use valid alternate corner indices.

Repeat after sequential solution and game builds:

```powershell
pwsh -NoProfile -File scripts/visuals/pilot-structure-review.ps1 -Name unique-run-name
```
