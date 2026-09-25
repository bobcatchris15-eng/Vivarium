# Growth simulation: moss, lichen, slime mold — design and decomposition

Status: design for review. This supersedes the S2 section of `S1_S2_plan.md`.
Terms: **cell** = fine coverage cell; **field cell** = the existing coarse 0.25 m environment grid; **flora step** = the scheduler's
`flora` system (every 60 ticks = 600 sim-seconds × `BioAcceleration`).

---

## 0. What "simulate" means here

The visible structure has to come from the process:
- mats that fill in from a rim;
- lichens with lobes and rings;
- slime veins that thicken because they carry flow.

The model is judged by the pictures it produces, not by botanical accuracy. Constraints:
- **Authoritative and deterministic.** The state is part of the world, saved, and included in the digest.
- **Affordable.** Growth runs only at the flora cadence. The per-step cost stays within a measured budget (§10).
- **Render-agnostic.** The simulation holds physical quantities (biomass, water, age, conductance) and never colours.

### 0.1 Biology → model mapping

| Real process | Visible consequence | Modelled? |
|---|---|---|
| Moss is poikilohydric: it wets fast, dries fast and grows only while hydrated | Patchiness follows moisture; mats brown and recover | **Yes**: per-cell water content W |
| Pleurocarp (carpet) mosses grow by lateral creeping | Flat mats spreading from a rim | **Yes**: lateral spread rate |
| Acrocarp (cushion) mosses grow upright shoots in dense clumps | Domes | **Yes**: height from distance-to-edge |
| Sphagnum holds water and raises local wetness | Bog cushions that expand into wet ground | **Yes**: moisture feedback |
| Moss spores / fragments establish far away | New patches appear | **Yes**: rare long-range establishment |
| Lichen radial growth (very slow, roughly linear in radius) | Round patches that stop where they meet | **Yes**: Eden front |
| Foliose lichen lobes grow at their tips | Lobed rosettes | **Yes**: tip-biased growth |
| Old lichen centres die and flake | Rings | **Yes**: age-driven centre death |
| Lichens colonise only stable substrate | Found on rock and wood, not on moving soil | **Yes**: substrate gate plus prop surfaces |
| Physarum forages as a fan-shaped front | Broad advancing sheet | **Yes**: front on the chemo-attractant field |
| Tubes adapt to flux (Tero et al. 2007/2010) | Vein hierarchy, shortest-path networks | **Yes**: conductance ODE |
| Unused regions retract | Network thins behind the front | **Yes** |
| Sclerotium when dry; fruiting when starved | Dormant crust; sporangia clusters | **Yes** |
| Compatible plasmodia fuse | Networks merge | **Yes** (same species) |
| Moss/lichen photosynthesis carbon balance in detail | — | No: collapsed into a growth-rate function |
| Water transport inside moss | — | No |

---

## 1. Core data structure: `CoverageLayer`

`src/Vivarium.Sim/Coverage/` (new namespace).

### 1.1 Geometry
- Cell size **2 cm** (`CoverageSpec.CellSize = 0.02`). There are 12.5 × 12.5 cells per field cell. The default island (16 m hex, ~166 m²) would have ~415k cells if fully covered.
- **Sparse tiles** of 32 × 32 cells (64 cm). A tile is allocated when anything grows in it and freed when it has been empty for N steps. Tiles are keyed by `(ti, tj)` in a `SortedDictionary`, so iteration order is deterministic.
- There are two coordinate spaces, each with its own tile set:
  - **Ground**: terrain XZ, used for ground mats, crusts and plasmodium.
  - **Prop surfaces**: one `SurfaceGrid` per rock and log in that prop's own UV parameterisation, used for epiphytic lichen and moss (§5.5).

### 1.2 Cell state (struct of arrays per tile)
| Field | Type | Meaning |
|---|---|---|
| `Occ` | `byte` | occupant slot index (0 = empty) into the layer's species table |
| `B` | `Half`/`float` | biomass per area, 0..1 of the species' max |
| `W` | `byte` | water content 0..255 (poikilohydric state) |
| `Age` | `ushort` | bio-days × 4 since colonised |
| `Dorm` | `byte` | consecutive dry steps (dormancy counter) |
| `Flags` | `byte` | Dead, Rim, Fruiting, Sclerotium, Front |
| `D2E` | `byte` | distance to colony edge in cells (incremental, capped at 255) |

