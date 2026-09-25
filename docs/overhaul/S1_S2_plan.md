# S1 form language + S2 growth models — execution plan

Baseline: `docs/baseline/v0.1.2_*`. Critic pass #0 is in `.claude/ledger.md`.
Every work unit ends with a Reference capture compared against the baseline (see CLAUDE.md `## VALIDATE`).

---

## S1 — Form-language kernel + first family migration (packet P1, dispatched)

### Problem
Every organism is assembled from `Primitives.Tube / Ellipsoid / Fan / CurvedLeaf`. Leaves are flat, single-sided
polygons with hard facets and no thickness; junctions are tube-on-tube; each species has 5 shared variant meshes,
so cloning is visible at interaction distance.

### Kernel (`src/Vivarium.Sim/Geometry/Form/`)
Pure C#, deterministic from a `ulong` seed, writing into the existing `MeshData` (same vertex-callback conventions
as `Primitives` so shaders keep working).

| Unit | What it builds | Biologically meaningful parameters |
|---|---|---|
| `Axis` | curved centre line (spline) with frames (parallel transport, no twisting flips) | length, base angle, gravitropic droop, phototropic bend, twist per unit length, wobble amplitude |
| `LeafBlade` | a double-sided leaf swept along an `Axis`: width profile (ovate/lanceolate/cordate/reniform/linear) and **camber** (cross-section arc), midrib fold (V), cupping, tip curl, edge ruffle, asymmetry of the two halves, margin (entire/crenate/serrate/lobed via profile modulation, not vertex noise), real thickness at the midrib tapering to the edge, petiole | shape preset + all of the above as floats |
| `SoftTube` | tapered organ along an `Axis` with elliptical cross-section, flare at the base, and optional **blended junction** onto a parent (shared ring + fillet, no intersecting cylinders) | radii profile, ellipticity, flare, junction smoothing |
| `Sheet` | variable-width ribbon or membrane over a curved axis (grass blades, petals, ribbon organs, fungal lobes); lobed and porous outlines | width profile, lobing, thickness, curl |
| `Variation` | per-individual parameter jitter drawn from a seed within species ranges (the tool that removes cloning) | ranges defined per species |

Rules:
- Shape comes from parameters (camber, fold, profile, droop), **never** from random vertex noise as the main source.
- Double-sided: the top and bottom faces are separate surfaces carrying a UV2 flag (the S5 material pass uses it for back translucency).
- Budget: one leaf ≈ 60–120 triangles. The kernel must accept a detail level (segments) so triangle counts can be traded against perf.

### Per-individual variation (removes visible cloning)
- Raise `MorphVariants` from 5 to 12 for the migrated species (more meshes, same draw grouping).
- Vertex-shader deformation in `flora.gdshader` driven by the per-instance seed (`INSTANCE_CUSTOM.w`): a low-frequency bend and scale of leaf tips around the local axis, weighted by vertex height/UV so bases stay anchored. The sim is unaffected.

### First family migration (the visible outcome of S1)
The broadleaf families (critic items 2 and 3):
- `roundleaf` (dichondra, watercress) → reniform/cordate cupped blades on arching petioles, unequal sizes, the youngest folded.
- `pairedleaf` (bacopa) → opposite ovate succulent-ish leaves along a decumbent stem.
- `herb` (ornamental_herb) → irregular basal crown of cambered, drooping lanceolate leaves plus a leaning flower stalk. Flowers become shallow cups of cambered petals with thickness; no flat stars. Include bud/open/spent stages chosen by seed.
- `trifoliate` flower (clover) → the same flower-head treatment; leaflets become cambered, folded obcordate blades.

### Acceptance (P1)
- Build passes; `Suite=Geometry` (new) plus the `Flora`, `World` and `Bootstrap` suites pass.
- New tests: determinism (same seed → identical mesh), finite and non-degenerate triangles, both faces present, triangle budget per leaf.
- Reference capture: `species_dichondra`, `species_watercress`, `species_bacopa`, `species_ornamental_herb`, `species_clover` and `ground_vegetation` are visibly different. No coin discs and no flat stars. Leaves show curvature and thickness.
- FPS on those scenes is not more than 10% below baseline.

---

## S2 — Growth models for moss, lichen and slime mold (design; packet P2 follows P1)

### Why the current model fails visually
Colonies are made of many independent `FloraIndividual` cells (≤90 per colony), each drawn as a separate mesh
instance. That is intrinsically a field of chips. Slime mold is a parent→child bud tree with no network dynamics,
so veins are equal-width straight sticks. Rendering alone cannot fix this: the simulation must hold a *continuous*
state that the renderer can draw as a continuous surface.

