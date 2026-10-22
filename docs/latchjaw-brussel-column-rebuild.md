# Latchjaw — brussel-column rebuild

The `snaptrap` species (named **Latchjaw**) was rebuilt from a low rosette of traps on radiating
petioles into an upright column: a stout stalk with the jaw-leaves set close around it, overlapping
the way a Brussels sprout stalk carries its buds, and one large fleshy trumpet flower opening at the
top of that column.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Snaptrap.cs` (new file). The old `SnapTrap`
generator was removed from `OrganismMeshes.VisualBreakFlora.cs`; the shared form kernel and every
other species are untouched.

## Reference

The reference is generated locally in Blender 5.2 by
`scripts/visuals/snaptrap/brussel_reference.py`:

```
blender --background --python scripts/visuals/snaptrap/brussel_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/snaptrap/brussel_reference.py -- --mode detail   --out <dir>
blender --background --python scripts/visuals/snaptrap/brussel_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

`--mode mesh` renders an engine mesh exported as OBJ under the same camera, light and ground, scaled
to the species' own footprint and height, so the built asset and the reference compare like for like.
The export comes from the CLI command added for the clover rebuild:

```
dotnet run --project src/Vivarium.Cli -- flora-mesh --species snaptrap --seed 42 --obj <file.obj>
```

The real-life references behind the form are *Brassica oleracea* var. *gemmifera*, the Brussels
sprout — a stalk carrying overlapping, cupped, veined leaves and buds set close around it — and
*Hippeastrum*, a single large fleshy trumpet bloom on a bare scape, six thick tepals with a paler
throat and a coloured throat line.

## Anatomy carried over from the reference

* **The column.** A stout stalk that **tapers hard** as it rises and **wanders** as it climbs, rather
  than standing as a straight pole. It is built from six **fibre bundles** that run up it and drift
  round it as they rise, each bundle a slightly different thickness, so the flutes never fall into a
  machined, repeating pattern. The radius **swells at the nodes** where old leaves sat.
* **The shredded stalk.** The old leaf bases sheathing the foot of the column, and the **narrow
  slivers of dead leaf base still hanging from it** — twenty-six of them, drooping and peeling off
  the stem as they fall, in the straw of dried tissue. A stripped stalk is never bare, and the
  slivers are what break the column's silhouette.
* **The bristles.** Fine hairs over the stalk. A snap trap's petiole is not smooth to the touch, and
  a few hairs catching the light are what stop the column reading as a painted pole.
* **The jaw-leaves.** Twelve of them, set close around the column and spiralling up it on a golden
  angle so they never sit in a ring. The lowest set are the largest, the top ones still tight. Each
  keeps the species' own anatomy: a pair of cupped lobes on a continuous hinge, a fringe of curved
  marginal cilia and three trigger hairs in the throat.
* **The flower.** One large fleshy trumpet at the top: a narrow throat that flares hard in its upper
  third into a bell with six soft lobes at the rim, and a rim that is **ruffled and tattered** — the
  edge wanders in and out, each column of the bell sits at its own height, and a handful of flaps of
  the margin hang loose where the edge has torn. A fleshy petal that has been open a while does not
  finish with a clean circle.

## The primal pass

The flower's throat is the one deliberate departure. It carries the nodule marker the night shader
already understands, so after dark a faint crimson light comes from inside the bloom and the throat
of the flow is the only lit part of the plant.

The traps' own colour is left to the species' palette: the lobes are green on the outside and take
the red-purple underside wash on the inside, which is what makes a snap trap read as a trap rather
than as a leaf.

## Budget

| tier | triangles | note |
|------|-----------|------|
| authored | 13 873 | |
| near (0) | 13 873 | not LOD-thinned |
| mid (1) | 7 626 | |
| far (2) | 2 496 | |

The shared top-tier cap is 100 000, so the latchjaw's near tier keeps its full detail without
needing a species budget of its own.

## Notes for anyone touching this again

* The trap's fine detail — the cilia, the trigger hairs, the hinge — is authored in **world metres**,
  not in mesh units. The old generator worked in mesh units, where the plant's radius was one unit;
  carrying those numbers over unchanged made the cilia several times the size of the trap they
  fringe.
* The stalk carries **no leaf pigment of its own**, so its colour is stated directly. The shader reads
  a vertex colour as sRGB and converts it to linear, so a stalk colour chosen to look right in the
  leaf branch comes out nearly black here.
* The species' `radiusAtMax` is its ecological footprint, not its visual width. The latchjaw is now a
  narrow column inside that footprint, so the specimen capture frames it against a much wider ground
  disc than the plant actually occupies.

## Verification

* `dotnet test tests/Vivarium.Sim.Tests --filter "FullyQualifiedName~Latchjaw"` — the new
  `FormTests.LatchjawCarriesItsJawLeavesUpAColumnUnderOneFlower` locks in the growth form: a tall
  stalk, jaw-leaves climbing it, and a bloom standing clear at the top.
* Specimen captures in `build/snaptrap-review/final/`; the matched Blender render is in
  `build/snaptrap-review/blender/`.