That is about 10 bytes per cell, roughly 4 MB at full island coverage.

### 1.3 Layers
| Layer | Occupants | Coordinate space | Notes |
|---|---|---|---|
| `Mat` | moss species | ground + prop surfaces | one occupant per cell |
| `Crust` | lichen species | ground (stable/dry only) + prop surfaces | can underlie Mat; moss overgrows lichen |
| `Plasmodium` | slime species | ground + log surfaces | thin sheet that moves over Mat and Crust without replacing them; it reads them (moss slows it down, and it feeds on detritus beneath) |

### 1.4 Randomness: order-independent
No sequential RNG stream is used. Every stochastic decision uses
`u = Hash01(worldSeed, layerId, tileKey, cellIndex, stepIndex, purpose)`.
Results therefore do not depend on the order cells are visited or on which cells are active. This is required for sparse and active-set iteration, and it makes parallelism possible later without breaking determinism.

### 1.5 Update discipline
- **Double-buffered for spread decisions:** neighbour reads come from the previous step's `Occ`/`B` snapshot of the active tiles, and writes go to the current buffer. Growth is then isotropic regardless of scan direction; without this, fronts sweep in the scan direction.
- **Active set:** tiles with any Rim, Front or non-steady cell. Interior-only, steady tiles run cheap physiology only, and every Kth step (K = 4, with the dt scaled to match).

---

## 2. Micro-environment inputs

`CoverageEnvironment` samples, per cell and per step, fields that already exist or are derived:

| Input | Source | Fine-scale refinement |
|---|---|---|
| Moisture M | `Fields.Moisture` (bilinear) | + seepage: `Water.DepthAt` within 3 cm → saturated; slope term (concave up = wetter, from the terrain Laplacian); prop shelter (leeward/under-overhang cells +) |
| Humidity H | M smoothed + standing-water proximity | drives drying rate |
| Light L | `Fields.Light` | − shade from vascular plant canopy (sampled from the flora spatial buckets: Σ leaf area over the cell) − prop shadow |
| Nutrients N | `Fields.Nutrients` | — |
| Detritus F | `Fields.Detritus` | slime food, and a mild boost for moss |
| Substrate S | substrate map + props | Rock, Log, Bark, StableSoil (age since last sculpt/disturbance > T), LooseSoil, Gravel, Water |
| Slope, aspect | terrain normal | anisotropy of spread |

The rules only ever see the result as a `MicroEnv` struct, so they are testable with synthetic inputs.
Disturbance: sculpt, tool actions and fauna burrowing reset `StableSoil` age, which is why crusts vanish from disturbed ground.

---

## 3. Shared physiology: water balance (moss and lichen)

Each step, for occupied cells:

```
target = clamp(M + wetBoost(seepage, rain events), 0, 1)
if target > W:  W += (target − W) · (1 − e^(−k_wet · dt))       # wets in minutes-hours
else:           W −= (W − target) · (1 − e^(−k_dry · (1 − H) · dt))  # dries slower in humid air
```

The growth multiplier is `g_W = smoothstep(W_min, W_opt, W)`. Growth also needs light: `g_L = L / (L + K_L)`, with a photoinhibition cap for shade species.
Species parameters: `k_wet`, `k_dry`, `W_min`, `W_opt`, `K_L`, and `L_max` (the limit for shade mosses).

---

## 4. Moss model

### 4.1 Biomass
```
dB/dt = r · g_W · g_L · g_N · (1 − B)          (logistic toward species max)
       − m_dorm · [Dorm > D_brown] · B          (die-back while dormant)
```
- `Dorm` increments when `W < W_min`, and resets to 0 when `W > W_opt`.
- **Browning:** `Dorm > D_brown` (a render cue only).
- **Death:** `Dorm > D_death` sets the Dead flag. The dead biomass decays into `Fields.Detritus` over T_decay, and the cell is cleared when B < ε. This produces patchiness with history.

