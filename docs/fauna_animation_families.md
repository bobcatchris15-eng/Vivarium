# Fauna animation families

This branch moves fauna motion away from the old shared sine-wave "wiggle" and into species-authored render-only locomotion families.

The simulation remains authoritative for position, feeding, habitat and behaviour. Animation never writes back into simulation state.

## Content schema

Each fauna `visual` block now contains an `animation` object:

```json
"animation": {
  "family": "sprawl",
  "cyclesPerBody": 0.65,
  "idleHz": 0.0,
  "maxHz": 5.0,
  "fullSpeed": 3.0,
  "idleMotion": 0.0,
  "amplitude": 1.0,
  "bodyWave": 0.55,
  "limbSweep": 0.75,
  "limbLift": 0.45,
  "bob": 0.08,
  "phaseSpread": 0.55,
  "dutyFactor": 0.62
}
```

- `cyclesPerBody`: cadence response to visible travel, in cycles per body length.
- `idleHz`: cadence that remains with little/no translation for swimmers, flyers and soft bodies. Ground stepping ignores it.
- `maxHz`: ceiling on the independent idle beat. Travel-driven cycles are uncapped so fast movement cannot outrun the gait.
- `fullSpeed`: body lengths/second at which swimming/flying/soft-body deformation reaches full amplitude. Ground stride is constant and activity fades after stopping.
- `idleMotion`: fraction of pose amplitude retained at zero translation.
- `amplitude`: master deformation multiplier.
- `bodyWave`: axial/trunk wave strength.
- `limbSweep`: fore/aft limb sweep or wing flap angle.
- `limbLift`: foot/paddle lift and sensory vertical motion.
- `bob`: whole-body vertical excursion. In `hop`, this controls airborne height.
- `phaseSpread`: phase delay along the body; especially important for metachronal legs.
- `dutyFactor`: stance/contact fraction of the cycle.

## Families

- `undulate`: fish-like axial propulsion; wave increases toward the tail.
- `paddle`: swimming arthropods / larvae; paired appendages cycle through the water.
- `walk`: insects, isopods, harvestmen and other alternating terrestrial walkers.
- `metachronal`: many-legged walking with a travelling leg wave.
- `soft_glide`: gastropod-style muscular-foot travel with no lateral snake wiggle.
- `peristaltic`: annelid longitudinal compression / radial expansion.
- `hop`: crouch, extension, airborne arc and landing.
- `sprawl`: salamander-like diagonal limb gait plus mild trunk/tail counter-wave.
- `flight`: wing-root hinging with body bob; only wing-tagged mesh parts flap.
- `still`: supported for special poses but shipped fauna should not use it.

## Mesh animation roles

`UV2.y` is now reserved for render-only animation roles:

- 0 = body / non-animated
- 1 = locomotor limb
- 2 = wing or fin
- 3 = sensory appendage
- 4 = rigid shell
- 5 = springtail jumping organ

Locomotor tubes store root-to-toe weight in the fractional part of `UV2.y`: `1 + 0.25 * weight`. The shader extracts integer role 1 and weight separately; sockets have zero stroke and toe rings have full stroke, including genetic appendage scaling.

Procedural appendages use signed floating-point CUSTOM0.rgb as their attachment-relative offset; CUSTOM0.a flags appendages. COLOR cannot carry signed offsets in Godot.

Flexible profiles bend along an arc after appendage articulation, rotating cross-sections and normals with the spine. Isopod, shield and snail profiles reduce steering curvature to protect stiff structures; the snail shell stays rigid. The renderer derives signed curvature from visible heading change per body length travelled, smooths it, and relaxes it while stationary. INSTANCE_CUSTOM.x stores hue plus `2 * (1 + bendBucket)` for these flexible bodies, where buckets 0–30 represent curvature −1.25 to +1.25. Unpacked hue remains a valid straight pose. Other profiles retain their existing data layout.

`FaunaBodyProfiles` binds the same body style and gait tuning in live rendering and previews. Rainspine uses a restrained trunk wave growing into tail motion and diagonal support pairs. Stonebell loads the hindquarters, springs into a pitched/tucked airborne pose and braces on landing. Stiltclaw alternates support groups under a slightly rocking rigid body. Dewmantle carries a muscular compression wave under independently searching sensory stalks (including their eye caps). Loamthread alternates longitudinal shortening and radial thickening along its body. These are procedural poses without terrain-aware foot IK. Stonebell now uses a render-side crawl/hop controller that synchronizes horizontal hop displacement to its airborne interval.

For deterministic close-up review, launch with `-- --fauna-preview SPECIES_ID --output PATH`. This captures both sides at phases 0.05/0.35/0.65/0.85 with straight and opposite turn poses, plus a 60-frame motion sequence. The harvestman species ID is `stiltclaw`. These scripted poses exercise the production mesh/shader; they do not prove live steering or ground contact.

Use `--fauna-preview all` for all 17 species or `rest` for the eleven following the first six. Motion sequences now travel over fixed quarter-body-length markers at 0.6 body lengths/second, using the same distance clock as production. Add `--speed-review` for 0.2 and 0.8 body lengths/second, with a stop after frame 45. Frames represent 1/30 second each.

`--fauna-motion-check --output PATH` separately exercises the actual renderer and interpolation at multiple speeds across all species, including vertical swimming, held animals and teleport resets, and writes `distance-clock.json`.

