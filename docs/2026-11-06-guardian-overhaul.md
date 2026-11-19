# 2026-11-06 — Autonomous Guardian Animal Overhaul (Working Record)

## 1. Mission & Scope
Transform all 10 guardian species in Vivarium into convincing, autonomous living organisms across six interacting dimensions:
1. **Visual Credibility:** Anatomical coherence, proportions, mesh/rig/material quality, shading, eye/fur/keratin/skin/plumage fidelity, individual seed-driven variation.
2. **Locomotion Credibility:** Species-appropriate gaits, limb coordination, IK foot placement & ground contact, terrain/slope/root/prop adaptation, body posture, smooth acceleration/deceleration/turning, velocity-coupled gait cadence, weight transfer, breathing, head/neck/tail secondary motion.
3. **Behavioral Credibility:** Internal needs/motivations, circadian rest/activity cycles (`NocturnalBias`), habitat use, foraging/feeding on actual ecological diet types, curiosity/investigation, alert/poke responses, behavioral persistence, natural idle pauses/hesitations.
4. **Physical Credibility:** Anatomical body/limb/tail collision and ground/root/rock/log/water/vegetation contact without floating, sinking, foot sliding, or trunk/boulder clipping.
5. **Ecological Credibility:** Dietary interactions with flora/fauna/fungi/bark/seeds/biofilm/detritus, seed dispersal, nutrient cycling, substrate disturbance, UI/Selection/Inspector/Poke integration.
6. **Temporal & Performance Credibility:** Remains believable over extended observation; maintains **>=30 FPS target on GTX 1080** in Forward+ `1600x900` `Quality = 1`; deterministic save/load roundtrip schema 5 with 0 invariant violations.

---

## 2. Complete Guardian Species & GLB Rig Audit (`GuardianCatalog.All`)

| # | Species ID | Archetype | Inspiration | Locomotion Mode | Mass (kg) | GLB Bone Hierarchy Summary | GLB Animations |
|---|---|---|---|---|---|---|---|
| 1 | `hoatzin_crested` | `crested_canopy` | Hoatzin | `AvianPerchHop` | 1.2 | `Root -> Body -> Chest -> Neck -> Head -> Crest / Jaw / Lid*`, `Wing_L/R -> Flight_L/R`, `HindUpper_L/R -> HindLower_L/R -> HindFoot_L/R` (parent: `Root`), `Tail0..Tail3` | `feed, idle, rest, walk` |
| 2 | `pangolin_armored` | `armored_forest` | Pangolin | `TerrestrialQuadruped` | 12.0 | `Root -> Body3 (pelvis) -> Body2 -> Body1 -> Body0 (chest) -> Neck -> Head -> Tongue / Blink*`, `FLUpper -> FLLower -> FLFoot` (and `FR`), `HLUpper -> HLLower -> HLFoot` (and `HR`), `Tail0..Tail7` | `idle, walk, feed, rest` |
| 3 | `serpent_canopy` | `canopy_serpent` | Tree Boa | `Serpentine` | 6.5 | `Root`, `Body00..Body32` (33 axial bones parented to `Root`), `Head -> Tongue`, `Jaw` (parented to `Root`) | `idle, walk, slither, feed, rest` |
| 4 | `monitor_great` | `great_ground` | Water Monitor | `TerrestrialQuadruped` | 25.0 | `Root -> Pelvis -> Spine1 -> Chest -> Neck -> Head -> Jaw / Tongue / Blink*`, `UpperArm_L/R -> Forearm_L/R -> Hand_L/R`, `Thigh_L/R -> Shin_L/R -> Foot_L/R`, `Tail1..Tail16` | `feed, idle, rest, walk` |
| 5 | `coati_woodland` | `woodland_omnivore` | Coati | `TerrestrialQuadruped` | 5.5 | `Root -> Pelvis -> Spine1 -> Chest -> Neck -> Head -> Snout / Jaw / Ear_L/R / Blink*`, `UpperArm_L/R -> Forearm_L/R -> Hand_L/R`, `Thigh_L/R -> Shin_L/R -> Foot_L/R`, `Tail1..Tail7` | `feed, idle, rest, walk` |
| 6 | `porcupine_quilled` | `quilled_arboreal` | Brazilian Porcupine | `ArborealClimber` | 4.8 | `Root -> Body -> Chest / Neck / Lumbar`, `Chest -> Head -> Jaw / Lid*`, `FrontUpper_L/R -> FrontLower_L/R -> FrontFoot_L/R`, `HindUpper_L/R -> HindLower_L/R -> HindFoot_L/R`, `Tail1..Tail7` | `feed, idle, rest, walk` |
| 7 | `thylacine_fernback` | `fernback` | Thylacine | `TerrestrialQuadruped` | 22.0 | `Root -> Body3 -> Body2 -> Body1 -> Body0 -> Neck -> Head -> Snout / Jaw / Ear_L/R`, `UpperArm_L/R -> Forearm_L/R -> Carpus_L/R -> Hand_L/R`, `Thigh_L/R -> Shin_L/R -> Hock_L/R -> Foot_L/R`, `Tail0..Tail3` | `feed, idle, rest, walk` |
| 8 | `saiga_dewhorn` | `dewhorn` | Saiga Antelope | `TerrestrialQuadruped` | 30.0 | `Root -> Pelvis -> Spine1 -> Chest -> Neck -> Head -> Snout -> SnoutTip`, `Ear_L/R`, `Horn_L/R`, `Jaw`, `Blink*`, `UpperArm_L/R -> Forearm_L/R -> Hand_L/R`, `Thigh_L/R -> Shin_L/R -> Foot_L/R`, `Tail1..Tail4` | `feed, idle, investigate_sniff, play, rest, walk` |
| 9 | `shoebill_hushbill` | `hushbill` | Shoebill Stork | `AvianPerchHop` | 5.5 | `Root -> Chest -> Neck -> Head`, `Wing_L/R`, `Thigh_L/R -> Shin_L/R -> Foot_L/R` (parent: `Root`) | `idle, walk, feed, rest, bow, look, alert` |
| 10 | `matamata_ruffleback` | `ruffleback` | Mata-mata Turtle | `TerrestrialQuadruped` | 12.0 | `Body`, `Fore_L/R -> ForeFoot_L/R`, `Hind_L/R -> HindFoot_L/R`, `Neck -> Head`, `Tail` | `idle, walk, feed, rest` |