### 4.2 Lateral spread (rim colonisation)
For each empty cell adjacent (8-neighbourhood, distance-weighted) to occupied cells of species s:
```
pressure = Σ_neighbours  B_n · g_W,n · w_dir(n→cell)
w_dir    = 1 + a_slope · max(0, downslope·dir) + a_moist · max(0, ∇M·dir)
p        = 1 − exp(−λ_s · pressure · suitability_s(cell) · dt)
colonise if Hash01(...) < p   → Occ = s, B = B_seed, Age = 0
```
`suitability_s` comes from existing preferences (moisture, light, substrate affinity; `HardMin/MaxMoisture` from the current `FloraSpeciesDef`). `λ_s` is the lateral speed: carpet is high, cushion is low.

### 4.3 Competition at fronts
An occupied cell of species a with a neighbour of species b can be taken over with
`p = 1 − exp(−κ · (V_b − V_a) · dt)`, where vigour `V = B · g_W · g_L`. It never happens if V_b ≤ V_a. Fronts therefore stall where the species are evenly matched, producing seams, and advance where the environment favours one of them.
Moss overgrows lichen (crust) when both want the cell. Lichen persists only where moss is water-limited.

### 4.4 Long-range establishment
Each step, each species emits a spore budget ∝ Σ B (capped). Candidate cells are chosen by `Hash` over allocated plus neighbouring tiles, weighted by suitability, and colonised with a very small probability. This replaces the current spore bank for mosses.

### 4.5 Growth forms: parameter sets, not code branches
| Form (current species) | λ lateral | B max height | Height rule | Special |
|---|---|---|---|---|
| Carpet (carpet_moss) | high | 4 mm | `h = h_max · B` | — |
| Wetbank (wetbank_moss) | high | 6 mm | `h = h_max · B · wetness` | needs M > 0.6 |
| Cushion (cushion_moss) | low | 25 mm | **dome**: `h = h_max · B · (1 − e^(−D2E/ℓ))` | tolerates drought, slow |
| Bog (sphagnum) | medium | 30 mm | dome + capitula | **feedback**: occupied cells add `+φ·B` to local moisture (written into a small `MoistureBonus` array read by §2), so the colony wets its own ground and expands into it |

`D2E` (distance to edge) is updated incrementally. On colonisation or death, a local BFS relaxes the distances within radius 8 cells. It provides the dome profile and the rim/interior distinction for rendering (interior = dense and darker, rim = young and bright).

---

## 5. Lichen model

Lichens are the same layer machinery with slower rates and different front rules.

### 5.1 Substrate gate
Allowed substrates are Rock, Log, Bark, StableSoil (crust species), and Gravel for crust species at a lower rate. Loose soil is never allowed.

### 5.2 Crustose (crust_lichen): Eden growth
- Colonisation uses §4.2 with `w_dir ≡ 1`, `λ` very low and a small noise exponent. The result is Eden clusters: round but rough-edged.
- `Age` is the ring source. The renderer bands colour by `Age mod period` near the rim and marks the areolate (cracked) texture by age.
- **Prothallus:** a cell adjacent to a different lichen species stops growing (`p = 0`) and gets a Boundary flag, which renders as a dark line.

### 5.3 Foliose (foliose_lichen): tip-biased growth → lobes
Plain Eden growth gives blobs. Lobes need a growth probability that favours exposed tips:
```
openness(cell) = (# empty cells in radius-3 disc) / (disc size)   # computed on the snapshot
p = 1 − exp(−λ · openness^γ · dt)        γ ≈ 3–5
```
This is a cheap local stand-in for Laplacian (DLA-type) growth, and it produces fingering whose lobe width is set by the radius and γ. Tuning: measure the lobe count with a perimeter²/area metric in the lab (§12).
- **Centre senescence:** `Age > A_centre` together with D2E > r → Dead. It decays and leaves rings or crescent shapes. The ring can be recolonised.

### 5.4 Fruticose (reindeer_lichen)
- The cushion footprint lives in the Crust layer with dome height as for cushion moss.
- Upright branching is **render-derived** from footprint cells, seeded by cell hash. It is geometry from the S1 kernel, not simulation individuals. `FloraIndividual` is not used.

