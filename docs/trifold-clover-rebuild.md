# Trifold — white clover rebuild

The `trifold` groundcover was rebuilt from a paddle-and-propeller approximation into a clonal white
clover (*Trifolium repens*) sward, authored against a live botanical reference rather than against
the previous mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Trifold.cs` (new file, the rest of
`OrganismMeshes.cs` is untouched apart from the dispatch line).

## Reference

Real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/trifold/clover_reference.py`, which builds a white clover mat from the species' own
anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/trifold/clover_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/trifold/clover_reference.py -- --mode fantasy   --out <dir>
blender --background --python scripts/visuals/trifold/clover_reference.py -- --mode leaf      --out <dir>
blender --background --python scripts/visuals/trifold/clover_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

`--mode mesh` renders an engine mesh exported as OBJ under the same camera, light and ground, so the
built asset and the reference can be compared like for like. The export comes from the new CLI
command:

```
dotnet run --project src/Vivarium.Cli -- flora-mesh --species trifold --seed 42 --obj <file.obj>
dotnet run --project src/Vivarium.Cli -- flora-tiers        # authored and per-tier triangle budgets
```

The export reverses the face order on the way out: the engine emits Godot's winding, where a
triangle's right-hand normal points into the surface, and OBJ expects the opposite. Without the
reversal every leaf blade imports as a back-facing sheet and renders black.

## Anatomy carried over from the reference

* **Runners.** Prostrate stolons creep across the soil and root at the nodes. The runner is beaded:
  a rooting node is visibly thicker than the internode beside it.
* **Petioles.** Long, arching stalks that leave the runner low and lift the blade clear of the mat.
  The stalk swells where it leaves the node and flares again where the leaflets spring from it.
* **Leaflets.** Obcordate blades — widest above the middle, narrowing to a cuneate base — with a real
  apical cleft between two rounded lobes, a finely serrate margin, a raised midrib keel, a drooping
  apex and a pale chevron watermark across the upper face.
* **Junctions.** Each leaflet sits on its own short petiolule around the stalk tip, and the three
  blades of a palm are set at slightly different pitches, so no two joints read as the same glued
  corner.
* **Stipules.** The paired membranous bracts that sheath every petiole base.
* **Heads.** Globose heads of papilionaceous florets on long peduncles, cream at first and blushing
  with age.

## Authoring in real metres

The clover is built in world metres and converted to the engine's mesh frame by `AppendWorld`, which
applies the inverse of the renderer's `(radius, height, radius)` instance scale and re-derives the
normals with the inverse transpose. The result is that the plant renders at the size and proportion
it was drawn at, instead of inheriting the mesh frame's vertical squash. Leaflet relief is expressed
as fractions of the leaflet's half width, so the fold and droop keep their real proportions at any
leaf size.

## The fantastical pass

Real white clover does throw the occasional four-leaflet sprig, so those are kept — about one group
in six carries a fourth leaflet. The chevron watermark is the one deliberate departure: it is painted
by a dedicated shader path (`leaf_variegation == 4` in `game/Shaders/flora.gdshader`) rather than by
the generic pale-vein lattice, because the generic lattice breaks into a hard white net once a
leaflet is only a few centimetres across. The same band drives a faint mint nocturnal glow through
emission mask mode 7, so the marking is the only part of the plant that lights up after dark.

## Budget

| tier | triangles | note |
|------|-----------|------|
| authored | 37 764 | 12 morph variants are built from this |
| near (0) | 37 764 | not LOD-thinned |
| mid (1) | 20 070 | |
| far (2) | 6 786 | |

The shared top-tier cap is 30 000 triangles, which is enough for a tree or a shrub but not for a mat
of several hundred small leaflets: at that cap the near tier thins the clover into scattered pads
instead of a continuous sward. `OrganismMeshes.FloraTopTriangleCapFor` therefore lets a species ask
for a richer closest mesh, and `trifold` asks for 40 000. The extra triangles are only paid for the
instances the camera is close to; the mid and far tiers are ratios of the top tier as before.

## Verification

* `dotnet test tests/Vivarium.Sim.Tests --filter "FullyQualifiedName~trifold|FullyQualifiedName~NightFlora"`
  — 12 passed.
* `GeometryLodTests.EveryFloraSpeciesHasThreeParametricTiersWithinBudget` now checks each species
  against the cap it was actually given.
* New checks in `tests/Vivarium.Sim.Tests/FormTests.cs` cover the authored budget, that the near tier
  is not LOD-thinned, and that the leaflets carry a midrib line and a full sward of blade vertices.
* Specimen captures (front, close, underside, backlit) in `build/trifold-review/final/`.
* The Blender comparison renders in `build/trifold-review/blender/`.

Unrelated failures in the wider suite at the time of writing (moss tier ratios, ecology, water and
integration tests) come from concurrent work elsewhere in the tree and were not introduced here.
