# Glassrush rebuild — a horsetail made of glass

**Species:** `glassrush` (archetype `plant`, shape `reed`)
**Generator:** `src/Vivarium.Sim/Geometry/OrganismMeshes.Glassrush.cs`
**Reference script:** `scripts/visuals/glassrush/horsetail_reference.py`
**Captures:** `build/glassrush-review/`
**Status:** candidate, awaiting user review

## What changed

The old glassrush was a fan of smooth green dowels with a few pale rings for joints, and a
velvet pod on some of them. The species now grows the way a horsetail grows.

## The real-life reference

The reference is the **great horsetail** (*Equisetum telmateia*), built in
`scripts/visuals/glassrush/horsetail_reference.py`:

* a compact fan of unbranched, jointed, hollow culms rising from a basal clump, pale and
  glaucous rather than grassy green;
* at every node a **sheath** — a short collar clasping the culm — whose upper rim is cut into
  a ring of **black, upward-pointing teeth**. The teeth are the great horsetail's clearest
  diagnostic and they are what makes a joint read as a joint at a glance;
* whorls of fine, simple, **jointed branchlets** springing from the nodes of the fertile culms
  and drooping away from the stem;
* a foot of old, brown, papery sheaths where the culms leave the mud;
* the cut top of a culm showing its **hollow bore**.

```powershell
blender --background --python scripts/visuals/glassrush/horsetail_reference.py -- --mode reference --out DIR
blender --background --python scripts/visuals/glassrush/horsetail_reference.py -- --mode fantasy   --out DIR
blender --background --python scripts/visuals/glassrush/horsetail_reference.py -- --mode mesh --obj FILE --out DIR
```

## The fantastical element

The horsetail is made of **glass**. The culms are hollow and faintly translucent, the black
teeth are cut from black crystal, and the species' faintly outlandish cattail-like seed spike
is a smoky glass cylinder with a pale crown and a haze of fine fibres. Every diagnostic
feature of the real plant is kept — only the material of the thing is changed.

The seed spike is deliberately kept rather than replaced. The species' approved review note
calls for "a few faintly outlandish cattail-like seed pods on mature shoots", and the horsetail's
own terminal **strobilus** would have been the literal answer but a different plant. Keeping the
approved cattail pod and glazing it is the smaller change.

### How it differs from Ringreed

`ringreed` is already a horsetail in this game, and it is the fluted one: ribbed culms, tiered
drooping whorls of four-angled branchlets, a terminal spore cone. The glassrush is the smooth,
glassy, black-toothed one, and it carries the cattail spike instead of a strobilus. The two
should never be mistaken for each other.

## Organic junctions

* The **node sheath** is a collar that clasps the culm and flares at the rim, and the teeth
  stand out of that rim — a real joint, not a painted band.
* The **cut top** of every culm shows the hollow bore, so the stem reads as a tube and not a
  dowel.
* The **branchlets** are jointed in their own right and droop away from the stem.
* The **foot of the clump** is a ring of old, brown, papery sheaths, which is what a horsetail
  clump actually looks like at the mud line.
* The **oldest culms** in the middle of the clump are the tallest and the palest; the young
  ones at the edge are shorter and darker.

## Structure of the plant

| Part | Count | Notes |
|---|---|---|
| Culms | 26–34 | jointed, hollow-looking, glaucous; the middle ones tallest |
| Nodes per culm | 7–9 | each with a sheathing collar and a ring of 9–14 black teeth |
| Branchlet whorls | on fertile culms | 6–9 fine, jointed, drooping branchlets per node |
| Seed spikes | every sixth culm | smoky glass, pale crown, a haze of fibres |
| Old basal sheaths | 11 | brown and papery, at the mud line |

A young plant (`--juvenile`) carries fewer culms, no spikes and a single whorl of branchlets.

## Budget

| Tier | Triangles |
|---|---|
| tier 0 (game) | 33 494 |
| tier 1 | 15 334 |
| tier 2 | 5 990 |

It sits well inside the flora system's top-tier budget, so it does not need a per-species cap.

## Files touched

| File | Change |
|---|---|
| `src/Vivarium.Sim/Geometry/OrganismMeshes.Glassrush.cs` | **new** — the whole generator |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` | one dispatch line; the old `Glassrush` removed |
| `scripts/visuals/glassrush/horsetail_reference.py` | **new** — the Blender reference |
| `docs/flora_visual_review_log.md` | the species row, updated |

The species' ecology, habitat and growth are untouched. The leaf profile already treated it as
a grass (leaf type 5, parallel striations), so no material change was needed.

## Verification

* `dotnet build game/Vivarium.csproj` — clean.
* `--specimen glassrush` — mature, close and juvenile captures, all rendering without error.
* `--specimen glassrush --game-tier --detail 1|2` — LOD tiers render cleanly.
