# Plumegrass rebuild — a tall grass with a chromatic ladder of plumes

**Species:** `plumegrass` / "Plumestep grass" (archetype `plant`, shape `pampas`)
**Generator:** `src/Vivarium.Sim/Geometry/OrganismMeshes.Plumegrass.cs`
**Reference script:** `scripts/visuals/plumegrass/pampas_reference.py`
**Captures:** `build/plumegrass-review/`
**Status:** candidate, awaiting user review

## What changed

The old plumegrass was a handful of smooth tubes with two blobs of wire "plume" on each and a
spray of flat blades. The species now grows the way a real pampas does.

## The real-life reference

The reference is built from **pampas grass** (*Cortaderia selloana*) and the tall-grass family
around it, in `scripts/visuals/plumegrass/pampas_reference.py`:

* a crown of long arching leaves, each one strongly **KEeled** — a V-shaped cross-section folded
  along a pale midrib, the way a grass blade folds, with a distinct white midrib line up the face;
* sheathing leaf bases overlapping into a crown, and a few **dead straw-coloured old leaves** at the
  bottom of the tussock;
* tall **flowering culms** that tower over the foliage, lightly jointed at the nodes and sheathed
  where they leave the crown;
* on each culm, a **ladder of panicles** set at intervals — the 'steps' the species is named for;
* each panicle built the way a real one is: a central **rachis** carrying whorls of arching
  **branchlets**, each branchlet bearing small **spikelets** and a spray of long, silky
  **stigmas**, the branchlets longest low down and shortest at the tip, which is what gives a plume
  its soft, tapering feather-duster silhouette.

```powershell
blender --background --python scripts/visuals/plumegrass/pampas_reference.py -- --mode reference --out DIR
blender --background --python scripts/visuals/plumegrass/pampas_reference.py -- --mode fantasy   --out DIR
blender --background --python scripts/visuals/plumegrass/pampas_reference.py -- --mode mesh --obj FILE --out DIR
```

`--mode mesh` imports the engine's own OBJ export so the two can be compared directly.

### A note on proportions

The species is 0.88 m tall and only 0.32 m across, so it cannot be the wide fountain a real pampas
tussock makes. The reference therefore leans on *tall* grasses as much as on pampas: a narrow crown
of steeply rising blades under tall bare culms, in the manner of *Miscanthus* and *Stipa*. The
foliage is kept inside the species' footprint on purpose — a full-width pampas fountain would
overrun its neighbours by more than twice.

## The fantastical element, and the ladder

A real pampas carries one silvery plume at the top of each culm. Here the panicles are **steps**,
set at intervals down the culm, and each step is a different band of the spectrum:

| Step (low → high) | Colour |
|---|---|
| 1 | deep indigo |
| 2 | cyan |
| 3 | spring green |
| 4 | gold |
| 5 | rose |

So a culm reads as a chromatic staircase climbing out of the foliage: a deep indigo tuft low down
where the leaves hide it, then cyan, green, gold, and a rose crown at the tip. Each step's plume
leaves the culm at an angle and to one side, so the treads read as separate steps rather than one
continuous column.

The colour is delivered at two levels:

* **Baked** — the filament pigment carries the step's own colour.
* **Live** — `UV2.x = 1.28` marks the prismatic plume in `game/Shaders/flora.gdshader`, where the
  view angle rotates the pigment the way a thin film does. Each step glints differently as the
  plant turns.

The prismatic plume is a **new band**, carved out of the existing pampas band: the old
`UV2.x in (1.20, 1.30)` plain-straw plume is now `(1.20, 1.26)`, and `(1.26, 1.30)` is the
prismatic one. Nothing that used the old band changed.

## Organic junctions

* The leaf **sheath swells** over the stem at its base, then the blade opens out of it, and the
  keel runs the whole length as a raised fold rather than a painted line.
* The culm is **jointed**: a slight ring swell at each node.
* The culm carries a **sheath** where it leaves the tussock, with the blade springing from it.
* The plume's **rachis is the culm continued**, so a step grows out of the stalk rather than
  sitting on it.

