# Glassfinger rebuild — ice-plant groundcover with an azure crystal frost

**Species:** `glassfinger` (archetype `plant`, shape `iceplant`)
**Generator:** `src/Vivarium.Sim/Geometry/OrganismMeshes.Glassfinger.cs`
**Reference script:** `scripts/visuals/glassfinger/iceplant_reference.py`
**Captures:** `build/glassfinger-review/`
**Status:** candidate, awaiting user review

## What changed

The old glassfinger was nine smooth tubes radiating from the origin, each carrying a flat
three-facet prism leaf. It read as a wire starfish with green paddles. The species now grows
the way the family actually grows.

## The real-life reference

The species is a **ground-hugging ice plant (Aizoaceae)** — the family that gives the "ice
plants" their name because of the glitter of their epidermal bladder cells. The reference is
built from the family's own anatomy rather than from any one species:

* prostrate, sprawling, repeatedly-branching shoots that hug the gravel and only rise a few
  millimetres off it;
* **opposite and decussate** leaves — every node rotates the pair 90° from the node below, so
  the blades never stack into one plane;
* succulent leaves **triangular in cross-section** with a raised dorsal ridge and a ventral keel,
  sheathed at the base where they clasp the stem, waisted above the sheath, widest about a
  quarter of the way along and tapering to an acute point;
* **nodal swellings** on the shoots, so a stem reads as a beaded succulent axis rather than an
  extruded wire;
* the family's dense coat of **epidermal bladder cells**;
* **anthocyanin flush at the leaf tips**, the red the real plant shows under strong light;
* daisy-like flowers: many narrow petals in two whorls over a ring of stamens with a yellow
  centre.

`scripts/visuals/glassfinger/iceplant_reference.py` builds that plant from scratch in Blender
and renders it three ways:

```powershell
blender --background --python scripts/visuals/glassfinger/iceplant_reference.py -- --mode reference --out DIR
blender --background --python scripts/visuals/glassfinger/iceplant_reference.py -- --mode fantasy   --out DIR
blender --background --python scripts/visuals/glassfinger/iceplant_reference.py -- --mode mesh --obj FILE --out DIR
```

`--mode mesh` imports the engine's own OBJ export so the two can be compared directly.

## The fantastical element, and the blue

The family's bladder cells are clear vesicles. Here they are grown as **azure crystal**: a
saturated cobalt veneer that spreads over the distal half of every leaf, densest along the
dorsal ridge and crowding towards the tip, with the real plant's red anthocyanin flush still
burning at the very point. The plant still reads as a drought-hardened ice plant; it just also
looks frozen from the middle out.

The blue is delivered at two scales, deliberately:

| Scale | Carrier | What it does |
|---|---|---|
| Macro | `leaf_variegation == 5` in `game/Shaders/leaf.gdshader` and `flora.gdshader` | Paints the cellular cobalt veneer over the distal blade, strongest along the ridge, with the red tip flush burning through it. Deep cobalt (linear `0.012, 0.145, 0.90`), never a pale wash. |
| Micro | Real geometry: faceted bipyramid crystal cells in `OrganismMeshes.Glassfinger.cs` | ~6–13 small cut gems per blade, a spine of them down the dorsal ridge and smaller ones scattered over the flanks, each with its own saturated azure vertex colour and a glossy cut. They catch the light as real sparkle rather than painted dots. |

`leaf_variegation` case numbers are a contract across the flora system. **5 is now azure
crystal frost**; 0–4 were already taken (none, pale veins, silver, red-purple underside,
clover chevron).

## Organic junctions

The brief was junctions over replica accuracy, so the joints carry the detail:

* the leaf **sheath swells** over the stem at its base, narrows to a waist, then opens into the
  blade — and the blade root starts *inside* the stem, so the join is never a seam;
* the stem **swells at every node** where a pair of leaves leaves it;
* side shoots leave the main shoot as their own tapering axes with their own leaf pairs, so the
  mat branches instead of radiating;
