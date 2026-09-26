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
- `idleHz`: cadence that remains with little/no translation. Keep at zero for ordinary walkers.
- `maxHz`: hard cadence ceiling.
- `fullSpeed`: body lengths/second at which deformation reaches full amplitude.
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

Procedural appendages still use vertex COLOR.rgb as their attachment-relative offset, so the shader can rotate/deform them around the root without a skeleton.

## Tuning workflow

Tune cadence first, then contact timing, then amplitude:

1. Adjust `cyclesPerBody`, `maxHz`, and `fullSpeed` until the feet/wings feel matched to travel speed.
2. Adjust `dutyFactor` so stance vs swing feels appropriate.
3. Adjust `limbSweep` and `limbLift`.
4. Add `bodyWave` and `bob` last. Large values here are the quickest route back to "rubber toy" motion.

Appearance/material work is intentionally separate from this branch's first pass.