### New sim primitive: `CoverageLayer` (replaces colony cells)
A sparse, tiled raster of **1.5 cm cells** (tiles of 32×32 are allocated only where there is growth), one layer per
colonial growth form, stored in the sim and serialised, so it stays deterministic and authoritative. Per cell:
`species (byte)`, `biomass/thickness`, `age`, `vigour` (recent growth rate), `moisture stress`.
It is coupled to the existing coarse fields (moisture, light, nutrients, substrate) by sampling them.

Growth rules per form (all cellular automata or reaction-diffusion on the raster, run at the flora cadence):

**Moss (clonal mat).**
- Spread happens at the rim: an empty cell gets colonised with a probability ∝ Σ vigour of its neighbours × substrate suitability × moisture.
- Rate is anisotropic: downslope and toward moist ground spread faster, so wet banks fill in and dry ridges stay sparse.
- Interior cells thicken toward a species max (cushion forms: high max, low spread; carpet: the reverse).
- Age increases continuously. Old cells under sustained dryness go dormant (browning), and long dormancy kills them, leaving holes that later recolonise, giving patchiness with history.
- Competition: a cell holds one species. At a boundary the invader succeeds with probability based on the two species' vigour, so fronts meet and stall into visible seams.

**Lichen.**
- Very slow radial growth only on permitted substrate (rock, log, bark). Growth is roughly linear in radius over time.
- *Crustose:* Eden-model growth with low anisotropy, which gives irregular round patches. The rings (`age` bands) come for free from the age field.
- *Foliose:* rim growth is concentrated at lobe tips (tip cells get vigour from a Laplacian-growth / DLA-style tip bias), giving lobed rosettes.
- *Fruticose:* a raster footprint plus sparse upright branch instances (the only part rendered as meshes).
- Colonies meet and stop, leaving a dark prothallus line at the boundary (a render cue from neighbour-species checks).

**Slime mold (Physarum).** A two-level model:
1. **Foraging front:** active plasmodium is also a `CoverageLayer` (a thin sheet). The front advances into cells along a chemo-attractant gradient: a diffusing food-signal field from detritus, recomputed at a coarse cadence. The front is broad and fan-shaped, and cells behind the front thin out.
2. **Transport network:** a graph over the colonised area whose edge conductances evolve by the **Tero et al. flow-adaptation model** (the flux Q through each tube comes from solving pressures between food sources and the front; conductance grows ∝ |Q|^μ and decays otherwise).
   - Tubes carrying flux thicken into veins; unused ones thin and vanish, and the sheet behind them retracts.
   - The result is the characteristic hierarchy (thick trunks, branching capillaries, broad front) that emerges from dynamics rather than being drawn.
   - Solve on a small graph (≤ a few hundred nodes per plasmodium, sparse conjugate-gradient solver) at a low cadence.
   - Starvation (no food flux for N days) triggers fruiting: the network collapses into sporangia clusters at former thick nodes, leaving a dull residue sheet.

Rendering (S2a/S2c) then draws:
- coverage layers as a terrain-draped mesh per tile (height = thickness, colour and normal from age/vigour/species, feathered edge from the coverage gradient) plus sparse shoot instances at the rim;
- the Physarum network as a width field splatted into its sheet, so veins are broad, low and merged into the sheet (no tubes);
- fruiting bodies as small kernel meshes.

Performance note: the tile meshes rebuild only when a tile's content version changes (the same pattern as the terrain mesh reuse).

### P2 scope (next dispatch after P1)
1. `CoverageLayer` data, serialisation and determinism tests.
2. The moss and lichen rules above, with existing moss/lichen species migrated from colony cells to layers (breaking change: `FloraColonyDef` cells are removed).
3. The Physarum front and Tero network; slime mold removed from `FloraIndividual`.
4. Ecology hooks (detritus consumption, litter, fauna grazing on coverage) ported.
5. A debug overlay showing the layer rasters.

The renderer rebuild for these layers is P3 (S2a + S2c together, since they share the draped-tile mesher).

### Risks
- **Sim cost:** a 1.5 cm raster over the ~10 m island is ~440k cells if dense. Sparse tiles plus a rim-only active set keep it small; measure with the Perf suite.
- **Determinism:** the conjugate-gradient solve must use a fixed iteration count and fixed ordering (no parallel reductions).
- **Ecology rebalancing:** coverage biomass has to map onto the existing nutrient budget. Keep the totals comparable and check with `EcologyTools` soak tests.
