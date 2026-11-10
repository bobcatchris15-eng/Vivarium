# Audit: fauna and guardian creatures

Read-only audit. No source edited, no Godot or dotnet run. Items marked **[RENDER]** are inferred from code and need a capture or running sim to confirm (section 6). Paths are relative to `C:\Misc\Vivarium`.

Facts that change how this audit can be validated:
- The repo was "reduced to runnable source" (commit 9650e95). `tests/Vivarium.Sim.Tests` holds only `bin/obj`, and `scripts/` does not exist, so `verify.ps1`, `compare-reference.ps1` and the xUnit suites cited in CLAUDE.md are not present.
- `--specimen-review` is FLORA ONLY: `game/App/SpecimenReviewRunner.cs:62` sets `session.Fauna.Visible = false`. There is no fauna review runner. `fauna.gdshader:29` has a `preview_time` uniform and `FaunaBodyProfiles.cs:7` mentions "pose previews", but no C# consumes either. `--visual-review` pauses the clock (`VisualReviewRunner.cs:21`) so animals are frozen. Fauna validation therefore needs a new runner (A0).
- `--guardian-review <dir> [--guardian-species a,b] [--guardian-populated]` (`game/App/GuardianReviewRunner.cs`) writes `<arch>_closeup.png`, `<arch>_locomotion.png`, `<arch>_feed.png` and `guardian_review.json`. It runs on an EMPTY island unless `--guardian-populated` (`Main.cs:119`), so vegetation and fauna contact are not exercised by default. Locomotion is ONE still after 60 frames (`GuardianReviewRunner.cs:88-110`), which cannot show foot slide. `MaximumStanceFootTravel` is collected (`GuardianTerrainLegModifier.cs:21`) but not written to the JSON (`GuardianReviewRunner.cs:131-146`).

---

## 1. Inventory

### 1a. Fauna: 17 species, 17 distinct mesh builders

Content: `game/content/fauna/<id>.json`. Pipeline: one `OrganismMeshes.Fauna(sp, seed)` mesh per species x 4 morph variants (`OrganismRenderers.cs:1797-1814`), drawn via MultiMesh with ONE shader `game/Shaders/fauna.gdshader` (vertex-shader animation, no skeleton). Builders: `src/Vivarium.Sim/Geometry/Fauna*.cs` and `OrganismMeshes.cs:6355-6960`. "Procedural" = ellipsoid rings, Catmull-Rom tubes and fans merged into one `MeshData` with role/attach data in UV2/COLOR/CUSTOM. No imported models.

| id | model builder | body plan | limbs / joints | anim family | sim behaviours | what is distinct today |
|---|---|---|---|---|---|---|
| coalback_beetle | beetle (`CinematicBeetle`, FaunaBeetleMeshes.cs:16) | 6-leg beetle, ground | DetailedTube legs + IK chain | walk | none | shape, 2.4/h |
| dewmantle | slug (`CinematicSlug`) | soft slug, damp ground | none (sole, tentacles) | soft_glide | none | shape, slowest land animal 1.1/h |
| duskflicker | midge (`Midge`, OrganismMeshes.cs:6897) | flyer, 3 ellipsoids, wing fans | 3 static 3-point legs, never animated | flight | flying | night glow, 9/h |
| emberglass_swimmer | shrimp (`Shrimp`, :6720) | aquatic | `Appendage` swimmerets | paddle | none | shape, translucency |
| ghostbristle | silverfish (`CinematicSilverfish`) | ground | DetailedTube + chain | walk (body wave .45) | none | fast 5/h, scales |
| glasscoil | snail (`Snail`, FaunaDetailedMeshes.cs:249) | aquatic bed grazer | foot + shell | soft_glide | none | bed-locked (FaunaSystem.cs:133-138) |
| glintfin | minnow (`Minnow`, :6910) | fish | `AppendageFan` fins | undulate | **schooling** (only one) | shape, flocking |
| loamthread | worm (`CinematicWorm`) | annelid | none | peristaltic | none | shape |
| marbleback | isopod (`CinematicIsopod`) | pill-bug type | DetailedTube + chain | walk | **conglobate** | only species with a second pose (`CinematicIsopodCurled`) and a flee reaction |
| moonveil | moth (`Moth`, FaunaDetailedMeshes.cs:293) | flyer | wing fans, static legs | flight | flying | wings, surface_family 2 |
| prismhopper | springtail (`Springtail`, :6624) | tiny hopper | `Appendage` legs | hop | none | hop gait, visualScale 7 |
| rainspine | salamander (`CinematicSalamander`) | 4-leg sprawl | DetailedTube limbs + 5 digits | sprawl | none | only quadruped predator |
| reedjaw | aquatic_larva (`AquaticLarva`, :202) | larva predator | DetailedTube legs | paddle | none | shape |
| rustcoil | millipede (`Millipede`, :150) | 20 tube rings, 2 legs per pair side | DetailedTube + chain | metachronal | none | segmentation. `FaunaBodyProfiles.Style` has no "millipede" case, so it renders as style 0 with no `tissue_chitin` (FaunaBodyProfiles.cs:10-14, 49) |
| siltshield | triops (`Triops`, :56) | shield + 16 leaf-leg pairs | DetailedTube legs | paddle | none | shape; role text says benthic but it swims in the column (A4) |
| stiltclaw | harvestman (`CinematicHarvestman`) | 8 long legs | 6-point legs + chain | walk | none | leg length, 4.2/h |
| stonebell | toad (`Toad`, OrganismMeshes.cs:6786) | toad predator | `Appendage` limbs, NO chain (A5) | hop + render `ToadLocomotion` FSM | none | only species with a render-side state machine |

Shared-builder flags:
- No two species share a mesh builder (the `pill_bug` model name has no species, section 5).
- They share locomotion code and the entire sim routine. `paddle` is one shader branch (`fauna.gdshader:256-264`) for shrimp, larva, triops, differing only in 8 numbers. `walk` (`:266-284`) is shared by beetle, silverfish, isopod, harvestman (different pair-phase `if`). `soft_glide` by slug and snail; `flight` (`:391-402`) by midge and moth.
- Sim behaviour is ONE routine (`FaunaSystem.StepBehaviour`, FaunaSystem.cs:147-291). Species differ by `speedPerHour`, `turnRatePerMinute`, `wander`, `habitatSeek`, `senseRadius`, diet and habitat. Only three behaviour flags exist: `flying` (2 species), `schooling` (1), `conglobate` (1); 13 of 17 species have `behaviors: []`.
- Per-individual variation: genes (size, hue_shift, pattern_strength, ornament_density, appendage_length, metabolic_efficiency; Genetics.cs:29-57), 4 morph variants (+-7% length/height/width/appendages, `ApplyFaunaMorph` OrganismMeshes.cs:6456-6530) chosen by id hash (OrganismRenderers.cs:1993), +-4% body y/z (`fauna.gdshader:217-218`). No behavioural individuality except a per-id wander phase (FaunaSystem.cs:170).

