# Botanical Geometry Rebuild: anatomy-driven foliage, stems and maturity meshes

**Status:** AUTHORITATIVE FULL IMPLEMENTATION PLAN — **not** a Visual Lab experiment, tentative prototype, or optional refactor.  
**Date:** 2026-10-18. **Working repository:** `E:\Vivarium`.  
**Goal:** Rebuild every leaf, petiole, rachis, stem, branch and plant junction generator so that individual organs read as convincingly biological at inspection distance.  
**Implementation status:** This file specifies the work; it does not claim the work has already been carried out.  
**Machine target:** Godot 4.7.1 Mono, .NET 8, Windows, first-generation Ryzen + GTX 1080, **30 FPS floor**.

## 0. Mandate and architectural decisions

User observation: the overall plant shape and density are reasonable, but individual leaves, tips, edges, veins, twigs, nodes and attachment points look visibly generated. A more complicated shader or a more detailed version of the same formula leaves does not fix that. The purpose of this project is **anatomically convincing components, not procedural-looking components rendered beautifully**.

**Central architecture: maturity-leveled, reference-derived leaf/organ meshes, with one-time identity-preserving deformation at placement.**

1. Every vascular species has authored/curated **organ families** (leaf/leaflet/needle/blade/bract/petal as applicable), each with a small **maturity sequence**: bud, emerging, juvenile, expanding, adult, senescent where biologically relevant.
2. Each stage is an anatomically authored/deformed *mesh*, with root/petiole anchor, vein landmarks, UV layout, local frame and stable correspondence to neighboring stages. Do not resynthesize the whole leaf from a sinusoidal width envelope for each age.
3. On organ creation, compute a **persistent deformation fingerprint** deterministically from species, parent node identity, organ identity, and stable seed. Store it in a render-only organ record/cache; re-derive identically on reload. Apply static anatomical deformation *once* to its staged base meshes (or generate static cached variant surfaces). **Do not reroll shape, texture registration, orientation or petiole whenever it matures.**
4. Individual leaf maturity is selected separately from whole-plant maturity. When it crosses a boundary, replace its stage mesh; keep its organ ID, spatial attachment, global pose, underlying fingerprint, surface marks and motion anchor. This is not a renderer-wide replacement of every leaf on a tree simultaneously.
5. Build a structural botanical graph for the plant, and mesh **stems and branch junctions** from connected anatomy—not independent tapering tube calls. Child branch collars, petiole flares, nodes, leaf-sheaths and rachis continuations must have continuous geometry/normals.
6. Build leaf lamina, venation, margin and material masks from *the same anatomical exemplar/deformation field*. Thick veins should affect actual surface geometry, not exist only as visually unrelated painted bright lines.
7. Preserve the species' approved silhouettes, growth habits, fictional palettes, ecological roles and plant-specific shapes. Avoid replacing a divided fern with generic broad fern straps merely because a model is scanned.
8. Physically explicit organ scale: geometric leaf thickness, petiole width and vascular grooves are in meters and cannot be accidentally stretched by an unrelated whole-plant XZ:Y transform.
9. Preserve/improve deterministic caches and GPU batching. **No MeshInstance3D per leaf** and no unbounded unique GPU meshes per organ. See the mandatory batching design in §6.
10. Convert **all** eligible living plant, woody, aquatic, vine, frond, reed, succulent, reproductive and pilot-tree geometry. Remove the old generators' production calls and fallback flags at cutover. This is **not a trial**.

**Interpretation of “static deformation”:** permanent individual geometry shape is decided once. Ambient wind animation remains temporary and anchored; it does not alter the underlying deformation fingerprint. The stage shape changes only when that individual organ matures (or undergoes a real represented state change). A short transition can hide swapping pops but cannot randomly regenerate its form.

### Anti-goals

- Reusing `sin(pi*t)`/simple power curves with merely more vertices, extra sinusoidal edge ruffles, random texture mottles and tube primitives is NOT a replacement.
- A catalog of stock whole plants that overwrites Vivarium's species silhouettes is NOT a replacement.
- A switchable old/new mode left forever, a three-species demo, or a passing compilation with missing species is NOT completion.
- No new event journal or historical growth log in the simulation; derive rendering from authoritative current state plus stable identity. If precise organ emergence chronology becomes essential, add minimal current-state organ birth/age metadata with clear ownership, not an event history.
- Do not take over work on water, fauna, guardians, environmental field balancing, canopy lighting or unrelated Visual Lab experiments.

## 1. Working-tree safety and known integration

`E:\Vivarium` has **many unrelated uncommitted/untracked changes** on 2026-10-18. Never run `git reset --hard`, `git clean`, mass checkout, blind rebase, or overwrite local files from GitHub. Do not kill other agents' Godot processes. Inspect `AGENTS.md` and coordinate changes to shared source files.

```powershell
Set-Location E:\Vivarium
git status --short
git rev-parse HEAD
git diff -- src/Vivarium.Sim/Geometry game/Render game/Shaders
Get-Content .\AGENTS.md
Get-Content .\docs\flora_visual_review_log.md
Get-Content .\docs\visual-lab\STATUS.md -Tail 50
```

Known map (verify again against **local**, not just GitHub):
- `src/Vivarium.Sim/Geometry/OrganismMeshes.cs`: ~7,000-line plant dispatch and dozens of primitive organ builders; `Flora`, `ClimberNode`, `FloraFruiting`; separate fungi and colony paths. Move vascular construction to compiler and remove obsolete helpers.
- `src/Vivarium.Sim/Geometry/OrganismMeshes.VisualBreakFlora.cs`: `CurvedBlade`, species-specific leaves, `FloraTier`, `BuildTier`; local dirty edits raise triangle cap and tier ratios. Do not stomp.
- `src/Vivarium.Sim/Geometry/Form/{Axis,Broadleaf,LeafBlade,FoliageBlade,PinnateFrond,Sheet,SoftTube}.cs`: fixed-form blades, regular wave margins, swept tubes, fern rachis/pinnae and junction fillets. Anatomy/nodes/frame math is salvageable; elementary generators are to be removed from production.
- `src/Vivarium.Sim/Geometry/Primitives.cs`: generic mesh utility remains for nonbotanical work, but tube/ellipsoid construction must not remain the default vascular geometry API.
- `src/Vivarium.Sim/Geometry/PilotTreeMeshes.cs`: **separate** hero trees; also needs full stem/leaf/needle conversion. Local dirty changes.
- `src/Vivarium.Sim/Geometry/{MeshData,FloraVisualCompiler}.cs`: render mesh attributes, `FloraLeaf` records, `FloraTissueSlot`, `FloraPartMetadata`, CUSTOM0/1. Rebuild explicit typed organ outputs, adapt to Godot surfaces.
- `game/Render/OrganismRenderers.cs`: `FloraRenderer` presently generates ~5 or 12 morph variants per species, three geometric tiers, batches **whole plants** in MultiMesh layers and only has separate juvenile meshes for certain species. This architecture **does not support per-leaf maturity swapping by itself**.
- `game/Render/FloraVisualProfile.cs`, `FloraSurfaceProfiles.cs`, `Shaders/{leaf,flora,living_leaf_optics}.gdshader*: material/shader machinery already exists. Align to new anatomical UV/veins, but don't make materials the whole project.
- `game/App/LeafMaterialBaker.cs`: texture arrays, independently generated anatomy/vein maps. Bake coherent exemplar-space masks and normals.
- `game/Render/CoverageRenderer.MossGrowth.cs`, `OrganismMeshes.MossClonalGrowth.cs`: concurrent local moss nucleus/microleaf work. Integrate with current clonal state; coordinate file ownership.
- `game/content/index.json`: current active species list **48 flora entries as of 2026-10-18**; enumerate dynamically at start/final gate. Older `docs/overhaul/flora_species_art_plan.md` only lists 33; use `docs/flora_visual_review_log.md` and new species definitions to fill gaps.
- `tests/Vivarium.Sim.Tests/GeometryLodTests.cs` assumes closed tubes and static triangle-ratio thresholds; replace obsolete invariants with topology, continuity, identity, stage mapping and measured complexity/perf gates.

Create a file ownership ledger under `docs/overhaul/botanical_rebuild_progress.md` after implementation begins. Keep it updated with current HEAD, finished packets, open problems, source hashes, test results, visual evidence and hardware measurements. Never claim a completed packet without a commit/diff + evidence.


## 2. New code architecture and explicit contracts

Create a well-factored `src/Vivarium.Sim/Geometry/Botanical/` package, not another giant switch. Suggested layout:

```text
Geometry/Botanical/
  BotanicalCompileRequest.cs       pure current-state input
  BotanicalSpeciesRecipe.cs         typed validated recipes; no hidden defaults
  BotanicalGraph.cs                 stable plant axes, node/organ IDs and parent relationships
  BotanicalOrgan.cs                 stage, shape fingerprint, organ family and attachment
  BotanicalExemplarLibrary.cs       versioned exemplars + all stage meshes
  AnatomicalDeformer.cs             deterministic correlated deformation
  VenationBuilder.cs                coherent veins, margin and lamina registration
  LaminaMesher.cs                   surface, edges, thickness and petiole-to-blade
  BranchMesher.cs                   axis sweep with biological section profile
  JunctionMesher.cs                 fused forks, collars, node and petiole interfaces
  BotanicalStageSelector.cs         stage boundaries + organ maturity calculation
  BotanicalBatchCompiler.cs         groups/buckets + dirty region rebuild scheduling
  BotanicalMesh.cs                  typed surfaces, per-part metadata + GPU adapters
  BotanicalValidation.cs            geometry, consistency and full-catalog checks
  BotanicalCacheKey.cs              stable content/version/deformation hashes