### 5.5 Prop surfaces (epiphytes on rocks and logs)
- **Logs:** a cylindrical grid (axial s × angle θ) at 2 cm, `SurfaceGrid(ls, lθ)`.
- **Rocks:** a cube-sphere parameterisation (6 faces × n²) projected onto the rock mesh by radial raycast. The mesh comes from `PropMeshes.Rock(seed)`, which is deterministic and can be generated sim-side.
- Micro-environment on a surface: light × facing (up-facing gets more light), moisture from the ground moisture below plus facing (north/down faces are wetter), and substrate from the prop type.
- The same rules apply, with a different neighbour topology (wrap in θ; face seams on cube-sphere cells).
- This gives lichen on top of rocks, moss on the shaded side of logs, and bands along the log–ground contact.

---

## 6. Slime mold (Physarum) model

Three coupled parts: **sheet**, **attractant field** and **transport network**. Plus a life cycle.

### 6.1 Life-cycle state machine (per plasmodium, a `PlasmodiumId`)
```
Spore/Dormant ──(moist + food nearby)──► Foraging ──(dry: mean W < w_s for T_s)──► Sclerotium
     ▲                                       │  ▲                                        │
     │                                       │  └──────────(rewetted)────────────────────┘
     │                          (food inflow < upkeep for T_starve)
     │                                       ▼
     └──── spores released ◄── Fruiting ◄── Migrating (moves toward light/dry for ≤ T_mig)
```
One plasmodium = one connected component of Plasmodium-layer cells with the same species. Components are recomputed (union-find over the active tiles) every step. Fusion happens automatically when two components touch. Splitting (a component cut in two) assigns the smaller part a new id with the parent's state.

### 6.2 Sheet (cells)
Each occupied cell has sheet thickness `B` (film). The Front flag is set on occupied cells with empty neighbours in the attractant-uphill direction.

### 6.3 Attractant field C
- It lives on a **coarse lattice at 4 cm** (2 × 2 cells), only over the tiles around each foraging plasmodium plus a margin of 1 m.
- Sources: detritus `F` (and designated food items). Sink: decay.
- Solve `∂C/∂t = D_c ∇²C − δ C + σ F` with a fixed **8 Jacobi iterations** per step, warm-started from the last step.

### 6.4 Foraging front
- **Front extension:** each front cell tries to colonise its neighbours with
  `p = 1 − exp(−λ_f · (β + max(0, ∇C · dir)) · g_W · dt)`.
  β is a small exploratory term, which gives the fan shape when the gradient is weak.
- **Cost:** a new cell costs mass `m_cell`, taken from the plasmodium's mass pool. With no mass there is no extension.
- **Feeding:** occupied cells over detritus take `Fields.Detritus.Take(cell, rate)` into the mass pool, which is conserved and tallied (as `DetritusDecomposed` is today).

### 6.5 Transport network (Tero flow-adaptation)
- **Graph:**
  - Nodes are the occupied coarse-lattice points (4 cm) of the plasmodium.
  - Edges connect 8-neighbours, with length `L_ij` equal to their distance.
  - Each edge has a conductance `D_ij`, which persists across steps and is stored per edge in a dictionary keyed by the (node, node) pair.
  - The node count is capped at ~600 per plasmodium. Above that, the lattice is coarsened to 8 cm.
- **Sources and sinks per step:**
  - Food nodes (detritus uptake > 0) are sources, s_i = +uptake_i.
  - Front nodes are sinks, s_i = −demand_i ∝ extension attempts.
  - Normalise so Σ s = 0 (the remainder goes to an upkeep sink spread over all nodes).
- **Pressure solve:**
  - `Σ_j (D_ij / L_ij)(p_i − p_j) = s_i`, a graph Laplacian with one node grounded (p = 0).
  - It is solved by **conjugate gradient with a fixed 60 iterations**, fixed node ordering, and no parallel reduction. The same inputs give bit-identical output.
- **Flux:** `Q_ij = (D_ij / L_ij)(p_i − p_j)`.
- **Adaptation** (Tero 2010 form):
  `dD/dt = q · f(|Q|) − γ · D`, with `f(Q) = Q^μ / (1 + Q^μ)`, μ ≈ 1.8, integrated with implicit Euler for stability.
