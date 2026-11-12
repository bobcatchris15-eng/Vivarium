# Vivarium overhaul

Coordination record for the reconstruction and polish effort. Orchestrator state is in `.claude/ledger.md`;
this folder holds findings and evidence.

| File | Contents |
|---|---|
| `baseline.md` | Pre-overhaul captures, harness commands, resource measurements |
| `audit-flora.md` | 125 plant/fungus/cover species: inventory, attachment mechanics, defects F1–F15 |
| `audit-fauna-guardians.md` | 17 fauna, 10 guardians: inventory, joints, behaviour, defects A0–A10, G1–G9 |
| `audit-ecology.md` | Tick structure, niches, matter loop, defects E1–E13 |
| `audit-performance-architecture.md` | Cost and structure, items P1–P19, R1–R11 |
| `audit-ui-ux.md` | Screens, flows, design tokens, defects U1–U24 |
| `species-matrix.md` | Per-species acceptance (created with the first species batch) |

All audit findings were read from code. None counts as confirmed until a render or measurement backs it.

## Backlog, in dispatch order

Edits are serial on `main` (one Clanker at a time; one Godot window at a time). Each batch ends with
build + boot test + the relevant review capture, compared against `build/overhaul/baseline/`.

### Batch 0 — evidence harness (blocks honest validation of everything else)
| id | item | why first |
|---|---|---|
| H1 | Fauna specimen review (A0): `--specimen-review` hides fauna | no way to evidence any fauna fix |
| H2 | Specimen review on sloped cells and with a forced LOD tier | F2 and F3 are untestable without it |
| H3 | Diagnostics counters (R9): GPU time, allocated bytes/frame, instance and VRAM counts | perf claims need numbers |

### Batch 1 — shared plant continuity
| id | item | files (from audit) |
|---|---|---|
| F5 | End caps on `Primitives.Tube` | `Primitives.cs` |
| F1 | One fork-junction helper: tapered children, closed trunk top, fillet via `SoftTube` | `Form/SoftTube.cs`, `OM.NewTreesA.cs`, `OM.Splitcrown.cs`, `OM.Lacemantle.cs`, `OM.Cragparasol.cs` |
| F6 | Leaf blade base width and closed petiole | `Form/Broadleaf.cs`, `LeafBlade.cs`, `Primitives.cs` |
| F2 | Ground contact: multi-sample base, skirt below grade | `OrganismRenderers.cs`, `OM.Veilfern.cs` |
| F4 | Continuous bark UVs in metres | `flora.gdshader`, `Primitives.cs` |
| F8 | Shared fruit anchoring | per audit |
| F3 | LOD cross-fade instead of hard swaps | `OM.VisualBreakFlora.cs`, `FloraLeaf.cs`, `OrganismRenderers.cs` |

### Batch 2 — animals
| id | item |
|---|---|
| G1 | Guardian clip chosen by actual motion, not activity (stops sliding in idle poses) |
| G3 | Guardian foot planting: lock stance foot, match stride to speed |
| A1 | Fauna behaviour states: stop, feed, rest, accelerate |
| G5 | Swimming guardians respect depth; use water-contact outputs |
| A2/A5 | Per-foot ground contact for fauna; IK for toad and springtail limbs |

### Batch 3 — measured efficiency (safe mechanical)
P1 environment parameter throttling · P2 caustics out of per-light `light()` · P3 sculpt rebuild throttling ·
U20 window relayout only when dirty · E12/E13 flora and ambient-cover tick frequency ·
R4/R6 remove dead code (`OrganismRenderers.GillTrial.cs`, `BranchModuleLoader.cs`, guardian audit/candidate flags).

### Batch 4 — UI defects
U1 Tab hide · U2 Esc and blocked keys · U3 unreadable autosave handling · U7 show placement-invalid reason ·
then the design-token pass from `audit-ui-ux.md` §6.

### Batch 5 — ecology
E6 coverage moisture decay · E1 fauna budget allocation · E2 predation as a death source ·
E3 nutrient yield (option A, content only) · E7 inheritance variance. Validate with `--soak` only.

### Batch 6 — species distinctiveness
Species-by-species pass using the matrix; starts after Batch 1 so shared junction fixes are in place.

## Needs owner decision
| id | question |
|---|---|
| P12 | Reduce SSAO resolution / volumetric fog froxels / GI settings at the single quality tier? **RESOLVED 2026-11-12: owner chose fog 64 + 3D resolution 85%** (pc5: GPU 33.7 -> 30.6 ms, 30 fps). SDFGI and SSAO unchanged |
| P13, R2 | **P13 RESOLVED 2026-11-12:** ambient cover is a 2 m chunk cache, rebuilt only on tracked-plant establishment or removal, terrain or prop edits, and quality change; camera moves never regenerate. R2 (merging the scatter systems) still open. |
| R1 | Extend `RenderSnapshot` to coverage (fixes a worker-thread race) or drop the unused dirty-region path? |
| R3 | Move guardian–plant contact into the flora vertex shader? |
| P11 | Drop unused 4K texture sets and unbound EXR sky maps from the repo/export? |
| U5, U6, U13–U18, U23, U24 | Confirm-on-discard, UI scaling model, pause menu, catalogue portraits, stats shown, rebinding, look/font/icons |
| OD-R | **RESOLVED 2026-11-12 (7667ed9):** rocks seated with their widest band at ground (lowest rim support +0.12*SizeY). Existing saves keep stored heights until a rock is moved. Size unchanged. |
| OD-W | **RESOLVED 2026-11-12 (7667ed9):** render-only. Pond extends 3 cm past the shore and fades by a 5x5-smoothed depth (UV2), so the edge is a smooth contour; sim heights unchanged. |
