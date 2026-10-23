# Photorealistic Rendering Overhaul — Execution Board

Authoritative progress record for [`PHOTOREALISTIC_RENDERING_PLAN.md`](PHOTOREALISTIC_RENDERING_PLAN.md) (WP-01 … WP-24).

> [!NOTE]
> This board is the source of truth for packet state. It is updated by the orchestrator after each merge, not by worker agents.

---

## 1. Checkpoint

| Item | Value |
| :--- | :--- |
| Foundation commit | `981b14b3` — `checkpoint(photoreal): WP-01..06 + WP-19/20 landed and building clean` |
| Branch | `main` |
| Build gate at checkpoint | `dotnet build Vivarium.sln -c Debug` → 0 errors; `dotnet build game/Vivarium.csproj -c Debug` → 0 errors |
| Working tree at checkpoint | clean (165 MB of texture masters + guardian assets committed; 69 doc deletions accepted as intentional) |

Every worker runs in its own git worktree branched from `981b14b3`, so packet work is isolated, individually revertable, and never collides on `obj/` or `bin/`.

---

## 2. Verified packet state

State is **evidence-based** — each row was confirmed by reading the source, not by trusting a prior report.

| Packet | State | Evidence in tree |
| :--- | :--- | :--- |
| WP-01 | **DONE** | `SdfgiEnabled/SsrEnabled/SsilEnabled`, `SsaoLightAffect 0.42`, `ShadowBias 0.0035`, `Parallel4Splits`; `project.godot` `msaa_3d=2`, `use_taa=true`, shadow size 8192 |
| WP-02 | **DONE** | `canopy_sky` → `enclosure_backdrop(EYEDIR)`; `DaySkyContribution 0.85`; `HabitatReflectionProbe` |
| WP-03 | **DONE** | `FogEnabled = true`, `FogAerialPerspective 0.18`, `VolumetricFogDensity 0.0045`, `_habitatMist`; night flora `ShadowEnabled`, `DualParaboloid` |
| WP-04 | **DONE** | `CameraAttributesSetDofBlurQuality(High)`, `DofBokehShape.Circle`, exp. metering, `DofBlurFarEnabled`; `CameraRig._focusDistance`, `VIVARIUM_DISABLE_DOF` |
| WP-05 | **DONE** | `SurfaceSet(Color, Normal, Roughness, Height, Ao)`; `_hgt`/`_ao` bound; `sample_stochastic_pbr` L70; `pom_raymarch` L149 |
| WP-06 | **DONE** | `const int sub = 6`; `CatmullRom` + `RenderHeight`; tangents in `Bridge.ToArrayMesh`; `damp_*`/`leaf_*` sampled; 24-step POM |
| WP-07 | **MERGED** | `BuildWalls` subdivided (8 rows, ~3 cm), layer-specific displacement (turf lip +1.2 cm, ped ±1.5, cobble ±2.2, bedding ±2.8), 12–25 rootlet tubes, `specular_schlick_ggx` + POM + rock binding + clearcoat seepage. Merged `39113eb4` |
| WP-08 | **NOT STARTED** | `LoamSurfaceDetailRenderer` L54 still `cz-1 .. cz+1` (6 m window); `SoilDetailRenderer` L54 still `StandardMaterial3D`, all `CastShadow = Off` |
| WP-09 | **PARTIAL** | 4K isolated atlas generated (`ForestFloorLitter_4K_*`, 6 maps) + `GenerateLitterAtlas.py`. Outstanding: `LoamLitterBuilder` still crops `ScatteredLeaves007`, no height-field stacking, no 6-seg bark twigs, no `loam_litter.gdshader` backlight. WIP commit `2512d554` |
| WP-10 | **MERGED** | Undercut peds (0.72 waist, 16×5 rings), triplanar `soil_*` with clamp removed, real `u,v` on blades/leaves, `BankFringe` wired, shadows on. Merged `4ad9e409` |
| WP-11 | **MERGED** | Lit Forward+ `blend_mix, depth_draw_always, cull_disabled, diffuse_burley, specular_schlick_ggx`; analytic B-spline normals; un-clamped optical path; humic absorption; `+0.055` floor removed. Merged `438b340d` |
| WP-12 | **NOT STARTED** | No `water_caustics.gdshaderinc`; albedo-additive `caustic_pattern` still in `terrain.gdshader` |
| WP-13 | **NOT STARTED** | No meniscus/foam; `_drops`/`_sheets` shadows off; no `plunge_impacts` |
| WP-14 | **NOT STARTED** | 6–8 sided prisms; 16 pebble variants; gravel 140/m²; no `rock_hgt`/`rock_ao`. Fresh worktree `wp14-rocks` ready |
| WP-15 | **NOT STARTED** | `around = 28`, no hollow tunnel, normals from undisplaced `dir` L406; `cap > 0.5` writes no normal |
| WP-16 | **MERGED** | `CastShadow = On`, `>= 4` gate removed, 8–14 shoots/cell, UV2.y base-to-tip, POM pore space + sheen + dew glints. Merged `d0afb267` |
| WP-17 | **NOT STARTED** | Lichen branch leaves `bump = 0.0`, `ao = 1.0` |
| WP-18 | **NOT STARTED** | No aquatic coverage renderer; `surface_mode == 4` absent |
| WP-19 | **DONE** | `Broadleaf` L224-225 → `rows 28/cols 16` at Tier 0; two-sided lamina |
| WP-20 | **DONE** | `sway`/`wind_strength` `0.35` with `VIVARIUM_FREEZE_WIND`; `T_leaf` Beer-Lambert L50 |
| WP-21 | **PARTIAL** | Bark fluting, buttresses, callus rolls and analytic normals done. Outstanding: crown still uses 5-sided `Blade` (not `Broadleaf.BuildLeaf`), no `leaf.gdshader` routing, fungi untouched (`OrganismMeshes`, `GhostbonnetGeometry`). WIP commit `5ee6cde1` |
| WP-22 | **NOT STARTED** | `FaunaRenderer.MakeMmi` `CastShadow = Off` |
| WP-23 | **NOT STARTED** | `fauna.gdshader` procedural noise, no SSS/thin-film |
| WP-24 | **NOT STARTED** | Vertex-coloured `StandardMaterial3D`; single-bone vertical foot offset; CPU `ClearSurfaces()`. Fresh worktree `wp24-guardians` ready |

