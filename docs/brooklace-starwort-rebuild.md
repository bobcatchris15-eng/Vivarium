# Brooklace — water starwort rebuild

The `brooklace` groundcover was rebuilt from flat paddles on wiry tubes into a creeping
water-starwort mat, authored against a live botanical reference rather than against the previous
mesh.

Source: `src/Vivarium.Sim/Geometry/OrganismMeshes.Brooklace.cs` (new file). The rest of
`OrganismMeshes.cs` is untouched apart from the one-line dispatch:

```csharp
else if (sp.Id == "brooklace") Brooklace(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height);
```

The superseded `Brooklace` generator was removed from `OrganismMeshes.cs`; it was used only by this
species.

## Reference

Real-life reference is generated locally in Blender 5.2 by
`scripts/visuals/brooklace/callitriche_reference.py`, which builds a creeping water-starwort mat
from the species' own anatomy and renders it under matched light:

```
blender --background --python scripts/visuals/brooklace/callitriche_reference.py -- --mode reference --out <dir>
blender --background --python scripts/visuals/brooklace/callitriche_reference.py -- --mode fantasy   --out <dir>
blender --background --python scripts/visuals/brooklace/callitriche_reference.py -- --mode clump     --out <dir>
blender --background --python scripts/visuals/brooklace/callitriche_reference.py -- --mode mesh --obj <file.obj> --out <dir>
```

Photographs of *Callitriche stagnalis* were used to check the anatomy: the dense mat of small
spatulate leaves in rosettes on thin thread-like runners, the glossy fleshy blades rounded at the
tip, and the pale runners spreading over wet mud.

## Anatomy carried over from the reference

* **The leaf.** A glossy, keeled spatulate blade: narrow on a stalk at the base, widest about half
  way, rounded at the tip, with a translucent margin.
* **The runner.** Thin, pale and thread-like, creeping over wet mud, swelling into a ring at every
  node and lifting slightly as it goes so that its tip reaches the surface.
* **The leaf pair.** Opposite, rising from the node ring.
* **The rosette.** The runner's end opens into a star of broader leaves lying flat at the surface.
* **The blossom.** Four pale lilac petals with a white throat and a golden eye, on a short stalk in
  the leaf axil.

## The fantasy layer

The plant's name is its trick. The rootlets are not roots but **lace**: each thread branches again
and again into finer white strands that knit the mud together, and the leaves are glassy and
translucent at their margins the way wet starwort leaves are.

## Junctions

* The leaf pair and the lace both leave the same swollen node ring on the runner.
* The runner thickens where it leaves the crown, so the mat reads as one plant rather than several.
* The rosette's leaves all spring from the runner's tip, which swells to carry them.

## Budget and tests

The authored mesh is about 20,100 triangles at tier 0, inside the engine's default 30,000
per-species top-tier cap, so the species needs no special cap of its own and the game renders it
at full detail.

`FormTests.BrooklaceIsFiniteNonDegenerateAndWithinTriangleBudget` is new: the species had no
triangle guard of its own before, and now that it is authored in world metres it does.

## Captures

`build/brooklace-review/`

* `pass2/`, `pass2-close/` — the current authored mesh.
* `blender/callitriche_reference.png` — the real plant, for comparison.
* `blender/callitriche_fantasy.png` — the lace fantasy variant.

Capture command:

```
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
    --path game -- --specimen brooklace --output build/brooklace-review/<pass> [--close]
```
