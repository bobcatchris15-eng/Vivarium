# L1b - make cached-lighting candidate A look like the control (candidate A2)

## Verdict in one paragraph
Candidate A looked wrong mainly because its ambient was calibrated against an image that was inflated by sky-radiance reflections (the reflected-light source stays `Sky` once the probe is hidden), so the ambient energy was set about 10x too low and the sky specular film washed out greens and ground. Fix: reflections off, ambient energy re-derived (day 0.8 instead of 0.15), plus a cheap warm ground-bounce term added in the shader hook. The result (`candidate=a2`) has mean abs pixel difference vs control of 2.2-4.3 (A_old: 6.8-9.7), saturation 93-106 % of control (A_old 14-76 %), mean luminance 96-98 % of control, and GPU cost equal to A_old (no measurable difference).

## Diagnosis (step 1), runs `d1` (frames=20, light_probe mid + under_close; `sheet_d1_*.jpg`)
| config | mid MAD / lum / sat | under_close MAD / lum / sat / bell-rect lum |
|---|---|---|
| A_old (candidate A) | 9.69 / 99.4 % / 71.1 % | 8.83 / 93.9 % / 52.2 % / 71.9 %* |
| A, skyvis off | 14.01 / 112.1 % / 55.7 % | 18.82 / 120.6 % / 31.7 % / 194 % |
| A, ambient source Sky | 53.42 / 180.1 % / 39.6 % | 45.71 / 177.0 % / 21.7 % / 383 % |
| A, reflections off (`refl_src=disabled`) | 7.82 / 87.3 % / 101.1 % | 8.00 / 83.6 % / 81.5 % / 39.8 % |
(*bell-rect in d1 was an earlier, larger rectangle; the final rectangles are in the tables below.)

Causes, with evidence:
1. **Desaturated foliage and pale ground** (verdict items 2, 3): the sky-radiance specular. Turning reflections off alone moves saturation from 71 % to 101 % (mid) and the shadow on the ground becomes the control's dark brown (`sheet_d1_mid.jpg`). The control gets its reflections from the probe, not from sky radiance; with the probe hidden Godot falls back to the very bright HDR sky.
2. **Black bells and trunks** (item 1): bells and bark have no transmission term in `flora.gdshader`/`leaf_direct_light.gdshaderinc` (`tissue_phase_mask` is 0 for the flower organ range UV2.x 1.3-1.5, bark is `tissue_nonleaf_mask`), so in shade they receive only ambient. A_old's ambient energy (0.15 of the rig value) was tuned by whole-image luminance, which the sky specular film was inflating, so it was far too low for them, and the sky-visibility AO (`AO *= vis`, down to 0 in canopy interiors) multiplied it again. With skyvis off the bells stay dark brown (ambient only); with `amb_src=sky` they light up but everything else is 180 % bright, so the control's bell colour is neither flat ambient nor sky: it is warm light bounced from the ground (probe capture).
3. Harsh canopy contrast (item 4): same AO with no floor, on top of the too-low ambient.

## What A2 consists of
Same cached volume as L1 (nothing changed in `src/Vivarium.Sim/Repr/Light`). New or changed:
- `refl_src=disabled` (no sky-radiance specular; probe stays hidden). Free.
- Ambient flat colour (rig colour, untinted) with a re-derived energy multiplier table (x rig value): DayFraction 0.05/0.25/0.50 = 0.8, 0.72 = 1.17, 0.90 = 4.6. Calibrated so mean luminance is 96-98 % of control (never above).
- Shaped AO in `repr_skyvis.gdshaderinc` (`repr_sky_ao`): `ao = mix(floor, 1, vis^(strength*exp))`, floor 0.25, exp 0.5 (strength 1), and thin tissue (leaf lamina, petals) takes the max of visibility at +normal and -normal (light reaches thin organs from behind). Ground/rock material: floor 0, exp 1 (plain visibility, keeps the dark shadow area under canopies).
- Bounce (`repr_sky_bounce`), added to EMISSION after the final albedo: `albedo * bounceColor * (0.5 - 0.5 n.y blended 35 % to flat) * (1 - 0.5 ao)`; bounceColor = linear (0.40, 0.19, 0.08) x ambient energy x gain table (DayFraction 0.05-0.50: 0.6, 0.72: 1.2, 0.90: 1.3), woody surfaces x 0.5. The colour is a constant, warm ground brown, because the control's probe-lit bell colour is the same in a dark-brown-ground scene and a pale-ground scene (checked on `plant_specimens`).
- Fixed exposure table unchanged (1.95/1.94/1.94/1.93/1.85). Per-time tables were re-checked, not re-derived: with ambient and bounce fixed, luminance already lands at 96-98 %.
- Plumbing: per-material uniforms `repr_skyvis_shape` (vec4) and `repr_skyvis_bounce` (vec3) declared in the include with default values (shape 1,0,0,1; bounce 0), pushed by `SkyVisibilityField.SetShapes/PushBounce` onto the materials it collected (`CollectMaterials`, `RegisterGround`); `ReprEnvDriver` pushes the bounce every frame only when it moved >1 %. I used material uniforms and not new `[shader_globals]` because `game/project.godot` is outside the owned paths; promoting both to globals (two entries in project.godot) is the right move when this ships. The hooks in `flora.gdshader` and `leaf.gdshader` grew by 3 lines each (declare `repr_ao`, call `repr_sky_ao`, one EMISSION line at the end), all gated by `repr_skyvis_strength > 0`. Defaults unchanged: `--boot-test` OK, `invariants=0`.
- New demo `plant_specimens_lit` (`game/Repr/Light/PlantSpecimensLitDemo.cs`): wraps `PlantSpecimensDemo`, filters to variant legacy and the shots `shadebell_whole`, `glowbriar_under` (override `shots=a+b`), feeds the field from the legacy plants and the stage grounds when `skyvis=1`, `ps_shift=-20` moves the stage row so shadebell sits inside the probe volume.

