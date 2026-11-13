# Orchestrator State — Vivarium
Updated: 2026-11-13
Git starting HEAD: 72a6234
Dependency discovery: rg-based (no graphify graph exists); native Codex workers adapt the pointer protocol.

## Project topology
Repr/Contracts -> Plant, Substrate, Colony, Light, Ecology. PlantGraph/Compiler -> game/Repr/Plant chunk renderer. SubstrateField -> Mesh/SubstrateMesher -> game/Repr/Substrate; Ecology consumes Substrate and updates World fields/Flora. OcclusionVolume -> SkyVisibilityField -> lit shaders; EnvironmentRig owns production lighting. Main dispatches Repr harness demos. Root owns Main/shared harness, selftest Program, docs, Git.

## Task DAG
| Task | State | Dependencies | Worker | Evidence |
|---|---|---|---|---|
| P2g | rejected; preserving evidence/restoring accepted baseline | accepted P1/P3 | Terra plants | baseline48 and candidate6 images inspected; worse appearance and higher geometry |
| E1/E2 | recovering | S1b | Terra | pending |
| INT-L | starting | L1b | Terra | pending |
| S2b | queued | S1b | Terra | pending |
| C2 | queued | C1/S1b | Terra | pending |
| INT-F | queued | P2g | Terra | pending |
| Review | queued | integrated winners | independent Terra | pending |

## Validation
Fresh pre-takeover dotnet build: exit 0, 9 warnings. Historical task statuses in ledger are stale after quota interruption.

## Current evidence / decisions
- P2g generated family rejected after two rendered iterations: sparse canopy initially, then detached dense foliage balls and crystalline fern with higher cost. Preserve the experiment and use RD12a cached legacy assemblies as production direction. This preserves visual quality rather than treating hypothetical generator wins as implemented.
- Targeted capture filter exercised: candidate report has exactly six requested shots and exits REPR_REVIEW_COMPLETE.
- Recovery substrate checks 18/18 and colony 12/12 pass on explicit isolated DLL; weak colony wet-route ratio remains 1.11 and is not accepted as strong ecological proof.
- dotnet run artifacts-path is stale-output hazard; use build then explicit DLL.

## Active wave after 823ee6d
- flora_chunks (Terra): production INT-F first slice landed, all-world draw records/camera selection; chunk locality still incomplete, GPU probe pending. Own OrganismRenderers/FloraRenderer.*.
- light_production (Terra): production controller/rig/shaders/Main/GameSession; Stock remains default until matched A/B. Consumes all-world flora source API from flora_chunks.
- ecology (Terra): fix round1 independent review findings in build/reviews/E1-review.md; no ecology commit yet. Nondefault grids, local terrain state continuity/save side effects, first infiltration delta require corrections. Eight author checks and 90day diagnostic runs passed but did not cover these.
- Independent lighting infrastructure review fixed FIFO/cancellation/fault replay; approved and committed823ee6d.
- Root corrected --repr-shots state-order bug: all logical ApplyVariant calls must execute before filtering screenshots; post-fix soil log names now align and requested8 captures complete. Shader cap30 runtime verified.
- Next queued: E rereview, S2b material/relief/litter look (current top remains camouflage wallpaper, cut face nearblack and outcrop cartoon cracks); C2 moss/lichen appearance; production substrate/colony integration; real paced perf/backlog and final six demos.
