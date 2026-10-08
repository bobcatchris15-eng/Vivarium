# Plan: light level, water, canopy shape

**Status: plan only — not approved for implementation (user, 2026-10-08).** Keep this document current as findings come in; start work only on an explicit go-ahead.

Follows the overnight loop on `overnight/lush-2026-10-07` (see `.claude/ledger.md`, bottom section). Targets are the photos in `docs/reference-targets/`. Every step uses the same harness: build both projects, capture twice, compare the judge sheet against the references, and gate on a mean of at least 45 fps for every judge scene.

Order: **light → water → canopy.** Light comes first because water and foliage are both judged under it. Retuning it afterwards would invalidate their tuning.

---

## 1. Light level

**Problem.** The backdrop change made the scene read as an enclosure, but brightness is uneven. Lit foliage looks right. Anything outside the key light falls toward black: cutaway strata, dry scenes, trunk sides, lichen on rock. Two causes in the code:
- Exposure is fixed (`TonemapExposure` in `EnvironmentRig.cs`).
- The fill light, ambient and minimum-light floors were added piecemeal (it02, it03, it14), so lighting has no single model.

**Approach: make the light one deliberate rig, then expose for it.**

1. **Measure before tuning.**
   - Add a luminance probe to reference mode: mean luminance, 5th and 95th percentile, and % of pixels clipped to black or white per scene, written into `reference_report.json`.
   - Compute the same numbers for the three reference photos.
   - This turns "too dark" into a number to hit, for example "5th percentile within ±X of the reference". Without it, every lighting change is judged by eye on a sheet.
2. **Replace the stacked fixes with an explicit vivarium light model** in `EnvironmentRig`:
   - **Key:** a steep LED canopy light, the existing directional light.
   - **Fill:** a soft area-like top fill, using the fill directional light at low energy and the same colour family.
   - **Bounce:** a green-tinted ambient tied to how much foliage is in view, rather than to the sky.
   - **Removed:** the per-shader light floors from it14 (strata, bark and trunk emission, which are hacks), once the rig is right.
3. **Exposure.** Try Godot's auto-exposure (`CameraAttributesPractical`) with tight min/max limits, so close shaded shots open up and lit canopy doesn't blow out. If it pumps or hunts in motion, keep fixed exposure, set from the probe numbers.
4. **Leave the day/night keys alone.** Express the new rig as multipliers on the existing `SkyKeys`, as it02 did.

**Acceptance**
- Probe percentiles for the judge scenes land within the reference band.
- sparse_dry and mixed_depth are readable.
- No judge scene looks washed back toward grey.
- The it14 emission floors are deleted.

**Risk.** Auto-exposure makes captures depend on frame history. Reference mode may need to pin exposure, or step enough frames to settle, or determinism breaks.

---

## 2. Water

**Problem.** The milky film is gone (it13), but the pond reads as dark olive with no glints, and the bed is barely visible mid-pond. Findings so far:
- The only shader actually loaded is `water_stream.gdshader`. `water.gdshader` and `water_table.gdshader` are never loaded, so they're dead code to confirm and remove.
- The material uses black albedo with emission-only colour. Godot's lighting therefore has almost nothing to work with, which is why the it14 custom `light()` glints never showed.

**Approach: rebuild the surface as a physically lit material, not an emissive one.**

1. **Restructure the shading.**
   - Real `ALBEDO`, kept small, with `ROUGHNESS` and `SPECULAR` driven by ripple slope.
   - Refraction from the screen texture (`hint_screen_texture`) with depth-based absorption: Beer–Lambert on the scene-depth difference, which replaces the current `depthf`-only tint.
   - Reflection from Godot's screen-space reflection plus the dark backdrop.

   That gives highlights from the grow light for free and lets the bed show through in proportion to the real water thickness.
2. **Glints that survive the camera angle.** The key light is near-vertical, so most camera views never sit in its mirror direction. That's why it14 saw nothing. Options, cheapest first:
   - a slightly broader, anisotropic highlight lobe
   - a tiny screen-space sparkle term on steep ripple facets
   - one or two dim "light panel" reflections painted into the reflection fallback, the way real tank photos show the light bar on the water

   Pick by testing on water_margin and cutaway_pond.
3. **Shoreline.**
   - Wet darkening and gloss on terrain within a few cm of the water line (`terrain.gdshader` already has a wet term; tie it to the water-table field).
   - A thin foam or meniscus line where water meets stems and rock.
4. **Underwater view (fauna_aquatic).** Add light shafts or caustics on the bed (an animated caustic texture projected on terrain below the water surface). Currently that camera just sees a flat lit brown bed.
5. **Clean up.** Confirm `water.gdshader` and `water_table.gdshader` are dead and delete them, so the next person doesn't tune the wrong file.

