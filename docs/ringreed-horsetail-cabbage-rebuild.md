# Ringreed rebuild — a horsetail crossed with a skunk cabbage

**Species:** `ringreed` (archetype `plant`, shape `reed`)
**Generator:** `src/Vivarium.Sim/Geometry/OrganismMeshes.Ringreed.cs`
**Reference script:** `scripts/visuals/ringreed/skunkcabbage_reference.py`
**Captures:** `build/ringreed-review/`
**Status:** candidate, awaiting user review

## What changed

The species used to be a straight horsetail: a fluted, jointed culm with tiered whorls of
branchlets and a terminal spore cone. It now reads as a horsetail that has been crossed with a
**skunk cabbage** (*Symplocarpus foetidus*), with a **fern's fiddlehead** where the spore cone
used to be. Both parents' silhouettes are still recognisable.

## The two parents

**The horsetail** gives the skeleton:

* thick, hollow, jointed stalks divided by dark ringed collars with **black-toothed rims**;
* the stalks **ribbed** the way an *Equisetum* culm is — a ring of longitudinal ridges
  separated by deep grooves, cut into the geometry so the flutes carry the shading the whole
  way up rather than reading as a painted stripe;
* tiered whorls of drooping branchlets on the lower nodes.

**The skunk cabbage** gives the flesh:

* a crown of broad, **heart-shaped** leaves with a cordate sinus at the petiole, pale along
  the midrib and the veins that radiate from it;
* the big mottled, **pleated hood** — the spathe, the structure that in the real plant
  encloses the flowering spadix — with its rim snapping inward over what it holds.

**A fern's fiddlehead** sits where the horsetail's spore cone used to be: a tight logarithmic
crozier, still coiled, with scales down its outside, nestled in the hood's mouth.

The result is a wetland curiosity that is neither quite a horsetail nor quite a skunk cabbage.

## Organic junctions

* The **node collar** is a flared cup seated on the stalk, and the black teeth are rooted in
  its rim rather than balanced on it, so the joint reads as one grown form.
* The **branchlets** root just *below* the collar, out of the culm itself, the way a
  horsetail's branchlets emerge from under a node.
* The **hood** is a single continuous surface from the stalk out to its scalloped rim, and
  the **crozier** is placed *in the hood's mouth* — at the rim, which leans forward over the
  coil — rather than on the stalk's axis behind it.
* The **leaf's sinus** is open, the two basal lobes reaching back past the petiole, and the
  blade's plane is close to horizontal so it is read from above rather than edge-on.

## Structure of the plant

| Part | Count | Notes |
|---|---|---|
| Cabbage leaves | 7–9 | broad cordate blades, cupping and drooping |
| Stalks | 5–7 | thick, fluted with 8 ribs, 7 nodes |
| Node collars | 6 per stalk | dark rings with 9 black teeth each |
| Branchlet whorls | nodes 2–4 | 7–10 drooping branchlets per whorl |
| Spathe (hood) | 1 per stalk | pleated, mottled plum over olive |
| Crozier | 1 per stalk | a tight spiral with 16 scales |

## Budget

| Tier | Triangles |
|---|---|
| authored | 10 890 |

The species is small and the fluting and the hood are the only expensive parts, so it sits
well inside the flora system's top-tier budget.

## Files touched

| File | Change |
|---|---|
| `src/Vivarium.Sim/Geometry/OrganismMeshes.Ringreed.cs` | **new** — the whole generator |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` | one dispatch line |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.VisualBreakFlora.cs` | untouched |
| `scripts/visuals/ringreed/skunkcabbage_reference.py` | **new** — the Blender reference |
| `docs/flora_visual_review_log.md` | the species row, updated |

The species' ecology, habitat and growth are untouched.

## Verification

* `dotnet build game/Vivarium.csproj` — clean.
* `--specimen ringreed` — mature and close captures, rendering without error.