---

## 3. Root-Cause Diagnoses (Code + Asset Verified)

### A. Skeleton, IK, and Contact Pipeline (`game/Render/`)
1. **Hardcoded 6-Archetype Gate in `GuardianRenderer.cs:250`:**
   - `fernback`, `dewhorn`, `hushbill`, and `ruffleback` are excluded from `GuardianTerrainLegModifier`, `GuardianAppendageModifier`, `GuardianVegetationContact`, `GuardianSkinContact`, and trunk avoidance.
2. **Broken Bone Resolution in `GuardianTerrainLegModifier.cs`:**
   - `LegName` only checks `UpperArm_L/FLUpper` and `Thigh_L/HLUpper`.
   - Fails completely (`_resolved = false`) on:
     - `crested_canopy` (biped: `HindUpper_L/R`, `HindLower_L/R`, `HindFoot_L/R`, no forelegs).
     - `hushbill` (biped: `Thigh_L/R`, `Shin_L/R`, `Foot_L/R`, no forelegs).
     - `quilled_arboreal` (quadruped: `FrontUpper_L/R -> FrontLower_L/R -> FrontFoot_L/R` and `HindUpper_L/R -> HindLower_L/R -> HindFoot_L/R`).
     - `fernback` (4-segment limbs: `UpperArm -> Forearm -> Carpus -> Hand` and `Thigh -> Shin -> Hock -> Foot`; 2-bone IK needs `UpperArm -> Forearm -> Carpus` and `Thigh -> Shin -> Hock`, with distal `Hand`/`Foot` aligned to ground).
     - `ruffleback` (2-segment sprawling limbs: `Fore_L/R -> ForeFoot_L/R` and `Hind_L/R -> HindFoot_L/R`).
   - Result: Only **3 of 10** species (`woodland_omnivore`, `great_ground`, `armored_forest`) currently had functioning leg IK!
3. **Tail & Serpentine Chain Defects in `GuardianAppendageModifier.cs`:**
   - `ResolveTailChain()` starts at `Tail1` and skips `Tail0` (used by `armored_forest`, `crested_canopy`, `fernback`) and single-bone `Tail` (`ruffleback`).
   - On `canopy_serpent`, `Head` and `Jaw` are sibling children of `Root` (not children of `Body00`), so when `Body00` is displaced by terrain/root/obstacle constraints in `GuardianAppendageModifier`, `Head` and `Jaw` do not inherit `Body00`'s delta position/rotation!