### 1b. Guardians: 10, all skinned imported GLBs

`game/assets/guardians/<archetype>/guardian.glb`, loaded in `GuardianRenderer.Build` (GuardianRenderer.cs:170-227). Animation = AnimationPlayer clips + a stack of `SkeletonModifier3D` post-processors. Materials replaced at load by `guardian_surface.gdshader` (`ApplySurfaceMaterials`, :443). Definitions in `src/Vivarium.Sim/Guardians/GuardianCatalog.cs`. Clips and joint counts below were read from the GLB JSON.

| id (catalog line) | archetype / animal | joints | clips present | locomotion enum | walk/run m/s (x0.5 applied) | distinct today |
|---|---|---|---|---|---|---|
| hoatzin_crested (:30) | crested_canopy, hoatzin | 25 | feed, idle, rest, walk | AvianPerchHop | .20/.36 | biped, crest/wing sway, hop arc .06 |
| pangolin_armored (:64) | armored_forest, pangolin | 32 | idle, walk, feed, rest | TerrestrialQuadruped | .21/.378 | digger, nocturnal .8, 8-bone tail |
| serpent_canopy (:98) | canopy_serpent, boa | 37 | idle, walk, slither, feed, rest | Serpentine | .44/.79 | 33-station body solver, bask 1.8, climbs |
| monitor_great (:131) | great_ground, water monitor | 40 | feed, idle, rest, walk | TerrestrialQuadruped | .577/1.04 | 16-bone tail, bask, swim, digs |
| coati_woodland (:164) | woodland_omnivore, coati | 33 | feed, idle, rest, walk | TerrestrialQuadruped | .468/.84 | snout twitch, climber, curiosity 1.6 |
| porcupine_quilled (:197) | quilled_arboreal, porcupine | 30 | feed, idle, rest, walk | ArborealClimber | .108/.194 | prehensile tail, slowest |
| thylacine_fernback (:228) | fernback | 31 | feed, idle, rest, walk | TerrestrialQuadruped | .46/.83 | 41 meshes, stripes |
| saiga_dewhorn (:240) | dewhorn | 33 | feed, idle, investigate_sniff, play, rest, walk | TerrestrialQuadruped | .40/.90 | only quadruped with play and sniff clips |
| shoebill_hushbill (:252) | hushbill | 12 | idle, walk, feed, rest, bow, look, alert | AvianPerchHop | .18/.34 | most clips (7), still stalker |
| matamata_ruffleback (:264) | ruffleback | 12 | idle, walk, feed, rest | TerrestrialQuadruped | .11/.20 | 2-bone limbs, shell, no Root bone |

Shared by all 10: ONE behaviour implementation (`GuardianSystem`, priority list at `SelectNextActivity` :327-548) parameterised by `GuardianBehaviorProfile`; ONE renderer; shared modifiers. Variation per individual: `Scale` 1.0 +(-0.05..+0.08) (GuardianSystem.cs:137), 6 colour variants (:60), and +-6% warmth/value jitter in `ApplySurfaceMaterials` (:445-501). `Temperament` is always "inquisitive" and read nowhere.

---

## 2. Joint and continuity mechanics

### 2a. Fauna (shader-side, no skeleton)

- Connection: every limb vertex stores `COLOR.rgb` = offset from its attach point, `COLOR.a` = 1, `UV2.y` = role (1 leg, 2 wing/fin, 3 sensory, 4 other; fractional part = root-to-toe weight) (`Appendage`, OrganismMeshes.cs:6537-6600; `AttachSpan`, FaunaDetailedMeshes.cs:9-17). The shader moves `VERTEX = attach + off` and animates only `off` (`fauna.gdshader:223-225`), so root rings never leave the attach point. `Appendage` inserts one flared socket ring (radius x1.26, +7% vertical fullness) to hide the tube-on-ellipsoid seam (OrganismMeshes.cs:6542-6600).
- Two articulation paths:
  1. **Chain IK** (`fauna_joint_ik.gdshaderinc:20-70`, call at `fauna.gdshader:415-438`): analytic two-link knee solve plus rigid ankle-relative toe. Only for limbs built by `DetailedTube` (role 1, root == path[0], >= 3 points), which calls `RegisterFaunaJoint`; `FinalizeFaunaArticulation` (FaunaArticulationMeshes.cs:31-90) derives knee and ankle and writes CUSTOM0/1. Used by beetle, silverfish, isopod, harvestman, millipede, salamander. The call is restricted to locomotion 3, 4 and 8 (`:415-416`), so triops/larva legs get the chain data but never use it (paddle uses shear).
  2. **Rigid shear**: `off.x += sweep*stride*foot_weight; off.y += lift*...` (`:276-281, 287-292, 376-385`). The tube is translated, not hinged. Used by **toad** (local `Limb` at OrganismMeshes.cs:6858 calls `Appendage` and never registers a joint), **springtail**, shrimp and triops swimmerets, midge legs.
- Digits and pads of IK legs are `distal:true` tubes sharing the leg root; they follow the ankle by pure translation (`fauna_joint_ik.gdshaderinc:57-60`), so toes never flex or pitch with the shin.
- Whole-body bend (`fauna.gdshader:514-550`): flexible bodies (family 4 or style 1, 4, 5, 7, 8, 9, 10, 11, 12, 16) are bent along a spine arc plus travelling wave AFTER limb articulation, rotating limb cross-sections with the tangent so roots stay attached. Millipede and salamander have planted-toe compensation (`:521-538`).
- Seams: no gap at limb roots by construction. Remaining seam risk is where separate primitives meet: millipede rings are 20 independent 3-point tubes spaced 0.043 (FaunaDetailedMeshes.cs:154-158), isopod plates, beetle elytra, toad head/trunk/jaw ellipsoids. **[RENDER]** check millipede at high `turn_bend`.

