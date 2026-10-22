# Ground-cover rebuild coordination — 2026-10-20

Three agents are rebuilding small ground-cover species in this tree at the same time, each against a
real-life botanical reference. A fourth session has since joined for `coinrunner`.

## The channel

**`docs/flora_visual_review_log.md` is the shared log.** Each agent adds or updates its own row in
the species table there, and writes a species-specific doc next to it. That log is what the user
reads, so it is the place to announce a rebuild and point at the detail.

| Species | Agent | Generator file | Reference script | Species doc | Captures |
|---|---|---|---|---|---|
| `trifold` (white clover) | trifold session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Trifold.cs` | `scripts/visuals/trifold/clover_reference.py` | `docs/trifold-clover-rebuild.md` | `build/trifold-review/` |
| `mooncoin` (navelwort) | mooncoin session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Mooncoin.cs` | `scripts/visuals/mooncoin/mooncoin_reference.py` | `docs/mooncoin-navelwort-rebuild.md` | `build/mooncoin-review/` |
| `coinrunner` (moneywort) | coinrunner session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Coinrunner.cs` | `scripts/visuals/coinrunner/moneywort_reference.py` | `docs/coinrunner-moneywort-rebuild.md` | `build/coinrunner-review/` |
| `pitcher_plant` (purple pitcher) | raincup session | `src/Vivarium.Sim/Geometry/OrganismMeshes.PitcherPlant.cs` | `scripts/visuals/pitcher_plant/pitcher_reference.py` | `docs/raincup-pitcher-rebuild.md` | `build/pitcher-review/` |
| `fenbead` (water crassula) | fenbead session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Fenbead.cs` | `scripts/visuals/fenbead/crassula_reference.py` | `docs/fenbead-crassula-rebuild.md` | `build/fenbead-review/` |
| `brooklace` (water starwort) | brooklace session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Brooklace.cs` | `scripts/visuals/brooklace/callitriche_reference.py` | `docs/brooklace-starwort-rebuild.md` | `build/brooklace-review/` |
| `glassfinger` (ice plant) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Glassfinger.cs` | `scripts/visuals/glassfinger/iceplant_reference.py` | `docs/glassfinger-iceplant-rebuild.md` | `build/glassfinger-review/` |
| `plumegrass` (pampas) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Plumegrass.cs` | `scripts/visuals/plumegrass/pampas_reference.py` | `docs/plumegrass-pampas-rebuild.md` | `build/plumegrass-review/` |
| `vaultfern` (cycad) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Vaultfern.cs` | `scripts/visuals/vaultfern/cycad_reference.py` | `docs/vaultfern-cycad-rebuild.md` | `build/vaultfern-review/` |
| `veilblade` (reed) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Veilblade.cs` | `scripts/visuals/veilblade/reed_reference.py` | `docs/veilblade-reed-rebuild.md` | `build/veilblade-review/` |
| `glassrush` (horsetail) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Glassrush.cs` | `scripts/visuals/glassrush/horsetail_reference.py` | `docs/glassrush-horsetail-rebuild.md` | `build/glassrush-review/` |
| `ringreed` (horsetail/skunk cabbage) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Ringreed.cs` | `scripts/visuals/ringreed/skunkcabbage_reference.py` | `docs/ringreed-horsetail-cabbage-rebuild.md` | `build/ringreed-review/` |

## House rules so the three do not fight over the same tree

1. **One species, one new generator file.** Each species owns a dedicated
   `OrganismMeshes.<Species>.cs` partial so nobody edits the 340 KB `OrganismMeshes.cs` monolith
   except for the one-line dispatch. The dispatch line is the only shared edit in that file.
2. **Give private helpers species-specific names.** The partial class is shared, so a helper
   called `AppendWorld` in two species files is a duplicate-member compile error. The glassfinger
   generator uses `AppendGlassWorld` and `GlassLobedTube` for exactly this reason.
3. **Shared shader/material files are edited additively only.** `game/Shaders/flora.gdshader`,
   `game/Shaders/leaf.gdshader` and `game/Render/FloraSurfaceProfiles.cs` are touched by everyone.
   Add a new branch/case; never renumber an existing one. Existing case numbers are a contract.
4. **Re-read a shared file immediately before editing it.** Another agent may have written to it
   seconds earlier; the edit tool fails loudly on a stale match, which is the behaviour we want.
5. **Species content and visual profiles are private.** `game/content/flora/<id>.json` and
   `game/content/visuals/flora/<id>.json` belong to that species' agent only.
6. **Build output and captures are per-species.** `build/<species>-review/`. Never write into
   another agent's capture folder.
7. **Do not commit.** The user is integrating; leave the working tree dirty and report the file list.

### Taken case numbers

`leaf_variegation` (both shaders, and `FloraSurfaceProfiles.Variegation`):

| Case | Owner | Meaning |
|---|---|---|
| 0 | — | none |
| 1 | mirrorleaf | pale veins |
| 2 | mooncoin *(formerly)* | silver-patterned |
| 3 | lanternbrush, snaptrap | red-purple underside |
| 4 | trifold | clover chevron watermark |
| **5** | **glassfinger** | **azure crystal frost** |

`FloraSurfaceProfiles.LeafPattern` is a different switch and has its own numbering; mooncoin
added case 7 there (glaucous farina). The coinrunner session took no new case number: it reuses
the plain pattern and carries its verdigris-and-bronze patina in vertex pigment.

## Notes from the sessions

* **Per-species top-tier cap.** `OrganismMeshes.FloraTopTriangleCapFor(id)` in
  `OrganismMeshes.VisualBreakFlora.cs` lets a species ask for a richer near-field mesh than the
  default 30,000. `trifold` uses 40,000, `coinrunner` uses 36,000. Both are groundcover mats whose
  near read is a dense carpet of individually modelled blades; at the default cap the tierer drops
  blades and the carpet opens into separate rafts. The extra triangles are only paid for by the
  instances the camera is close to. (The global `FloraTopTriangleCap` was raised to 100,000
  during the session, so most species now fit without an override.)
* **The tree will not always compile.** With several sessions editing species files at once, an
  in-flight edit can leave `OrganismMeshes.cs` unbuildable for a while. Do not "fix" another
  session's file — it will be overwritten anyway. `scratch/cb_build.ps1` mirrors the sources into
  `scratch/cb`, stands a placeholder in for any file that is mid-edit, builds there and deploys the
  two assemblies the Godot renderer loads, so the specimen renderer keeps working.
* **Remove a `cb_build.ps1` placeholder the moment its file compiles.** A placeholder deploys an
  **empty species** into the live tree: with the `OrganismMeshes.Vaultfern.cs` placeholder still
  in place a capture came back with `triangles=0` and the plant simply vanished. The placeholders
  for `OrganismMeshes.Plumegrass.cs` and `OrganismMeshes.Vaultfern.cs` have been removed now that
  those files compile.
* **The headless specimen run loads the Debug assembly** from
  `game/.godot/mono/temp/bin/Debug`. Build both configurations before capturing, or the capture
  silently shows the previous mesh.

## Shared conventions for the mesh work

* Real-world metres, converted to the engine frame by the species' own `Append<Species>World`.
* `m.RecordLeaf(...)` for every blade so the LOD tierer can drop whole leaves, never half of one.
* Respect `m.Lod` (radial sides, length rings, blade segments) and `m.FloraDetailLevel`.
* **Smooth-shade fleshy blades.** A lofted leaf built as a stack of independent quads shades as
  folded paper; build one shared vertex grid and accumulate the face normals into it instead.
  The glassfinger generator has this in `GlassLeaf` as the worked example.
* Organic junctions beat replica accuracy: stem-node swelling, sheathing leaf bases, and petiole
  flare that a plant would actually grow.
* Vertex colour is the only per-organ pigment channel that survives into the split stem/leaf
  materials, so it carries species-specific gradients (tip blush, crystalline tips).
* Anything that should read as its own material (a berry, a crystal, a gland) can be flagged
  through `UV2` and picked up by the shader's organ-marker branches.

## Blueprint to copy for a new species

1. `scripts/visuals/<species>/<plant>_reference.py` — Blender scene built from real botany, with a
   `--mode reference` (real plant), `--mode fantasy` (the fantastical twist) and `--mode mesh`
   (import the engine OBJ for a side-by-side). The engine OBJ comes from
   `dotnet run --project src/Vivarium.Cli -- flora-mesh --species <id> --seed 42 --obj <file>`;
   the engine frame is radius-1 in XZ and height-1 in Y, so scale by `(radius, height, radius)`.
2. `src/Vivarium.Sim/Geometry/OrganismMeshes.<Species>.cs` — the generator.
3. Capture with:
   `Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --specimen <id> --output build/<id>-review/<pass> [--close]`
   and, for the plant in the running world,
   `--species-view <id> --output build/<id>-review/world`.
4. Iterate on the capture, not on the numbers.

## The 2026-10-21 new-flora set — ten species added at once

A set of ten new species was authored in one pass, five of them in this session and five in
parallel child sessions. Design, anatomy, budgets and captures: `docs/new-flora-set.md`; the
review-log rows are in `docs/flora_visual_review_log.md`. Captures: `game/build/newflora-review/`.

| Species | Generator file | Placement group | Triangles (seed 42) |
|---|---|---|---|
| Glasslily | `OrganismMeshes.Glasslily.cs` | waterside_aquatic | 15,400 |
| Silverweed | `OrganismMeshes.Silverweed.cs` | waterside_aquatic | 37,938 |
| Coalspike | `OrganismMeshes.Coalspike.cs` | waterside_aquatic | 35,262 |
| Marrowbloom | `OrganismMeshes.Marrowbloom.cs` | terrestrial | 14,183 |
| Blackneedle | `OrganismMeshes.Blackneedle.cs` | terrestrial | 37,366 |
| Starbulb | `OrganismMeshes.Starbulb.cs` | terrestrial | 30,465 |
| Bonecup | `OrganismMeshes.Bonecup.cs` | decomposer | 18,012 |
| Embercup | `OrganismMeshes.Embercup.cs` | moss_lichen | 39,541 |
| Hollowcrown | `OrganismMeshes.Hollowcrown.cs` | woody | 56,900 |
| Tankbloom | `OrganismMeshes.Tankbloom.cs` | terrestrial | 8,164 |

### What the set adds to the shared layer

`src/Vivarium.Sim/Geometry/OrganismMeshes.NewFlora.cs` is a new shared file, and every member in
it is prefixed `NewFlora` so it cannot clash with an existing generator. It holds the lofted
blade, the cupped petal, the arcing stalk, the fine filament, the shingled scale column, the
metre-to-unit conversion and the detail budget. The organ marker is the part a later session
needs to know about:

* `NewFloraMarkOrgan(m, first, count, glass)` flags a vertex range so the shader keeps the
  pigment the organ was authored in. The vascular branch of `flora.gdshader` mixes every leaf
  towards the species' own green leaf pigment, which is right for a leaf and wrong for a petal, a
  bract, a fungus cup, a glass spine or a coal-black scale.
* `glass: false` writes `UV2.x = 1.35` — the pigment is used as authored. This is also the
  shader's marked-nodule band, so a species that asks for night emission lights these organs too.
* `glass: true` writes `UV2.x = 1.28` — the pigment is used as authored and picks up the
  view-angle prismatic shift, which is what makes cut glass read as cut glass. It never glows, so
  it is the band to use for the coal around a glowing ember.

Three species ask for night emission, and each lights a different organ, so
`NightFloraMaterials.Bind` needs a per-species mask mode: `coalspike`, `embercup` and
`hollowcrown` all use mode 2 (marked nodule).

### Tools

`scratch/isolated-sim-build.ps1` compiles one species in a private directory with the other nine
stubbed out, so parallel sessions never fight over `bin/` and `obj/`. It takes `-Name <Species>`
and an optional `-Measure <species id>`, which prints that species' triangle budget.
`scratch/preset-append.ps1` adds starter-flora entries to a preset without reformatting it, and
`scratch/pin-warmup-digest.ps1` re-pins the default-preset warm-up digest that the aquatic
canary test holds.

### Counts that moved

The roster is 62 species, not 52: moss_lichen 9, terrestrial 17, waterside_aquatic 17,
decomposer 7, woody 12. `BootstrapTests` carries those numbers. The default preset and seven
other presets now carry the new species; `ReferenceWarmupTimingTests`' golden digest was
re-pinned, as that test's own comment instructs when content moves it.

The roster now has a tree. `PilotTreeTests.PilotWoodSupportsLogHostsAndShrubsHaveNoCarryingCap`
used to assert that no flora species was tree-layer, and it now asserts that the veteran hollow
tree is the only one and that it is held to the island's area-based tree budget.

Against a pristine `HEAD` checkout the suite fails 32 of 735; with the set it fails 28 of 758.
The 23 extra cases are the per-species theories that grew with the roster, and all of them pass.
The failure set is unchanged: the moss tier budgets, the legacy save migration, the dead-flora
round trip, the coverage-layer migration, the fauna food check and the perf canaries.

## Photorealism coordination — 2026-10-21

User authorized coordination with this rebuild. Report: `docs/2026-10-21-vivarium-photorealism-research.md`; live task board: `.clanker/tasks/photorealism-20261021/PLAN.md`; handoff: `.clanker/tasks/photorealism-20261021/STATE.md`.
First wave owns only new `scripts/visuals/photorealism/` tooling and `game/Shaders/leaf_direct_light.gdshaderinc` (BRDF separation audit/candidate). No species generators, content, profiles, `leaf.gdshader`, `flora.gdshader`, or renderer edits claimed. Any need to alter shared caller contracts will be posted here first. Existing M13 leaf-shape work stays with species owners. No commits; native captures/build deployment must be serialized with existing sessions and editors preserved. Contact current chat `Locate today’s report` for coordination.

### Photorealism wave 2
Reserved new `game/App/PhotorealismReview.cs` and additive CLI branch in `game/App/Main.cs` for isolated leaf calibration; no edits to existing MaterialPortReview or species materials/generators. Native mode uses local fixture only. Python ROI comparison tooling is private under scripts/visuals/photorealism. Coordinator will take existing visual-lab-gpu.lease for native work; no other sessions/processes will be stopped.

Wave 2 finished native leaf calibration: new standalone PhotorealismReview + Main dispatch, no species/profile edits. Verified capture build/photorealism/leaf-calibration-03 (19 PNGs; exit0/source stable). Default F0 control still off. See .clanker/tasks/photorealism-20261021/WAVE2.md. GPU lease released, no owned capture running; original Godot PIDs 15536/32500/21724 remain.

Final review corrections: all frames now settle eight frames; ambient-off captures explicitly name key+fill; wrapper provenance includes revision and tool hashes. Final native validation is build/photorealism/leaf-calibration-04. Partial infrastructure accepted; physical/visual promotion still pending.

### Photorealism wave 3
Reserved existing PhotorealismReview.cs for an opt-in M03 exposure diagnostic and new PhotorealismLightReview.cs for M02 isolated-light controls. Main.cs and run-native.ps1 additive dispatch controlled by coordinator. No production EnvironmentRig/shader/profile/species edits. Private comparison scripts under scripts/visuals/photorealism. Production settings unchanged; GPU runs serialized by existing lease.

Photorealism wave3 complete: shared leaf_direct_light include now explicit F0=true at existing .055; isolated light mechanism and all3 shader consumers validated, actual production default plus static/motion eight pairs pixel-identical. No per-species/profile changes. M03 exposure diagnostic produced no coverage response; exposure unchanged. Native leases released; no commits. Details .clanker/tasks/photorealism-20261021/WAVE3.md.

### Photographic material families — 2026-10-22
User authorized implementation of documented-family research. Reserve new game/App/PhotographicFamilyReview.cs and scripts/visuals/photorealism/family-profiles.json + validate_family_profiles.py; coordinator Main.cs/run-native.ps1 additive dispatch. Existing species/profile/shader/EnvironmentRig work remains unclaimed. Build real production terrain/flora reference patch, freeze state, compare named relative-unit light/material profiles; no global physical-unit/exposure changes. Native captures serialized under existing lease. Task board .clanker/tasks/photographic-families-20261022/PLAN.md.

F05 continuation: opt-in backlight sweep in the owned PhotographicFamilyReview class and new analyze_backlight.py only. Front/back directional geometry, production/half/zero direct tissue transport and separately labelled zero-SSS control. Shared production materials, species and rig remain unclaimed. Local diagnostic turns screen-space indirect/AO off to isolate the mechanism; normal family mode retains its established behavior.

F06 continuation: reserve new PhotographicPhysicalReview.cs and analyze_physical.py. Dedicated process-only physical-unit setting, isolated Lambert/camera/light calibration; no project settings save or global rig migration. Coordinator additive Main/wrapper dispatch, serial GPU lease. Existing production lighting/species remain unclaimed.

F07 continuation: PhotographicFamilyReview opt-in wet-layer experiment, private duplicate of production terrain shader/material only. Shared terrain/water/moss/species sources remain unclaimed. Preserve absorbed moisture and submerged bed response, test exposed-soil film separately. Same GPU lease and no commits.