4. **Breathing, Head Tracker & Secondary Motion Bone Mismatches:**
   - `GuardianBreathingModifier` looks for `Chest`, `Spine1`, `Pelvis`, `Root`. On `armored_forest` and `fernback`, the torso bones are `Body0..Body3` (`Body1`/`Body2` is the ribcage); falling back to `Root` bobs the entire creature including its planted feet! On `ruffleback`, root is `Body`.
   - `GuardianSecondaryMotion` misses `Horn_L/R` (`dewhorn`), `Wing_L/R` & `Flight_L/R` (`crested_canopy`, `hushbill`), `Tongue` (`armored_forest`, `canopy_serpent`, `great_ground`), `Snout`/`SnoutTip` (`dewhorn`, `fernback`, `woodland_omnivore`).

### B. Locomotion, Kinematics & Gait Coupling (`GuardianSystem.cs` & `GuardianRenderer.cs`)
1. **Instant Velocity & Moonwalking While Turning (`GuardianSystem.cs:488-538`):**
   - Guardians move at full speed instantly even when facing 180 deg away from their target while rotating at a fixed `2.5 rad/s`, causing sideways sliding/moonwalking.
   - No acceleration/deceleration curve, no turn-in-place threshold, no slope effort slowdown, and no water-wading drag.
2. **Uncoupled Animation Speed (`GuardianRenderer.cs:889`):**
   - `UpdateAnimationState` sets `strideScale = 1f / g.Scale` regardless of actual horizontal displacement speed (`CurrentSpeed`), causing foot sliding during acceleration, deceleration, slow stalking, or slope climbing.
3. **Obstacle Blindness in `ExecuteLocomotion` (`GuardianSystem.cs:506`):**
   - Only checks `_world.PilotTree.BlocksTrunk(nextXZ, 0.35)`. Completely ignores `_world.Props.Rocks`, `_world.Props.Logs`, steep cliff drop-offs, and deep water for non-swimmers (`!profile.CanSwim`).

### C. Behavioral Architecture & Ecological Integration (`src/Vivarium.Sim/Guardians/`)
1. **Six Activities Dead in `GuardianSystem.SelectNextActivity`:**
   - `Investigate`, `Dig`, `ScentMark`, `Alert`, `Yawn`, and `Retreat` exist in `GuardianActivity` and have execution/animation handlers, but are **never selected** in `SelectNextActivity`!
2. **`NocturnalBias` Unused:**
   - `GuardianBehaviorProfile.NocturnalBias` is defined in `GuardianCatalog` for all 10 species, but never referenced in `GuardianNeeds` or `GuardianSystem`.
3. **Ecological Diets Unimplemented (`FindRichestFoodCell` & `ExecuteFeeding`):**
   - Only `FruitMass` and `FineMass` litter are checked and consumed.
   - Carnivores/pisicivores (`canopy_serpent`, `great_ground`, `fernback`, `hushbill`, `ruffleback` with `fauna:small`), browsers/grazers (`crested_canopy`, `dewhorn` with `flora:foliage`), bark/fungus feeders (`quilled_arboreal` with `flora:bark`, `flora:fungus`), and biofilm feeders (`armored_forest`, `ruffleback` with `biofilm`) cannot locate or feed on their actual ecological resources!
4. **No Selection, Focus, Inspector, or Poke Integration:**
   - `Selection.Raycast` does not test `world.Guardian`, `Inspector.cs` has no guardian readout, and `ToolActions.Poke` ignores the guardian.
   - `VivariumWorld.CheckInvariants()` does not validate `Guardian`.

---

## 4. Shared Architectural Contracts & Work Packages

### Package 1: Simulation Locomotion, Navigation, Kinematics & Habitat Awareness (`src/Vivarium.Sim/Guardians/`)
- Add runtime kinematic state on `GuardianIndividual` (`CurrentSpeed`, `VerticalVelocity`, `TurnRate`, `GaitPhase`, `LastStepPos`, `TargetEntityId`, `AlertSourcePos`, `RecentVisitedTargets` ring buffer) — keep non-breaking defaults for schema-5 persistence.
- Upgrade `ExecuteLocomotion` in `GuardianSystem.cs`:
  - Smooth acceleration and approach deceleration.
  - Heading alignment factor: slow forward translation when heading error > 45 deg so the guardian turns naturally rather than sliding sideways.
  - Slope and wading speed modulation.
  - Local obstacle avoidance (steering feelers around `PilotTree.BlocksTrunk`, `Props.Rocks`, `Props.Logs`, domain boundary, cliff slopes, and deep water when `!profile.CanSwim`).
  - Species-specific gait cadence & hop arc (`AvianPerchHop` for `crested_canopy` and slow stalking stride for `hushbill`).
