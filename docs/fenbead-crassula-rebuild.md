# Fenbead — crassula rebuild

The `fenbead` groundcover was rebuilt from flat paired blades on thin stems into a clump of
bead-leaved water crassula, authored against a live botanical reference rather than against the
previous mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Fenbead.cs` (new file). The rest of
`OrganismMeshes.cs` is untouched apart from the one-line dispatch:

```csharp
if (sp.Id == "fenbead") Fenbead(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height);
```

The superseded `Fenbead` generator was removed from `OrganismMeshes.cs`; it was used only by this
species.

## Reference

Real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/fenbead/crassula_reference.py`, which builds a water-cressula clump from the
species' own anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/fenbead/crassula_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/fenbead/crassula_reference.py -- --mode fantasy   --out <dir>
blender --background --python scripts/visuals/fenbead/crassula_reference.py -- --mode clump     --out <dir>
blender --background --python scripts/visuals/fenbead/crassula_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

Photographs of *Crassula helmsii* were used to check the anatomy: the pinkish fleshy stems that
run prostrate and then arch up, the swollen node rings, the narrow club-shaped leaves in strictly
opposite decussate pairs with bronze-red tips, the fused leaf bases, and the small **four**-petalled
white flowers with a cluster of brown anthers.

## Anatomy carried over from the reference

* **The bead.** Every leaf is a three-dimensional club, not a card: lofted around its own axis with
  a keeled cross-section, narrow on a stalk at the base, widest about a third of the way up, and
  blunt at the tip.
* **Decussate pairing.** Every opposite pair is turned a quarter turn from the pair below it.
* **The connate collar.** The bases of the two leaves are fused into a ring that clasps the stem —
  the junction that makes a crassula read as a crassula.
* **The stem.** It swells into a raised ring at every node, runs prostrate and reddened over the mud
  and then arches up green.
* **The blossom.** Four white petals over a cluster of brown anthers on pale filaments, on a short
  stalk clear of the leaves. The previous mesh had five petals; *Crassula* is four-merous.
* **Buds.** A bronze-red bud at a few of the upper nodes.

## The fantasy layer

The beads are pearls. Each leaf carries a soft nacreous lustre band along its upper face and a
blush of colour at the tip, and the blossoms are pearl-white with a golden eye, so the clump reads
as a strand of wet pearls rather than as a weed.

## Junctions

* The connate collar is a real ring of tissue round the stem at every node, and the leaves rise out
  of it rather than out of the stem itself.
* The stem's node rings and the collar are one swelling, so the leaf pair looks seated on the stem.
* The blossom's stalk rises out of the stem tip, not through the leaves.

## Budget and tests

The authored mesh is about 23,700 triangles at tier 0, inside the engine's default 30,000
per-species top-tier cap, so the species needs no special cap of its own and the game renders it
at full detail.

`FormTests.PairedLeafIsFiniteNonDegenerateAndWithinTriangleBudget` was raised from the old
pre-kernel figure of `9216` to `OrganismMeshes.FloraTopTriangleCapFor("fenbead")`, which is the
engine's own per-species cap for that species.

## Captures

`build/fenbead-review/`

* `pass2/`, `pass2-close/` — the current authored mesh.
* `blender/crassula_reference.png` — the real plant, for comparison.
* `blender/crassula_fantasy.png` — the pearl fantasy variant.

Capture command:

```
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
    --path game -- --specimen fenbead --output build/fenbead-review/<pass> [--close]
```