Feet vs terrain (fauna):
- Sim: one point per animal, `f.Y = GroundHeight(xz)` (FaunaSystem.cs:285; terrain, root shelf or prop top, whichever higher, VivariumWorld.cs:259-266).
- Renderer (OrganismRenderers.cs:1955-1991): ONE normal by central differences over `max(scale*0.5, 0.01)` m (`SurfaceFrame.SurfaceNormal`, :2070-2077, tilt capped at 60 degrees), smoothed with `k = 1-exp(-8 dt)`, resampled at 8 Hz or after 2 mm of travel (:1963-1968). The whole rigid body is rotated to it.
- Legs are posed in body space only (`rest_ankle + (sweep*stride, lift*lift_height, 0)`, `fauna_joint_ik.gdshaderinc:30`). No per-foot ground query. Correct on a uniform plane; on rock edges, logs, root shelves or any bump smaller than the leg span, feet float or sink.
- Strength: the distance-clock gait is exact on a plane: `HalfStroke = duty/(2*cyclesPerBody)` cancels stance translation (FaunaGait.cs:15-16) and stance pose is held at stop (`fauna.gdshader:214`), so there is no stance slide on flat ground.

Vegetation and water (fauna): none. `IsPassable` (FaunaSystem.cs:297-305) tests domain, Pilot Tree trunk and water depth only. No flora code references fauna. `WaterSurfaceRenderer` has no external impulse; water is a volume/flux sim (`SurfaceFluidDynamics.Deposit/Add`, :336-410).

### 2b. Guardians (skeletal)

Skin: authored in Blender, linear-blend skinned in Godot, 0 morph targets in all GLBs, so joint continuity depends on authored weights. **[RENDER]** inspect elbows and hocks at the extremes of `walk` and `feed`.

Post-process stack, in order added (GuardianRenderer.cs:247-332; Godot runs modifiers after AnimationPlayer):
1. `GuardianBreathingModifier`: scales chest/spine bones (`:100-113`).
2. `GuardianHeadTracker`: yaw clamp 0.60, pitch 0.35 rad, blend 3.5/s (`:15-16`). Returns early when `_jawIsSiblingOfHead` (`:67`): the boa's Jaw and Head are both children of Root, so it never tracks.
3. `GuardianSecondaryMotion`: ear/crest/horn/wing sway from angular velocity, snout twitch, blinks.
4. `GuardianTerrainLegModifier` (not created for the boa).
5. `GuardianAppendageModifier`: tail (for the boa, the whole 33-station body) as a world-space inertial chain with ground, trunk and vegetation constraints, per-station radii, and a frame-skip when still (`:98-100`).
6. `GuardianSkinContact`: CPU linear skinning of sampled vertices per region ("Hand/Foot/Body/Head"), the ground-clearance authority for 4 and 5 (`Evaluate`, GuardianSkinContact.cs:617); plus a GPU coat displacement of at most 1.5 mm via `contact_sphere` (:57; GuardianRenderer.cs:799-810).

Foot placement (`GuardianTerrainLegModifier._ProcessModificationWithDelta`, :37-342):
- Per frame, per leg (L/R x front/hind), names resolved across 3 rig conventions (`LegName` :30-36). Birds' "front" legs resolve to nothing and are skipped, so only hind legs are solved for hoatzin and hushbill.
- Swing is inferred from foot height above rest: `swings[foot] = max(0, c.Y - rest.Y)` (:79); target Y = `SupportHeight(c) + sole + swing` (:80), refined by CPU skin clearance (:83). Only Y is corrected; XZ stays whatever the clip does, so there is no foot locking.
- Unreachable targets: `UseLocalFootholds` searches 5 radii x 16 directions (80 probes) and moves the foot XZ (:168-189, :258-283).
- Two-bone analytic IK around the animated knee (:286-298), then up to 8 fit iterations each calling `SkinContact.Evaluate` (:221, :286-337).
- Pelvis: one drop = min over legs, clamped to -0.20*scale (:71, :96), applied to Root/Pelvis/Hips/Body (:107-116); lift up to 0.40*scale so belly and tail attach clear ground (:118-138). No pelvis or spine tilt from foot heights; body tilt is the sampled terrain normal blended 45% toward up (GuardianRenderer.cs:652; `TerrainBasis` :897-909); soles align to 70% of local normal (:209, :303).
- Skipped while climbing (:41).

Vegetation (`GuardianVegetationContact`):
- It rewrites live MultiMesh instance transforms of `FloraRenderer` (GuardianVegetationContact.cs:315-333): each plant within 4 m gets `rotation(Up x bend, |bend|) * basis`, a RIGID lean about its root, blended 14/s on contact and 4/s on release (:312), with bookkeeping for flora's periodic full buffer uploads (:330-340).
- Collision shape: AABB plus a stem cylinder `min(0.045, size.x*0.10) + radius`, active below 65% of plant height (`PushOutOfStem`, ~:568-577). Canopy never pushes (the unused `GuardianPlantSurface` BVH was meant for that).
- Callers: tail capsules (`ResolveCapsule`, :519) and, ONLY when a Pilot Tree exists, body bones in the trunk-avoidance loop (GuardianRenderer.cs:687-750; bones Pelvis..Head/Snout/Body*, `ShrubContact.Resolve` :707). There the push is applied to the WHOLE GUARDIAN (`GlobalPosition += correction`, :742): a stem moves the animal rather than the plant yielding. Hands and feet never query vegetation (`LimbContactBones` only feeds trunk penetration, :728-740).
- Capacity: 128-bit candidate mask (`_orderedPlants = new Plant[128]`, :74); `_plants.Count > 128` falls back to a full scan (:583).

Water (`GuardianWaterContact`):
- Computes `Wetness` (decays over 18 s) and a carried waterline `WetHeightWorld` (:66-83) that feed `guardian_surface.gdshader:64-68, 265-268` (darkening, clearcoat, wicking). That is all.
- `IsInWater`, `IsDrinking`, `RipplePositions` (a new array each frame while wet, :93-129) and `DisturbanceRadius` have no consumer in `game/` or `src/`. No ripple, wake, splash, drip, foot submersion or swim pose.
- Body support: sim `g.Y = GroundHeight` (the bed; GuardianSystem.cs:201, 909-920), renderer `SupportHeight` = ground (GuardianRenderer.cs:62-69). Non-swimmers are blocked at depth > 0.14 (`IsPositionBlocked`, GuardianSystem.cs:767); `CanSwim` species have NO depth limit (speed floor 0.45, :846).