- **Pruning:** an edge with `D < D_min` is deleted.
- **Retraction:** sheet cells whose nearest edge has D < D_ret, and which are neither front nor feeding, lose thickness and eventually vacate. Their mass returns to the pool. This makes the network thin out behind the front, the signature behaviour.
- **Output for rendering:** tube width `w_ij = w0 · sqrt(D_ij)` is splatted into a per-cell `TubeWidth` array. The sheet thickness seen by the renderer is `max(film(B), tube profile)`, so veins are broad, low ridges merged into the film, not cylinders.

This is the component that has to be *right*. The acceptance test is the classic result (§12): with two food sources at opposite ends of a maze or lattice, the network converges to a single tube along the shortest path, and the other branches decay.

### 6.6 Sclerotium, migration, fruiting
- **Sclerotium:** freeze the network (D is kept), thicken and darken the flag, stop all flux. Resume on rewetting.
- **Migrating:** the front is driven by a light/dry gradient instead of C, and feeding stops.
- **Fruiting:**
  - Choose K sites = the local maxima of Σ D over incident edges (the old hub nodes), spaced ≥ 5 cm apart. K ∝ total mass.
  - Mass converts into `FruitingBody` records `{pos, maturity, seed}`. These are small sim entities because they release spores, and they replace the current Fruiting individuals.
  - The network and sheet decay into a residue flag, which renders as dull and collapsed and fades over days.
- **Spores:** released into the existing spore-bank mechanism for slime mold (cheap and already deterministic). Establishment creates a new plasmodium seed patch in a moist, food-rich cell.

---

## 7. Coupling to the rest of the world

| Interaction | Mechanism |
|---|---|
| Nutrients | moss growth takes `Fields.Nutrients` ∝ dB (small); a nutrient shortage enters as g_N |
| Detritus | slime feeding takes it; dead moss/lichen biomass returns to it over time |
| Moisture | Sphagnum `MoistureBonus`; moss mats reduce the soil drying rate slightly (optional, off by default) |
| Light / shade | vascular canopy shades mats (§2); mats do not shade plants |
| Vascular establishment | seedlings are blocked on cells with mat B > 0.7 (cushion, sphagnum) → visible competition; carpet moss does not block |
| Fauna grazing | springtails and similar graze **Mat/Crust biomass** in a radius: `CoverageLayer.GrazeDisc(pos, r, amount)`, which returns the amount eaten. It replaces grazing on `FloraIndividual` moss. Grazed cells get a reduced B, visible as thinning. |
| Tools (introduce, remove, sculpt) | introduce = seed a disc; remove = clear a disc; sculpt = clear plus reset substrate stability |
| Stats / UI | per-species covered area (m²) and biomass replace the individual counts for these species in the catalog and stats |

---

## 8. Time scaling and content schema

Real rates are far too slow: moss spreads millimetres per month, lichen millimetres per year. Content stores rates in
**world units per bio-day**, tuned for pictures: carpet mats visibly advance over ~1–3 bio-days, lichens over ~10–20.

Content changes (breaking):
- The `colony` block is removed.
- The following blocks are added to species JSON:
```json
"mat":        { "layer": "Mat", "lateral": 0.012, "maxHeight": 0.004, "heightRule": "flat|dome|wet",
                "water": { "kWet": 6, "kDry": 1.2, "wMin": 0.25, "wOpt": 0.6 }, "light": { "k": 0.3, "max": 0.8 },
                "dormBrownDays": 2, "dormDeathDays": 8, "vigourCompetition": 1.0, "sporeRate": 0.001,
                "moistureFeedback": 0.0 },
"lichen":     { "layer": "Crust", "lateral": 0.0015, "tipBias": 0, "centreDeathDays": 30, "substrates": ["rock","log","stable_soil"] },
"plasmodium": { "front": 0.04, "explore": 0.15, "feedRate": ..., "cellCost": ..., "tero": { "mu": 1.8, "gamma": 0.4, "q": 1.0 },
                "starveDays": 1.5, "sclerotiumDryDays": 0.5, "fruitingDays": 1.0 }
```
Archetypes moss, lichen and slime_mold no longer create `FloraIndividual`s. Fruticose branches and fruiting bodies are the only discrete entities.

---

## 9. Persistence and digest

