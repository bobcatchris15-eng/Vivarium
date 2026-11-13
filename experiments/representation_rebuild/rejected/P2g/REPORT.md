# P2g diagnostic, 2026-11-13

The interrupted growth-form implementation is not ready for acceptance. The catalog currently maps five forms; unsupported shapes retain legacy rendering. The 15 hard-coded test presets are a sample, not a full content-catalog test.

An isolated build and explicit execution were used:

```
dotnet build experiments/representation_rebuild/selftest --artifacts-path build/selftest-plants --nologo -v quiet
dotnet build/selftest-plants/bin/ReprSelfTest/debug/ReprSelfTest.dll plant
```

Build succeeded. The test reports 13 tests, 3 failures: distinctness (moonfern and swanfrond crown ratios), lifelike (glowbriar tip droop), and sanity (thornmantle height). Attachment now covers 100 growth ages for all 15 presets and passes, as do compiler, determinism and monotone growth checks. A zero tangent at the precise emergence instant has no direction and is excluded from the tangent-angle comparison.

The footprint output shows severe area shortfalls: glowbriar LAI 0.22 versus 2.5 target, moonbark 0.06 versus 1.5, and mirespire rounds to 0.00 versus 2.4. Removing the leaf-count cap avoids deliberately suppressing organs but does not solve these deficits; thornmantle rose from 438 to 461 leaf instances and LAI from 0.23 to 0.24 versus 2.6. Proper branch/foliage topology and a needle representation need further work, with measured cost and visual evaluation. Enlarging individual leaves to meet the number would not establish a believable crown.

Windowed Godot capture on Radeon 860M completed at `build/overhaul/P2g/capture/plant_forms` (48 images, report JSON); the capture was before the later demo-spacing and cap changes. `glowbriar_row_main.png` shows sparse generated crowns and contamination by neighboring species; `moonfern_close_main.png` shows oversized overlapping pinnae. Demo rows were subsequently moved apart but that change has not been rendered. Exit reported seven texture RID leaks and one ObjectDB leak. No comparative performance or visual acceptance is claimed.

The production demo, full mapped catalog and growth stages still need validation, and the remaining growth forms remain unimplemented. The three existing failures are not relaxed or hidden by this work.

## Candidate: more topology and smoother LOD

Six evenly spaced leaf sites per shrub structural internode, trait-sized shrub/fern leaves, and more LOD outline segments were tried. Explicit isolated selftest still reported three failures. For the measured thornmantle individual the descriptor increased from 1,916 to 6,647 organs and detail-0 triangles from 40.9k to 158.1k; another individual exceeded the existing organ-count sanity check at 11,394. Glowbriar LAI increased from 0.22 to 0.36, still well below 2.5. `FlatArea` estimates area as 0.7 times a length-width rectangle; the rendered outline and curvature vary, so this is not an exact measurement of mesh area.

The six targeted images are under `build/overhaul/P2g/candidate/plant_forms`: the glowbriar and shadebell crowns remain clumped and angular, and moonfern still reads as a crystalline slab. These are not acceptable at the increased geometry cost. This is a rejected candidate; diagnostic images do not constitute a controlled performance comparison. Godot exited successfully with the same seven texture RID and one ObjectDB leak warnings.
