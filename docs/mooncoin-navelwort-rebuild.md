# Mooncoin — navelwort rebuild

The `mooncoin` groundcover was rebuilt from a fan of dished saucers on wiry tubes into a peltate
silver-sage mat, authored against a live botanical reference rather than against the previous
mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Mooncoin.cs` (new file). The rest of
`OrganismMeshes.cs` is untouched apart from the one-line dispatch:

```csharp
if (sp.Id == "mooncoin") Mooncoin(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height);
```

The superseded `Mooncoin` generator and its `PeltateSaucerLeaf` helper were removed from
`OrganismMeshes.cs`; both were used only by this species.

## Reference

Real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/mooncoin/mooncoin_reference.py`, which builds a creeping peltate mat from the
species' own anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/mooncoin/mooncoin_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/mooncoin/mooncoin_reference.py -- --mode fantasy   --out <dir>
blender --background --python scripts/visuals/mooncoin/mooncoin_reference.py -- --mode blade     --out <dir>
blender --background --python scripts/visuals/mooncoin/mooncoin_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

## Anatomy carried over from the reference

* **Blades.** Peltate and orbicular: the petiole meets the underside at the centre of the blade,
  so the blade is a shallow dish with a low collar around the attachment, a margin that lifts
  again, and seven to eight broad, shallow, rounded lobes.
* **Venation.** Primary veins leave the pip and run out to the lobe tips, raised on both faces
  because a vein makes the blade locally thicker. They are modelled, not painted.
* **Petioles.** Long, slender, arching stalks that leave the runner low and lift the blade clear
  of the mat.
* **Runners.** Prostrate stolons that hug the soil, swell into beaded rooting nodes, fork through
  a collar rather than an intersection, and are clothed in fine root hairs.
* **Nodes.** Every node carries a tuft of adventitious roots diving into the soil and, on about
  half of them, a papery bract folded against the runner.
* **Flowers.** A minute umbel of small florets on a slender peduncle above the mat.

## The fantasy layer

The mat is a xerophytic silver-sage:

* a dry farina bloom over the blade, so the leaf is dust rather than varnish,
* a pale waxy boss at the pip,
* a cool moonlace flush in the hollows between the radiating veins,
* a minority of blades carry a bleached silver sector, as if moonlight had caught one side of the
  mat,
* and the runners are bronze-olive rather than green.

## How it differs from Coinrunner

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
* the petiole thins as it climbs and flares where it opens into the blade's boss, and the boss
  swallows the petiole's tip;
* every node carries its tuft of adventitious roots and its papery bract.

## Shared-file changes

**`game/Shaders/flora.gdshader`** — one new additive case, `leaf_pattern == 7`, "glaucous
farina". Nothing existing was renumbered.

* a dry, dusty farina tone instead of a wet cuticle,
* `ridge = venation * 0.05`,
* `rough = clamp(rough + 0.22, 0.50, 0.95)` — matte,
* `leaf_spec` dropped to ~0.07 for this pattern, because a farina-coated blade is dust, not
  varnish.

**`game/Render/FloraSurfaceProfiles.cs`** — the Mooncoin row only:

* `Leaf("mooncoin")` → top `(.105,.128,.112)`, back `(.20,.21,.185)`, type `0`, veins `.34`,
  sheen `.18`, transmission `.04`,
* `LeafPattern("mooncoin")` → `7` (moved off the shared `4` fleshy-bloom case),
* `Variegation("mooncoin")` → `0` (moved off the shared `2` silver wash).

Two reasons for those two rows:

* the shared chlorophyll transmission tint paints a round blade lime green, and a glaucous,
  farina-coated xerophyte leaf barely transmits light, so Mooncoin keeps almost none of it;
* the shared silver variegation is a radial noise field in the shader's own UV space, which on a
  round blade reads as camouflage blotches. Mooncoin carries its silvering in vertex pigment
  instead (farina bloom, pale palmate veins, a bleached sector on some blades).

## Budget and tests

The authored mesh is 28,635 triangles at tier 0, inside the engine's default 30,000 per-species
top-tier cap, so the game renders it at full detail with no LOD thinning.

`FormTests.RoundLeafIsFiniteNonDegenerateAndWithinTriangleBudget` was raised from a hard-coded
`15000` to `OrganismMeshes.FloraTopTriangleCap` — the cap the engine already enforces on the
tiered build.

## Captures

`build/mooncoin-review/`

* `final/`, `final-close/`, `final-underside/` — the current authored mesh.
* `blender/mooncoin_reference.png` — the real plant, for comparison.
* `blender/mooncoin_fantasy.png` — the silver-sage fantasy variant.

Capture command:

```
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
    --path game -- --specimen mooncoin --output build/mooncoin-review/<pass> [--close]
```