**Acceptance**
- In water_margin and cutaway_pond:
  - the bed is visible in the shallows and fades with depth
  - at least visible glints or a light-bar reflection
  - water reads as water in a still frame
- The underwater scene shows caustics.
- fps gate holds. Screen-texture refraction has a cost on the 860M, so measure it.

**Risk.** Screen-space refraction artefacts at the screen edge and around thin stems. Clamp the refraction offset, and fall back to a no-refraction tint where depth is invalid.

---

## 3. Canopy shape

**Problem.** This is the biggest remaining gap to the references and it is geometry, not shading. The tall plants are single strap blades on stems; the low plants are coin- or rosette-shaped. The references are layered:
- big broad leaves (philodendron and anthurium type) at mid-height
- fine fern fronds
- leaves overlapping at every height
- plants growing on wood (bromeliads, small vines hanging)
- moss on vertical surfaces

The it04 leaf-form tweaks (size jitter, cupping, droop) barely registered because the plant architectures themselves are too simple.

**Approach: add a few new plant forms rather than tune the existing ones.** All of this is in the existing geometry pipeline (`src/Vivarium.Sim/Geometry/OrganismMeshes.cs`, the form kernel, `FloraVisualProfile`), which already supports per-species form, LODs and variants.

1. **Large broadleaf aroid form (highest impact).**
   - Heart- or arrow-shaped blades with a real outline (sampled curve, not a polygon fan).
   - Midrib fold, wavy margin, petiole kink, and blades held at varied angles.
   - 4–9 leaves per plant at very different sizes, from new small leaves to big old ones, so leaves overlap.
   - Assign it to 2–3 existing understory species whose content descriptions fit (shadebell, embercrown and lanternbrush are candidates) and give their leaves the it08 veins and variegation.
2. **Fern frond form.** Pinnate fronds: rachis curve plus paired pinnae, with fiddlehead tips on young fronds (the veilfern / Veilfern-type species). Fronds give the fine-textured contrast that every reference photo has.
3. **Epiphytes and hangers.**
   - Let existing climbers (Clinglace, Spiralvine, Fenhook) render trailing segments that hang off logs and rocks, not only climb.
   - Add a rosette-epiphyte form (bromeliad or tillandsia shape) that places on log tops.
   - Both are render-side forms on existing species, so no sim rule changes.
4. **Layering check.** Once the forms exist, check whether the sim's populations produce mid-height layering at all. If plants are too evenly spaced or too few at 6 bio-days, that's a sim and preset question to decide separately. Don't paper over it with render-only filler again; it05 showed that's invisible and costs fps.
5. **Budget.** Keep each species within about 1.3× its current triangle count per tier, enforced by `FloraBuildTimingTests` counts. Spend triangles on silhouette and outline, not subdivision: real outlines, fewer interior vertices.

**Acceptance**
- In the judge scenes, at least half of the visible foliage area is the new broadleaf or fern forms.
- Leaves visibly overlap at several heights.
- At least one epiphyte or hanging stem appears in log scenes.
- Geometry tests stay at the known 12 failures, with new form tests added.
- fps gate holds.

**Risk**
- This is multi-iteration work, roughly 4–6 iterations.
- The form kernel has 12 failing `FormTests` already; fix or understand those first, so new form tests aren't added on a broken base.
- Changing species meshes changes geometry digests, so re-pin them deliberately.

---

## Sequencing and effort

| Step | Iterations (estimate) | Depends on |
|---|---|---|
| Luminance probe in reference mode | 1 | — |
| Light rig and exposure | 1–2 | probe |
| Water rebuild (refraction, absorption, glints) | 2 | light |
| Shoreline and caustics | 1 | water rebuild |
| Fix or triage the 12 FormTests | 1 | — (can run in parallel with light/water) |
| Broadleaf aroid form | 2 | FormTests |
| Fern frond form | 1 | FormTests |
| Epiphytes and hangers | 1–2 | broadleaf form |
| Layering review (sim/preset decision) | 1 + your call | forms |

Total is roughly 12–14 iterations at the overnight pace, so about two nights. The FormTests triage can run in its own worktree alongside the light and water work, because it touches different files.

## Decisions needed from you before starting

1. **Commit `5013da7`** (standing-dead plants fade from green): keep or revert.
2. **Auto-exposure:** acceptable in the game if it behaves, or should exposure stay fixed?
3. **Canopy realism vs. invented species:** should the new forms look like real terrarium plants (philodendron, fern, bromeliad), or stay recognisably the game's invented species with realistic construction?
