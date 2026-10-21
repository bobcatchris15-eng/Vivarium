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
| `glassfinger` (ice plant) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Glassfinger.cs` | `scripts/visuals/glassfinger/iceplant_reference.py` | `docs/glassfinger-iceplant-rebuild.md` | `build/glassfinger-review/` |
| `plumegrass` (pampas) | glassfinger session | `src/Vivarium.Sim/Geometry/OrganismMeshes.Plumegrass.cs` | `scripts/visuals/plumegrass/pampas_reference.py` | `docs/plumegrass-pampas-rebuild.md` | `build/plumegrass-review/` |

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
  instances the camera is close to.
* **The tree will not always compile.** With several sessions editing species files at once, an
  in-flight edit can leave `OrganismMeshes.cs` unbuildable for a while. Do not "fix" another
  session's file — it will be overwritten anyway. `scratch/cb_build.ps1` mirrors the sources into
  `scratch/cb`, stands a placeholder in for any file that is mid-edit, builds there and deploys the
  two assemblies the Godot renderer loads, so the specimen renderer keeps working.
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