Climbing (GuardianSystem.cs:973-1010, GuardianClimbing.cs): route of 12 cm steps ray-cast against the same woody triangles the tree and shrubs render (:36-60, :115), body moved along the polyline at 0.65x walk speed, orientation lerped to `ClimbNormal`. No `climb` clip exists in any GLB, so `walk` plays (GuardianRenderer.cs:935); leg IK is off (above); feet do not grip bark.

---

## 3. Behaviour mechanics

### 3a. Guardians (`src/Vivarium.Sim/Guardians/GuardianSystem.cs`)

- Tick: every 4 ticks (`Cadence.FaunaBehaviour`, VivariumWorld.cs:87, 207) divided into <= 0.1 s visible slices (:176-178). Renderer interpolates one interval late (GuardianRenderer.cs:601-607).
- Decision: fixed priority list, re-evaluated when `ActivityDuration <= 0` (:265) or when Explore is interrupted by need thresholds (:204-211): Retreat (alertness > .88, comfort < .45) > Alert (> .78) > Yawn (35% after rest) > fatigue (Rest/ReturnHome/Settle) > thirst > hunger (Forage/Feed/Dig) > discomfort (Bathe/Groom) > bask > scent mark (180 s) > Investigate (65%) > Play > roam (:327-548). Day/night only via a `daylight` scalar nudging fatigue and hunger (:333-350).
- Transitions in sim: Settle -> Sleep (30-60 s) -> Yawn (45%) or Stretch -> next (:266-300); Bathe -> ShakeOff. Everything else is a hard switch. No stand-up/lie-down state, no turn-in-place.
- Movement (`ExecuteLocomotion`, :773-959): turn at `MaxTurnRate`; speed = base x align factor (0.12..1) x slope factor (0.35..1, halved above `MaxSlopeDegrees`) x water factor (0.45..1) x arrival ramp (0.15..1 within 0.65 m); accel/decel from profile (:848-860). The align factor lets it walk forward while still turning.
- Obstacles: `IsPositionBlocked` rejects out-of-domain (0.45 m), Pilot Tree trunk (0.38), any rock/log footprint except the bask target, deep water for non-swimmers (:743-771). On block, 6 fixed feeler angles (+-0.45, 0.95, 1.45 rad), first free wins; none free = re-roll target and stop (:866-891). Flora, shrubs, fauna are ignored by pathing.
- Y re-snapped to `GroundHeight` every slice (:201, :920). Hop arc only for AvianPerchHop (hoatzin .06, hushbill .015; :961-972).
- Fauna interaction: eats any fauna within 1.2 m, first in list order, drains energy, then `Fauna.Remove` (`ExecuteFeeding`, :1028-1054). No chase, pounce or reaction.
- Player poke: bold species (hushbill, great_ground, fernback, serpent) always Alert; others coin-flip Alert or Retreat; hard-coded string list (:66-109, :70-71).
- Unused profile data: `PreferredRestType`, `DigAbility`, `Temperament`, `Pitch`.

Renderer side (`GuardianRenderer.UpdateAnimationState`, :911-979): clip chosen by `g.Activity` ONLY (:917-939); `walk` for Forage/Explore/ReturnHome; `run` falls back to `walk` for Retreat (no run clip exists); `slither` for the boa; crossfade 0.35 s (0.1 s alert/shake_off); `SpeedScale = clockSpeed x activitySpeed x strideScale`, `strideScale = (1/Scale) x clamp(curSpeed/(WalkSpeed*0.5), 0.35, 1.65)` for locomotion clips (:946-971).

### 3b. Fauna (`src/Vivarium.Sim/Fauna/FaunaSystem.cs`)

- Tick: behaviour every 4 ticks, metabolism every 6, lifecycle every 120 (VivariumWorld.cs:87). Renderer interpolates between the two latest samples one behaviour interval late (OrganismRenderers.cs:1847-1893).
- "State machine": there is none. Per step: add correlated value-noise wander (`WanderPeriod` 60 ticks, :170-174); maybe refresh a cached `IntentHeading` every 6 steps (:65, :189-242) from habitat/food probes (3 or 7 headings, score `Suitability + 0.5*FoodAt`, :210-233), a flee heading, or a school heading (:307-326); turn toward it weighted by `HabitatSeek` (:244-251); advance at a CONSTANT speed `sp.Speed x SpeedScale (x3 disturbed, x0.6 starving)` (:253). **No idle, rest, feeding, mating or sleeping state; no acceleration or speed variation; animals never stop** except when blocked, grabbed or curled.
- Feeding and mating are accounting at metabolism/lifecycle cadence at the current position (`Feed` :461-502, `StepLifecycle` :613-698). Predation removes the nearest eligible prey within `clamp(senseRadius*0.35, .10, .30)` m instantly, one per pass (:566-609). Birth: new animal 1-8 cm from parent (:732-736), full adult visual size.
- Obstacles: `IsPassable` only; first blocked step turns +-0.6 pi (hash) and tries a half-step sidestep (:257-273). Props are PASSABLE (prop-top test, :304), so animals climb any rock/log at any slope with Y stepping.
- Flyers: `Y = ground + max(0.14, visualScale*(sizeMin+sizeMax)*0.8)` (:129); both flyers compute below 0.14 (moth .067, midge .035), so both hover at exactly 0.14 m.
- Aquatic: `Y = bed + margin + Pitch x column`, `Pitch` a clamped random walk 0.05-0.95 (:278-284); schooling pulls it to 0.55; snail bed-locked.
- Disturbance: only player tools set `DisturbedUntil` (ToolActions.cs:175, 214). Fleeing = away heading x3 speed; marbleback curls. Fauna do not react to predators, prey, guardians or each other (except glintfin schooling).
- Render (OrganismRenderers.cs:1840-2035): yaw critically damped to travel direction; bend = heading change per body length (clamped +-1.25, lag 4/s) packed into instance data for flexible bodies (:1915-1918); gait phase from travelled distance (`FaunaGait.Advance`). The toad alone has a render FSM (`ToadLocomotion`): crawl below 0.5 body lengths/s, hop above 0.9, launch held until take-off, landing tracked during the crouch (ToadLocomotion.cs:27-71).

---

## 4. Ranked defect list (most visible first)

