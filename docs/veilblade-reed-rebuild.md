# Veilblade rebuild — a woven curtain of reed blades

**Species:** `veilblade` (archetype `plant`, shape `veilblade_curtain`)
**Generator:** `src/Vivarium.Sim/Geometry/OrganismMeshes.Veilblade.cs`
**Reference script:** `scripts/visuals/veilblade/reed_reference.py`
**Captures:** `build/veilblade-review/`
**Status:** candidate, awaiting user review

## What changed

The old veilblade was five fans of eight flat cards on smooth stalks, with five lollipops on
wires above them. The species now grows the way a tall wetland reed grows.

## The real-life reference

The reference is a **giant reed** (*Arundo donax*) / common reed (*Phragmites australis*),
built in `scripts/visuals/veilblade/reed_reference.py`:

* a dense clump of long, broad, arching blades, each one rising from a **sheathing base** that
  wraps the culm;
* a blade that leaves the sheath at a set angle, arcs **gently** over, and finishes
  **drooping** — the curve of a grass blade, not the inward curl of a vase;
* a **wide, thin** strap of blade: a shallow midrib fold on a broad section, so the blade
  reads as a flat leaf of grass rather than a keel or a gutter;
* each blade **twisted** as it runs out, and still **rolled** at the tip the way a monocot leaf
  unrolls from its sheath;
* wiry, jointed culms that rise clear above the foliage and end in a **feathery panicle** of
  small spikelets.

```powershell
blender --background --python scripts/visuals/veilblade/reed_reference.py -- --mode reference --out DIR
blender --background --python scripts/visuals/veilblade/reed_reference.py -- --mode fantasy   --out DIR
blender --background --python scripts/visuals/veilblade/reed_reference.py -- --mode mesh --obj FILE --out DIR
```

## The fantastical element, and the veil

A real reed blade has a smooth margin. Here the margin grows a fine comb of marginal
**filaments** — the kind cattail and iris carry, and that people once wove — and the filaments
of neighbouring blades interlace. The curtain of blades is therefore literally **woven**, which
is the plant's name made good. The panicles above it trail a long **silk** of seed filaments
that hangs and drifts.

The filaments are modelled as flat tapered ribbons rather than tubes: a hair is a hair at any
distance, and a ribbon costs a fraction of what a solid tube would at this count.

## Organic junctions

* The **sheathing base** is a short collar wrapping the stem the blade springs from, so the
  blade emerges from something rather than starting in mid-air.
* The **midrib** runs the whole length of the blade as a pale ridge in a shallow fold. The
  fold depth is a *fraction* of the blade's width rather than a share of it, so a wide blade
  stays a wide thin strap instead of turning into a gutter.
* The **rolled tip** is a real geometric roll: the section curls and shrinks over the last tenth
  of the blade, so the tip is a scroll rather than a point.
* The blade **twists** along its length, so no two blades present the same face to the light.
* The **arc** is integrated from a turning angle rather than faked with a pair of stacked
  curves, so it is one long, even sweep. The earlier version curled the tip back *inward*,
  which made the clump read as a vase; the curve is now the other way round, and the tips hang
  out and down.
* The fertile **culms** are jointed, and each joint is a visible collar on the stem.
* The panicle's **rachis** is the culm continued, so the plume grows out of the stem.

## Structure of the plant

| Part | Count | Notes |
|---|---|---|
| Fans | 5 | clumps of their own, so the curtain has depth |
| Blades per fan | 17 | the inner ones stand up, the outer ones lie further over |
| Marginal filaments | 18 per edge | leaning forward and lying along the blade |
| Fertile culms | 5 | wiry, jointed, standing clear above the curtain |
| Panicle whorls | 9 | branchlets longest low down, shortest at the tip |
| Spikelets | 3–4 per branchlet | small dark clusters |
| Seed silk | per spikelet | a long filament trailing and drifting |

## Budget

| Tier | Triangles |
|---|---|
| tier 0 (game) | 62 038 |
| tier 1 | 31 866 |
| tier 2 | 11 150 |

The near read of this plant is the fringe on the blade margins, so the filaments are built at
full detail and the LOD tierer drops whole blades rather than thinning them. It sits inside the
flora system's top-tier budget, so it does not need a per-species cap.

## Files touched

| File | Change |
|---|---|
| `src/Vivarium.Sim/Geometry/OrganismMeshes.Veilblade.cs` | **new** — the whole generator |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` | one dispatch line |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.VisualBreakFlora.cs` | the old `Veilblade` removed |
| `scripts/visuals/veilblade/reed_reference.py` | **new** — the Blender reference |
| `docs/flora_visual_review_log.md` | the species row, updated |

The species' ecology, habitat and growth are untouched. The leaf profile already treated it as
a grass (leaf type 5, parallel striations, and the highest transmission of any species, which
suits a wet-ground blade), so no material change was needed.

## Verification

* `dotnet build game/Vivarium.csproj` — clean.
* `--specimen veilblade` — mature and close captures, all rendering without error.
* `--specimen veilblade --game-tier --detail 1|2` — LOD tiers render cleanly.
