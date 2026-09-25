# Orchestrator state

Revision basis: main `8f2546d` (2026-09-25). Canonical goal and constraints: `CURRENT_WORK.md`, `HUMAN_HANDOFF.md`.

## Structural topology
- `src/Vivarium.Sim/Content/Definitions.cs` and `ContentLoader.cs`: species data contracts and JSON loading.
- `src/Vivarium.Sim/World/VivariumWorld.cs`: simulation scheduling and persistence root.
- `src/Vivarium.Sim/Flora/FloraSystem.cs`: vascular individuals, legacy colonies, slime individuals.
- `src/Vivarium.Sim/Coverage/*`: sparse raster state and growth rules; `task/gmb` adds world-facing system.
- `src/Vivarium.Sim/Geometry/Form/*` and `OrganismMeshes.cs`: reusable form kernel and organism meshes.
- `game/Render/OrganismRenderers.cs`: flora/fauna instances, variants, materials.
- `game/Shaders/flora.gdshader`: per-instance visual deformation and flora materials.
- `game/App/SmokeRunner.Reference.cs`: deterministic 40-scene reference capture.

## Active work
| Task | Location | Dependency | Acceptance |
|---|---|---|---|
| S1 variation | new isolated worktree from main | form kernel exists | 12 flora variants, individual seed deformation, geometry/reference/perf checks |
| Coverage integration audit and fixes | `task/gmb` worktree | gma branch base | all coverage integration tests pass; no stale colony individuals for coverage species |
| Main integration | main | reviewed tasks | merge or cherry-pick gma/gmb, resolve compatibility, full focused validation |

Native Codex subagents replace the Gemini-specific `define_subagent`/`invoke_subagent` commands from the orchestration skill. Each worker receives a bounded task and file pointers; root performs merge and independent validation.