[S] = shared infrastructure, [sp] = species-specific. Size: S < 1 day, M 1-3 days, L > 3 days or new art.

### Guardians

**G1. Motion is shown with the wrong clip: guardians slide in rest/feed/idle during approach phases.** [S]
- Evidence: clip comes from `g.Activity` alone (GuardianRenderer.cs:917-939), yet locomotion runs in Drink, Bathe, Bask, Dig, ScentMark until within 0.4 m (GuardianSystem.cs:801-817, 827-931), and always in Investigate and Play. Mapping: Drink -> `drink` else `feed`; Bathe -> `bathe` else `idle`; Bask -> `bask` else `rest`; Dig -> `dig` else `feed`; ScentMark -> `scent_mark`/`bow`/`idle`; Investigate -> `look`/`investigate_*`/`idle`; Play -> `play_*`/`bow`/`idle`. Only hushbill (look, bow, alert) and dewhorn (investigate_sniff, play) have any of these, so 8 of 10 glide across ground in idle, rest or feed.
- Affected: all 10; worst Drink, Bask, Play, Investigate.
- Root cause: animation state derived from intent, not motion.
- Remedy: in `UpdateAnimationState` (:911) compute `moving = g.CurrentSpeed > 0.03 || rendered horizontal speed > 0.03` and `approaching = distance(pos, target) > 0.4`; if either, force `walk`/`slither` (or `run` when it exists) and switch to the activity clip only after speed falls below threshold. Set `activitySpeed` to 1 for locomotion clips.
- Benefit: removes the most visible fault (skating) for all species in one function.
- Validate: `--guardian-review` with `Activity = Drink` set 3 m from water; capture a 6-frame strip (extend the runner) and confirm legs cycle while speed > 0.
- Size S. Files: `game/Render/GuardianRenderer.cs`.

**G2. Clip vocabulary collapses: 21 activities share 4 clips; rest, climb, swim, pounce postures do not exist.** [S code, L art]
- Evidence: clip table in 1b. With 4 clips (six species) Sleep -> `rest` at 0.4x, Settle -> `idle`, Stretch -> `bow`/`idle`, Yawn/Groom/Bathe/ShakeOff/Alert/Play -> `idle`, Climb -> `walk`, Retreat -> `walk` at 1.8x (GuardianRenderer.cs:917-969). `PreferredRestType` is read nowhere: the pangolin never balls, the boa never coils, hoatzin and porcupine never perch. Crossfade is a fixed 0.35 s linear blend (:975-977), which cuts through the body between standing and lying clips.
- Affected: crested_canopy, armored_forest, canopy_serpent, great_ground, woodland_omnivore, quilled_arboreal, fernback, ruffleback most.
- Root cause: clip set was not authored to the activity list.
- Remedy: (a) shared, cheap: procedural activity layers in `GuardianBreathingModifier`/`GuardianHeadTracker` for Groom (head to flank), Stretch (front-down bow via Chest/Pelvis pitch), Alert (head high), ShakeOff (spine twist oscillation), Drink (neck pitch to water point), Sleep (lower pose); (b) per-species clips to author: `curl` (pangolin ball, ruffleback withdraw), `coil` (boa), `climb` (4 climbers), `run`, `drink`, `dig`, `turn_L/R`; (c) use `PreferredRestType` in rest-site choice and clip selection.
- Benefit: owner priority 3 (idle behaviour and transitions) and species distinctiveness.
- Validate: `--guardian-review` per species with added rest/sleep/climb/drink shots.
- Size (a) M, (b) L, (c) S. Files: `GuardianRenderer.cs`, `GuardianBreathingModifier.cs`, `GuardianHeadTracker.cs`, `GuardianSystem.cs`, GLBs in `game/assets/guardians/*`.

**G3. Foot sliding: horizontal foot position is never locked and clip stride is not calibrated to ground speed.** [S]
- Evidence: leg modifier corrects only Y (GuardianTerrainLegModifier.cs:79-80, 249). Playback rate = `curSpeed/(WalkSpeed*0.5)` (GuardianRenderer.cs:956-958) assumes every clip's authored stride equals `WalkSpeed*0.5` per cycle length. Distance covered per authored walk cycle at nominal speed (m / body scale): armored .21*.5*2.02 = 0.21 / 0.53; boa 0.44 / 0.55; hoatzin .2*.5*1.04 = 0.10 / 0.50; saiga 0.30 / 0.50; fernback 0.47 / 0.50; monitor 0.40 / 0.63; hushbill 0.18 / 0.50; porcupine .108*.5*0.75 = **0.040** / 0.45; ruffleback 0.14 / 0.50; coati 0.24 / 0.48. Porcupine is a 5-10x outlier; hoatzin and ruffleback are low. **[RENDER]** (assumes authored stride is comparable across species).
- Also: walking while turning (align factor 0.12) plays the cycle at >= 0.35x, so feet step up to 3x faster than the body moves.
- Affected: all; worst quilled_arboreal, crested_canopy, ruffleback.
- Root cause: no stride calibration, no stance XZ pinning, no turn-in-place.
- Remedy: (1) in `Build`, measure each locomotion clip's root-space foot travel per cycle (sample `Foot_*` over the clip with `AnimationPlayer.Advance`) and set playback rate = `speed / (strideMetres / cycleLength)` instead of the `WalkSpeed` ratio (:956); fix porcupine `WalkSpeed`; (2) in the leg modifier store per-foot world XZ at stance start, pin during stance (stance from clip phase window, not height) and IK to it; (3) pivot rule: if heading error > 60 degrees, stop translating (alignFactor floor 0) and rotate with a turn clip or leg shuffle.
- Benefit: removes skating, gives weight (owner priority 3).
- Validate: write `leg_max_stance_foot_travel_m` to `guardian_review.json` and capture 4 frames at 0.2 s intervals.
- Size M. Files: `GuardianRenderer.cs`, `GuardianTerrainLegModifier.cs`, `GuardianSystem.cs` (:838), `GuardianCatalog.cs`.

