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

Historical wave workers ran in their own git worktrees branched from `981b14b3`, so packet work is isolated, individually revertable, and never collides on `obj/` or `bin/`.

---

## 2. Current implementation and verification state (2026-10-24)

Supersedes the former foundation-only table. User authorized completion before visual review/tweaks. This campaign edits the shared main checkout under explicit file ownership; the historical wave/worktree records below are retained. Implementation presence, native verification, and subjective photographic acceptance are separate. Current-source detailed audit: `2026-10-23-rendering-plan-state-review.md`; continuation: `.clanker/tasks/rendering-completion-20261023/`.

| Packet | Implementation present | Verification / limits |
| --- | --- | --- |
| WP-01 | Fidelity settings and quality>=1 overrides | final release-world01 passes113PNG, stable source/zero errors |
| WP-02 | HDR procedural sky plus three authored4K EXRs; probe512 configured at project level | final release-world01 passes113PNG, stable source/zero errors |
| WP-03 | Above-water/underwater fog and shadowed night lights | final release-world01 passes113PNG, stable source/zero errors |
| WP-04 | Focus-driven DOF, bloom, exposure | final release-world01 passes113PNG, stable source/zero errors |
| WP-05 | Eight original ambientCG4K five-channel sets, source hashes and import caches verified | native aquatic02 loads; final release-world01 passes113PNG, stable source/zero errors |
| WP-06 | Dense curved terrain, masks, proper camera-local POM frame and wet lobes | native aquatic02 passes |
| WP-07 | Displaced layered strata and rootlets | final release-world01 passes113PNG, stable source/zero errors |
| WP-08 | Whole-domain floor, loading-frame batches, shadows and fleshy fruit material response | final release-world01 passes113PNG, stable source/zero errors |
| WP-09 | 4K isolated litter/stacking/curved4K bark twigs; real moisture in vertex alpha | final release-world01 passes113PNG, stable source/zero errors |
| WP-10 | Undercut peds, normals/AO, groundcover UV and bankfringe | final release-world01 passes113PNG, stable source/zero errors |
| WP-11 | Forward+ lit water, smooth fluid normals, true optical paths | native water D passes:9 PNG/80 motion JPG,8 active impacts,stable/no errors |
| WP-12 | Shadowed chromatic caustics, underwater optics, initial/live prop field and water bindings | native water D passes:9 PNG/80 motion JPG,8 active impacts,stable/no errors |
| WP-13 | Impact history, shared ripple/foam include, meniscus, sub-mm rising bubbles, coherent fluid clocks | native water D passes:9 PNG/80 motion JPG,8 active impacts,stable/no errors |
| WP-14 | Highpoly rocks tied to exact canonical support;32 variants/650 per m2 gravel | 10 focused checks pass; highpoly rock faces remain coplanar to preserve exact support |
| WP-15 | 72-side displaced logs, real48-side tunnels, fractured rims, shards, normals and POM/endgrain | 9 focused checks pass; final release-world01 passes113PNG, stable source/zero errors |
| WP-16 | Dense3D moss shoots, draping, shadows and sheen | final release-world01 passes113PNG, stable source/zero errors |
| WP-17 | 3D foliose/fruticose/crustose lichens | final release-world01 passes113PNG, stable source/zero errors |
| WP-18 | All3 aquatic layers and explicit slime optics | native aquatic02:12 captures, all3 uploaded layers, stable source/world |
| WP-19 | Closed high-detail botanical shells; revised measured budgets and retention checks | integrated focused geometry passes |
| WP-20 | All vascular synthesized profiles,4 rebuilt2K family pools,4-order veins, wind, corrected light energy | FLORA_BAKE_OK all4 shared pools; final release-world01 passes113PNG, stable source/zero errors |
| WP-21 | Dedicated pilot leaf material, closed leaves, layered bark,3D gills/pores/coral | 16 focused pilot/fungi checks pass |
| WP-22 | Fauna shadows, anatomy refinements,14folded isopod legs, CUSTOM1/2 knee/ankle articulation | 19 focused fauna checks pass; 5 sharp live families pass,240 frames each |
| WP-23 | Thinfilm chitin, mucus/SSS, aquatic stripes, compound/refracted eyes | finite Airy domain check; 5 sharp live families pass,240 frames each |
| WP-24 | HairR/TT/TRT approximation, refracted iris, local wetness/contact uniforms; existing analytical IK preserved | guardian02 passes6 archetypes,180 images with sharp material details; no obsolete CPU reupload removal claimed |

Fresh integrated focused geometry:113 passed,0 failed. One unrelated `TerrainStaysSoilEvenAtMaximumRockExposure` failure was reproduced in an archived clean e7d65714 baseline (281/1044 cells); it was excluded explicitly rather than changing ecology. Native23-spatial-material Forward+ probe passed after repairing custom-light SPECULAR stage access. Initial full-world113, dynamic water9+80motion,5 sharp fauna families240each, and guardian6archetypes180images pass native gates with stable sources and no engine errors. Final release-world01 passes113PNG. Mature reference02 passes81scenes at6.082days,380flora/86fauna,unchanged digest/stable source/zeroerrors. It fails30FPS: mean20.0, minimum1s14.0, p99 72.29ms onGTX1080 at1600x900; performance acceptance remains open. Night01 passes35PNG atlight1020,unchanged digest/stable source/zeroerrors. Implementation/native integration handoff complete; overall photographic/performance acceptance remains partial. Gallery:build/photorealism/review-2026-10-24/index.html (11 successful runs,1630PNG). Sparse dark flora flecks remain visually unaccepted: probes find finite/nonnegative/nonzero ALBEDO, changing ALBEDO alone removes them, and the precise dark-pigment/lighting/subpixel cause is not established. The clean recorded e7 baseline emits1007 shader/engine error lines under Godot4.7.1 and is excluded from historical A/B acceptance. Detailed report:2026-10-24-rendering-completion.md. Initial diagnostic world01 changed sources during capture and remains excluded.

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