* the flower peduncle is a separate tapering axis that carries on into the calyx, so the head
  is swallowed by the flower rather than butted against it.

## Structure of the plant

| Part | Count | Notes |
|---|---|---|
| Primary shoots | 10 | 12 rings each, wandering, with node swellings |
| Leaf pairs per shoot | 6 | decussate, opposite, one pair per node |
| Side shoots | ~30% of nodes | 4 rings, 3 leaves each |
| Crown leaves | 18 | young, upright, paler |
| Outer skirt | 16 | older blades lying flat on the gravel |
| Flowers | 3–4 | short peduncles, two-whorl corolla, stamen ring |

Shoot-tip leaves are the smallest, because the tip is the newest growth; the mass of the plant
sits in the older wood near the crown.

## Budget

| Tier | Triangles | Leaves |
|---|---|---|
| authored | 58 252 | 169 |
| tier 0 (game) | 59 162 | ~200 |
| tier 1 | 30 618 | — |
| tier 2 | 9 972 | — |

Tier 0 sits well inside the flora system's top-tier budget, so the ice plant does not need a
per-species cap.

## Files touched

| File | Change |
|---|---|
| `src/Vivarium.Sim/Geometry/OrganismMeshes.Glassfinger.cs` | **new** — the whole generator |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` | one dispatch line; the old glassfinger block removed |
| `game/Shaders/leaf.gdshader` | new `leaf_variegation == 5` branch |
| `game/Shaders/flora.gdshader` | same branch for the non-profile path |
| `game/Render/FloraSurfaceProfiles.cs` | `glassfinger` → variegation 5; cooler, more translucent leaf pigment |
| `game/content/visuals/flora/glassfinger.json` | transmission 0.24, vein strength 0.05, relief 0.16 |
| `scripts/visuals/glassfinger/iceplant_reference.py` | **new** — the Blender reference |
| `docs/flora_visual_review_log.md` | the species row, updated |

## Deliberate exclusions

* **No night glow.** The crystals could carry a blue emission at night, but the flora renderer
  creates one `OmniLight3D` per luminous individual, and glassfinger is a *groundcover* that
  seeds in dense mats. A per-plant light in a mat of dozens was judged a worse trade than the
  glow is worth at the 30 FPS target. The blue reads in daylight, which is where the reference
  captures are judged.
* **The species' ecology is untouched.** Habitat, growth, spread and competition are unchanged;
  this pass is geometry and materials only.

## Verification

* `dotnet build game/Vivarium.csproj` — clean, zero warnings.
* `dotnet build src/Vivarium.Cli` — clean.
* `--specimen glassfinger` — mature, close and underside captures, all rendering without error.
* `--specimen glassfinger --game-tier --detail 1|2` — LOD tiers render cleanly.
* `--species-view glassfinger` — `VIVARIUM_SPECIESWORLD_OK checks=2 failed=0`. The camera in
  that shared-world shot happens to be behind a log, which is a known staging limitation of the
  species-view harness and not a defect in the plant.
* `dotnet test --filter "FullyQualifiedName~FloraVisualTests|FullyQualifiedName~OrganicVisualTests"`
  — **20/20 pass**. Both classes carry `glassfinger` in their `InlineData`, including the check
  that every glassfinger leaf is flagged volumetric.
* `dotnet test --filter "Suite=Flora|Suite=Bootstrap"` — 42 pass, 9 fail, 3 skip. **All nine
  failures are pre-existing.** They were verified against a clean worktree at `HEAD`
  (`8c17bad5`) with no agent changes present, which fails the same nine plus two more
  (`SlimeMoldCreepsTowardFoodThenFruitsWhenItRunsOut`,
  `DecomposersReturnFromTheSporeBankWhenLitterIsRich`). The failures are in `dewbonnet`,
  `clinglace`, `emberfan_fungus`, the climber tests, the slime-mold tests, the woody budget
  test and the death/litter test — none of them touch flora geometry, and glassfinger is not
  among them.
