# Raincup Pitcher — Sarracenia rebuild

The `pitcher_plant` groundcover was rebuilt from a ring of straight tubes with flat paddle lids into
a *Sarracenia purpurea* rosette, authored against a live botanical reference rather than against the
previous mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.PitcherPlant.cs` (new file). The rest of
`OrganismMeshes.cs` is untouched apart from the one-line dispatch:

```csharp
case "pitcher_rosette": PitcherPlant(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
```

The superseded `PitcherPlant` generator was removed from `OrganismMeshes.VisualBreakFlora.cs`; it
was used only by this species.

## Reference

Real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/pitcher_plant/pitcher_reference.py`, which builds a Sarracenia clump from the
species' own anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/pitcher_plant/pitcher_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/pitcher_plant/pitcher_reference.py -- --mode fantasy   --out <dir>
blender --background --python scripts/visuals/pitcher_plant/pitcher_reference.py -- --mode trap      --out <dir>
blender --background --python scripts/visuals/pitcher_plant/pitcher_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

Photographs of *Sarracenia purpurea* were used to check the anatomy: the narrow foot, the trumpet
that only opens in its top third, the erect kidney-shaped hood leaning forward over the throat, the
red areolate net enclosing pale windows, the hairy hood margin, and the collapsed brown remains of
last year's pitchers around the crown.

## Anatomy carried over from the reference

* **The trap.** Narrow at the foot, opening into a trumpet only in its top third, with a thin,
  smooth, slightly flared rim. The mouth is never a filled disc: the rim rolls over from the outer
  skin into the descending inner wall.
* **The throat.** The inner wall is darker than the outer skin, descends into the cup and closes on
  a rounded floor.
* **The hood.** A broad, erect lid standing over the mouth and leaning forward so that its throat
  stays open underneath it. It narrows as it rises, is notched at the front, and its free margin is
  waved. The hood carries the **areolate net**: a lattice of veins enclosing pale windows, painted
  in the red form and the yellow-green form the other way round.
* **The keel.** A thin vertical wing down the back of the tube.
* **Old remains.** Two to four collapsed, torn, brown traps ring the crown — the thing that makes a
  Sarracenia clump read as a clump rather than a bouquet.
* **The flower.** One tall, leafless scape carrying a single nodding flower with five spreading
  sepals over five hanging petals.

## The fantasy layer

This is the Raincup, and it is not after prey. The cup keeps a skin of cold rain that never quite
runs off, lying mirror-still deep in the throat, and the plant has grown its nectar-guide hairs and
its hood fringe as fine crystalline dew needles instead of plain bristles, so the whole trap is rimed
with a cold light that gathers on the wet interior and the pale areolate windows.

## Organs and how they are flagged

The trap walls and the hood carry their pigment in **vertex colour** rather than in the shared
procedural tissue, because the shared ochre mottle fights the Sarracenia areolate net that is the
whole character of the species. `UV2` then separates the surfaces:

| Surface | `UV2.y` | Result |
|---|---|---|
| Outer skin and hood outer face | `0` | matte waxy tissue, longitudinal fibre |
| Throat and hood inner face | `-6` | glossy wet tissue |
| The rain in the cup | `-4` | mirror surface |

No new shader case was taken: this reuses the existing `pitcher_tissue` markers and the shared
living-epidermis path.

## Junctions

* The rolled lip is a continuous half-round band that runs from the outer skin, over the top of
  the rim and down into the throat, so the outer wall and the inner wall are one surface.
* The hood's margin, the keel and the crown all meet the tube they grow from rather than hovering
  near it.
* The old remains spring from the same crown as the live traps.

## Budget and tests

The authored mesh is about 40,000 triangles at tier 0. That is over the engine's default 30,000
per-species top-tier cap, so — following the precedent the clover rebuild set — this species asks
for a richer near-field mesh:

```csharp
// OrganismMeshes.VisualBreakFlora.cs
"coinrunner" => 36000,   // the clover-and-coin mat argument
```

The trap and its hood are a single organ and must never be half-dropped, so the trap's spans are
registered through `MeshData.EssentialSpans` and survive every LOD tier whole. Only the crystalline
fringe is allowed to thin out.

## Captures

`build/pitcher-review/`

* `final/`, `final-close/`, `final-underside/` — the current authored mesh.
* `blender/sarracenia_reference.png` — the real plant, for comparison.
* `blender/sarracenia_fantasy.png` — the Raincup fantasy variant.
* `blender/sarracenia_trap_detail.png` — one trap, large in frame.

Capture command:

```
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
    --path game -- --specimen pitcher_plant --output build/pitcher-review/<pass> [--close]
```