- Save payload `CoveragePayload { layers: [ { id, tiles: [ { ti, tj, occ, b, w, age, dorm, flags } ] } ], plasmodia: [ {id, state, mass, timers, edges:[(a,b,D)] } ], fruitingBodies: [...] }`.
- Arrays are stored as base64 of the raw little-endian bytes (with Brotli compression if the save system compresses).
- The digest hashes the layers in tile-key order.
- Old saves are not migrated (breaking changes are allowed).

---

## 10. Performance budget (flora step every 600 sim-s; at 16× speed that is about every 3.75 real seconds at 60 ticks per real second)

| Part | Budget per flora step | Scaling |
|---|---|---|
| Physiology (W, B) | ≤ 2 ms | active occupied cells; steady interiors every 4th step |
| Spread and competition | ≤ 2 ms | rim cells only |
| D2E incremental | ≤ 0.5 ms | changed cells × local radius |
| Attractant Jacobi | ≤ 1 ms | coarse lattice around plasmodia only |
| CG network solve | ≤ 1 ms per plasmodium | ≤ 600 nodes × 60 iterations ≈ 0.3M flops |
| **Total** | **≤ 6 ms** | measured by a new `Perf` suite test on a dense synthetic world |

Tiles also carry a `Version` so the renderer rebuilds only changed tiles.

---

## 11. Render contract (for P3, the renderer rebuild)

The simulation exposes the following, read-only:
- `IEnumerable<TileView> ChangedTiles(sinceVersion)`, where a `TileView` gives per-cell `Occ, B, W, Age, Flags, D2E, TubeWidth`.
- Species visual params (from content).
- Prop `SurfaceGrid` views in prop-local UV.
- `FruitingBody` list; per-plasmodium state (for colouring by state).

The renderer derives height, feathered edges, colour, gloss and shoots. **No colour is stored in the simulation.**

---

## 12. Verification tooling: "growth lab"

Iterating on these rules through the full game loop is too slow. So:

- **`tests/Vivarium.Sim.Tests/GrowthLab/`: a headless harness.**
  - It builds a synthetic `MicroEnv` world (for example a moisture gradient, a slope, a rock disc, food points), runs N steps and writes **PNG timelapse frames** of each layer.
  - It uses a minimal PNG encoder via `System.IO.Compression.ZLibStream`, with no dependencies.
  - Frames go to `build/growthlab/<scenario>/frame_###.png`, with contact sheets from `scripts/reference-sheet.py`-style tooling.
  - Trait `Suite=GrowthLab`, `Speed=Slow` for image output. The fast asserts run in the normal suite.
- **Scenarios with quantitative asserts:**

