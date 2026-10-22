# Hookthicket — bramble thicket rebuild

The `hookthicket` brake was rebuilt from a generic arching-thorn form into a bramble thicket
(*Rubus*, blackberry/raspberry), authored against a live botanical reference rather than against the
previous mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Hookthicket.cs` (new file). The old
`Hookthicket`/`HookPrickle` pair was removed from `OrganismMeshes.VisualBreakFlora.cs`; the shared
form kernel and every other species are untouched.

## Reference

The real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/hookthicket/bramble_reference.py`, which builds a bramble from the species' own
anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/hookthicket/bramble_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/hookthicket/bramble_reference.py -- --mode detail   --out <dir>
blender --background --python scripts/visuals/hookthicket/bramble_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

`--mode mesh` renders an engine mesh exported as OBJ under the same camera, light and ground, scaled
to the species' own footprint and height, so the built asset and the reference compare like for like.
The export comes from the CLI command added for the clover rebuild:

```
dotnet run --project src/Vivarium.Cli -- flora-mesh --species hookthicket --seed 42 --obj <file.obj>
dotnet run --project src/Vivarium.Cli -- flora-tiers        # authored and per-tier triangle budgets
```

## Anatomy carried over from the reference

* **Crown.** A knotted burl of old wood at the origin that every cane leaves from. A bramble is a
  perennial stool, not a single trunk.
* **Canes.** Twelve arching canes that leave the crown nearly vertically, bow over and come back down
  to the soil. The section is an uneven heptagon — a bramble cane is angular, never round — with the
  ring's centroid held on the path so the lumpiness never wanders the cane off its axis.
* **Prickles.** Stout, recurved and barbed, in rows down every cane, biggest low down where a hand
  would meet them.
* **Leaves.** Palmately compound: three or five ovate leaflets spread like fingers from the end of a
  long petiole, each with a raised midrib, a rugose surface that bulges between the lateral veins, a
  drooping tip and a sharply toothed margin. The tooth count follows the row spacing of the blade, so
  the margin reads as fine serration instead of a coarse lobe.
* **Stipules.** Narrow bracts clasping the petiole base.
* **Flowers and fruit.** Pale five-petalled flowers with a crown of stamens, and dark aggregate
  berries built from clustered drupelets.

## The primal pass

A bramble is primal in the way a bramble is primal: it propagates by tip layering. A mature cane
reaches the soil, roots where it lands, and throws up fresh shoots of its own, so the thicket spreads
by making copies of itself. That behaviour is now in the geometry: the rooted tips carry new shoots
in a vivid young green against the near-black old wood, and the berries carry a faint crimson light
in their hearts after dark — the berries use the nodule marker the night shader already understands,
so only the fruit glows and the thicket stays black.

## Budget

| tier | triangles | note |
|------|-----------|------|
| authored | 58 150 | 5 morph variants are built from this |
| near (0) | 58 146 | not LOD-thinned |
| mid (1) | 30 393 | |
| far (2) | 10 462 | |

The shared top-tier cap is 100 000. A bramble's near read is its prickles and its palmate leaves:
without them the brake is a tangle of bare wires, so `FloraTopTriangleCapFor` gives the species its
own near-field budget of 64 000 and the extra triangles are only paid for by the instances the camera
is close to.

## Notes for anyone touching this again

* The cane's cross-section is an uneven heptagon. An uneven ring pulls its own centroid off the
  path, so the section's centroid is measured once and taken back out: the cane keeps its lumpy
  section *and* stays on its axis. Without that, the cane's extracted centreline drifts and the
  "mature cane returns to the soil" behaviour breaks.
* The cane bases are buried in the litter, so the mesh is allowed to sit 2 cm below the soil. Clamping
  the mesh to the surface instead flattens the base rings and pulls the cane axes out of line.
* The `BrambleTube` ring layout matches `Primitives.Tube` on purpose: seven sides plus one duplicated
  seam vertex, with the ring's longitudinal UV reaching 1 at the tip. The flora refinement checks
  recover the cane's centreline from that layout, so changing it breaks those checks.


## Verification

* `dotnet test tests/Vivarium.Sim.Tests --filter "FullyQualifiedName~Hookthicket|FullyQualifiedName~FloraRefinement"`
  — the cane sweep, the mature-cane-returns-to-soil and the young-cane-still-rising behaviours all
  hold for the new geometry.
* Specimen captures (front, close, underside, backlit) in `build/hookthicket-review/final/`; the
  matched Blender renders are in `build/hookthicket-review/blender/`.
* Anatomy, budget and the reference workflow: `docs/hookthicket-bramble-rebuild.md`.