### `candidate=a2` expands to
`sdfgi=0, ssao=0, amb_src=color, refl_src=disabled, aa=fxaa, fog=1, sky_static=1, reflprobe_static=1, exposure=fixed, skyvis=1, skyvis_strength=1, skyvis_exp=0.5, skyvis_floor=0.25, skyvis_thin=1, ground_exp=1, ground_floor=0, bounce=autoa2, bounce_wood=0.5, ambient_scale=autoa2` with tables `AmbientA2` {5:0.8, 25:0.8, 50:0.8, 72:1.17, 90:4.6}, `BounceA2` {5:0.6, 25:0.6, 50:0.6, 72:1.2, 90:1.3}, bounce colour (0.40, 0.19, 0.08), voxel 0.10 m. Extra `ReprEnv` keys: `skyvis_exp, skyvis_floor, skyvis_thin, ground_exp, ground_floor, bounce=x|autoa2, bounce_a2, bounce_col, bounce_k, bounce_wood, amb_tint, ambient_a2, exposure=auto, ps_shift`.

## What was tried and its effect (step by step)
Tuning runs used frames=20 (images only); numbers vs control (MAD / lum), mid and under_close:
| change | mid | under_close | note |
|---|---|---|---|
| A_old | 9.69 / 99 % | 8.83 / 94 % | bells black |
| reflections off, ambient 0.15 (`n15`) | 7.83 / 87 % | 8.01 / 84 % | saturation fixed, too dark |
| + ambient 1.0, vis^0.5 (`n100s05`) | 3.93 / 98 % | 4.73 / 92 % | big step: bells visible |
| + ambient 2.0 (`n200s05`) | 6.09 / 106 % | 5.17 / 100 % | bells right in luminance but pale pink, trunk dark |
| ambient 2.0 + floor 0.25 + thin (`b0`) | 6.73 / 107 % | 6.35 / 106 % | brighter, not better |
| + bounce, pale brown colour (`b1`) | 9.88 / 113 % | 11.25 / 118 % | trunk yellow, bells too pale |
| bounce colour = ground-albedo ratio, ambient 0.6, gain 1.5 (`g4`) | 4.66 / 98 % | 4.67 / 99 % | bells and trunk colour match |
| + wood bounce 0.5, ambient 0.8, bounce 0.6 (`c2`, final day setting) | 4.26 / 98 % | 4.03 / 97 % | best of the day grid |
Ablation of the final A2 (one component removed, no re-calibration, MAD / lum, `ablation.txt`):
| removed | mid | under | under_close | dusk | night |
|---|---|---|---|---|---|
| nothing (A2) | 4.28 / 98 % | 3.76 / 97 % | 4.19 / 97 % | 2.25 / 98 % | 2.17 / 98 % |
| bounce | 4.21 / 96 % | 3.76 / 96 % | 3.83 / 94 % | 2.50 / 86 % | 2.20 / 82 % |
| floor (0.25 to 0) | 4.36 / 97 % | 3.77 / 96 % | 3.95 / 95 % | 2.28 / 96 % | 2.10 / 96 % |
| thin both-sides | 4.36 / 98 % | 3.76 / 97 % | 3.91 / 97 % | 2.30 / 98 % | 2.10 / 98 % |
| exponent 0.5 (use 1) | 4.20 / 97 % | 3.78 / 96 % | 3.91 / 95 % | 2.26 / 95 % | 2.00 / 95 % |
| field + bounce (`skyvis=0`) | 4.29 / 102 % | 3.80 / 99 % | 4.32 / 100 % | 2.37 / 97 % | 2.05 / 93 % |
| bounce wood 0.5 (use 1) | 4.49 / 98 % | 3.79 / 97 % | 4.10 / 98 % | 2.35 / 99 % | 2.22 / 100 % |
| reflections on (`refl_src=sky`) | 32.6 / 145 % | 26.3 / 134 % | 36.5 / 160 % | 28.4 / 287 % | 27.1 / 330 % |
Honest reading: MAD barely separates floor, thin and exponent (they mostly lift interior luminance, so the ambient could be lower without them); they cost nothing measurable and are kept because they keep thin organs lit in deep canopy, but they are not individually proven by these numbers. The bounce is what restores bell/trunk colour and the dusk/night foliage level (luminance 82-86 % without it). The cached field itself contributes little to MAD at these scenes (the `skyvis=0` row also disables the bounce, since it is gated by the field strength), its job here is the canopy/ground contact darkening and per-voxel occlusion for the bounce weighting. Step 4 (sky reflections as a substitute for the probe, attenuated by visibility): not viable, the sky radiance is far too bright (row `reflections on`); no cheap substitute was found, the control's soft reflections are simply dropped. Not timed.