| Scenario | Assert |
|---|---|
| `moss_gradient` | cover grows faster on the wet side (front speed ratio > 2); no growth below `HardMinMoisture` |
| `moss_drought_recovery` | browning after D_brown, death holes after D_death, recolonisation after rewetting |
| `moss_competition` | two species meet and form a seam; the favoured one advances under a biased environment |
| `cushion_dome` | height profile is monotone toward the centre; rim height < 30% of the centre height |
| `sphagnum_feedback` | the colony expands into marginal wetness that is unsuitable without feedback |
| `crust_eden` | roundness (area / π r_max²) > 0.75; rough edge (perimeter above a circle's) |
| `foliose_lobes` | perimeter² / area ≥ 2× the crust value at equal area; lobe count 5–15 |
| `lichen_prothallus` | two thalli stop at a dark boundary line; no overlap |
| `physarum_two_food` | **the network converges to one dominant path between two food sources; its length is ≤ 1.15× Euclidean; other edges' D → pruned** |
| `physarum_maze` | on a walled lattice the surviving path equals the BFS shortest path |
| `physarum_starve` | food removed → migrating → fruiting bodies at old hubs within T |
| `physarum_fusion` | two plasmodia meet → one id, merged network |
| `determinism` | two runs give identical layer bytes; runs with shuffled tile allocation order are also identical |
| `perf_dense` | full-island synthetic world at the budget in §10 |

- **In-game debug overlay:** a raster texture of the layers draped on the terrain (the existing `OverlayRenderer` pattern). It is cheap and shows the real simulation before the P3 renderer exists.

---

## 13. Work packets (Clanker-sized, sequenced)

| id | Outcome | Targets (indicative) | Depends | Validate |
|---|---|---|---|---|
| **G1** | `CoverageLayer` core: tiles, cell SoA, hash RNG, double-buffer, active set, iteration order, save payload, digest, tests | `Coverage/CoverageLayer.cs, CoverageSpec.cs, HashRng.cs`, `Persistence/WorldSerializer.cs`, tests | — | `Suite=Coverage`, Persistence round-trip |
| **G2** | Growth lab harness plus PNG encoder, one trivial scenario | `tests/.../GrowthLab/*` | G1 | the lab writes frames; determinism assert |
| **G3** | `CoverageEnvironment` / `MicroEnv` sampling (moisture refinements, light and shade, substrate stability, slope) | `Coverage/CoverageEnvironment.cs`, minor hooks in `World/TerrainEditing` for disturbance | G1 | unit tests on synthetic fields |
| **G4** | Moss: water balance, biomass, spread, competition, spores, growth forms, D2E; migrate the 4 moss species; delete moss colony cells | `Coverage/MatRules.cs`, content moss JSON, `FloraSystem` colony removal (moss) | G2, G3 | lab scenarios moss_* and cushion, sphagnum; `Suite=Flora` updated |
| **G5a** | Lichen on the ground: crust Eden, foliose tip-bias, centre death, prothallus, fruticose footprint; migrate the 3 lichens | `Coverage/LichenRules.cs`, lichen JSON | G2, G3 | lab crust/foliose/prothallus |
| **G5b** | Prop `SurfaceGrid`s (log cylinder, rock cube-sphere) plus surface micro-environment; epiphytic moss/lichen | `Coverage/SurfaceGrid.cs`, `PropMeshes` (sim-side sampling) | G4, G5a | lab `epiphyte_log`, `epiphyte_rock` |
| **G6** | Plasmodium sheet + attractant field + foraging front + feeding (no network) | `Coverage/Plasmodium/*` | G2, G3 | lab front advances uphill in C; mass conserved |
| **G7** | Tero network: graph build, CG solve, adaptation, pruning, retraction, tube splat, fusion/split | `Coverage/Plasmodium/Network.cs, CgSolver.cs` | G6 | **physarum_two_food, physarum_maze, fusion**, determinism |
| **G8** | Slime life cycle (sclerotium, migrate, fruit, spores); migrate slime_mold off `FloraIndividual` | `Plasmodium/Lifecycle.cs`, `FloraSystem` slime removal | G7 | physarum_starve; `Suite=Flora` |
| **G9** | Coupling: nutrients/detritus, vascular blocking, fauna grazing on coverage, tools, stats/catalog | `FaunaSystem` grazing, `ToolActions`, UI stats (game) | G4–G8 | `Suite=Ecology|Fauna|Tools`, 4-week soak stays bounded and deterministic |
| **G10** | In-game debug overlay of the layers | `game/Render/OverlayRenderer.cs` | G1 (grows with each) | Reference capture with the overlay on |
| **P3** | Renderer: draped tile meshes, feathered edges, dome heights, rim shoots, vein ridges, fruiting bodies, surface grids on props | `game/Render/CoverageRenderer.cs` (new), shaders | G4–G8 | Reference scenes dense_colony, colony_edge, slime_*; FPS gate |

Parallelism:
- After G3, **G4 ‖ G5a ‖ G6** can run in separate worktrees because their rule files are disjoint. Content JSON also splits by species.
- G5b and G7 follow their parents. G9 runs last.
- G10 can trail each packet.

### Decision records
- **OBS** colonies of individual cells read as chips; slime bud-trees read as sticks → **DECISION** a sim-side 2 cm sparse coverage raster with process rules; the renderer draws continuous surfaces → **EFFECT** continuous mats, lobed lichens, emergent vein hierarchy → **TEST** growth-lab asserts plus the Reference colony/slime scenes.
- **OBS** sequential RNG breaks with active-set iteration → **DECISION** hash-per-(cell, step, purpose) randomness plus double-buffered neighbour reads → **EFFECT** isotropic, order-independent, deterministic growth → **TEST** shuffled-allocation determinism scenario.
- **OBS** Physarum vein hierarchy is an emergent property of flux adaptation → **DECISION** implement Tero adaptation with a fixed-iteration CG solve; do not fake the hierarchy → **EFFECT** thick trunks and pruned capillaries that respond to food placement → **TEST** two-food / maze shortest-path convergence.
- **OBS** growth tuning through the full game is too slow to iterate → **DECISION** a headless growth lab with PNG timelapses and quantitative asserts → **EFFECT** rule changes are judged in seconds → **TEST** the lab runs in `Suite=GrowthLab`.

### Open questions for the user
1. Should slime molds visibly *move* in real time at normal speed (front advance of cm per minute of play), or only across accelerated time? This affects `front` rates and whether the renderer interpolates the front between steps.
2. Should the island's vascular plants and mosses compete for ground (thick mats blocking seedlings, §7)? This makes wet areas moss-dominated.

---

## 6R. Plasmodium revision (2026-09-25, supersedes §6.4 mass pool, §6.5 source/sink framing, §6.6 Migrating)

A plasmodium is a **spatial organism**: territory plus fields. It never has a commanded transform, and its centroid is measured only.
Causal loop: **contraction → pressure → flow → mass redistribution → morphology → conductance → flow**.

1. **Local biomass.** Each occupied cell holds cytoplasm mass `m_i`. The global mass pool is removed. Mass moves only along sheet adjacencies and vein edges, conservatively: `Δm_i = Σ_j Q_ij·dt` with an outflow limiter so no cell goes negative. Feeding adds mass locally at food cells. Maintenance cost removes mass everywhere (to detritus/CO2 tally).
2. **Contraction phase field.** Each node has a phase θ_i and weakly coupled oscillators: `dθ_i/dt = ω_i + K·Σ_j sin(θ_j − θ_i)`, where ω rises with local nutrient uptake and falls with local stress (dryness, light). Local pressure is `P_i = P0·m_i/m_ref + A·sin θ_i`. Travelling waves emerge from gradients in ω.
3. **Flow.** `Q_ij = D_ij (P_i − P_j)`. Pressure comes directly from the phase and mass, so no global solve is needed each step; this is an explicit graph step. A few warm-started Jacobi relaxations smooth P over veins if needed.
4. **Rectified net transport.** Forward and back flow over a cycle cancel, except where local conditions differ: cells with higher attractant/food and lower stress retain a small fraction ε more of the inflow (gel/sol stiffening). Migration is the accumulation of that ε over many cycles. No steering vector exists.
5. **Frontier.** An empty neighbour of a boundary cell is occupied when that cell has m > m_occ; the probability rises with mass, pressure phase, attractant gradient and moisture. The new cell takes mass from its parent.
6. **Withdrawal.** A cell below m_min vacates (residue flag + timer). Low-flow, food-less regions drain naturally through (1)+(4).
7. **Veins.** `dD_ij/dt = r·|Q_ij| − γ·D_ij` (time-averaged |Q| over the cycle), with pruning below D_min. Sources and sinks are never designated; hierarchy must emerge. The two-food, maze and fusion lab tests remain the acceptance checks for emergence.
8. **Stress instead of a Migrating state.** Light and dryness lower ω and raise the maintenance cost locally, so the body drifts away through (4). Fruiting (starvation) and sclerotium (drought) stay as developmental switches.

**Cadence.** A dedicated `plasmodium` scheduler system every 3 ticks (30 sim-s). The period is ~100 sim-s at ω0, so ≥3 samples per cycle; ω is in **sim-seconds, not bio-accelerated time**, which equals real time at 1× speed. Budget: ≤1 ms per plasmodium per step (Debug) at ≤600 nodes.
**Rendering.** The renderer reads m, D, θ, front and residue. It shows the vein swelling wave with a **wall-clock** phase offset seeded from sim θ, so at high game speed the visual pulse stays at a natural real-time rate. The display is non-authoritative.
**Abstraction.** `SpatialOrganism` base (territory + per-cell fields + step), with `Plasmodium` first and `ColonialMat` (moss/lichen coverage) second. `Mycelium` is reserved for later.

Packets: **Pl-1** local mass + conservative transport · **Pl-2** phase field + cadence + rectification · **Pl-3** stress replaces Migrating; re-prove two_food/maze/fusion · **Pl-4** SpatialOrganism refactor + in-game slime off FloraIndividual (old G8b) · **P3-slime** renderer.