game/content/botanical/
  schema.json
  families/*.json
  species/*.json                    all eligible species
game/assets/botanical/
  source/*.blend                    authored source when license permits
  exemplars/<family>/*.glb          packaged stage families
  manifests/*.json                  unit scale, author, hashes, reference/license
scripts/botanical/
  inventory.py
  author-exemplars.py
  validate-exemplars.py
  render-contact-sheets.ps1
  verify-full-catalog.ps1
  profile-foliage.ps1
tests/Vivarium.Sim.Tests/
  BotanicalRecipeCoverageTests.cs
  BotanicalStageTests.cs
  BotanicalGeometryTests.cs
  BotanicalDeterminismTests.cs
  BotanicalAttachmentTests.cs
  BotanicalBatchTests.cs
```

Names are suggestions; explicit responsibilities/contracts are mandatory. `BotanicalSpeciesRecipe` links species to growth architecture + organ families + stage sequence + materials + dimensional envelopes, not to a full imported stock plant.

### Request and output shape

```csharp
// Design sketch; adapt types to existing Vec3/entity facilities.
public readonly record struct BotanicalCompileRequest(
    string SpeciesId,
    ulong PlantIdentity,
    ulong VisualSeed,
    double CurrentAgeOrMaturity,
    double BiomassFraction,
    double SizeMetres,
    double Health,
    double CurrentMoisture,
    BotanicalHostFrame? Support,
    int GeometryTier);

public readonly record struct BotanicalOrganKey(
    ulong PlantId, ulong ParentAxisId, int NodeIndex, int OrganOrdinal);

public readonly record struct StaticOrganIdentity(
    BotanicalOrganKey Key,
    ulong DeformationSeed,
    string OrganFamily,
    string ExemplarFamily,
    int ExemplarVariant,
    DeformationCoefficients Fingerprint);

public readonly record struct LeafStageState(
    BotanicalOrganKey Key,
    int CurrentStage,
    float FractionToNextStage,
    BotanicalAttachmentFrame Attachment,
    StaticOrganIdentity Identity);
```

`StaticOrganIdentity` is stable. `LeafStageState` changes by maturity only. A **mesh is compiled from both**. `BotanicalCompiledMesh` outputs separate typed surfaces (wood, young stem, broad leaf, thick leaf, petal, needle, etc.), per-organ bounding boxes, local frames and custom shader data (organ ID, stage, spine/attachment, flexibility).

The new `BotanicalGraph` should represent axis hierarchy, node positions, rank, phyllotaxis, bud sites and organ attachments, distinct from GPU triangles. The same graph is evaluated across all geometry tiers. Generate graph first; emit geometry second. This eliminates a large class of implicit metadata bugs.

### Deterministic identity and shape

Compute IDs from stable entity/species identifiers and *biological node paths*, never `Leaves.Count` or traversal order (the current `MeshData.RecordLeaf` uses a sequential leaf count for identity). Keep independently keyed variation streams: architecture, mature template variant, lamina shape, edge asymmetry, venation, attachment offset, pigment, damage and mesh tessellation. Changing a mesh tier must not change structural RNG draws.

Example:
```text
plant seed   = Hash(world seed, plant persistent ID, species)
axis ID      = Hash(plant seed, biological node ancestry)
leaf ID      = Hash(axis ID, node index, organ ordinal)
shape seed   = Hash(leaf ID, "anatomy-v1")
pigment seed = Hash(leaf ID, "pigment-v1")
stage mesh   = Meshed(exemplar family, maturity stage, shape seed)
```

The static deformation coefficients can be rederived rather than serialized if organ identity and recipe version survive across frames/save-load. **Store stable organ identity in authoritative current state when spontaneous topology additions cannot be reproduced from plant age/seed alone.** This is state, not event sourcing; no historical journal.

### Real-world unit contract

Existing meshes often use radius=1/height=1 normalization, then `FloraRenderer` applies different XZ and Y scales. This can squash leaf thickness, petiole proportions, calyx geometry and frond texture gradients. Author exemplars in **meters** (or clearly documented native millimeters converted to meters), and compile each organ in its plant-local real dimensions. Whole-organ proportional growth is a botanical transform; don't blindly multiply each tiny feature by whole-plant dimensions. Support live plant growth by changing organ scale and topology intentionally (within each maturity stage), not relying on nonuniform global shear.

When conforming to bark/support/soil/water, compute attachment frames in world/local units and inverse-transpose transformed normals correctly. Branch joints, leaf tip radii and material texel density must remain physically meaningful. Unit-scale/Godot import conversion tests are required.

## 3. Maturity-leveled exemplar families: the authoritative leaf representation

### 3.1 Stage scheme

Use a **species/organ-family adjustable** number of stages rather than forcing all plants into one exact count. Default candidates:

| Stage ID | Semantic state | Required geometry |
|---|---|---|
| B0 | Bud or folded primordium | Compact folded/rolled tissue + protected insertion site, not a tiny copy of mature leaf |
| B1 | Emerging/unfurling | Opening, partly folded tissue; coherent midrib/veins; exposed petiole |
| J | Juvenile | Recognizable small blade with different edge, roughness and pose |
| E | Expanding | Intermediate adult proportions, broader margins, unfolding deformation |
| A | Adult | Full mature anatomical structure, species-specific outline |
| S | Senescent, optional | Curvature and turgor change, discoloration and localized wear, not generic shrinking |

Some species use fewer stages (e.g., needles, specific short-lived grass blades); some need explicit crozier/compound unfolding or thick succulent organ states. Each organ family records *which* stages are actually present and meaningful.

**Important distinction:** a plant may carry buds, young leaves and mature leaves simultaneously. Never replace all leaves at once based on a single whole-plant stage. A fern crown should expose multiple frond ages; shrubs should expose young terminal growth and older inner foliage.

### 3.2 Stage-mesh authoring and topology

- Author **at least three anatomically distinct base shape examples** for each organ family where appropriate. For flagship leaves, target 6–12 exemplar variants per family; do not mistake that for 6–12 entirely separate foliage species.
- Within a given example's stage sequence, maintain matched root/petiole pivot, primary vein and tip landmarks, left/right margin arc-length coordinates and UV anatomy. Prefer the same topological indexing where possible to allow a brief morph during stage changes. A bud-to-open lamina may require distinct topology: then use a semantic pivot/landmark mapping and swap with a carefully controlled local transition.
- Store stage extent in **physical units** and differentiate genuine leaf growth from scaling. Young cordate leaves don't always look like uniformly reduced mature leaves.
- Stage shapes need coherent venation, petiole insertion, asymmetry and edge complexity. It is better to have four correct meshes than ten linear resizes.
- A single leaf **deformation fingerprint** drives *every maturity mesh for that leaf*. The same left-right asymmetry and vein-biased roll persists as the lamina expands. It should not turn into a different individual after each swap.
- Build complete anatomical material masks in the exemplar's UV space. Veins, ridges, chlorophyll patterns, leaf margin wear and cuticle normal detail must deform with the leaf. No arbitrary repeated photo tiles disconnected from structure.
- Keep mesh normals sound, continuous over intended surfaces, deliberately split at anatomical creases, and correct front/back orientation. Thick leaves use actual volume; thin laminae use geometry sufficient for curvature and a shader for thickness-dependent light transport.
- Use adaptive triangulation concentrated at margin turns, vein ribs, petiole flares, compound leaflet tips and sharp local folds, not uniform high-resolution grids.

### 3.3 One-time anatomical deformation — **static across stage swaps**

The fingerprint is sampled **once per organ identity**, not once per draw. Example coefficients:

```json
{
  "exemplar_variant": 3,
  "length_ratio": 1.06,
  "left_lobe_growth": 0.94,
  "right_lobe_growth": 1.04,
  "apex_offset": [-0.012, 0.004],
  "midrib_curve": [0.02, -0.01, 0.005],
  "vein_zone_growth": [0.03, -0.02, 0.01],
  "margin_wear_sites": [],
  "petiole_lean": 0.08,
  "lamina_twist": -0.06
}
```

Numbers are **illustrative**, not final calibrated limits. Sample correlated parameters: larger outer-side expansion changes outline, secondary-vein spacing, lateral surface tension and leaf roll *together*. Position-specific deformation should use smooth sparse basis functions or constrained anatomical cage/landmark deformations, **not white noise per vertex**. Use a fixed seed to compute the same fingerprint for all stages. Mature damage (if a feature is included) must be anchored in persistent UV/vein coordinates; an injury should not jump locations when stage changes.

**Static vs animation:** combine immutable organ shape with transient bend/wind around its registered attachment. Store the shape in a baked mesh/batch; do not repeatedly evaluate new RNG/mesh topology every render frame. Stem motion must not detach petioles.

### 3.4 Swapping without popping

At maturity boundary, preserve leaf root world position, local frame, vein/tip correspondences, prior shape fingerprint, handedness, and material identity. Where compatible, perform a **short bounded vertex morph between the two already-deformed stage meshes** and then discard the previous stage. Where topology differs, use a short carefully masked transition or switch when the change is sufficiently small at actual projected size; don't silently let a huge leaf snap from rolled to full size. This interpolation is a stage-transition presentation detail—not continuous random shape regeneration. No permanent doubled transparent leaf planes.

Stage timing must be species-authored. An illustrative schedule is `B0 0–0.07, B1 0.07–0.22, J 0.22–0.48, E 0.48–0.76, A 0.76–0.95, S 0.95–1.0` of **organ-relative** maturity, not necessarily plant age. Most mature healthy leaves should persist rather than cycle through stages endlessly.



## 4. How does a specific leaf age if ecology does not track every leaf?

**Do not fake leaf age by simply setting it equal to the whole plant age.** The current renderer mostly knows a plant's `Id`, biomass, current health, stage, `ReproductiveStage`, environment and (for climbers) `ClimberSegments`, but not a persistent leaf object for every blade.

Create a render-side **stable developmental program** per plant. Every persistent botanical graph node has a deterministic initiation threshold/position on its parent axis. Its leaves are present/visible only after their initiation threshold, and their organ-relative maturity is computed from the plant's current developmental coordinate:

```text
relative_maturity =
    clamp((plant_development - organ_emergence_threshold)
          / organ_expansion_interval, 0, 1)
```

The emergence threshold is stable from species grammar, node rank, plant morphology and seed. This is adequate if the graph's order of emergence is deterministically a function of the plant's authoritative present age/development. **Growth is not rewound** by scene reloading or camera changes. During plant senescence, leaf-specific current stress and stage can change based on present authoritative plant state plus leaf role.

But there are cases where a visual leaf *really is* initiated by an ecological event that cannot be inferred from current plant state: independent regrowth after browsing/cutting, individual branch mortality, node-level dieback, climbing reattachment, etc. For these, render-only guessing is insufficient. Store the minimal current authoritative **organ bud/node state** (`birth/development age, active/dead status, stable ID`), or derive from existing persistent node data. That is present state, **not** an event log. Do not add thousands of per-leaf sim entities unless the ecology actually needs them.

Before implementing age calculation:
1. Inspect `Flora` model fields for age/maturity and its persistence contracts, `FloraRenderer.Refresh`, and `ClimberSegments`.
2. Write `BotanicalStageSelector` unit tests for persistent IDs, growth/age progression, reloads, mixed-age crowns, deliberate stress, and selective branch loss.
3. Ensure maturity monotonically advances in stable conditions; don't reset organ age because the parent's biomass fluctuates by 2%.
4. Establish plant-specific lifecycle rhythms: older shade leaves, young terminal growth, overlapping fern frond generations, basal leaves vs flowering stems, and asynchronously matured foliage on sprawling vines.

## 5. Stem, node, attachment and branch infrastructure

Implement a shared **biological axis graph** as the authoritative *geometric* skeleton (not a new ecology). Each axis owns centerline, radius/elliptical cross-section, local frame, parent socket, internode stations, nodes, daughter axes, organ socket frames, tissue age, orientation tendency, and material/vein fibers. Every bud, petiole, rachis and branch is attached to a node on this graph.

### 5.1 Axis construction

Retain parallel-transport frames from existing `Form/Axis.cs` if correct, but stop prescribing entire species axes through a single constant curvature plus sinusoid. Author species shape envelopes and growth grammars with control points and constraints: gravitropic/phototropic tendency, young axis curvature, mechanical droop, crowding and branching order. Fit splines through authored irregular structural examples. Use correlated individual-/shoot-level coefficients; do not add per-vertex jitter. Radius profiles taper according to axis order and child load; a twig, culm, rachis and woody limb do not share an identical tube profile.

### 5.2 Genuine junction topology (mandatory)

Current `SoftTube`'s limited fillet correction only prevents deep interpenetration; it does not universally produce a watertight branch union.

For a child branch:
- Cut/replace affected parent ring(s), construct a connecting socket/collar and transition region with stitched shared boundary loops; blend tangent fields and vertex normals by continuous anatomy. Make wood grain/UV direction follow the junction.
- Build branch collars, bark ridges and asymmetrical swelling appropriate to branch angle, age and species.
- Small thin stem/petiole connections can use a constrained loft/sheath transition rather than an expensive full Boolean.
- Optional mesh-SDF/voxel union **offline at compilation time** can handle very difficult forks. Extract a smooth, high-quality junction only locally; do not voxelize entire trees on the runtime GPU. Compare topology, boundaries and memory.
- Avoid self-intersecting polygons, z-fighting sleeves, pinched normals, open broken collars or child meshes visually protruding through parent surfaces.

For a leaf/petiole junction:
- Petiole emerges from the real node/socket, transitions organically to the midrib and broadens into the lamina rather than ending as a capped cylinder pushed into a separate leaf plane.
- For compound leaves, the rachis continues through successive leaflet insertions; petiolules should be properly embedded and taper continuously into veins.
- For reed/grass sheath plants, use a genuine wrapping sheath/ligule/attachment shape when species morphology calls for it; not a spherical ring glued onto a tube.
- Root/stolon runners obey substrate support and rooted nodes. Vine rootlets and hooks originate at biologically meaningful node locations.

### 5.3 Material mapping at connections

Provide durable tangent/UV frames through axis and branch collars. Explicitly partition young epidermis, older wood, inner fresh tissue, leaf/lamina and scarring. No bark texture seams exactly through a fork and no backward-mapped fibers on attached twigs. Color/material response remains controlled by current shaders, but spatial channels must agree with real geometry.

### 5.4 Anatomy vs specialized forms

- Broadleaf, divided/compound leaf, pinnate frond, peltate water leaf, clover trifoliate, linear grass, waxy succulent, modified pitcher/trap, tendril, conifer needle/spray each gets a **separate family-specific authoring/meshing grammar** built on shared graph, deformation, stage, cache and validation.
- Moss shoots, lichen lobes and thin colony/fruticose structures need micro-organ families, not pseudo-broadleaf petioles.
- Fungi remain fungal geometry, slime mold remains plasmodium; they may use general junction helpers, but don't force leaf maturity onto mushrooms.
- Flower/reproductive tissues share organ meshes, attachment rules, maturation identity and material registration as appropriate; fruit structures do not magically float above a disconnected stem.

## 6. **Mandatory per-leaf swapping and GPU batching design**

**Existing state:** `FloraRenderer` caches a handful of whole-species morph meshes across three tiers and draws plant instances with `MultiMesh`. Its separate `Juvenile` field is a **whole-specimen alternative**, not a mechanism for maturing one leaf at a time. Changing only `FloraTier` or `FloraVisualProfile` cannot satisfy the user's request.

**Design choice: two-layer batch representation.** Maintain a structurally coherent/batched woody/stem/axis layer, plus a logical organ layer with stable leaf IDs and maturity stage. Never create a Godot node for each leaf. Bake each leaf's chosen stage, static deformed geometry and attachment into **batched surfaces**; update only dirty regions or affected plant batches when stage changes.

Support two cases based on measured performance, with one common `BotanicalOrganKey` and stage graph:

**A. Cached staged mesh pools (default for highly repeated micro-organs):**
- Compile each species/family/stage/geometry-tier/exemplar-variant combination into a predeformed **static template pool**.
- Select among several **stable fingerprints** for many distinct leaf looks. Per-organ placement/orientation/scale adds continuous variation with a static transform; guarantee these transform degrees do not ruin proportions.
- Group by material, maturity stage, family and template ID into compatible MultiMesh instance batches (per visible species/chunk as beneficial). Stable assignment permits an individual leaf to move from stage J's bucket to stage E's bucket without rebuilding entire plant meshes.
- Define hard bounded variant counts, e.g. initial design **6–12 shape fingerprints per exemplar family**; measure and adapt. Do not multiply all 48 species × all stages × all tiers × all variants into active draw calls; lazily instantiate only occupied buckets and share asset resources between batches.
- Cache stage-corresponding fingerprints so the same leaf's exact variation applies at J and A. A template must represent a whole cross-stage identity, not an unrelated random mesh selected at each age.

**B. Prebaked unique-leaf chunk surfaces (for large/hero/closely inspected leaves):**
- On plant graph creation, run the deformer once on all stage meshes for each leaf and bake its specific geometry into a **material-grouped spatial chunk or per-plant merged surface**. The result is fully static, uniquely shaped geometry.
- Track each organ's triangle/vertex segment or submesh grouping, stage, attachment, and bounds in a compact lookup. On a stage transition, mark only the affected chunk/submesh dirty and asynchronously rebuild/upload it while rendering the old mesh until ready. Do not regenerate the entire island or create combinatorial whole-plant stage variants.
- Keep chunks small enough that swapping an individual leaf is cheap; double-buffer handoff on render thread, thread-safe pure managed compilation off thread, and targeted `RenderDirtyRegions` invalidation.
- Prefer this for large leaves where the extra individuality is visible; default tiny blades to pooled templates.

**Do not evaluate random deformation anew per frame.** If a GPU vertex shader implements a deterministic *immutable* fingerprint on every draw, that is a legitimate optimization only if it reproduces the exact same shape across runs/stages and beats the baked approach on real hardware. It is not permission for camera-dependent procedural wobble.

**Crucial orientation rule:** a stage change may not alter the node's attachment frame. Preserve leaf-local `root`/midrib and parent stem position. The stem's real wind deformation can move the parent frame; the leaf's *rest pose* and identity are fixed.

### Batching acceptance/performance metrics

- Track active material batches, occupied stage buckets, GPU object/draw count, per-stage memory, CPU rebuild frequency, average/max dirty-region cost, frame-time percentiles, and VRAM. The product of **species × stages × variants × tiers** is a **potential count**, not something to eagerly create.
- Stage updates are ecological/developmental changes, not per-frame CPU mesh rebuilds. Rebuild when stage/fingerprint/geometry changes, never just because the camera rotates or the light direction changes.
- No frame stalls >33.33 ms during dense simultaneous leaf-maturation events; if background compilation can take longer, keep old geometry visible until prepared, schedule uploads within a budget, and verify progressive completion.
- No tiny-leaf spray may lose its biological identity just to meet a draw-call target; optimize it as a whole tuft/shoot with internally mixed maturity when necessary, still driven by per-organ ages.
- When swapping stage meshes, update bounds, shadow caster, material attributes and motion attachment coherently. Rebuilds must not cause a flash from culling/old bounds or drop leaf counts for a frame.
- Capture normal and unusually dense scenes on the actual **GTX 1080**; average FPS alone does not establish the floor. Inspect frame-time p95, p99, worst and frames >33.33 ms. Avoid overlapping unrelated Godot GPU processes during accepted comparisons, but do not kill them.



## 7. Exemplar authoring workflow — concrete Blender/Godot instructions

Source photographs are **anatomical references**, not unlicensed textures copied into released assets. Base exemplar geometry must be original or legally reusable with documented attribution/license. Blender 5.2 LTS has previously been confirmed installed; actual version/executable path should be rechecked. Houdini, SpeedTree and proprietary apps are NOT required. The existing GLB author/review pipeline already validates GLBs and can render native Godot scenes.

### 7.1 For every distinct organ family

1. Open `docs/flora_visual_review_log.md`, `docs/overhaul/flora_species_art_plan.md`, and the current species JSON. Write down required morphology: blade outline, apex/base type, margins, major venation, phyllotaxis, petiole/rachis arrangement, juvenile posture, and material class. For fictional species use real morphological analogues while keeping fictional shapes.
2. Collect **multiple real photo angles**, ideally top, backlit underside, edge silhouette, side profile and node/petiole base. In the manifest record source URLs, copyright/license status and **which properties** each reference motivates; do not assume a copyrighted photo can be baked into textures.
3. Construct an accurate **adult** exemplar first. Model blade in normalized anatomical coordinates: `u=0` base to `u=1` apex, `v=-1..1` left/right margin. Lay down a midrib spine, secondary/tertiary vein curves, leaf edge, petiole continuation and main crease/camber. Model broad folds and slight irregularities intentionally, not perfectly repeated wavelets.
4. Give the exemplar enough geometry to carry **real silhouette deviations** and major local folds. At image resolutions where tertiary veins are invisible, retain them in material normals instead of wasteful geometry. Build UVs from the surface coordinates, with seam placement anatomically sensible.
5. From the adult exemplar create stage meshes J/E/B1/B0/S as applicable. Do not merely multiply vertices by a scale factor. Pay attention to folded/budding geometry, lobe expansion, juvenile tips and petiole lengths.
6. Ensure every stage has the **same attachment root and a declared compatible landmark mapping**. Check midrib and margin correspondences; export stage meshes with manifest metadata. When vertex topology can match, maintain matched vertex order across stages.
7. Generate at least 3 source variants or a small deformable shape space. Avoid clones by varying anatomical *families* and hierarchical growth, not by randomly rotating copied cards.
8. Define deformation channels at the anatomical landmarks: long/short lateral growth, local vein-zone width, tip deviation, leaf roll/fold, asymmetric blade tension, and small age-dependent wear. Validate deformer bounds to avoid fold-overs or inverted triangles.
9. Export deterministic GLB and source BLEND plus optional per-stage LOD representation. Store axes, scale, unit conversion, pivot, UV channels, material slots and bounding box explicitly in JSON.
10. Run offline validation. Check Blender opening/export/reimport, glTF validator warnings/errors, watertightness for closed organs, intentional open thin membranes, degenerates, flipped normals, finite UVs, tangent-space handedness, anchor continuity and realistic rendered size.
11. Render isolated gray clay at 0.3 m / 1 m / 3 m and side/underside. If geometry still reads as a sterile formula, **return to authoring**, not material tuning.
12. Wire into the proper `OrganFamilyCompiler`. Add the resulting stage sequence to recipe/asset coverage tests. Do not write a one-off special-case to `OrganismMeshes`.

### 7.2 Conventions and asset manifest example

```json
{
  "family": "cordate-broadleaf",
  "revision": 1,
  "source_glb": "exemplars/cordate-broadleaf/umbraheart-variant03.glb",
  "scale": "metres",
  "root_joint": "petiole_base",
  "midrib_axis": "+X",
  "surface_normal": "+Y",
  "uv_contract": "u=base-to-apex, v=left-to-right",
  "stage_meshes": ["bud", "emergent", "juvenile", "expanding", "adult", "senescent"],
  "registered_landmarks": ["root", "blade_base", "midrib", "apex", "left_margin", "right_margin"],
  "authorship": "original derived geometry",
  "reference_links": [],
  "asset_license": "project-owned",
  "source_sha256": "<compute actual digest at export>"
}
```

This is a **target schema example**, not proof that the file/assets exist. All exported exemplar arrays must specify how unit scale, handedness and Godot axes convert. Fail fast on missing meshes, inconsistent stages and empty provenance.

### 7.3 Organ-family construction details

| Family | Required non-generic geometry |
|---|---|
| Broad simple leaves | Irregular developmental outlines; asymmetric lateral/secondary veins; actual midrib relief and gradual petiole-to-lamina transition; species-specific basal sinus, apex and tip |
| Compound leaves | One continuous rachis, paired/subopposite/alternate petiolules; uneven terminal leaflet, scale variation by rachis station; functional connections |
| Ferns | Correct number/density/orientation of pinnae, terminal taper and divided leaflets, crozier-to-frond unfolding; pinnae attach to a tapered rachis; no isolated distal tips |
| Grass/sedge/ribbon | Sheathing base, longitudinal structure, torsion and physically thin creasing; broad distribution of bend/age; no regular rectangular ribbons |
| Needle/scale | Needles/short spur shoots/whorls from biologically coherent insertion sites; cross-section, tapered tip, age distinction; branchlets not duplicated angular rods |
| Fleshy succulent | Closed volume, variable cross-section/turgor, blunt/pointed tips and meaningful joint to stem; no inflated flat leaf shader |
| Floating/peltate | Peltate petiole underside insertion, radial veins, complex cup/notch and variable rim; compatible waterplane anchor |
| Carnivorous | Sundew rising petiole/leaf/tentacles, pitcher continuous wall/lip/hood, snaptrap hinged lobes/cilia, all with stage sequences and coherent mesh |
| Vine/climber | Support-following axis graph with rooted nodes/tendrils/hooks; real leaf petiole attachment and proper branch sheaths; host curvature from current support |
| Woody stems | Branch collars, taper by order, bark-flow normals/UVs, crown internodes, tapered terminal buds; contiguous junctions rather than mesh intersections |
| Moss microshoots | Species-specific leaflet/capitulum axis; small stage-dependent tip appearance; clustering from authoritative occupied coverage and biomass |
| Foliose/fruticose lichen | Lobed thallus with actual margin curl, or coherent branched podetia; typed nonvascular surface stages, *not* vascular midrib fantasies |
| Flower/fruit bracts | Continuously attached pedicel/receptacle, controlled opening/closing and lifecycle stages, appropriate cuticular geometry |

## 8. Complete species migration matrix — **all 48 catalog entries**

At implementation start, run `scripts/botanical/inventory.py` against `game/content/index.json` and the 48 live `game/content/flora/*.json` files. A stale README's 33-species list is NOT a completion checklist. The current entries are grouped below; re-evaluate new/removed entries programmatically.

| Organ-family workstream | Species / morphological requirements |
|---|---|
| Trees, woody crowns, shrub canes | **embercrown, glowbriar, shadebell, lanternbrush, hookthicket, kinkcane**; preserve each differentiated scaffold/cane and existing major crown design. Explicitly include woody tree species registered through content other than `game/content/flora` if present. |
| Climbers, runners, prostrate stolons | **clinglace, spiralvine, fenhook, coinrunner, brooklace**; stable support/node path; short petioles and compound/running growth. |
| Ferns and divided aquatic leaves | **veilfern, moonfern, fencomb**; refined rachides and pinnae; terminal distribution and actual leaflet development. |
| Grasses, reeds, sedges and fine ribbons | **frosttussock, glassrush, ringreed, lanternsedge, plumegrass, veilblade, streamribbon**; sheaths, whorls, tips, plume/bract stages and water deformation readiness. |
| Round/peltate/clover/paired leaves | **mooncoin, mirrorleaf, fenbead, trifold, tidebell**; center/underleaf insertion, correct multi-leaflet and paired nodes, hydrated blade deformation. |
| Rosettes, modified and carnivorous leaves | **glassfinger, sunstone_rosette, blue_sundew, pitcher_plant, snaptrap, prismstar**; volume and stage-specific traps, tentacles, petals, lanceolate leaves. |
| Sparse/wetland miniature flowering bodies | **siltstars**; preserve tiny real-unit body dimensions and correct colony contacts. |
| Mosses and lichen with microleaves/lobes | **velvetweave_moss, starmoss, bogglass_moss, floodlace_moss, pearl_cushion_moss, embercrust_lichen, ruffle_lichen, antlerlace_lichen**; use appropriate *nonvascular* organ compiler/colony surface; don't force same leaf age system. |
| Fungus and slime mold compatibility audit | **ambervein, carrion_bell, dewbonnet, emberfan_fungus, ghostbonnet, glass_antlers, rain_jelly**; explicitly mark as fungal/plasmodial; ensure new general tools don't break their separate geometry/lifecycle. Do not claim these as vascular stage-mesh conversions. |

**Pilot/hero tree mandatory second roster:** `PilotTreeMeshes.cs` dispatches `gloomspire`, `needlevault`, `crowncoil`, `emberpillar`, `basinwarden`, `palehollow`. Include needles, fronds, broad leaves, branches and bark attachments as applicable. Also inspect trees whose species definitions may be embedded in special content/managers and the visually approved **Ironlace, Umbraheart, Kiteleaf, Fenneedle**; don't assume their absence from `game/content/flora/*.json` means there is nothing to migrate.

**Coverage table script output (required artifact):** `species_id | archetype | visual.shape | organ families | recipe path | stage mesh families | assembler | validated static deformation | maturity swaps | juvenile/mature/stress render | in-world render | measured perf | status`. Every vascular species must be `complete`, every nonvascular species must be explicitly `not-applicable: fungal` or `complete: colony/microshoot`. Missing is a hard failure. The automated inventory should discover all known leaf-producing call sites in addition to the content registry, so hidden fallback paths are exposed.



## 9. Implementation work packets — complete in order, no optional pilot release

These packets are a dependency-ordered **full rewrite**. Do not stop at one demo species, and do not rename a Visual Lab candidate to “production” as a substitute. The suite needs an end-to-end conversion. Each packet includes exact implementation, proof, and next dependency.

### Packet 0 — Baseline, ownership, catalog manifest

**Do:** Snapshot Git status/HEAD/dirty source hashes, relevant `docs`, current active Godot PIDs, shader/runtime versions and hardware. Read the latest `.clanker`/agent ownership notes without modifying unrelated work. Generate machine-readable 48-entry catalog/shape/renderer inventory; locate all current uses of `LeafBlade.Build`, `Broadleaf.BuildLeaf`, `FoliageBlade.Build`, `Primitives.CurvedLeaf`, `CurvedBlade`, `SoftTube.Build`, `Primitives.Tube`, `PilotTreeMeshes.Build`, `FloraTier` and `ClimberNodeTier`.

**Save:** `docs/overhaul/botanical_rebuild_progress.md` with owner table, `build/botanical-rebuild/baseline/{repo-status.txt,catalog.json,renderer-variants.json,test-results.txt,perf.json}`. Do **not** commit machine-specific build outputs or giant binaries without a reason.

**Proof:** exact catalog count and explicit classification for each active species; list of plant-bearing code calls and stage dependencies. Run unmodified focused test baseline and record pre-existing failures; test environments contaminated by other Godot processes are diagnostics only.

### Packet 1 — Schema, exemplar loader, anatomical registry

**Do:** Create `Geometry/Botanical/` with strongly typed recipes, material/tissue types, asset manifest schema, family definitions, signed axes/units and root/landmark mapping. Parse recipes in pure managed C# usable by simulator tests; load GLBs only at Godot boundary or offline authoring pipeline, with a deterministic engine-neutral baked mesh representation accessible to tests. Validate hashes, source units, stable key, stage sequence, UV and correspondence. Implement fail-closed catalog validation.

**Proof:** loaders round-trip recipe/manifest; assets validated in Blender/glTF; C# tests reject missing stage, mismatched landmarks and malformed geometry; empty recipes fail, never call legacy fallback.

### Packet 2 — Stable botanical graph and leaf-age model

**Do:** Create typed parent/child axis graph, biological node/station positions, organ IDs, attachment frames, stable visually meaningful developmental programs, and organ maturity selector. Node hierarchy independent of tessellation and renderer batching. Tie render organ maturity to existing plant age/biomass and seeded emergence position; add minimal persistent *current* node states only where real individual initiation cannot be derived.

**Proof:** two recompiles same request produce identical graph digest; leaf IDs and static shape seeds remain identical across all three geometry tiers; two neighboring leaves can have different maturity stages; maturity persists through save-load/current-state reconstruction; a camera orbit doesn't change any organ state.

### Packet 3 — Author/build maturity exemplar families

**Do:** Build the Blender batch script, fixed units, typed GLB export, stage meshes and landmarks. Start with the most morphologically different **authoring representatives** (e.g. Umbraheart cordate, a divided fern, Shadebell/Embercrown compound leaf, Glassfinger fleshy leaf, a fine grass or reed, a peltate floating leaf). They are implementation **reference families**, not a gated optional trial. Complete all required families and register every dependent species before declaring done.

**Proof:** gray-clay sheets of buds/juveniles/adults for each family; valid geometry/UV/normal and registered attach points; morph-compatible stage transitions where feasible; provenance and asset hashes. Reject malformed exemplars rather than silently degrading them to formula blades.

### Packet 4 — Deformer applied once per organ

**Do:** Implement hierarchical/correlated shape coefficients and exemplar-space deformation. Apply at organ creation, preserve across stages, and share UV/material anatomy deformation. Use fixed sub-seeds independent of batch order/visibility; compile stage meshes from same fingerprint. Write a mesh self-intersection/normal orientation validator.

**Proof:** J/E/A variants preserve asymmetry and material marks; reordering leaves does not alter fingerprints; no inverted triangles/strange edge tears in 50+ seeds per family; same seed same mesh digest; different seed meaningful but within species shape envelope. Render multiple close-up pairs in neutral gray, no beautifying light changes.

### Packet 5 — Stem-axis and junction replacement

**Do:** Implement branch-axis centerlines, variable cross-section, organ insertion sockets, swept woody tissue and blended stitched junctions; petiole-to-lamina meshing, rachis/leaflet joins, stem/root/culm sheath, branching collar geometry, terminal buds and thin runners. Integrate into the graph before producing leaves. Review existing old tube util only for math; do not reuse it as the shape model.

**Proof:** high-angle and low-angle branch joints closed, no disconnected caps, no self-intersection or large abrupt normal seam. Gray clay side/back/silhouette captures; tests for parent child junction boundary continuity, frame orientation, UV registration, branch radius/taper and attachment transform after simulated wind bends.

### Packet 6 — Renderer, stage swaps, batching, cache

**Do:** Replace `FloraRenderer` whole-plant-only assumptions with explicit structural layer plus organ stage batches. Implement pool vs unique-chunk strategy in §6. Support asynchronous compilation, bounded uploads, material family surfaces, static identity, age-stage events derived by diffing *current stage state*, and dirty region scheduling. Preserve batch instancing of organism placement and separate ecological state ownership.

**Proof:** leaf `X` matures while adjacent leaf `Y` stays juvenile; only `X` changes geometry. Its petiole root, asymmetry, edge mark, material and seed remain constant. The plant is not rebuilt every render tick; no extra MeshInstance per leaf; stage bucket metrics documented. Force hundreds/thousands of simultaneous maturations without stalls or leaked batches. Preserve existing `FloraRenderer` frustum bounds, tier hysteresis, refresh budgets and world transform contracts where technically appropriate.

### Packet 7 — Convert **every** living organ family and production call

**Do:** Route all 48 registered flora through typed recipes; fungal/slime cases explicitly through their own dedicated assemblers. Convert/retire all vascular species' original leaf/stem builders, including bespoke `VisualBreakFlora` cases and all `ClimberNode` and reproductive variants. Migrate `PilotTreeMeshes` and special tree definitions. Remove `VIVARIUM_LEGACY_FLORA` switch and silent missing `FloraVisualProfile` fallback. The current ecology may remain unchanged; its render data must map to new inputs.

**Proof:** catalog coverage test fails if an eligible species has no new recipe or stage library. Static source scan + runtime counters show **zero legacy vascular-organ calls** during real world render. A 48-species specimen gallery plus separate pilot-tree gallery includes each species juvenile, mature, stressed/late where applicable. No major user-approved structural identity loss.

### Packet 8 — Integrate material anatomy and dynamic motion

**Do:** Rebind leaf/woody shaders to new authored anatomical channels, update `FloraVisualCompiler` and texture bake to consume stage-registered UV and vein masks, preserve undersides and species pigments. Align the motion anchor with the new node/parent frames; stage transitions must not snap when stems sway. Keep existing biologically credible transmission/roughness unless evidence shows a geometry integration defect. No new global lighting rig.

**Proof:** underside normals not inverted, leaf translucency follows actual thickness/vein location, petiole-midrib seam doesn't jump in wind, no texture drift between stages, no specular alias from split surfaces. Night-biome emission markings remain on their organs and move with them.

### Packet 9 — End-to-end load, save, catalog, high-density performance

**Do:** Exercise complete game/presets, ecological growth, reproduction/fruiting, climber support changes, death/collapse, leaf shedding, colony moss and lichen, world serialization, close camera passes and steady camera pans. Avoid expensive work on main render thread. Remove temporary test flags and dead code. Optimize the worst path with batching/chunk sizing/mesh reuse, not with blanket visual-quality loss.

**Proof:** game builds with zero new warnings; controlled unit/integration tests pass; 48-entry complete matrix + pilot tree roster; no new graphical errors; no runtime missing GLB/material; no unstable output across reloads; **30 FPS floor on GTX 1080** under measured representative worst-case forest, marsh and mixed vegetation. Record all frame statistics, draw calls, VRAM and changed GPU cost. Also run fallback/integrated-GPU sanity tests where available, but GTX 1080 is the hard local target.

### Packet 10 — Remove obsolete production infrastructure and finish handoff

**Do:** Delete/retire old leaf/axis tube generation APIs *from vascular production*, no unreachable compatibility wrappers for fear of breaking old content. Keep documented primitive functions for legitimate **nonbotanical** uses. Update all authoring, test, bake, API and README docs. Ensure no legacy compiler flag, duplicate geometry dispatch or hidden “missing recipe -> old procedural leaf” remains.

**Proof:** full source scan, all tests, final real-game captures and perf gate; one plain handoff with architecture diagram, how to author a new leaf stage, how to add a new species, how to regenerate caches and one-command verification.

## 10. Test catalog (implement these actual tests, not just manual inspection)

**Topology**
- No NaN/Inf, out-of-range indices, triangles with near-zero area relative to local organ scale, reversed lamina orientation, open boundaries where a closed woody stem is required, or accidental duplicate coincident surfaces.
- Junction boundary loops share positions/normals/tangent continuity within deliberate profile tolerances. No “tube jammed into leaf” attachment.
- Real-unit tolerance tests for attachment gap: at inspection scale, root/petiole displacement remains below one projected pixel; validate in world units with camera resolution and expected distance rather than one universal arbitrary meter threshold.

**Stage identity**
- Fixed leaf ID and variant match between bud, juvenile and adult.
- Shape fingerprint bitwise/serialized equal across save/reload and geometry tiers.
- Stage swap preserves organ anchor, left/right handedness, material source, growth direction and local pose.
- Neighbor leaves with different emergence thresholds correctly occupy different maturity buckets.
- Representative plant contains a mixed-age canopy/frond population, not synchronized all-at-once swap.
- Controlled bud-to-juvenile/full-size transitions do not leave double surfaces, large flashes or abrupt oversized popping.

**Authoring and silhouette**
- Mature and juvenile specimen galleries for **all 48** active species; compare against previously approved morphology and actual natural references. Negative tests for the known bad model: flat terminal fern pinnae, repeated leaf sine waviness, angular straps, evenly spaced branch tubes, unsupported lollipop leaves.
- Texture/vein/deformation registration through all life stages. Magnified clay normals/UV/curvature diagnostic snapshots.

**Runtime**
- Force repeated partial leaf maturation; prove no O(N plants × N leaves) global rebuild every frame.
- Batch compaction/removal and stage reallocation do not corrupt other organ transforms.
- Deterministic indexed geometry output at frozen current state, stable across camera, animation phase and light.
- Test shadow caster bounds, camera clipping and tier hysteresis; debug overlays correctly show selected buckets.
- Verify float stability/scale at smallest microshoot and largest pilot tree.
- Validate existing nighttime glow on stage-transitioning organs and aquatic placement at waterline.

**Performance**
- GPU and CPU p50/p95/p99/worst, frames over 33.33ms, total draws, resident VRAM, stage-swap dirty rebuild cost and upload latency.
- 60+ second dense moving camera; 60+ second stationary full-dynamics scene; one stress burst with many stage transitions. Use representative saved seed/preset, quality mode, camera path, shader phase and hardware in the evidence. Make comparisons with an isolated GPU; never kill unrelated processes.
- Track warm-up separately. A good mean with stalls below 30 FPS **fails** the target. If unrelated simulation causes stalls, attribute separately with GPU vs CPU profiling, but user-facing overall 30 FPS remains the target.



## 11. First-day implementation instructions for the next coding agent

1. **Read this entire document** and `AGENTS.md`. Confirm `E:\Vivarium` is your target checkout. No repository resets. Read latest working-tree `docs/visual-lab/STATUS.md` only to learn safe capture tools, not to change this assignment into a Visual Lab experiment.
2. Run `git status --short` and `git diff --name-only`; create progress ledger and list which old files are being edited by others. If a central file is concurrently owned, build additive infrastructure first and negotiate a narrow integration edit.
3. Inspect `game/content/index.json`, load every `flora/*.json` and emit a full CSV/Markdown catalog matrix. Verify the 48 count is current rather than hardcode it.
4. Search all plant mesh callers: `rg -n "LeafBlade\.Build|Broadleaf\.BuildLeaf|FoliageBlade\.Build|Primitives\.CurvedLeaf|CurvedBlade\(|SoftTube\.Build|Primitives\.Tube|PilotTreeMeshes|FloraTier\(" src game tests`. Classify the call sites; include direct `FloraVisualCompiler` callers and special terrain/coverage/climber render paths.
5. Inspect how `FloraRenderer` gets current plant age/biomass and how `WorldSerializer` reconstructs `Flora`. Write a short decision note on render-only developmental thresholds versus persisted current node age, and where stable IDs will come from. Do not start a new event-history mechanism.
6. Add strongly typed `BotanicalSpeciesRecipe`, `BotanicalGraph`, `BotanicalOrganKey`, `StageSelector`, `StaticOrganIdentity`, `BotanicalMesh` and corresponding unit tests. Make compilation work **without Godot** first.
7. Author one **whole maturity sequence** in Blender for a high-visibility real leaf family (e.g. Umbraheart-style cordate blade) and export GLB + manifest + corresponding deformer basis. The family is an implementation dependency to exercise the infrastructure, *not an optional acceptance pilot*. Ensure you can produce two leaves with different stable fingerprints and the **same fingerprint preserved from juvenile to adult**.
8. Author a real parent stem -> petiole -> leaf root connection using shared geometry. Render a neutral clay sheet with top, side, underside and close node views. Validate geometry, not shader improvements.
9. Wire staged organ surfaces into a new organ batch assembler; prove swapping leaf 1 from J to E without changing leaf 2, and prove root/shape/mark identity is unchanged. Bake deformation once and cache both predeformed shapes for that organ.
10. Measure actual draw and mesh counts with 500+ visible leaves. Choose pooled vs chunked large-leaf path based on data. Avoid a per-leaf node explosion and eager cross-product of stage meshes.
11. Populate recipes/exemplars for all organ families, reroute **every** eligible species, then migrate the separate pilot-tree builders. Run full coverage tests continuously so later species additions cannot quietly use legacy formulas.
12. Finish with end-to-end captures, performance, static source scan, deletion of old production callers, authoring documentation, progress ledger and handoff. Do not call it finished until the full acceptance checklist below passes.

### Example developer commands (validate available paths/executables first)

```powershell
Set-Location E:\Vivarium

# Inventory and call-site discovery
Get-Content .\game\content\index.json -Raw | ConvertFrom-Json |
  Select-Object -ExpandProperty flora
rg -n "LeafBlade\.Build|Broadleaf\.BuildLeaf|FoliageBlade\.Build|Primitives\.CurvedLeaf|CurvedBlade\(|SoftTube\.Build|Primitives\.Tube|PilotTreeMeshes|FloraTier\(" src game tests

# Pure managed build and tests
dotnet build .\Vivarium.sln -c Debug -nologo
dotnet test .\tests\Vivarium.Sim.Tests\Vivarium.Sim.Tests.csproj -c Debug -nologo

# Godot C# client build (actual path present on 2026-10-18)
dotnet build .\game\Vivarium.csproj -c Debug -nologo

# Existing Godot executable; verify path/version before use
& .\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe --version
```

Add real runnable commands to `scripts/botanical/README.md` during implementation for:
- Blender source authoring/export to declared GLB/manifest with source hashes;
- strict validation of every package;
- compiling reproducible staged botanical assets;
- rendering stage/connection clay sheets through the live Godot client;
- catalog coverage and frame-time stress tests.

Don't invent final script flags before implementing their handlers. The PowerShell block above describes existing or straightforward repo/tool paths, not proof that any new botanical command already exists.

## 12. Final definition of done (all mandatory)

- [ ] All currently indexed vascular flora and special pilot trees use **the new anatomical leaf/stem and junction system**; all nonvascular exceptions are explicit and accurate.
- [ ] Every relevant species/organ family has validated, versioned, documented maturity-level mesh sequences. No missing GLBs/recipes are quietly replaced by generic old shapes.
- [ ] Individual leaves can **mature independently**, visibly replacing their own stage mesh while retaining permanent deformation, organ ID, attachment, venation/material correspondence and background wind connection.
- [ ] Static shape deformation is chosen and baked **once per organ identity**, and deterministic across restart, world reload, camera/tier switches and build ordering.
- [ ] No visually obvious independent cylinders meeting sheets, disconnected petiole roots, regular sine-wave margins, repeated isolated fern terminal pinnae or generic rubber-tube limbs at normal inspection distance.
- [ ] Specimen and habitat images preserve all user-reviewed biological/silhouette features and fictional species identities; genuine visual improvement confirmed in close geometry-only (clay) inspection.
- [ ] Correct normals, UVs, vein masks, optional backface transmission and stable material mapping survive transformations and stage changes.
- [ ] Batch counts, memory and dirty-region updates are bounded; no per-leaf Godot nodes, massive same-frame geometry churn or permanent transparent crossfades.
- [ ] Real GTX 1080/first-gen Ryzen dense-world measured FPS **never falls below 30 FPS** in the agreed representative workload, with p95/p99/worst recorded (if pre-existing unrelated stalls prevent this, document causal attribution and do not falsely claim acceptance).
- [ ] Automated geometry/maturity/determinism/catalog/scene tests pass with no new warnings or rendering failures. Former failing unrelated tests are listed accurately, not erased.
- [ ] Obsolete vascular generation call paths and `VIVARIUM_LEGACY_FLORA` flags are gone from production; docs, cache/export commands and future-species authoring instructions updated.
- [ ] Existing unrelated working-tree changes and agents' ongoing work preserved. Final diff/commits and evidence locations recorded in `docs/overhaul/botanical_rebuild_progress.md`.

## 13. How to author the next new species after the rebuild

1. Define ecological behavior and current species visual identity in `game/content/flora/<species>.json` as normal. Add to `game/content/index.json`.
2. Select one or several typed organ families and define the species' axis/branching/developmental grammar in `game/content/botanical/species/<species>.json`. Set stages and maturity thresholds/overlapping growth.
3. Reuse well-matched anatomical exemplars or author a new family. For new family, produce adult first, then associated age stages and fixed registered landmarks; validate GLB/units/UVs.
4. Specify coherent shape-deformation ranges, family variants and per-organ stable seed behavior. Preview multiple juvenile, mature and mixed-age specimens in clay.
5. Compile packages, validate stage-root/tip/vein mapping, configure appropriate material family and build texture maps aligned to anatomical coordinates.
6. Run full catalog coverage, stage/motion/attachment tests, habitat view and perf profiling. Only then consider the species supported. No special `OrganismMeshes` fallback allowed.

## 14. Source material and internal precedents

- `docs/flora_visual_review_log.md`: most recent approved and pending plant morphology.
- `docs/overhaul/flora_species_art_plan.md` and `flora_reference_board.md`: actual botanical reference links and distinct fictional morphology (older inventory).
- `docs/photorealism/flora_visual_system.md`: existing multi-surface/multi-tier metadata, texture pool, and shader constraints.
- `docs/visual-lab/STATUS.md` and `scripts/visual-lab/`: real Godot before/after capture, deformation/GLB import, normal-stage scene render.
- `src/Vivarium.Sim/Geometry/Form/`: math/meshing routines to **replace or selectively recycle**, not general quality benchmarks.
- `game/content/index.json`: single active flora catalog; authoritative over this document's dated list.
- `game/Render/OrganismRenderers.cs`: batching/visibility implementation that cannot support leaf-level swaps without redesign.

**Execution priority:** start with typed organ identity and maturity stage/cache/renderer design, *then* exemplar authoring and junctions. Otherwise even beautiful stage meshes cannot actually swap per leaf under the existing whole-plant `MultiMesh` approach. Continue to full species and pilot-tree conversion; do not hand back a partial pilot as the requested overhaul.

## 2026-10-18 — Species-specific fictional leaf and frond silhouette mandate

The leaf-scale designs are **invented Vivarium lineages**, not direct reconstructions of Earth plants. Ecological realism is preserved through biomechanics and anatomy, not through using ordinary terrestrial outlines. **The appearance of every leaf, needle spray and frond should be immediately species-distinctive.** Existing plant habitat, growth form, material palette and spatial silhouette remain important, but original generic leaf shapes must be redesigned whenever they impede visual identity.

**Authoritative silhouette roster:** `game/content/botanical/silhouette_bible.json` specifies a distinct adult hero contour, edge structure, venation, attachment, growth and developmental transition for every one of the 33 vascular species and 6 pilot trees. The `scripts/botanical/verify_silhouettes.py` gate checks catalog coverage and distinct semantic design signatures. It **does not** prove that authored geometry faithfully matches the roster.

For each living vascular primary organ family, author an anatomical model from those species-specific features. At least three closely related but visibly differing mature exemplars and a meaningful stage sequence are required. Each novelty must arise from real tissue mechanics, named vein paths, differentiated lamina lobes, asymmetric expansion, sheath topology, distributed vascular support, flotation, mechanical defense, trapping or fluid-flow adaptation—not from adding random spikes, waves, texture patterns or serrations.

Verify black silhouette at close scale and a distance where the organ is still resolvable. A plain clay render must carry each hero feature without optical tricks. Test within-plant variation and between-species confusability, avoiding identical serration rhythms or fixed template outlines. The characteristic contour and vein architecture should arise naturally through maturity, rather than suddenly appearing as a final-stage scale swap. Roots and petiole sockets remain stationary across swaps, as do individual deformation fingerprints.

**Current three anatomical revisions:** Shadebell's broad asymmetric, three-veined bell-manta leaf; Glassfinger's four-keeled inflated digit with differentiated apical claws; Veilfern's unequal staggered crescent-sail pinnae and divided terminal spear. These are early visual passes, not final artistic acceptance. Remaining 36 main foliage/spray families still need authored geometry according to the roster, alongside their woody, reproductive and special-purpose organs.

Do not change the explicit nonvascular/fungal routing into inappropriate vascular leaves. Moss/lichen colonies and fungus/slime molds still require their separate anatomy specifications.