- Upgrade `GuardianHabitat.cs` to index rocks/logs for basking (`GreatGround`, `Ruffleback`, `CanopySerpent`), shallow water / shoreline wading points (`Hushbill`, `Ruffleback`, `GreatGround`), and scent-mark / scratch landmarks.

### Package 2: Ecological Diets, Circadian Needs & Full Utility Behavior Engine (`src/Vivarium.Sim/Guardians/`)
- Wire `NocturnalBias` into `GuardianNeeds.Tick` and `GuardianSystem.SelectNextActivity` using `world.Clock` / solar elevation so diurnal species (`crested_canopy`, `great_ground`, `woodland_omnivore`, `dewhorn`, `hushbill`) are active by day and drowsy at night, while nocturnal/crepuscular species (`armored_forest`, `canopy_serpent`, `quilled_arboreal`, `fernback`, `ruffleback`) stir at dusk/night and seek shaded rest sites during midday.
- Implement full diet foraging (`FindBestForageTarget` & `ExecuteFeeding`):
  - `fauna:small`: stalk and strike nearby `FaunaIndividual` (detritivores/pollinators/herbivores), consuming biomass and recycling nutrients to soil.
  - `flora:foliage`: browse mature herbaceous/shrub flora without killing healthy plants (pruning small biomass & dispersing seeds).
  - `flora:bark` & `flora:fungus`: gnaw woody shrub/tree bark or fungal fruiting bodies.
  - `biofilm` & `detritus`: forage along wet shoreline / submerged cells (`Water.OpenWaterDepth`) and decomposing logs.
  - `fruit` & `seeds`: consume fruit litter and queue viable seeds for gut-passage dispersal (`PendingSeeds`).
- Activate all 6 missing activities in `SelectNextActivity` with clear ecological triggers:
  - `Investigate`: triggered by curiosity toward nearby fauna, newly fruited flora, or unfamiliar landmarks.
  - `Dig`: triggered for `armored_forest`, `woodland_omnivore`, `fernback`, `great_ground` when foraging for soil grubs/roots/detritus, disturbing soil and cycling nutrients.
  - `ScentMark`: territorial marking at `ScentMarkTargets` (root buttresses, boulders, logs) when `TerritoryDrive` is high.
  - `Alert` & `Retreat`: triggered by player `Poke`, sudden proximity, or high alertness; `Hushbill` performs threat/alert display, shy species (`dewhorn`, `crested_canopy`, `quilled_arboreal`) retreat to cover.
  - `Yawn`: natural transition between `WakeUp` / `Stretch` / `Settle` and `Idle`.

### Package 3: All-10-Species Skeleton Modifiers, Biped/Quadruped/Sprawling IK, Tail/Serpent Contact & Animation (`game/Render/`)
- Remove the hardcoded 6-species gate in `GuardianRenderer.cs:250` so **all 10 species** get full `LegContact`, `AppendageContact`, `VegetationContact`, `SkinContact`, and trunk avoidance.
- Upgrade `GuardianTerrainLegModifier.cs` to support:
  - **Standard Quadrupeds (3-bone):** `woodland_omnivore`, `great_ground`, `dewhorn` (`UpperArm/Forearm/Hand`, `Thigh/Shin/Foot`), `armored_forest` (`FLUpper/FLLower/FLFoot`, `HLUpper/HLLower/HLFoot`), `quilled_arboreal` (`FrontUpper/FrontLower/FrontFoot`, `HindUpper/HindLower/HindFoot`).
  - **4-Bone Digitigrade Quadruped (`fernback`):** `UpperArm -> Forearm -> Carpus` (+ distal `Hand` ground alignment) and `Thigh -> Shin -> Hock` (+ distal `Foot` ground alignment).
  - **Avian Bipeds (`crested_canopy`, `hushbill`):** 2-leg mode (`LegCount = 2` active hind legs: `HindUpper/HindLower/HindFoot` on `crested_canopy`, `Thigh/Shin/Foot` on `hushbill`), with avian center-of-mass balance and toe/foot ground adaptation.
  - **Sprawling Chelonia (`ruffleback`):** 2-bone limb mode (`Fore_L/R -> ForeFoot_L/R`, `Hind_L/R -> HindFoot_L/R`) with low carapace clearance and shoreline/bottom contact.
