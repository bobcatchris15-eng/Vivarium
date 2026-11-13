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