**G4. Slope foot hover: swing is inferred from foot height, so uphill stance feet may be held off the ground.** [S, hypothesis]
- Evidence: `swing = max(0, c.Y - rest.Y)` (GuardianTerrainLegModifier.cs:79, 247) is added to the target (:80, 249). Body tilt is only 45% of terrain slope (GuardianRenderer.cs:652), so on a ~25-30 degree slope the uphill foot's rest point sits ~0.55 x slope x lever above the body plane; the code reads that as a lifted foot and keeps it up. `leg_max_stance_gap_m` cannot see it because stance gap is recorded only when `swing < .003` (:330).
- Affected: all quadrupeds and bipeds on slopes > 10 degrees. **[RENDER]**
- Root cause: heuristic swing detection plus partial-tilt body.
- Remedy: fit body orientation from the four foot supports instead of 45% normal blending (GuardianRenderer.cs:644-671, 897-909); derive swing from the clip phase or foot velocity in root space; log per-foot hover for all feet.
- Validate: `--guardian-review` pass 2 already walks to a slope (`FindStageSpot preferSlope`); add hover metric and a side-view capture.
- Size M. Files: `GuardianTerrainLegModifier.cs`, `GuardianRenderer.cs`, `GuardianReviewRunner.cs`.

**G5. Water: CanSwim guardians walk the pond bed with the body at bed level; no wade-depth limit, swim pose, ripple or splash; only shader wetness.** [S]
- Evidence: GuardianSystem.cs:767 (depth block only for `!CanSwim`), :201, :920 (`Y = GroundHeight`); GuardianRenderer.cs:62-69; `RipplePositions`, `DisturbanceRadius`, `IsInWater` produced (GuardianWaterContact.cs:93-129) and never consumed. `CanSwim` true for serpent, great_ground, woodland, fernback, hushbill, ruffleback.
- Affected: those six, whenever they drink, bathe or cross the pond.
- Root cause: water added as a coat effect only, no body-water model.
- Remedy: (1) `MaxWadeDepth` in `GuardianBehaviorProfile` (about 0.8 x leg length; larger for ruffleback and boa), enforced in `IsPositionBlocked`; (2) when water is deeper than belly clearance, raise pelvis so feet stay on the bed and the body stays above the surface, or float ruffleback and boa at surface minus draft; (3) feed `WaterContact.RipplePositions` into a small uniform array in the water surface shader (render-only ripple rings) plus a ring at each foot-down; (4) drip particles for ShakeOff.
- Validate: `--guardian-review --guardian-populated` with a `wet` stage mode added to `FindStageSpot`; capture Drink, Bathe and crossing.
- Size M-L. Files: `GuardianSystem.cs`, `GuardianBehaviorProfile.cs`, `GuardianCatalog.cs`, `GuardianRenderer.cs`, `GuardianWaterContact.cs`, `WaterSurfaceRenderer.cs`, `Shaders/creek_surface.gdshaderinc`.

**G6. Vegetation contact is rigid plant tilt plus whole-animal push; legs and canopy never interact.** [S]
- Evidence: GuardianVegetationContact.cs:315-333 (rigid rotation of instance transform), ~:568-577 (stem cylinder only), GuardianRenderer.cs:687-750 (body push moves the guardian, only when a Pilot Tree exists), :728-740 (feet query trunk only), 128-plant cap (:74, :583).
- Affected: all, whenever walking through shrubs, ferns, grass; tail.
- Root cause: contact solved on the CPU by rewriting MultiMesh transforms rather than in the flora vertex shader.
- Remedy: pass a small array of capsule colliders (tail segments, feet, spine) as uniforms to `flora.gdshader`/`ground_cover.gdshader` and apply a height-weighted vertex bend away from each capsule, so plants deform instead of tilting rigidly; delete the CPU `SetInstanceTransform` path and its upload-revision bookkeeping; keep stems as hard colliders for woody plants only; drop the whole-animal push in the non-tree case.
- Benefit: plants bend around legs and flanks; large CPU and allocation savings.
- Validate: `--guardian-review --guardian-populated` on a dense-shrub stage; compare closeup and locomotion captures.
- Size L. Files: `GuardianVegetationContact.cs`, `Shaders/flora.gdshader`, `Shaders/ground_cover.gdshader`, `OrganismRenderers.cs` (FloraRenderer uniform feed), `GuardianRenderer.cs`.

**G7. Climbing has no climb clip and no limb contact on bark.** [sp: crested_canopy, canopy_serpent, woodland_omnivore, quilled_arboreal; IK part is S]
- Evidence: no `climb` clip in any GLB; `walk` substituted (GuardianRenderer.cs:935); leg IK returns while climbing (GuardianTerrainLegModifier.cs:41); orientation rigid lerp (GuardianRenderer.cs:673-684); route steps 12 cm (GuardianClimbing.cs:115).
- Remedy: author `climb` clips or a procedural 4-limb gait; add a "wall" mode to the leg modifier that ray-casts foot targets against the same woody triangles `GuardianClimbing.Surface.Ray` uses, with G3 pinning.
- Size L. Files: GLBs, `GuardianRenderer.cs`, `GuardianTerrainLegModifier.cs`, `GuardianClimbing.cs`.

**G8. Foothold pops and per-frame CPU cost in the leg solver.** [S]
- Evidence: 80-probe XZ retarget when unreachable (GuardianTerrainLegModifier.cs:168-189, 258-283); up to 8 fit iterations per foot, each a `SkinContact.Evaluate` (:214-237, 316-337); tail solver runs every other frame when still (AppendageModifier :98-100).
- Remedy: hysteresis (keep last target unless error > 3 mm for 3 frames); ease target XZ over 0.1 s; cap fits to 2-3 warm-started from last frame; re-evaluate skin only for feet that moved.
- Benefit: removes foot popping, protects the 30 fps target on the 860M.
- Size S-M. File: `GuardianTerrainLegModifier.cs`.

**G9. No individual personality; boldness and species behaviour hard-coded or inert.** [S]
- Evidence: `Temperament` constant (GuardianSystem.cs:141), archetype string list (:70-71), `PreferredRestType` and `DigAbility` unused, `TerritoryDrive` defaults 0.5 for all (:506).
- Remedy: seeded per-individual multipliers (curiosity, boldness, fatigue rate, activity durations +-20%) in `InitializeGuardian`; replace the string list with `Boldness` in the profile.
- Size S. Files: `GuardianSystem.cs`, `GuardianBehaviorProfile.cs`, `GuardianCatalog.cs`, `GuardianIndividual.cs`.

**G10. Head tracker silently off for the boa.** [sp: canopy_serpent] GuardianHeadTracker.cs:67 plus GLB parentage. Size S. File: `GuardianHeadTracker.cs`.

### Fauna

