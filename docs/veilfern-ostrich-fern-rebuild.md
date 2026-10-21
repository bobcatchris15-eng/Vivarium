# Veilfern — ostrich fern rebuild

The `veilfern` understory plant was rebuilt from the shared generic-fern form into an ostrich fern
(*Matteuccia struthiopteris*), authored against a live botanical reference rather than against the
previous mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Veilfern.cs` (new file). The shared form kernel in
`Form/PinnateFrond.cs` is untouched; the veilfern carries its own anatomy, as the other
species-specific builders in the tree do.

## Reference

The real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/veilfern/ostrich_fern_reference.py`, which builds an ostrich fern from the species'
own anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/veilfern/ostrich_fern_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/veilfern/ostrich_fern_reference.py -- --mode detail   --out <dir>
blender --background --python scripts/visuals/veilfern/ostrich_fern_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

`--mode mesh` renders an engine mesh exported as OBJ under the same camera, light and ground, scaled
to the species' own footprint and height, so the built asset and the reference compare like for like.
The export comes from the CLI command added for the clover rebuild:

```
dotnet run --project src/Vivarium.Cli -- flora-mesh --species veilfern --seed 42 --obj <file.obj>
dotnet run --project src/Vivarium.Cli -- flora-tiers        # authored and per-tier triangle budgets
```

## Anatomy carried over from the reference

* **Crown and habit.** A scaly, stubby caudex holds a vase of arching fronds. Each frond leaves the
  crown nearly vertical, arches right over and hangs its tip down towards the ground, which is the
  silhouette an ostrich fern makes.
* **Pinnae.** Twenty-seven pairs per frond, sub-opposite and swept forward towards the frond tip, each
  pinna broadest a little above its base and shrinking steadily to the tip. Pinna length follows a
  spline profile rather than a straight taper, and each blade is lobed, with a raised midrib, a blade
  that falls from the midrib to the margin and a tip that curls under.
* **Croziers.** Emerging fronds come up as coiled fiddleheads wearing golden-brown chaffy scales, the
  fleece an ostrich fern pushes up through the litter.
* **Fertile fronds.** The species is dimorphic: the fertile fronds are shorter, narrower and darker
  than the sterile ones, and in autumn they turn bronze and roll their pinna margins inwards over
  rows of bead-like sori.

## The fantastical pass

The shuttlecock is the one deliberate departure. An ostrich fern overwinters as a tight brown
shuttlecock of fertile fronds standing in the middle of the crown; here that shuttlecock stands proud
of the green fronds all year, and its beads of sori carry a faint mint light after dark. The same
light sits in the heart of each crozier's coil. The beads and the coil are marked with the nodule
marker the night shader already understands, so only those marks light up — the fronds stay dark.

The fertile fronds are marked outside the living-leaf shading path, so their bronze reads as dead
tissue: no leaf transmission, no vein pattern, a matte surface.

## Budget

| tier | triangles | note |
|------|-----------|------|
| authored | 50 553 | 5 morph variants are built from this |
| near (0) | 50 537 | not LOD-thinned |
| mid (1) | 27 743 | |
| far (2) | 9 060 | |

The shared top-tier cap is 30 000 triangles. A fern's near read *is* its pinnae — dozens of finely
divided blades per frond, nine fronds deep — and at the shared cap the near tier drops pinnae until
the fern reads as a handful of broad leaves. `OrganismMeshes.FloraTopTriangleCapFor` therefore lets a
species ask for a richer closest mesh, and `veilfern` asks for 52 000. The extra triangles are only
paid for by the instances the camera is close to; the mid and far tiers stay ratios of the top tier.
The veilfern has five morph variants, so the whole near tier costs less than the clover's.

## Verification

* `dotnet test tests/Vivarium.Sim.Tests --filter "FullyQualifiedName~Veilfern"` — 5 passed.
* `FormTests.VeilfernSpeciesIsFiniteNonDegenerateAndWithinBudget` now judges the authored budget
  against the cap that species actually holds itself to.
* `GeometryLodTests` checks each species against the cap it was actually given.
* Specimen captures (front, close, underside, backlit) in `build/veilfern-review/final/`; the
  integrated world capture reports `VIVARIUM_SPECIESWORLD_OK checks=2 failed=0`.
* The Blender reference and the matched engine render are in `build/veilfern-review/blender/`.

The moss tier-ratio failures still reported by `GeometryLodTests` (`pearl_cushion_moss`,
`starmoss`, `velvetweave_moss`) predate this work and are unrelated to it: those species sit far below
the shared cap, so their tier path is identical before and after this change.