## Structure of the plant

| Part | Count | Notes |
|---|---|---|
| Basal leaves | 46 | arching, keeled, pale midrib, swept in a fountain |
| Dead old leaves | 7 | straw-coloured, at the very bottom of the tussock |
| Flowering culms | 3 | sheathed at the base, jointed, leaning slightly |
| Plume steps per culm | 5 | at 36 %, 53.5 %, 70 %, 86.5 % and 100 % of the culm |
| Whorls per panicle | 6 | branchlets longest low, shortest at the tip |
| Branchlets per whorl | 14 | flat feather-like vanes, not wires |
| Spikelets | every third branchlet | small dark seed clusters |
| Stigmas per branchlet | 1 per side | long, silky, the plume's haze |

A young plant (`--juvenile`) opens only its first two steps and two culms.

## Budget

| Tier | Triangles |
|---|---|
| authored | 40 588 |
| tier 0 (game) | 43 302 |
| tier 1 | 22 890 |
| tier 2 | 7 794 |

The plume's near read is its filaments — a pampas panicle is thousands of fine vanes and stigmas,
and at the default 30,000 cap the near tier strips them back until the ladder of plumes reads as a
sparse starburst instead of a soft feather. The species therefore asks for a richer near-field
mesh through the per-species cap mechanism, `OrganismMeshes.FloraTopTriangleCapFor("plumegrass") =
44000`, alongside `trifold` (40,000), `coinrunner` (36,000) and `veilfern` (52,000). The extra
triangles are only paid for by the instances the camera is close to.

The plume's whorls, branchlets and stigmas all scale with `m.Lod`, so the LOD tierer can strip the
filament detail down rather than the whole plant, and tier 2 still reads as a chromatic ladder.

Footprint: 1.4 units of the unit-radius frame, i.e. about 0.22 m in the world against the
species' 0.16 m radius.

## Files touched

| File | Change |
|---|---|
| `src/Vivarium.Sim/Geometry/OrganismMeshes.Plumegrass.cs` | **new** — the whole generator |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` | one dispatch line |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.NightFlora.cs` | the old `Pampas` generator removed |
| `src/Vivarium.Sim/Geometry/OrganismMeshes.VisualBreakFlora.cs` | a per-species near-field triangle cap for the plume filaments |
| `game/Shaders/flora.gdshader` | new prismatic plume band `UV2.x in (1.26, 1.30)` |
| `game/Render/FloraSurfaceProfiles.cs` | `plumegrass` → grass leaf type, parallel striations, single leaf sheet |
| `scripts/visuals/plumegrass/pampas_reference.py` | **new** — the Blender reference |
| `docs/flora_visual_review_log.md` | the species row, added |

The species' ecology — habitat, growth, spread, competition — is untouched. This pass is geometry
and materials only.

## Verification

* `dotnet build Vivarium.sln` — clean, zero warnings.
* `dotnet test --filter "FullyQualifiedName~FloraVisualTests|FullyQualifiedName~OrganicVisualTests"`
  — **20/20 pass**.
* `dotnet test --filter "Suite=Flora|Suite=Bootstrap"` — 42 pass, 9 fail, 3 skip. **All nine
  failures are pre-existing**: the same nine that a clean worktree at `HEAD` (`8c17bad5`) fails
  (`dewbonnet`, `clinglace`, `emberfan_fungus`, the three climber tests, the slime-mold network
  test, the woody budget test and the death/litter test). None of them touch flora geometry and
  plumegrass is not among them.
* `--specimen plumegrass` — mature, close and juvenile captures, all rendering without error.
* `--specimen plumegrass --game-tier --detail 0|1|2` — LOD tiers render cleanly, and tier 2 still
  reads as a chromatic ladder rather than a bare stick.
* `--species-view plumegrass` — `VIVARIUM_SPECIESWORLD_OK checks=2 failed=0`. That harness shot is
  a wide island overview rather than a close-up, which is a staging limitation of the species-view
  camera, not a defect in the plant.