**A0. Prerequisite: no fauna review harness.**
- Evidence: `SpecimenReviewRunner.cs:62`, `VisualReviewRunner.cs:21`, `preview_time` unused.
- Remedy: add `--fauna-review <dir> [--fauna-species a,b]` modelled on `GuardianReviewRunner`: per species a flat stage, a slope, a rock edge, ankle-deep water, and a 30-frame strip with the clock running at 1x and a fixed seed.
- Size M. Files: new `game/App/FaunaReviewRunner.cs`, `Main.cs:45`, `Bridge.cs:486`.

**A1. Animals never stop, feed, rest or accelerate: all 17 species are constant-speed random walkers.** [S, sim + render]
- Evidence: FaunaSystem.cs:253-273 (constant speed, no stop state); no idle/feed/rest field in `FaunaIndividual` (Fauna.cs:7-52); shader idle motion is reachable only for families 1, 2, 5, 6, 9 (`fauna.gdshader:214`).
- Affected: all; most obvious for slow or ambush species (stonebell, rainspine, reedjaw, dewmantle, rustcoil).
- Root cause: steering-only behaviour; feeding is accounting at the current position.
- Remedy: add a render-visible mode to `FaunaIndividual`: `Mode {Move, Pause, Feed, Rest, Flee}` + `ModeUntil`, chosen deterministically in `StepBehaviour` (Feed when `FoodAt(p) > 0.3` and hungry; keyed-hash Pause; Ambush for predators when prey is within sense radius). Speed = `speed * easedModeBlend`; persist like `IntentSteps` for save determinism. Render: pass mode in spare bits of `custom.x`; add feed head-dip and idle sway in the shader keyed on `speed01` and mode.
- Benefit: weight, idle behaviour, transitions, per-species character (owner priority 3).
- Validate: `--fauna-review` strips with a speed plot; save/reload determinism.
- Size L. Files: `Fauna.cs`, `FaunaSystem.cs`, persistence, `OrganismRenderers.cs`, `fauna.gdshader`, species JSON (new behaviour profile).

**A2. Feet do not meet uneven ground: one rigid body tilt, legs posed in body space only.** [S]
- Evidence: OrganismRenderers.cs:1963-1991 (single normal, 60 degree cap), `fauna_joint_ik.gdshaderinc:30` (no ground input). Props are passable (FaunaSystem.cs:304) so animals walk over rock and log tops with Y stepping while legs stay planar.
- Affected: all terrestrial walkers (beetle, silverfish, isopod, harvestman, millipede, salamander, toad, springtail) plus belly clearance for slug, worm, snail.
- Remedy: sample 3 support heights (front, rear, side) per animal at render and pass `support_pitch`/`support_roll` (replacing the single normal) and per-end foot offset as instance data, added to `target.y` in `fauna_joint_ik`; clamp belly of soft bodies to support.
- Validate: `--fauna-review` rock-edge stage, side view.
- Size M. Files: `OrganismRenderers.cs`, `fauna.gdshader`, `fauna_joint_ik.gdshaderinc`, `SurfaceFrame`.

**A3. Flyers hover at a fixed 14 cm.** [sp: duskflicker, moonveil]
- Evidence: FaunaSystem.cs:127-129; shader bob 3% of body (`fauna.gdshader:400`). No altitude variation, no landing, no clearance over flora, fixed pitch, no banking.
- Remedy: per-individual altitude state (keyed noise 0.08-0.6 m, drift toward food/attractors, landing on flora tops to rest), bank angle from `tr.Bend`, clearance above local flora height.
- Size M. Files: `FaunaSystem.cs`, `OrganismRenderers.cs`, `fauna.gdshader`.

**A4. Aquatic fauna ignore niche: random mid-water placement, no bed contact.** [sp: emberglass_swimmer, siltshield, reedjaw]
- Evidence: FaunaSystem.cs:278-284 applies the random column walk to every aquatic except the snail; role text "benthic" (siltshield), "ambush" (reedjaw). Paddle legs have no IK (`fauna.gdshader:415-416`). Depth changes snap Y.
- Remedy: `columnPreference` per species in content (benthic 0-0.15, midwater, surface); benthic species bed-locked with belly clearance; ease Y changes in the renderer.
- Size M. Files: `Definitions.cs`, `ContentLoader.cs`, 3 species JSON, `FaunaSystem.cs`, `OrganismRenderers.cs`.

**A5. Toad and springtail limbs are rigid shear, not joints; springtail hop is cosmetic.** [sp: stonebell, prismhopper]
- Evidence: toad `Limb` never calls `RegisterFaunaJoint` (OrganismMeshes.cs:6858-6882); springtail/shrimp use plain `Appendage`; shader falls to shear (`fauna.gdshader:376-385`) and a hinge pose (:339-352). Springtail hop height is model-space only (:358) while world position advances smoothly, no `ToadLocomotion` equivalent. **[RENDER]** limb stretch and glide.
- Remedy: build toad limbs with `DetailedTube` registration so locomotion 8 uses IK; generalise `ToadLocomotion` to `Hop`-family species and apply to springtail.
- Size M. Files: `OrganismMeshes.cs`, `FaunaArticulationMeshes.cs`, `OrganismRenderers.cs`, `fauna.gdshader`, `ToadLocomotion.cs`.

**A6. No life-stage visuals; spawn and despawn are instant.** [S]
- Evidence: scale is `BodySize * VisualScale` only (OrganismRenderers.cs:1942); newborn at full adult size 1-8 cm from parent (FaunaSystem.cs:732-745); death removal immediate (:803-812); `FaunaLifeStage` unused by render.
- Remedy: `ageScale = lerp(0.45, 1, Age/MaturityAge)`; 2 s scale-in at birth; short collapse/tint before removal through the render track.
- Size S. Files: `OrganismRenderers.cs`, `FaunaSystem.cs` (death hook).

**A7. Animals walk through flora and over any rock or log at any slope.** [S]
- Evidence: `IsPassable` ignores flora; props passable (FaunaSystem.cs:297-305); no slope penalty.
- Remedy: flora-stem probe bias in steering, slope speed factor like the guardian (:840-846), render-side tilt smoothing across prop edges.
- Size M. File: `FaunaSystem.cs`.

