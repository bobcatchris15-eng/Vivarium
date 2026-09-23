# ADR-001 — Engine, language, renderer, test mechanism

Status: accepted (2026-09-23)

## Decision
| Concern | Choice |
|---|---|
| Engine | **Godot 4.7.1-stable, .NET build** (`Godot_v4.7.1-stable_mono_win64`) |
| Language | **C# 12 / .NET 8** (SDK 8.0.4xx) for both simulation and client |
| Renderer | Godot **Forward+** on Vulkan (D3D12 fallback via Godot's driver selection) |
| Simulation | Pure C# class library `src/Vivarium.Sim` (net8.0) with **no Godot reference** |
| Tests | xUnit (`tests/Vivarium.Sim.Tests`) + Godot boot/render/smoke runs driven by `scripts/verify.ps1` |
| Packaging | Godot export preset "Windows Desktop", export templates `4.7.1.stable.mono` |

## Commands (from repository root, PowerShell)
- Build: `dotnet build Vivarium.sln -c Debug`
- Test: `./scripts/verify.ps1` (all suites) or `./scripts/verify.ps1 -Suite World,Hydrology`
- Export: `./scripts/export.ps1` → `build/release/Vivarium/` and `build/Vivarium-<version>-win64.zip`

## Prerequisites (not in git)
- .NET 8 SDK.
- Godot 4.7.1 mono editor unpacked to `tools/godot/Godot_v4.7.1-stable_mono_win64/`, or set
  `$env:VIVARIUM_GODOT` to the console executable path.
- Export templates `4.7.1.stable.mono` in `%APPDATA%\Godot\export_templates\`.
- NuGet access at **build** time only (Godot.NET.Sdk, xUnit). The runtime is fully offline.

## Rationale
- **Windows packaging**: Godot exports a self-contained exe + pck + .NET runtime folder; no installer needed.
- **Procedural 3D / shaders**: `ArrayMesh`, `MultiMesh`, and a Vulkan shading language cover the
  procedural terrain, cut walls, water, and instanced organisms without authored assets.
- **Simulation**: C# gives value types, predictable performance, and strong typing for a large
  deterministic ecology. Keeping it engine-free makes determinism auditable and makes
  "render state is never authoritative" structurally true — the sim cannot see the renderer.
- **Headless verification**: nearly every accept clause is a plain `dotnet test`, running in seconds with no
  GPU. Godot runs `--headless` for the boot test, and windowed briefly for shader and render smoke.
- **Offline**: no engine services, telemetry or asset stores are used at runtime.
- **Cold-worker editability**: plain C# files, JSON content, a scene built in code (a single trivial
  `.tscn`), and one PowerShell entrypoint.

## Rejected alternatives
- **Godot + GDScript**: the fastest to iterate on, but a slow interpreted inner loop for thousands of organisms
  × fixed ticks at 16× speed, and it can't be tested without the engine.
- **Unity**: licensing and editor-bound builds, and a heavy headless story. A poor fit for a clean offline checkout.
- **Rust + Bevy**: excellent determinism, but a much larger rendering and UI build-out, and slower iteration.
- **MonoGame / raw D3D**: every renderer feature (shadows, tone mapping, UI) would have to be built by hand.

## Consequences
- Determinism is "same binary, same machine class" (IEEE double, no FMA contraction relied on, no
  parallelism in the sim). Cross-architecture bit-identity is not promised.
- Godot C# projects need the .NET-enabled editor and templates. The standard build cannot open them.