## Distance and stance

`FaunaGait.Advance` adds exactly `shownDistance / bodyLength * cyclesPerBody` each render frame (3-D distance, including vertical swimming). Ground locomotion has no idle advancement; other families add their independent idle beat. Smoothed speed affects body activity, not travelled phase, so stops no longer cause extra footsteps and cadence caps no longer lose distance.

Walking half-stroke is `dutyFactor / (2 * cyclesPerBody)` in model units. During the linear stance sweep its fore/aft velocity cancels world translation on straight, level travel. Limb length and body amplitude no longer shrink this stride at low speeds. Authored cycles were retuned to keep these calibrated strokes within the limbs' practical reach. The existing lift/sweep tuning remains useful for articulation outside this calibrated fore/aft walking stroke.

This is procedural ground contact, without terrain-aware foot IK. Turns, trunk deformation and sloped terrain can still introduce some foot drift. Generic hoppers advance their pose by travel distance. Stonebell instead crawls below the hop threshold, with its foot stroke calibrated to actual shown distance, and follows buffered simulation targets in distinct crouch/flight/landing cycles at higher speed. Its body remains at the launch point during grounded stance. Simulation position remains authoritative for ecology.

## Tuning workflow

Tune cadence first, then contact timing, then amplitude:

1. Adjust `cyclesPerBody`, `maxHz`, and `fullSpeed` until the feet/wings feel matched to travel speed.
2. Adjust `dutyFactor` so stance vs swing feels appropriate.
3. Adjust `limbSweep` and `limbLift`.
4. Add `bodyWave` and `bob` last. Large values here are the quickest route back to "rubber toy" motion.

Appearance/material work is intentionally separate from this branch's first pass.

## Stonebell close-up material and anatomy

Stonebell now uses a denser procedural toad mesh: a broad jaw with a mouth seam and nostrils, raised brows, bronze irises with horizontal pupils, low parotoid glands, muscular folded hind legs, four foredigits and five hind digits. Digit geometry carries the same attachment origin as its parent limb so the hop pose keeps the feet together. Seeded dermal bumps add silhouette detail across the back.

The toad-only material samples object-space mottling, fine grain and surface relief across head, body and limb UV islands. Inherited hue, ornament density and pattern strength choose earthy olive/brown/gray palettes, marking scale, contrast and occasional dorsal striping. Its pattern seed derives from those traits rather than the transient MultiMesh slot, so culling and population changes do not change an individual's markings. The existing four seeded morph variants also change body proportions and bump placement.

Idle respiration and throat pulses run independently of the locomotion clock and fade while moving. Hops extend the hind legs at launch, gather the feet during flight and reach the forelegs toward landing. These remain render-only procedural poses; terrain-aware foot placement is not implemented. Slow travel uses a diagonal crawl with 2.2 cycles per body length and 70% stance, matching the planted-toe sweep to shown translation. Above 0.9 body lengths/second it hops; it returns to crawling below 0.5 after landing. Hops hold horizontal position during stance, lock the landing at take-off and finish even if the simulation target stops. Hop rate is 1.8�4 cycles/second. Mode changes retain the shown position and crawl any remaining lag away, avoiding a snap.

`--fauna-preview stonebell --output ABS_DIR` additionally captures six inherited appearance samples, 90 deterministic idle frames and 120 travel frames (a complete hop at the review speed). `preview_time` overrides only the respiration clock for repeatable captures; production materials use engine time. `--speed-review` retains its existing slow/fast stop checks.

`--fauna-preview stonebell --live-motion --output ABS_DIR` records 240 frames through the actual fauna behaviour system, scheduler and production renderer: slow crawl, faster hopping, then a stop. Its JSON trace contains both simulation targets and shown transforms. `--fauna-motion-check` now asserts grounded hop position is fixed, airborne translation is exercised, crawl phase follows actual distance, and stopping/grabbing settles correctly; a passing distance clock alone does not establish contact.

## Rainspine salamander anatomy and skin

Rainspine has a low flattened head with separate small eyes, irises, pupils, mouth seam and nostrils, a slender trunk with subtle costal folds, curved limbs, four foredigits, five hind digits and a smooth tapered tail. Distal digit geometry uses its parent shoulder/hip attachment and the full calibrated foot stroke. The diagonal fore/hind phase uses those attachment positions rather than a partial front-leg blend. During straight stance, the toes retain the lateral body-wave position at touchdown and do not roll with the trunk; the upper limbs continue to follow its axial motion. Turns and uneven ground still lack terrain-aware foot IK.

The salamander-only skin uses stable inherited ornament density/pattern strength to choose eight palette families (black/yellow, blue/cyan, red/dark, black/orange, green/cream, violet, cream/brown and black/ivory) and five marking families (spots, dorsal stripe, transverse bands, blotches and sparse flecks). Hue shift and pattern strength further vary the result. Texture coordinates are attached model-space positions, and pattern hashes avoid the hue channel that packs live steering: culling, draw order and steering do not reseed markings. Fine pores, subdued folds and varied roughness give the skin a moist finish. Idle breathing fades during travel.

Rainspine's normal preview includes twelve appearance samples and idle captures. `--fauna-preview rainspine --live-motion --output ABS_DIR` exercises actual fauna behaviour, scheduler interpolation and the production renderer at slow and fast travel, then a stop. Its JSON trace includes body length and cycles per body for checking phase against shown distance.