**A8. Distinctiveness is cosmetic: shared gait families, one sim routine, 13 of 17 species without behaviours.** [S]
- Evidence: section 1a; rustcoil missing from `FaunaBodyProfiles.Style` and `tissue_chitin`.
- Remedy: add `millipede` to `Style` and chitin list; per-species behaviour profile driven by A1 modes (stiltclaw stalk-pause, stonebell sit-and-wait, prismhopper skitter bursts, dewmantle rain activity); differentiate paddle species by shader style branches (shrimp tail flick, triops continuous, larva sculling; partly present `:149-176`).
- Size M. Files: `FaunaBodyProfiles.cs`, species JSON, `FaunaSystem.cs`.

**A9. No water coupling for fauna.** [S, shares G5 mechanism]
- Evidence: species may stand in 0.2-3 cm of water; no ripples or wet shading (water has no impulse hook, SurfaceFluidDynamics.cs:336-410).
- Remedy: render-only foot-down ripple rings (same uniform as G5) and a wet uniform from `OpenWaterDepth`.
- Size M.

**A10. Per-individual behavioural variation is absent.** [S]
- Remedy: hash-of-id multipliers (+-15%) on speed, turn rate and pause lengths. Size S. File: `FaunaSystem.cs`.

### Shared vs species-specific summary
- Shared infrastructure: G1, G2a, G3, G4, G5, G6, G8, G9 (guardian skeleton stack); A0, A1, A2, A6, A7, A9, A10 (fauna sim/render/shader); G5 and A9 share one water-ripple feature.
- Species-specific: G2b, G7, G10, A3, A4, A5, A8.

---

## 5. Unused, debug or candidate code (likely removable)

"Unused" = no non-self reference found by grep. The xUnit tests mentioned in CLAUDE.md are not in the tree, so test dependence is unverified.

Guardian side:
- `game/Render/GuardianPlantSurface.cs` (124 lines) and `GuardianPlantSurfaceReview.cs` (92): `new GuardianPlantSurface` appears only in the review; `GuardianPlantSurfaceReview.Run` has no caller. Wire into G6 or delete.
- `game/Render/GuardianStarterModel.cs` (220 lines) and its branches (`GuardianRenderer.cs:222-227, 844-845`): all 10 archetypes ship a GLB, so it only runs if a GLB fails to load. Keep `CreateFallbackMesh` (:557-579) as the failure fallback.
- Review-only flags living in production classes: `--guardian-coati-model-candidate` and `--guardian-coati-coat-candidate` (+ `LoadReviewCoat`, GuardianRenderer.cs:191-205, 356-381), `--guardian-model-no-contact` (:319-320), `--guardian-stem-audit` (:263), `--guardian-lowest-point-audit` (:328), `--guardian-chain-cache-audit` (GuardianSkinContact.cs:136), `--guardian-constraint-cache-audit` (GuardianAppendageModifier.cs:153), `--guardian-registry-cache-audit` (GuardianVegetationContact.cs:163), `--guardian-cpu-cache-audit` (:268). `GuardianReviewRunner` uses none of them. With them goes audit state: `ChainCacheAudit*`, `LowestPointAudit*`, `FixedPointAudit*`, `GroundCacheAudit*`, `ConstraintCacheAudit*`, `StemQueryMaskAudit*`, `DrawFrameAudit*`, `RegistryCache*Audits`, the convergence-study fields, `BenchmarkSkinTransforms`, `AuditPoseCache`, `AuditSpatialQueries`, `RunBroadphaseSelfTest`. Removal shrinks `GuardianAppendageModifier` (624 lines), `GuardianSkinContact` (826) and `GuardianVegetationContact` (775) and drops hot-path branches. Do not remove the contact index or `StemQueryCacheEnabled` without profiling.
- `GuardianRenderer.ReviewSupportPlaneHeight` and `ReviewGroundSample` (`:58-59`, used :52-55, 653, 899, AppendageModifier :277, 295, 579): nothing sets them.
- `GuardianWaterContact` outputs `RipplePositions`, `DisturbanceRadius`, `IsInWater/IsDrinking/IsBathing/IsWalkingInWater`: no readers (keep if G5 consumes them); arrays allocated each frame.
- Profile data with no reader: `PreferredRestType`, `DigAbility`, `Temperament`, `HomeAttachmentId`, `GuardianIndividual.Pitch`.

Fauna side:
- `OrganismMeshes.Worm` (FaunaDetailedMeshes.cs:183), `Harvestman` (:112) and `Silverfish` (OrganismMeshes.cs:6749): superseded by `Cinematic*`, zero callers (about 150 lines).
- `pill_bug` paths: the `pill_bug` builder in `FaunaCurled` (OrganismMeshes.cs:6403-6453) and the "pill_bug", "newt", "frog", "planarian", "leech" keys in `FaunaBodyProfiles` (:10-14, 27-35); no species uses those models.
- `fauna.gdshader` `preview_time` uniform (:29, used :441, 447): nothing sets it. `FaunaRenderer.DisplayPhase`, `DisplayCrawling`, `ReservedGpuInstanceSlots`: zero references (likely test-only).
- Flora items (`OrganismRenderers.GillTrial.cs`, `--flora-light-cache-audit`) were out of scope.

---

## 6. Needs a render or running sim to confirm

1. G1, G3: whether guardians visibly slide in rest/feed/idle while approaching; actual per-clip foot travel (strip captures per species), especially porcupine (0.040 m per cycle).
2. G4: uphill-foot hover on a ~25 degree slope; add a per-foot hover metric.
3. G5: whether CanSwim guardians submerge in the pond and how deep the pond is near `NearestWadingZone`; Drink, Bathe and crossing shots with `--guardian-populated`.
4. G6: rigid lean versus leaf-card intersection for legs and flanks in dense shrubs; frame cost with 100+ plants near the guardian.
5. G7: climbing feet versus bark on tree and shrub routes.
6. A1: confirm animals never stop (log positions at 1 Hz for 60 s per species).
7. A2: feet over rock edges and logs; millipede ring seams at high turn bend.
8. A3, A5: flyer altitude relative to flora height; springtail glide versus hop; toad limb stretching during hop.
9. A4: siltshield, emberglass, reedjaw depth distribution.
10. GLB skinning quality (elbows and hocks) at 0.5 scale; weights are not inspectable here.
11. Guardian stack cost on the 860M iGPU with vegetation (`FrameProfiler` markers "Guardian.Legs", "Guardian.Appendage", "Guardian.WaterContact", via `--perf-test`) before and after G6 and G8.
12. Whether any test outside the tree depends on the audit flags and hooks listed in section 5.
