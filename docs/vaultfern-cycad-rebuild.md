# Vaultfern rebuild — a cycad under a vault of ribs

**Species:** `vaultfern` (archetype `plant`, shape `microcanopy_vaultfern`, woody shrub layer)
**Generator:** `src/Vivarium.Sim/Geometry/OrganismMeshes.Vaultfern.cs`
**Reference script:** `scripts/visuals/vaultfern/cycad_reference.py`
**Captures:** `build/vaultfern-review/`
**Status:** candidate, awaiting user review

## What changed

The old vaultfern was a stout tree-fern trunk under an umbrella of fronds whose pinnae were
divided again and again, the way a fern frond is. The species now grows the way a **cycad**
grows.

## The real-life reference

The reference is a small, short-trunked cycad — the dwarf-zamia kind of plant, not a tree fern —
built in `scripts/visuals/vaultfern/cycad_reference.py`:

* a stout, **barrel-shaped caudex** armoured in the spiral rhombic **leaf scars** a cycad keeps
  from every frond it has ever shed, each scar a raised plate with a boss, and the plates laid on
  a **golden-angle helix** so they march around the trunk rather than banding it;
* a crown of stiff, arching **pinnate fronds** springing evenly from the caudex crown;
* each frond a **rachis with a spiny basal petiole** and regular opposite pairs of **entire,
  leathery pinnae** — a cycad leaflet is never divided again the way a fern pinna is — each with a
  raised midrib, overlapping like shingles;
* **circinate vernation**: the newest fronds still stand as tight coiled **fiddleheads**, the way
  a cycad pushes a frond up and only then lets it unfurl.

```powershell
blender --background --python scripts/visuals/vaultfern/cycad_reference.py -- --mode reference --out DIR
blender --background --python scripts/visuals/vaultfern/cycad_reference.py -- --mode fantasy   --out DIR
blender --background --python scripts/visuals/vaultfern/cycad_reference.py -- --mode mesh --obj FILE --out DIR
```

`--mode mesh` imports the engine's own OBJ export so the two can be compared directly.

## The fantastical element, and the vault

A real cycad throws its fronds out in a rosette. Here the crown's fronds are the **ribs of a
vault**: every rib springs from the caudex to the same ring, arches down and out, and meets its
neighbour, so the canopy closes into an **arcade of foliage** over the scarred caudex instead of a
rosette. The plant is armoured: the caudex's plates are cold stone, the pinnae a hard blue-green
with a pale mineral midrib, and the pale bud sheaths of the fiddleheads are the one bright thing on
it.

No new shader case was needed. The bud sheath is marked with the existing nodule organ band
(`UV2.x = 1.35`), which already means "keep your own vertex pigment and take a glossier cut", so
the sheath keeps its ivory colour instead of taking the species' green. The pinnae's midrib is
carried by the vertex colour and the leaf shader's own midrib term.

## Organic junctions

* **The pinnae are dense and every frond is capped.** The pinnae run from just above the spiny
  petiole to 99 % of the rachis, in 30 opposite pairs, and each frond carries a **terminal
  leaflet** at its very tip, set at a slight angle to the rachis so it reads as a leaflet rather
  than as a thickening of the stem. A cycad frond does not end in a bare point, and a bare
  rachis tip is the first thing that reads as "procedural" at distance.
* The **spiny basal petiole** is the first thing a hand meets: prickles on the lower rachis,
  stepping down to bare stem above.
* The pinnae **shingle** over one another along the rachis, and the pinna base is decurrent, so
  the frond reads as a continuous blade and not a comb of separate teeth.
* The **fiddlehead** rises from the crown on its own stub, so the plant's growth is visible rather
  than implied.
* The caudex's plates are a **continuous spiral** of relief on the trunk, not a texture wrapped
  over a smooth cylinder.

### A bug worth recording

The scar pattern did not appear at all at first, and then appeared as flat horizontal bands. Two
separate mistakes were involved:

