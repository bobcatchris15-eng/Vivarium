# Orchestrator state

Revision basis: main `7455b73` (2026-09-25). Canonical goal and constraints: `CURRENT_WORK.md`, `HUMAN_HANDOFF.md`.

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
| S1 variation | merged main `9d416ae`, `7455b73` | form kernel exists | 12 variants, blade-only seed deformation, geometry/reference passed; isolated perf impact unmeasured |
| Coverage integration audit and fixes | `task/gmb` worktree | gma branch base | all coverage integration tests pass; no stale colony individuals for coverage species |
| Main integration | main | reviewed tasks | gma merged; cherry-pick gmb after review, resolve compatibility, full focused validation |
| S2b grass family | `task/s2b-grass` worktree | S1 variation merged | distinctive, deterministic fine-blade tussock and reference evidence |

Native Codex subagents replace the Gemini-specific `define_subagent`/`invoke_subagent` commands from the orchestration skill. Each worker receives a bounded task and file pointers; root performs merge and independent validation.