**Correction to the prior handoff:** WP-03, WP-04 and WP-11 were misreported. WP-02/03/04 are complete; WP-11 was never begun (zero file changes). The WP-07/08/21 files that showed as modified were pre-existing unrelated edits, not packet work.

---

## 3. Wave plan

Waves are ordered by file ownership, not by plan phase, so that no two concurrently-running packets touch the same file.

| Wave | Packets (parallel) | Rationale |
| :--- | :--- | :--- |
| **1** | WP-07, WP-09, WP-10, WP-11, WP-16, WP-21 | Disjoint file sets. Longest dependency chains started first |
| **2** | WP-08, WP-12, WP-17, WP-22, WP-14, WP-24 | Each unblocked by a wave-1 merge or already independent |
| **3** | WP-13, WP-18, WP-23, WP-15 | Tails of the water, coverage, fauna and prop chains |

### File-ownership conflicts that force serialisation

- `surface_fluid.gdshader` / `water_stream.gdshader` → WP-11 **then** WP-12 **then** WP-13
- `CoverageRenderer.cs` → WP-16 **then** WP-17 **then** WP-18
- `PropMeshes.cs` + `PropRenderer.cs` → WP-14 **then** WP-15
- `OrganismMeshes.cs` → WP-21 **then** WP-22
- `LoamLitterBuilder.cs` / `LoamAggregateBuilder.cs` → WP-09 ∥ WP-10 **then** WP-08 (WP-08 sets `CastShadow` inside both builders)

---

## 4. Universal worker rules

Repeated verbatim in every dispatch prompt.

1. **Build both assemblies before reporting:**
   ```powershell
   dotnet build Vivarium.sln -c Debug
   dotnet build game/Vivarium.csproj -c Debug
   ```
   `Vivarium.sln` does **not** build `game/Vivarium.csproj`.
2. **Clockwise front-face winding.** Godot 4 front faces are clockwise. For hand-built triangles verify `dot(faceNormal, vertexNormal) > 0`.
3. **Shader colour constants are linear**, not sRGB, under AgX (`sRGB 0.5` ≈ `linear 0.214`).
4. **Stay inside the packet's declared file list.** Touching a file outside it is a packet failure, not initiative.
5. **Do not run Godot.** The orchestrator runs the single serialised GPU capture batch per wave.
6. **Do not modify `VivariumWorld` simulation state or digests.** Presentation-side only.

---

## 5. Wave 1 dispatch record

All worktrees live under `C:\Users\Chris\.local\share\opencode\worktree\8235e5\`, branched from `981b14b3`.

| Packet | Worktree | Branch | Dispatched | Session | Merge |
| :--- | :--- | :--- | :--- | :--- | :--- |
| WP-07 | `wp07-strata` | `wp07-strata` | yes | `ses_e9981f0e0ffeB3vbcyiXXEn8c0` | **MERGED** `39113eb4` |
| WP-09 | `wp09-litter-atlas` | `wp09-litter-atlas` | yes | `ses_e9981f0deffeTOABbH5CMgj7Si` | partial WIP `2512d554` (unmerged) |
| WP-10 | `wp10-aggregates` | `wp10-aggregates` | yes | `ses_e9981f0beffe3mxEO6j2wvEwlI` | **MERGED** `4ad9e409` |
| WP-11 | `wp11-lit-water` | `wp11-lit-water` | yes | `ses_e9981f0bdffeMwUSq1395dr37e` | **MERGED** `438b340d` (via `36f8eaaa`) |
| WP-16 | `wp16-moss` | `wp16-moss` | yes | `ses_e9981f0bdffdgV5S1NLaMqa8di` | **MERGED** `d0afb267` |
| WP-21 | `wp21-pilot-tree` | `wp21-pilot-tree` | yes | `ses_e9981f0bcffekhu9uQe6vmkbA8` | partial WIP `5ee6cde1` (unmerged) |

Merge procedure per packet (orchestrator only):

1. Verify the worktree has a commit and `git status` is clean.
2. Review `git diff 981b14b3..<branch> --stat` — confirm every changed file is inside the packet's declared file list. Reject out-of-scope edits.
3. Build both assemblies **in the worktree** before merging.
4. `git merge --no-ff <branch>` into `main` in `E:\Vivarium`, then rebuild both assemblies in `main`.
5. On conflict: do **not** hand-resolve. Re-dispatch the packet against the new `main` in a fresh worktree.
6. Update the state table in section 2 and this table.

---

## 6. Verification log

| Wave | Build gate | Capture gate | Result |
| :--- | :--- | :--- | :--- |
| Checkpoint | pass (0 errors ×2) | not run | baseline established |