1. **The ring was sampled on the cell edges.** With the plate lattice in `(around, up)` and the
   ring sampled at `u = s / segments`, `u * cells` landed on an integer for every vertex, so the
   fractional part was the same all the way round a row and the plates collapsed into bands.
2. **The distance to the plate centre was inverted.** `frac - 0.5` is zero at the *groove* between
   two plates and half a cell at the centre, so the relief was raised in the wrong places.

The fix is to sample the ring evenly, undo the shear in the plate's own frame, and measure the
distance to the nearest cell centre as `min(frac, 1 - frac)`. The same two bugs were in the
Blender reference, which is why the reference caudex was a smooth bottle while the engine's was
correct.

## Structure of the plant

| Part | Count | Notes |
|---|---|---|
| Caudex | 1 | barrel, 5 rows of 4 big spiral plates, domed crown, marked woody |
| Fronds (vault ribs) | 14–18 | spring to the same ring; the tips close the arcade |
| Pinna pairs per frond | 30 | opposite, regular, entire, shingled, with a raised midrib |
| Terminal leaflet | 1 per frond | at the very tip, set at a slight angle to the rachis |
| Spiny petiole prickles | 7 per frond | on the lower rachis |
| Fiddleheads | 3 | tight logarithmic coils with pale bud sheaths |

A young plant (`--juvenile`) carries six fronds, fewer pairs and one fiddlehead.

## Budget

| Tier | Triangles |
|---|---|
| tier 0 (game) | 58 640 |
| tier 1 | 32 184 |
| tier 2 | 10 488 |

The species' near read is its pinnae, so the fronds are built at full detail and the LOD tierer
drops whole pinnae rather than thinning them. It sits well inside the flora system's top-tier
budget, so it does not need a per-species cap.

Footprint: 0.93 units of the unit-radius frame, i.e. about 1.18 m in the world against the
species' 1.27 m radius.

## Files touched

| File | Change |
|---|---|
| `src/Vivarium.Sim/Geometry/OrganismMeshes.Vaultfern.cs` | **new** — the whole generator |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` | one dispatch line |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.Midstory.cs` | the old `MidVaultfern` and `MidVaultfernCaudex` removed |
| `game/Render/FloraSurfaceProfiles.cs` | `vaultfern` → a leathery, low-transmission leaf pigment and a cold stone bark tint |
| `scripts/visuals/vaultfern/cycad_reference.py` | **new** — the Blender reference |
| `docs/flora_visual_review_log.md` | the species row, added |

The species' ecology — habitat, growth, spread, competition, canopy and shade — is untouched.
This pass is geometry and materials only.

## Verification

* `dotnet build Vivarium.sln` — clean.
* `dotnet test --filter "Suite=Flora|Suite=Bootstrap"` — 26 pass, 9 fail, 2 skip. **All nine
  failures are pre-existing**: the same nine that a clean worktree at `HEAD` (`8c17bad5`) fails
  (`dewbonnet`, `clinglace`, `emberfan_fungus`, the three climber tests, the slime-mold network
  test, the woody budget test and the death/litter test). None of them touch flora geometry and
  vaultfern is not among them.
* `--specimen vaultfern` — mature, close and juvenile captures, all rendering without error.
* `--specimen vaultfern --game-tier --detail 1|2` — LOD tiers render cleanly and the vault still
  reads at the middle tier.
* `--species-view vaultfern` — `VIVARIUM_SPECIESWORLD_OK checks=2 failed=0`.

### A note on the shared build

`scratch/cb_build.ps1` stands a placeholder in for any species file another session was
mid-way through writing, then deploys the built assemblies into the live tree. Its placeholders
for `OrganismMeshes.Plumegrass.cs` and `OrganismMeshes.Vaultfern.cs` were removed once those
files compiled, because a stub deploys an **empty species** into the live tree — which is exactly
what happened: a capture came back with `triangles=0`. A stub should be removed the moment its
file compiles.