## GPU (light_probe, 1600x900 scale 0.85 tier 1, 180 frames, GPU ms; 2 sets alternating with controls, delta vs the adjacent controls in brackets)
| config | n | mid | under | night | dusk | under_close | dVRAM mid MB |
|---|---|---|---|---|---|---|---|
| control (8 runs) | 8 | 20.58 | 17.82 | 20.48 | 21.09 | 17.32 | 0 |
| A_old | 2 | 8.38 (-12.09) | 7.08 (-10.43) | 8.26 (-12.17) | 8.99 (-12.31) | 6.19 (-11.08) | -369 |
| A2 | 2 | 7.92 (-12.70) | 7.04 (-10.63) | 8.27 (-11.51) | 8.74 (-13.24) | 6.20 (-11.68) | -369 |
| all_off | 2 | 7.61 (-13.07) | 6.92 (-11.21) | 7.58 (-12.95) | 7.82 (-13.05) | 5.51 (-11.87) | -384 |
Control min/max per shot: mid 18.8-22.3, under 17.3-19.1, night 19.3-21.9, dusk 19.7-24.1, under_close 15.6-19.4 (a spread of 2-4 ms, so A2 vs A_old differences under about 1 ms are noise). A2 costs +0.01 to -0.46 ms relative to A_old, i.e. nothing measurable, well inside the 1 ms allowance. `plant_specimens_lit` (90 frames): control 11.2 / 15.0 (shadebell_whole / glowbriar_under), A_old 4.74 / 5.95, A2 4.90 / 5.07, all_off 4.38 / 5.11; shadebell inside the probe (1 set): control 13.7, A_old 5.03, A2 5.05, all_off 4.19. The field build in this demo: 260x42x60 voxels at 0.10 m, bake 73 ms, upload 16 ms.

## Pixel metrics vs control (light_probe; luminance and saturation ratios of control = 100 %; saturation = HSV S over non-background pixels; bell rect = two fixed rectangles in under_close, luminance ratio)
| shot | config | MAD | lum | sat | bell-rect lum |
|---|---|---|---|---|---|
| mid | A_old | 9.68 | 99.4 % | 71.1 % | |
| mid | **A2** | **4.26** | 98.0 % | 104.7 % | |
| mid | all_off | 31.39 | 122.4 % | 65.0 % | |
| under | A_old | 7.45 | 96.8 % | 75.9 % | |
| under | **A2** | **3.79** | 97.0 % | 105.8 % | |
| under | all_off | 36.72 | 102.6 % | 70.5 % | |
| under_close | A_old | 8.80 | 94.6 % | 51.2 % | 50.2 % |
| under_close | **A2** | **4.00** | 97.3 % | 104.7 % | 109.7 % |
| under_close | all_off | 44.50 | 127.7 % | 40.9 % | 267.7 % |
| dusk | A_old | 7.03 | 99.9 % | 53.7 % | |
| dusk | **A2** | **2.26** | 98.1 % | 94.9 % | |
| dusk | all_off | 37.65 | 302.6 % | 53.7 % | |
| night | A_old | 6.82 | 99.3 % | 13.5 % | |
| night | **A2** | **2.18** | 98.3 % | 93.3 % | |
| night | all_off | 36.41 | 365.1 % | 65.2 % | |
Bell rectangles (1600x900 px): (340,40)-(470,440) and (930,0)-(1080,300). In A_old the bell/trunk pixels average RGB (4, 2, 1) against the control's (82, 56, 34); A2 gives (105, 68, 44) (slightly brighter and a bit more orange than control).