- Upgrade `GuardianAppendageModifier.cs`:
  - Support `Tail0` start (`armored_forest`, `crested_canopy`, `fernback`), `Tail1` start (`dewhorn`, `great_ground`, `quilled_arboreal`, `woodland_omnivore`), and single-bone `Tail` (`ruffleback`).
  - On `canopy_serpent`, propagate `Body00`'s delta transform to `Head` and `Jaw` so the serpent's head never detaches from its neck when undulating over roots/rocks!
- Upgrade `GuardianBreathingModifier.cs`, `GuardianHeadTracker.cs`, and `GuardianSecondaryMotion.cs` for all 10 skeletons (ribcage breathing on `Body1/Body2` when `Chest` is absent; wing/crest/horn/ear/tongue/jaw/snout secondary motion; species-specific clip mapping such as `dewhorn` `investigate_sniff`/`play` and `hushbill` `bow`/`look`/`alert`).
- Couple walk/run/slither animation playback speed in `GuardianRenderer.UpdateAnimationState` to `g.CurrentSpeed` and stride length so foot movement matches ground translation without sliding.

### Package 4: Visual/Material Realism, Seed Variation, World Invariants & UI/Tool Integration (`game/Render/`, `game/Shaders/`, `src/Vivarium.Sim/Tools/`, `game/UI/`)
- Apply deterministic individual morphological/color variation from `g.Seed` (`VisualVariant`, subtle coat/plumage/scale tint, roughness & pattern shift) in `GuardianRenderer.cs` and `guardian_surface.gdshader`.
- Verify material mode mapping (`ModeFor`) across all 10 GLB surface names (including `fernback`'s 41 mesh primitives, `dewhorn`'s amber horn/coat, `hushbill`'s plumage/horn/leg skin, `ruffleback`'s carapace/mottled skin).
- Add `HitKind.Guardian` to `Selection.cs`, wire `ToolActions.Poke` to startle/alert the guardian, support clicking/focusing (`F`) the guardian in `ToolController.cs`, and display rich live guardian physiology, diet, activity, and ecological impact in `Inspector.cs`.
- Add guardian position/NaN/domain validation to `VivariumWorld.CheckInvariants()`.

---

## 5. Implementation & Runtime Verification Results (`--boot-test` & `--guardian-review`)

### A. Root-Cause Runtime Defects Discovered & Fixed During Live Engine Verification
1. **Why All 10 Guardians Pitched Their Heads 60°–90° Into the Ground in Baseline (`GuardianHeadTracker.cs`):**
   - In `GuardianRenderer.Build`, `_instance` (parent of `_skeleton`) is rotated by `new Basis(Vector3.Right, Mathf.Pi / 2)` (`+90°` around X) to convert Blender's glTF export axes to Y-up.
   - Consequently, in `skeleton.GlobalTransform.Basis`, `Basis.Z` points **World Up (`+Y`)** and `-Basis.Y` points **World Forward (`-Z`)**.
   - `GuardianHeadTracker` previously used `bodyForward = -skelTransform.Basis.Z.Normalized()` and `bodyUp = skelTransform.Basis.Y.Normalized()`, which treated World Down as Forward and World Back as Up, pitching every guardian's head/neck 45°–75° straight into the dirt whenever `TargetLookPoint` was active, and detaching `canopy_serpent`'s `Head` from its sibling `Jaw` bone.
   - **Fixed** in `GuardianHeadTracker.cs` (`bodyRight = skelTransform.Basis.X.Normalized()`, `bodyForward = -skelTransform.Basis.Y.Normalized()`, `bodyUp = skelTransform.Basis.Z.Normalized()`, eye-height look offset, and skipping `HeadTracker` on `ContinuousGroundedBody` rigs where `GuardianAppendageModifier` drives `Head`/`Jaw`).
2. **Why Corneal Eyes (`mode == 4`) Rendered as Solid Tan Spheres or Oversized Dark Discs (`guardian_surface.gdshader` & `GuardianRenderer.cs`):**
   - In `guardian_surface.gdshader`, `else if (vtx_luma < 0.92)` previously treated mid-tone vertex colors (`vtx_luma` in `0.2..0.85`) as vertex-painted eyes with `pupil_mask = 1.0` everywhere, producing a solid tan ball with zero pupil; and `if (vtx_luma < 0.12) r = min(r, vtx_luma * 1.8)` forced the entire UV-mapped cornea on dark-vertex eye meshes (`dewhorn`, `fernback`, `hushbill`, `woodland_omnivore`, `great_ground`) into the pupil radius (`r < pupil_radius`).
   - **Fixed** in `guardian_surface.gdshader` and `GuardianRenderer.ResolveGuardianBaseColor`: corneal/iris surfaces now use seamless UV radial mapping with Snell tear-film refraction, radial iris fibers, limbal darkening, daylight-responsive pupil dilation, and canopy sky catchlights.
