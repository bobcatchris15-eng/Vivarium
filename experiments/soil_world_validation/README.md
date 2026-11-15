# Physical soil validation

Run from the repository root. Each console project references the real simulation library and writes diagnostics under `build/soil-surface-20261115/`. These are bounded engineering fixtures, not visual or whole-game performance acceptance.

Build each project into a distinct artifacts directory, then execute that directory's DLL. For example:

```powershell
dotnet build experiments/soil_world_validation/SoilWaterRegression.csproj --artifacts-path build/soil-water-check
dotnet build/soil-water-check/bin/SoilWaterRegression/debug/SoilWaterRegression.dll
```

- `../soil_surface_test/SoilSurfaceTest.csproj`: canonical triangles, zero-amplitude preservation on arbitrary coarse spacing, cache/rebase/snapshot behavior, physical mesh, sculpt/save/load, closed basin, and water addition.
- `../soil_surface_test/SoilTileMesherTest.csproj`: adjacent LOD boundary vertex/segment equality and sampled triangle height errors. Sampled errors are not a global mathematical error bound.
- `SoilWorldValidation.csproj`: real tree-bearing world creation, root-area/outside sculpt, composed support, exact save/load digest. Requires the baseline legacy fixture below.
- `SoilUpgradeTest.csproj`: real pre-soil tree save migration, original trunk anchor, macro/sculpt storage, mobile/subsurface water, new save/load digest and idempotency. Requires the tree legacy fixture below.
- `SoilWaterRegression.csproj`: mature high-flow spring pool, before/after upgrade and after settling. Asserts finite state, persistent pool and conservation; temporal surface-motion measurements are diagnostics, not acceptance thresholds.

## Legacy fixture provenance

The compatibility checks intentionally require saves written by a **pre-soil** simulation assembly. Writing a version-zero save with the current assembly cannot prove historical compatibility. The retained source assembly is `build/water-redesign-20261114/final-source/Vivarium.Sim.dll` (module ID `9c5ffb8b-5882-43bc-af1c-5459e603403e`; use the writer log as authoritative if this identifier differs).

Build `../soil_surface_test/LegacySaveProbe.csproj` with `-p:FrozenSimAssembly=<absolute-pre-soil-dll>` and a unique artifacts directory. Run its resulting DLL with:

```text
write build/soil-surface-20261115/before-soil.vivsave
write build/soil-surface-20261115/before-soil-tree.vivsave tree
```

The writer logs the actual assembly module ID and saved digest. If that retained assembly is absent, obtain a verified pre-soil build first; do not silently replace it with the current assembly. User saves are never required by these tests.

Native review commands and limitations are recorded in `docs/representation-rebuild/soil-surface-checkpoint-2026-11-15.md`. Native artifacts are local and ignored by Git.