Second scene set (`plant_specimens_lit`, legacy variant):
| shot | config | MAD | lum | sat |
|---|---|---|---|---|
| glowbriar_under (inside probe) | A_old | 5.23 | 96.9 % | 56.5 % |
| glowbriar_under | **A2** | **3.60** | 98.4 % | 104.8 % |
| shadebell_whole shifted into the probe (`ps_shift=-20`) | A_old | 7.31 | 96.1 % | 73.4 % |
| shadebell_whole shifted | **A2** | **3.63** | 97.6 % | 115.0 % |
| shadebell_whole (stage at x=20, as the demo places it) | A_old / A2 / all_off | 33.5 / 33.2 / 34.1 | 69 % / 70 % / 69 % | 153 % / 238 % / 125 % |
The last row is not a defect of A2: at x=20 the stage lies outside the reflection probe volume (a control run with the probe hidden is nearly identical, mean pixel value 107.2 vs 108.1, ground-shadow RGB within 1), so the control there is lit by sky ambient (shadow area 87 % of the sunlit ground) while every candidate has the in-probe look. No single flat ambient can match both regions; it needs a spatial ambient (volume), not tuned here. Also the glowbriar stage near the origin is inside the probe and matches.

## Visual verdict per shot (sheets `sheet_final_*.jpg` = control | A_old | A2 | all_off; `sheet_final_ps_*.jpg`, `sheet_final_ps_inprobe_*.jpg`)
- mid: A2 is close to control at sheet scale: lit tan bells, trunks brown not black, foliage warmth and saturation match, ground colour and the dark shadow under the canopy match. Residual: canopy interior a touch brighter than control; bells slightly more orange.
- under: very close (MAD 3.8); trunks and bells right.
- under_close: bells visibly lit, tan with the control's gradient; trunks a little lighter and redder than control (bell/trunk luminance 110 %); leaf interiors match. This was the worst shot for A_old.
- dusk: close; bells a little brighter than control, foliage similar, ground dark red like control. fg luminance 103 %.
- night: close; flowers keep their warm glow like the control (that glow is the control's daylight-captured probe ambient, not emission, so it does not fade at night; A2 reproduces it on purpose); trunks and foliage read a little lighter/redder than the control's near black. Saturation 93 %.
- plant_specimens glowbriar_under: matches (trunks and the dark-green nodules are lit like control). shadebell in probe: matches; saturation 115 % because the bells are slightly more orange. shadebell at x=20 (out of probe): does not match, see above.

## What could not be matched cheaply / limits
- Per-scene exposure: the fixed table is calibrated on light_probe; auto exposure in A2 over-brightens (frames=20 runs: dusk 175 %, night 134 %, day 105-107 %), so fixed stays and any new scene needs a check.
- Control behaviour outside the probe (sky-ambient look at x=20) is a different lighting regime, not reproduced.
- The control's night warmth on the flowers is a daylight-captured probe artefact; A2 imitates it with the night rows of the tables. If that artefact is fixed in the control, A2's night/dusk bounce entries should drop (without bounce, night luminance is 82 % and dusk 86 % of the present control).
- Hooks use per-material uniforms; `project.godot` globals not added (not an owned path). Bounce colour and gains are calibration data, not final design. The field does not light dynamic objects that are not in the volume, and ambient on non-flora shaders (ground shader in the real game) is not covered: the real ground has no hook, only the demo's `LitMaterial` does.
- Not verified: other species, the full game scene, animated plants, larger worlds, a real day cycle (tables are keyed at five DayFractions with nearest-key lookup).

## Files
`game/Repr/Light/SkyVisibilityField.cs`, `game/Repr/Light/LightProbeDemo.cs`, `game/Repr/Light/PlantSpecimensLitDemo.cs` (new), `game/Repr/Harness/ReprEnv.cs`, `game/Shaders/repr_skyvis.gdshaderinc`, hook lines in `game/Shaders/flora.gdshader` and `leaf.gdshader`. Scripts, raw results and sheets in `build/overhaul/L1b/` (`run.py`, `metrics.py`, `sheet.py`, `make_table.py`, `plant_table.py`, `results_*.json`, `metrics_final.txt`, `ablation.txt`, `gpu_table.txt`, `plant_gpu.txt`, `sheet_final_*.jpg`, `png/`). Validation: build clean, `--boot-test` `VIVARIUM_BOOT_OK invariants=0`, `selftest -- light` PASS (2 tests).