3. **Why `canopy_serpent` Bridged in Midair Across Sloped Ground (`GuardianAppendageModifier.cs`):**
   - `GuardianAppendageModifier` pinned `_points[0]` (`Body00` at the head) while elevated tail points were pushed upward by terrain, and distance + chord constraints pulled intermediate segments into a straight line in midair because `point.Y = Math.Max(point.Y, Ground)` only pushed upward.
   - **Fixed** in `GuardianAppendageModifier.cs` by relaxing floating `ContinuousGroundedBody` points toward `goals[i].Y` (`Mathf.Lerp(point.Y, goals[i].Y, 0.35f)`) before the ground floor clamp.

### B. 10-Species Verification Telemetry (`logs/guardian_after_v2/guardian_review.json`)

| Archetype | Species ID | Locomotion Mode | Leg IK | Foot Skin Samples | Max Sole Pen (m) | Max Skin Pen (m) | Max Stance Gap (m) | Appendage Contact | Skin Contact Valid | Median FPS (GTX 1080) |
|---|---|---|---|---|---|---|---|---|---|---|
| `crested_canopy` | `hoatzin_crested` | `AvianPerchHop` | `true` | 186 | `0.0023` | `0.0000` | `0.0011` | `true` | `true` (426,734 verts) | **51.3 FPS** |
| `armored_forest` | `pangolin_armored` | `TerrestrialQuadruped` | `true` | 433 | `0.0065` | `0.0000` | `0.0006` | `true` | `true` (140,019 verts) | **60.3 FPS** |
| `canopy_serpent` | `serpent_canopy` | `Serpentine` | `false` (axial) | 0 | `0.0000` | `0.0000` | `0.0000` | `true` (33 axial) | `true` (24,449 verts) | **51.1 FPS** |
| `great_ground` | `monitor_great` | `TerrestrialQuadruped` | `true` | 418 | `0.0398` | `0.0000` | `0.0011` | `true` | `true` (68,611 verts) | **54.7 FPS** |
| `woodland_omnivore` | `coati_woodland` | `TerrestrialQuadruped` | `true` | 404 | `0.0163` | `0.0000` | `0.0025` | `true` | `true` (98,897 verts) | **56.0 FPS** |
| `quilled_arboreal` | `porcupine_quilled` | `ArborealClimber` | `true` | 438 | `0.0055` | `0.0000` | `0.0015` | `true` | `true` (238,343 verts) | **67.9 FPS** |
| `fernback` | `thylacine_fernback` | `TerrestrialQuadruped` | `true` (4-seg) | 460 | `0.0213` | `0.0000` | `0.0021` | `true` | `true` (253,919 verts) | **60.5 FPS** |
| `dewhorn` | `saiga_dewhorn` | `TerrestrialQuadruped` | `true` | 458 | `0.0005` | `0.0000` | `0.0015` | `true` | `true` (89,785 verts) | **54.4 FPS** |
| `hushbill` | `shoebill_hushbill` | `AvianPerchHop` | `true` (biped) | 123 | `0.0000` | `0.0000` | `0.0008` | `true` | `true` (253,967 verts) | **50.8 FPS** |
| `ruffleback` | `matamata_ruffleback` | `TerrestrialQuadruped` | `true` (2-seg) | 326 | `0.0065` | `0.0000` | `0.0013` | `true` (1-bone) | `true` (59,459 verts) | **56.8 FPS** |

- **Boot & Save/Load Invariants (`--boot-test`):** `VIVARIUM_BOOT version=0.1.4 schema=5 ticks=33 flora=153 fauna=77 save=True load=True roundtrip=True invariants=0` (`VIVARIUM_BOOT_OK`).
- **Full-Island Visual & Performance Review (`logs/after_visual_review/review.json` on GTX 1080, Forward+ `1600x900` `Quality = 1`):**
  - `overview`: **112.89 FPS** (`8.858 ms` median)
  - `foliage_terrain`: **36.47 FPS** (`27.419 ms` median, `28.936 ms` p95 — comfortably exceeding the `>=30 FPS` GTX 1080 target)
  - `grazing_transition`: **35.23 FPS** (`28.382 ms` median, `29.258 ms` p95 — comfortably exceeding the `>=30 FPS` GTX 1080 target)
