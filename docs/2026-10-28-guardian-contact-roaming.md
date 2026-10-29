# Guardian contact and roaming — 2026-10-28

## Changes

- Guardian plant bending is submitted from evaluated skinned triangles at the end of the appendage modifier. Bone spheres no longer request plant bends.
- Conservative per-bind bounds reject groups of 96 guardian triangles; only candidate groups are skinned. Actual triangle/triangle intersection against a cached plant hierarchy is required before bending.
- Triangle tests include coplanar separation and triangle interiors. Bounds alone cannot move a plant. The older sphere query remains for diagnostic fixtures and now also checks the actual plant surface.
- Satisfied guardians select a dry destination across the island, retain it for the journey, and select another on arrival. Need thresholds interrupt exploration. Destinations reject the island boundary, trunk and water deeper than 8 cm. Existing trunk routing and authored walking speed remain.
- Drinking and bathing no longer satisfy needs while the animal is still walking toward the destination.

## Verification

- `dotnet build Vivarium.sln`: passed, zero warnings/errors.
- `dotnet build game/Vivarium.csproj`: passed, three warnings in existing LoamGroundReview, SmokeRunner and unused MeshScan.Used code.
- Guardian tests: 62 passed when excluding the existing CatalogMapsAllSixPilotTreesToOneToOneGuardians assertion. That test expects six pilot-tree entries; the current catalog has ten. The full guardian run was not a clean pass.
- Added all-archetype tests for destination retention, immediate selection after arrival, and hunger interruption. Updated the isolated stride fixture to satisfy needs so new intentional interruptions do not replace its walking action.
- Native Compatibility-renderer fixture: 24 checks; 6,144 spatial comparison cases; 4,088 contact cases; 5 draw audits; maximum comparison error zero; 72 captured frames. Includes the production triangle-contact entry point, empty gaps, triangle interiors, coplanar separation/containment, and scaled/rotated plant geometry.
- Evidence: `build/guardian-contact-roaming-20261028/plant-surface-proof.json`, `plant-contact-motion.json`, PNGs and engine logs. This fixture is geometry evidence, not Forward+ populated-scene or performance acceptance.

## Remaining limits

The response bends an individual MultiMesh instance about its root. A mesh combining several stems still moves as a unit; per-stem deformation is not implemented. Contact uses CPU skeletal triangles and plant instance transforms, excluding tiny shader displacement, blend shapes and unskinned guardian decorations. Spatial registration retains its existing 128-instance nearby cache. This work does not establish complete contact coverage or matched frame-time performance.

The user's live editor and running game were left open. Unrelated pilot-tree edits were preserved. The running game must load the rebuilt assembly in a new run to pick up these changes.

## Climbing follow-up and final build gate

The user added climbing during this task, then requested build-only verification and will restart the game to playtest. No further tests or native reviews were run after that request.

- Existing species capabilities are honored: coati, porcupine, hoatzin and boa can climb; monitor and pangolin remain ground travelers.
- GuardianClimbing samples continuous woody surface routes from the shared procedural tree/plant geometry. It excludes leaf spans, rejects gaps and checks a small lateral support width. The first pass covers lower trunk/stem routes (pilot tree up to 3.5 m; shrubs up to 2.5 m), not a canopy branch navigation network.
- Upright flora growth transforms and variant counts are shared with rendering through FloraGeometryPlacement so shrub routes follow the same seeded shape, growth scale, yaw and lean.
- Routes, progress, return direction and support normal are serialized with the guardian. Animals approach on foot, climb with their body aligned to the support, and retrace their route to descend. Ground-height limb/tail modifiers are bypassed during climbing; available climb clips are preferred.
- A shrub being used as a climbing support is exempted from the soft brush bend response while occupied.
- The final renderer body-avoidance calls use stem projection only; they cannot request bends from bone spheres.
- Final `dotnet build Vivarium.sln`: success, zero warnings/errors.
- Final `dotnet build game/Vivarium.csproj`: success, three existing warnings, zero errors.
- Prior 62-test and contact-fixture results above predate the climbing follow-up and final sphere-trigger removal. They are bounded earlier evidence, not final-version runtime acceptance.
- The separate populated Forward+ review timed out during flora loading at the four-minute limit. It produced no completed populated-motion receipt. Its owned processes exited; the user's game/editor were preserved. Climbing appearance, contact coverage and frame-time cost await the user's playtest.
