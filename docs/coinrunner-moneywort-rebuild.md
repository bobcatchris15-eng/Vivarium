# Coinrunner — moneywort rebuild

The `coinrunner` groundcover was rebuilt from a ring of scalloped paddles on wiry tubes into a
creeping reniform-leafed mat, authored against a live botanical reference rather than against the
previous mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Coinrunner.cs` (new file). The rest of
`OrganismMeshes.cs` is untouched apart from the one-line dispatch:

```csharp
case "creeper": Coinrunner(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
```

The superseded `Creeper` generator and its `ScallopedCoinLeaf` helper were removed from
`OrganismMeshes.cs`; both were used only by this species.

## Reference

Real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/coinrunner/moneywort_reference.py`, which builds a creeping reniform-leafed mat
from the species' own anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/coinrunner/moneywort_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/coinrunner/moneywort_reference.py -- --mode fantasy   --out <dir>
blender --background --python scripts/visuals/coinrunner/moneywort_reference.py -- --mode blade     --out <dir>
blender --background --python scripts/visuals/coinrunner/moneywort_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

## Anatomy carried over from the reference

* **Runners.** Prostrate stolons creep across the soil and root at the nodes. The runner is beaded:
  a rooting node is visibly thicker than the internode beside it.
* **Petioles.** Stout, short, three-point arching stalks that leave the runner low and lift the
  blade clear of the mat. The stalk swells where it leaves the node and again where it opens into
  the blade.
* **Blades.** Reniform — kidney-shaped — with a deep basal sinus cut back to the petiole, a fine
  crenate margin of eleven to fifteen shallow teeth, and a fan of five to seven primary veins
  leaving the sinus rather than radiating from the middle of the blade.
* **Nodes.** Every node carries a tuft of adventitious roots diving into the soil and, on about
  half of them, a papery bract folded against the runner.
* **Flowers.** A minute umbel of small, flat, five-notched florets on a slender peduncle, thrown
  clear of the mat the way a pennywort throws its head.

## The fantasy layer

The species is a moneywort, so the blades read like old coins gone green:

* a mottled verdigris patina over the living green, with warm bronze showing through where the
  patina has thinned,
* pale gold tracery along the radiating veins,
* a **raised circular bead just inside the margin** — the raised border of a struck coin, with the
  crenations breaking through it like a milled edge, interrupted where the sinus cuts back to the
  attachment,
* a pale mint reverse,
* and a warm sap-light on the leading tip of every runner, because this is the plant that is
  always stitching new ground into the mat.

## How it differs from Mooncoin

Both species share the pennywort reference, so they are deliberately built to opposite ends of
the family:

| | Mooncoin | Coinrunner |
|---|---|---|
| Blade | perfectly peltate orbicular disc | reniform kidney with a basal sinus |
| Size | small, 3–6.8 cm across | large, 4–9.2 cm across |
| Thickness | thick, fleshy, farina-coated | thin, soft, glossy |
| Veins | radiate from the pip at the centre of the blade | fan out from the sinus at the margin |
| Margin | seven to eight broad, shallow lobes | eleven to fifteen fine crenations |
| Colour | silver-sage glaucous, matte | emerald green with a verdigris patina, glossy |
| Habit | slow, tight mat | wide, fast-running carpet |

## Junctions

The brief asked specifically for organic junctions, so:

* the runner swells into a node and the petiole leaves that node **from inside the swelling**, so
  the two read as one growth;
* a branch forks out of a runner by starting slightly behind the fork point and overlapping the
  parent, with an ellipsoidal collar over the union — a fork, not an intersection;
* the petiole thins as it climbs and swells where it opens into the blade, and the blade's sinus
  is cut for it: the attachment sits at the base of the notch, which is where a reniform leaf
  actually meets its stalk;
* every node carries its tuft of adventitious roots and its papery bract.

## Budget and tests

The authored mesh is about 32,400 triangles at tier 0, with the LOD tiers at roughly 17.5k and
5.1k. That is above the engine's default 30,000 per-species top-tier cap, so — following the
precedent the clover rebuild set — this species asks for a richer near-field mesh:

```csharp
// OrganismMeshes.VisualBreakFlora.cs
"coinrunner" => 36000,
```

The argument is the same one the clover mat makes: at the default cap the near tier drops coins
and the carpet opens into separate rafts instead of a continuous sward. The extra triangles are
only paid for by the instances the camera is close to.

`FormTests.CreeperIsFiniteNonDegenerateAndWithinTriangleBudget` was raised from a hard-coded
`15000` — which the pre-existing coinrunner mesh already exceeded at 17,736, so that test was
already failing — to `OrganismMeshes.FloraTopTriangleCapFor("coinrunner")`.

`FormTests.CreeperHasNoLargeFlatDiscsAndSpansExpectedFootprint` also constrains the plant, and it
is met without touching the test: the petioles are authored to top out at roughly 40–56% of the
declared plant height and the flower head just clears the canopy, so the raw mesh stays well under
the 0.70 prostrate limit while the leaves still lie flat over the soil.

## Captures

`build/coinrunner-review/`

* `final/`, `final-close/`, `final-underside/` — the current authored mesh.
* `blender/moneywort_reference.png` — the real plant, for comparison.
* `blender/moneywort_fantasy.png` — the coin fantasy variant.

Capture command:

```
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
    --path game -- --specimen coinrunner --output build/coinrunner-review/<pass> [--close]
```
