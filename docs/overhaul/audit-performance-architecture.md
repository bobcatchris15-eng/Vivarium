# Audit a4 - performance and architecture (static, read-only)

Date 2026-11-10, HEAD a193e6d. Scope: game/App, game/Render, game/Shaders, game/Camera, sim<->render boundary (src/Vivarium.Sim/Time, Water, Coverage, Tools).
Method: source reading only. No Godot/dotnet run, nothing measured. "Impact" is reasoned from code structure; any arithmetic shown is derived from constants in source and is labelled as such. Every claim needs the stated before/after measurement before it is acted on.

Premise that changes how to measure: the benchmark path in CLAUDE.md (`--perf-test`, `--reference`, scripts/verify.ps1, compare-reference.ps1) is not in the tree. `--perf-test` appears only as a flag check (GameSession.cs:94, Bridge.cs:494) with no runner; scripts/ and tests/ content were removed in 9650e95 (ledger D2). The only live measurement tools are `LiveDiagnostics` (`--diagnostics <dir>`, 1 Hz JSONL) and `FrameProfiler` scopes. Several FrameProfiler APIs have no caller left (TakeIntervalTotals, TakeFrameScopeMetrics, GetCpuTimingSummary, MeanDurations, PerFrameDurations, TakeReport, TakeIntervalReport).

## 0. Ranking index

| ID | Title | Group | Impact | Size |
|----|-------|-------|--------|------|
| P1 | Environment/sky/fog rewritten every frame | safe | medium | S |
| P2 | Caustics evaluated inside per-light `light()` | safe | medium | S |
| P3 | Sculpt rebuilds: pond mesh unthrottled, terrain mesh on main thread | safe | high (during sculpt) | M |
| P4 | Ground-cover buffer thrash and shadow policy | safe | medium | S |
| P5 | Flora refresh allocation/LINQ churn | safe | medium | S-M |
| P6 | Guardian contact scan walks every flora MMI | safe | medium | S-M |
| P7 | Per-frame picking and ToolController churn | safe | medium | S |
| P8 | Per-frame env-var reads, profiler lock, un-disposed frustum arrays | safe | low-medium | S |
| P9 | Coverage tile sync scans all tiles, queries terrain per tile | safe | low-medium | M |
| P10 | Load hitches: GPU readback in Compile, main-thread LOD generation | safe | medium (load only) | M |
| P11 | Unused 4K sets, EXRs, masters shipped | safe (export) / decision (EXR) | disk/pack, not VRAM | S |
| P12 | Post/GI/shadow stack at Quality 1 | decision | high | S each |
| P13 | Ground-cover density, triangle budget, regen policy | decision | high | M-L |
| P14 | Terrain fragment shader | decision | high | M |
| P15 | Water surface shader (per-pixel kernel, screen copy) | decision | medium-high | M |
| P16 | Flora variant MMI explosion, material duplicates | decision | medium-high | M |
| P17 | Loam patches: unique meshes, 4K litter textures | decision | medium-high | L |
| P18 | Sim wall budget and thread-pool contention | decision | medium | M |
| P19 | Smaller: fauna vertex shader + shadows, water publish allocs, night lights | decision | low-medium | S-M |
| R1 | Snapshot/dirty-region boundary half-built; unsynchronised coverage reads | decision | correctness+perf | M-L |
| R2 | Four overlapping camera-local scatter systems | decision | perf+continuity | L |
| R3 | Guardian mutates flora MMI buffers | decision | continuity | L |
| R4 | Per-species leaf texture pools do not scale | decision | VRAM, blocks overhaul | L |
| R5 | Switch/flag sprawl | mixed | maintainability | M |
| R6 | Dead code and stubs | safe | maintainability | S |
| R7 | Field->shader transport is 2 s stepped | decision | continuity | S-M |
| R8 | Mesh generation cache is in-memory only; triple CPU copies | decision | startup, RAM | M-L |
| R9 | Diagnostics fragmented; harness absent | safe/decision | measurement | M |
| R10 | `Bridge` god class; builders in App/ | safe | structure | M |
| R11 | Clock domains | decision | continuity | S |

---
## Part A - Performance

### A.0 Structural facts used below (evidence)
- Frame order: GameSession `_Process` (parent, runs first) calls `Host.Advance(elapsed)` (GameSession.cs:292) which runs the scheduler on the main thread (Scheduler.cs:154-185) then `World.Liquid.Advance` (SimHost.cs:29). Renderers run after in tree order (GameSession.cs:62-79). Sim tick = 10 sim-seconds (Clock.cs:12); per-frame cap `WallBudgetMs = 6` (GameSession.cs:170), default 12 (Scheduler.cs:51), `MaxTicksPerAdvance` 400 (Scheduler.cs:49).
- Per-frame `_Process` work: Environment (every frame), Camera, Island, WaterRenderer (rebuild check + 2 shader params), WaterSurface (snapshot version check, per-tile uniform writes cached), Props, PilotTree, SoilDetail, AmbientGround, Coverage (every frame while any pending/in-flight, else 4 Hz), Flora (build budget 2 ms/50 ms; refresh every 0.2 s moving / 4 s still), Fauna (every frame, all animals), Guardian (+ skeleton modifiers, vegetation contact every frame), Overlay, Tools (raycast every frame), Session.
- Threads: sim systems on main; Coverage step on thread pool with `Parallel.For` inside (CoverageSystem.cs:324, MatRules.cs:222/354/495, LichenRules.cs:117/197); liquid job on pool (SurfaceLiquid.cs:154, SurfaceFluidDynamics.cs:203); autosave job (Autosave.cs:54); flora mesh Tasks up to 24 in flight (OrganismRenderers.cs:293, 396); coverage mesh Tasks 4 (CoverageRenderer.cs:129, 565). All share one thread pool with the Godot render thread's CPU cores.
- Draw structure (static): flora = per species up to MorphCount(5 or 12, FloraGeometryPlacement.cs:11) x FloraLodTiers(3, OrganismMeshes.VisualBreakFlora.cs:117) variant MMIs, each with Full + optional Fruit/Juvenile/Tendril MMI (OrganismRenderers.cs:426-469); 125 flora JSON in content/flora; fauna 17 species x 4 morphs x (high [+curled]) MMIs (OrganismRenderers.cs:1797-1814); loam ~3-4 unique MeshInstance3D per 2 m patch; one MeshInstance3D per coverage tile per layer (CoverageRenderer.cs:728); one per rock/log (PropRenderer.cs); gravel and ground cover as MMIs; water = one MeshInstance3D + unique ShaderMaterial per 16-cell tile (WaterSurfaceRenderer.cs:152-170).
- Shadow casting: flora tier<=ShadowMaxTier(0), height>=0.055, non-colony (OrganismRenderers.cs:423-424); fauna all On (:1823); loam leaf/twig/peds On, duff Off (LoamSurfaceDetailRenderer.cs ConfigureVisualsRecursive); SoilDetail fruit/blades/stalks On (SoilDetailRenderer.cs:90-93); coverage, ambient ground, water Off.
- Transparency/overdraw: water (blend, screen+depth read, discard), loam_litter `depth_prepass_alpha` + discard (loam_litter.gdshader:2), leaf.gdshader/pilot_tree discard, VisibilityRangeFadeMode.Self (dither) on coverage, ambient ground, loam, props.

### A.1 Safe mechanical wins

**P1 - Environment/sky/fog rewritten every frame** (safe, S)
- Evidence: EnvironmentRig.cs:469-494 `_Process` calls `ApplyDayNight()` every frame (401-455: sun rotation, ~20 Environment/light property writes, 8 `_sky.SetShaderParameter` incl. Vector3 marshalling at 439-446) then re-sets 12+ constants that never change (483-492: VolumetricFogLength/DetailSpread/AmbientInject/SkyAffect/TemporalReprojection, FogAerialPerspective). Sky uses `ProcessMode.Incremental` (:149) so changed sky params keep the radiance map dirty every frame. Day cycle is 1200 s (NightFloraMaterials.cs NightFactorAtSeconds), sun sweeps 0.3 deg/s.
- Impact: medium. Sky cubemap/radiance work and ~35 native setter calls per frame for a change that is sub-perceptual per frame. Cannot state GPU ms without measurement.
- Remedy: in `_Process` call `ApplyDayNight()` only when `Clock.Seconds` moved >= 0.2 s since last apply (5 Hz). Move constants at 483-492 and 481 into `_Ready`. Gate the underwater block (478-486, 493) on `_underwater` changing (cache last value). Cache `VIVARIUM_FORCE_NIGHT` (see P8).
- Visual risk: low (water glint reads `CanopyLightDirection`, steps of ~0.06 deg at 5 Hz; keep lerp of keys unchanged).
- Measure: FrameProfiler "Environment" scope (already exists); GPU time of sky pass needs ViewportSetMeasureRenderTime (see R9). A/B: 60 s fixed camera, compare process_ms and Environment mean.
- Files: game/Render/EnvironmentRig.cs.

**P2 - Caustics evaluated inside per-light `light()`** (safe, S)
- Evidence: terrain.gdshader:418-453, log.gdshader:222-253, rock.gdshader:203-242 call `smooth_field(sub_tex)` then `evaluate_submerged_caustics` from `light()`, which Godot runs once per light per pixel (sun, fill, every overlapping glow OmniLight; up to 28 energised, MaxActiveLuminousLights, OrganismRenderers.cs:145). `evaluate_submerged_caustics` (water_caustics.gdshaderinc:62-89) costs 3 caustic layers, each 2 Voronoi (9-cell loops) + a 4-sample Jacobian, only after the depth<=0 early-out, but `smooth_field` and `refract` run for every pixel and light.
- Impact: medium on submerged beds, medium-high at night near water (cost scales with lights x submerged pixels). Also physically wrong: each glow light adds its own caustic pattern.
- Remedy: in each `light()`: `if (!LIGHT_IS_DIRECTIONAL) return;` before the caustic block; move the `sub_tex` fetch/`water_surface_y` behind `if (world_pos.y < water_table + 2.6)` (max surface offset is (255-1)/100 m from `sub.b`). Better: compute caustic once in `fragment()` into a varying/local and apply only for the sun using `ATTENUATION`... keep it in `light()` for shadow masking but skip non-sun lights.
- Visual risk: low-medium (removes caustics from fill/omni lights; check night pond edge).
- Measure: GPU frame time at a fixed submerged-bed camera, day and forced night (`VIVARIUM_FORCE_NIGHT=1`), before/after. Needs the GPU timer from R9.
- Files: game/Shaders/terrain.gdshader, log.gdshader, rock.gdshader (water_caustics.gdshaderinc unchanged).

**P3 - Sculpt-time rebuilds** (safe, M)
- Evidence: IslandRenderer.cs:240-245 rebuilds the whole top mesh + walls synchronously on the main thread up to ~12/s (`BuildMeshes`, :213-221; comment at :233 cites a 157k-vertex mesh). `TerrainMesh.BuildTop` (src/Vivarium.Sim/Geometry/TerrainMesh.cs:19-56) is a full-heightfield pass with per-triangle `new[]` arrays, a `Dictionary` vertex-share map and Catmull-Rom `RenderHeight` per vertex; then `Bridge.ToArrayMesh` repacks into 5 managed arrays + tangents (Bridge.cs:53-66, 111-112) per rebuild. WaterRenderer.cs:26-29 calls `Rebuild()` on every `Terrain.Version` change with no throttle (Props has 0.25 s, PropRenderer.cs:94). AmbientGround/SoilDetail/Loam are invalidated per dab (GameSession.cs:163-168).
- Impact: high while sculpting (main thread blocks, LOH allocations, GC gen2 risk the code comment already hints at at :215-217); nil when idle.
- Remedy: (a) throttle WaterRenderer to the same 0.25 s as Props. (b) Run `TerrainMesh.BuildTop/BuildWalls` + `Bridge.PrepareMeshUpload` in `Task.Run` using the existing Coverage pattern (CoverageRenderer.cs:565-569), commit result on main; keep the previous mesh until ready. Requires a copy of the heightfield rows touched or a version check (see R1 race note). (c) Later (decision, L): 16x16-cell terrain chunks keyed by dirty region.
- Visual risk: low for (a)(b) (sculpt follow latency +1-2 frames); medium for (c).
- Measure: FrameProfiler "Island.MeshBuild", "Water.Pond" max and total during a scripted brush stroke; GC gen2 count in LiveDiagnostics (gc2). Existing scopes suffice.
- Files: game/Render/IslandRenderer.cs, WaterRenderer.cs; src/Vivarium.Sim/Geometry/TerrainMesh.cs (allocation cleanup only).

**P4 - Ground-cover buffer thrash and shadow policy** (safe, S)
- Evidence: AmbientGroundCoverRenderer.Fill (:544-572) sets `mm.InstanceCount = n` every refresh (n differs each time), which reallocates the native MultiMesh, and builds `List<Transform3D>`/`List<Color>` then copies to a managed `float[]` then `mm.Buffer = buf` (full copy). Flora already uses capacity-with-VisibleInstanceCount (OrganismRenderers.cs:1532-1546). OverlayRenderer.Fill (:136-141) uses per-instance `SetInstanceTransform/Color` calls (debug only, 2 Hz). SoilDetailRenderer.cs:90-93 sets `CastShadow = On` for fruit/blade/stalk MMIs, contradicting AmbientGroundCoverRenderer.cs:56-60 which documents turning it off because tiny blades in the 8192 shadow map "added millions of tiny triangles for no readable shadow".
- Impact: medium (a native buffer reallocation each 2 m of camera travel plus ~MBs of copy; shadow pass triangles for SoilDetail blades inside 6 m).
- Remedy: grow-only capacity (`needed*1.5+8`) with `VisibleInstanceCount = n`; write directly into the pooled `float[]` (skip the Lists); apply the same capacity change to SoilDetail `_buffers`. Set SoilDetail blades/stalks `CastShadow = Off` (keep fruit On if desired).
- Visual risk: low (shadows from 1.5-3 cm blades; verify close-up contact shadows with the existing basal AO in ground_cover.gdshader).
- Measure: `RenderTotalPrimitivesInFrame` (LiveDiagnostics `primitives`) with SoilDetail blades shadow on/off at a fixed camera; AmbientGround/SoilDetail scope totals.
- Files: game/Render/AmbientGroundCoverRenderer.cs, SoilDetailRenderer.cs.

**P5 - Flora refresh allocation churn** (safe, S-M)
- Evidence (all per refresh, i.e. every 0.2 s while the camera moves): RenderSnapshot.Capture copies every plant (RenderSnapshot.cs:104-172) unless tick unchanged; `RequestMissingLayers` does `Select/Concat/Where/ToHashSet` over all plants every 1 s (OrganismRenderers.cs:269-274, called :557-563); `PopulationReady` uses `.All` over Flora+DeadFlora (:1153-1155); `_visualTiers.Keys.Where(...).ToArray()` x2 (:1156-1157); `new HashSet<string>` per glass_antlers plant (:963); `bool[] hasChild` per climber (:890); `UpdateLuminousLights` re-calls `Camera.GetFrustum()` and allocates `Plane[]`, `List` (ranked), 2 `Dictionary`, 2 arrays every call (:1257-1258, 1348, 1365-1373); each bucket stores three `List`s then copies into the pooled `float[]` (Fill :1666-1689).
- Impact: medium. Steady GC pressure from a 0.2 s cadence plus O(plants) hash work; scales with population (population size is not logged, see R9).
- Remedy: replace LINQ with loops and reused HashSets; compute PopulationReady/prune only when `Flora.Version`/`DeadFlora.Version` changed; static readonly `HashSet` for the log-only anchor call; pass the frustum array already built at :760-762 into `UpdateLuminousLights`; reuse `_neighbourhood`, `_cellReach`, position/range arrays as fields; pack instances straight into `PooledMultiMeshBuffer.Buffer` (grow on demand) instead of three Lists.
- Visual risk: none intended (identical arithmetic).
- Measure: GC counts and `managed_memory_bytes` (LiveDiagnostics), FrameProfiler "Flora", "Flora.Fill", "Flora.Lights"; add allocated-bytes delta per refresh (R9).
- Files: game/Render/OrganismRenderers.cs (FloraRenderer), RenderSnapshot.cs (optional).

**P6 - Guardian contact scan walks every flora MMI** (safe, S-M)
- Evidence: GuardianVegetationContact.cs:245-330 each frame advances `_scan = Flora.GuardianContactInstancesNear(Focus,4f)` 24 steps; the iterator (:710-745) builds a `List<MultiMeshInstance3D>` of every Full/Juvenile/Fruit variant node of every species, then for each calls `IsInstanceValid`, `draw.GlobalTransform` (native) and dictionary lookups before the 2 m bins. With 125 species x 15-36 variants x up to 3 organs this is thousands of draws per scan restart. `new List<(ulong,int)>()` per frame (:303). Registry eviction scan `FindFarthest` (:119-147).
- Impact: medium (per-frame CPU proportional to total MMI count, not nearby plants).
- Remedy: FloraRenderer maintains a list of draws with `VisibleCount > 0` (update in `Fill`/`Upload`) and passes that; cache `draw.GlobalTransform` per draw (static parents); reuse `expired` list.
- Visual risk: none.
- Measure: scopes "Guardian.Vegetation.Scan"/".Apply" exist.
- Files: game/Render/GuardianVegetationContact.cs, OrganismRenderers.cs.

**P7 - Per-frame picking and ToolController churn** (safe, S)
- Evidence: ToolController.cs:108 `_pendingPlantGhosts.Keys.ToArray()` every frame; :112-117 `Selection.Raycast` every frame, even with static mouse/camera. Selection.Raycast (src/Vivarium.Sim/Tools/Selection.cs:28-99) allocates a `List<WorldHit>` and linearly scans all rocks, logs, every flora (dictionary lookup `FloraOrThrow` + `Radius(sp)` sqrt each), every fauna. `SnapToCritter`, `UpdatePreview` follow.
- Impact: medium, scales with flora count.
- Remedy: skip the raycast when (mouse position, camera transform, `Clock.Tick`, tool/held state) are unchanged since the last frame; store `sp` radius per plant (or a coarse grid of flora centres) in the scan; guard `ToArray()` with `_pendingPlantGhosts.Count > 0`.
- Visual risk: none (hover feedback frame-exact when inputs unchanged).
- Measure: FrameProfiler "Tools" scope.
- Files: game/App/ToolController.cs; src/Vivarium.Sim/Tools/Selection.cs (optional).

**P8 - Per-frame env-var reads, profiler cost, un-disposed arrays** (safe, S)
- Evidence: CameraRig.cs:283 reads `VIVARIUM_DISABLE_DOF` every frame (inside UpdateDistanceBlur); EnvironmentRig.cs:465 reads `VIVARIUM_FORCE_NIGHT` every frame; CoverageRenderer.MossGrowth.cs:21-27 `MossMicroGrowthStrength` getter reads + `int.TryParse` on each use (per tile build, BuildTileMesh ~:854); WaterSurfaceRenderer.cs:171 per tile; Bridge.Shader reads 4-5 env vars per shader created (Bridge.cs:329-356). FrameProfiler.RecordDuration takes a global `lock` and does 9 dictionary updates with string keys per scope (FrameProfiler.cs:57-71) with ~60 scopes/frame; `"Sys." + sys.Name` allocates per system run (GameSession.cs:222). `Camera.GetFrustum()` returns a native Godot array that is never disposed in Fauna (OrganismRenderers.cs:1851), Guardian (GuardianRenderer.cs:612), Coverage (CoverageRenderer.cs:500), Flora (:760, :1257).
- Impact: low-medium individually (allocation + finalizer pressure each frame), cheap to remove.
- Remedy: `static readonly` cached env flags in one `RenderSwitches` class (see R5); per-scope struct slots (array indexed by interned id, no lock; single main-thread writer, `Interlocked`/thread-local for workers) or sample only when LiveDiagnostics is recording; cache `"Sys."+name` once per SimSystem; `using var planes = Camera.GetFrustum();` (or reuse one array).
- Visual risk: none.
- Measure: gc0 count per minute and managed alloc rate (R9).
- Files: game/Camera/CameraRig.cs, game/Render/EnvironmentRig.cs, CoverageRenderer.MossGrowth.cs, WaterSurfaceRenderer.cs, game/App/Bridge.cs, FrameProfiler.cs, GameSession.cs, OrganismRenderers.cs, GuardianRenderer.cs.

**P9 - Coverage tile sync** (safe, M)
- Evidence: CoverageRenderer.cs:247-252 runs `SyncTiles` every frame while `_inFlight`/`_pendingRebuilds` is non-empty (typical under moss growth), else 4 Hz. SyncTiles (:465-593) loops all tiles for removal (:475-481), `EnqueueTiles` Mat+Crust (:610-663) and `HasDirtyOutOfViewTiles` x2 (:666-676); `IsTileInFrustum` calls `_w.Terrain.Height()` per tile per call (:684); `GetFrustum` array (:500); closure `RemoveAll` (:592). `MaxConcurrentBuilds = 4`, commit budget 0.5 ms (:33, :586).
- Impact: low-medium (CPU, scales with tile count; also thread-pool pressure from 4 concurrent builds).
- Remedy: have the sim coverage layer expose a dirty-tile list (tile.Version bump already exists) consumed here instead of scanning; cache tile centre Y per terrain version; compute frustum once per sync.
- Visual risk: none.
- Measure: FrameProfiler "Coverage"; `PendingRebuildCount`, `TileCount`, `TrianglesBuilt` exist but are not logged (R9).
- Files: game/Render/CoverageRenderer.cs; src/Vivarium.Sim/Coverage/CoverageLayer*.cs (dirty list).

**P10 - Load hitches** (safe, M)
- Evidence: FloraVisualProfile.Compile (FloraVisualProfile.cs:~395-420) builds the leaf surface with `Bridge.ToArrayMesh`, then `blade.SurfaceGetArrays(0)` to re-add it as surface 2: for RenderingDevice meshes that is normally a synchronous GPU readback per variant (confirm against FLORA_PUBLISH_SLOW logs, OrganismRenderers.cs:429-431). Fauna builds 4 morphs x (high+curled) per species with `ImporterMesh.GenerateLods` on the main thread (Bridge.cs:122-129, called OrganismRenderers.cs:1803-1809) during Stage("Fauna") (GameSession.cs:208-209). Loam patches build on the main thread at load (GameSession.cs:194-198).
- Impact: medium for startup/new-species hitch; none steady-state.
- Remedy: add `Bridge.AddSurface(ArrayMesh, MeshData, Material)` and build the stem+leaf mesh from `PreparedSplit` arrays without readback; run `ImporterMesh.GenerateLods` inside the Task.Run that already prepares fauna meshes (or bake LODs into cached meshes, R8).
- Visual risk: none (same arrays).
- Measure: WORLD_RENDER_STAGE_* log lines and `StageTimings` (GameSession.cs:125, 149-157); FLORA_PUBLISH_SLOW.
- Files: game/App/Bridge.cs, game/Render/FloraVisualProfile.cs, OrganismRenderers.cs (FaunaRenderer.Build).

**P11 - Assets shipped but never loaded** (safe for 4K PNG sets; decision for EXR)
- Evidence: `Bridge.FindTexPath` prefers the 1K variant for every `BindSurface` call (Bridge.cs:413-436), so the ten `*_4K-PNG_*` sets are never loaded except Bark014 4K (3 maps) and ForestFloorLitter 4K (5 maps) hard-coded in LoamLitterBuilder.cs:27-53. export_presets.cfg:8-10 uses `export_filter="all_resources"` and excludes only build/, --out/, game/*, visual-lab-candidates, so imported 4K sets ship. Three `vivarium_canopy_*_4k.exr` (89 MB source each; 97 MB imported ctex each) are referenced only by `canopy_sky.gdshader` samplers `canopy_day/dusk/night` (:6-8), and no C# binds them (grep: no `SetShaderParameter("canopy_*")`, no `enclosure_daylight`). Textures/LeafPools/Masters (4 species x 4 PNG ~3 MB) are bake inputs only.
- Impact: package size/disk/import time, not VRAM (unloaded textures cost none). Unbound sampler2D samples as white, so the sky currently evaluates against a constant; deleting vs binding the EXRs changes lighting.
- Remedy: add `Textures/*_4K-PNG_*` (except `Bark014_4K*` and `ForestFloorLitter_4K*` unless P17 changes them) and `Textures/LeafPools/Masters/*` to exclude_filter; decide on the EXRs (D1: bind and re-grade vs delete). Keep sources outside res:// if not needed.
- Visual risk: none for 4K sets/Masters; EXR decision changes ambient/reflection light.
- Measure: exported pck size; boot time.
- Files: game/export_presets.cfg; game/Textures/Environment/*, game/Shaders/canopy_sky.gdshader (if EXR decision).

Largest files (bytes): Moss002_4K NormalGL 99.3 M, Ground048_4K NormalGL 96.7 M, Ground037_4K NormalGL 96.5 M, Bark013_4K NormalGL 94.0 M, Gravel022_4K NormalGL 93.2 M, Rock058_4K NormalGL 89.6 M, three canopy EXR 89.0-89.4 M, Bark014_4K NormalGL 86.9 M, ScatteredLeaves007_4K NormalGL 60.6 M, crested_canopy guardian.glb 57.6 M, armored_forest 45.7 M, hushbill 30.9 M; 12 LeafPools .res at 22.4 M each (4 species x color/normal/physical). Totals: Textures 2.0 GB, assets 557 MB, `.godot/imported` 2.0 GB. Both 1K and 4K variants ship for 10 PBR sets (Bark001, Bark013, Bark014, Gravel022, Ground037, Ground048, Moss002, Rock058, ScatteredLeaves007 + ForestFloorLitter 4K only). Runtime VRAM (computed): a 4096^2 BC7 + mips = 21.3 MiB, so the 8 hard-coded 4K loam textures = about 171 MiB; each LeafPool array (2048^2 x 4 layers) = about 21.3 MiB, 3 per species = 64 MiB + vein maps. Projected shadow atlases: positional 8192 at project.godot:50 = 128 MiB if 16-bit (default).

### A.2 Needs a design decision

**P12 - Post/GI/shadow stack at Quality 1** (decision, S each, high combined)
- Evidence (single tier, GameSession.cs:94 forces Quality 1): SSAO Medium at **full resolution** (`EnvironmentSetSsaoQuality(..., false, ...)` EnvironmentRig.cs:311; project.godot:42 half_size=false); TAA (:321); SDFGI 3 cascades at 5 cm minimum cell, 25.6 m reach (:179-189, 279-287); **no MMI sets `GIMode`** (grep: none), so all flora/ground-cover MMIs default to Static GI and are candidates to dirty SDFGI when their buffers change every 0.2 s (verify in engine behaviour); volumetric fog 128x128x128 froxels (project.godot:45-46) with global density 0 and one 1.85 m tall FogVolume density 0.0035 (EnvironmentRig.cs:359-377); glow, auto-exposure, far DoF (CameraRig.cs:263-306), 2-split 4096 directional shadow with SoftMedium filter (:312-322), positional shadow atlas 8192 (project.godot:50), up to 28 omni lights with 6 DualParaboloid shadows at night; RenderScale 0.95 bilinear (UserSettings.RenderScale Bridge.cs:516, GameSession.cs:103) which saves ~10% pixels but softens without upscaling. project.godot:51-52 set SDFGI ray count/convergence under `environment/sdfgi/*`; in Godot 4 these live under `rendering/global_illumination/sdfgi/*`, so they may be silently ignored (verify in editor).
- Impact: high combined; each is a full-screen or volume pass on an iGPU. Not quantified here.
- Remedy (experiments, one at a time): (1) volumetric fog 128^3 -> 64^3 (or off at Quality 1; the fog is ~5% opacity across the habitat); (2) SSAO half_size true; (3) `GIMode = Dynamic` on flora/ground-cover/fauna MMIs (they already receive GI; Static only feeds SDFGI geometry); (4) positional atlas 8192 -> 4096; (5) RenderScale 1.0 or an upscaler; (6) fix/remove the sdfgi keys.
- Visual risk: medium for 1,2,5 (contact AO and light shafts); low for 3,4. Use paired captures per CLAUDE.md "Reference" flow.
- Measure: GPU frame time per toggle at 3 fixed cameras (needs the R9 GPU timer), plus `video_memory_bytes`.
- Files: game/Render/EnvironmentRig.cs, game/App/GameSession.cs, game/project.godot, game/App/Bridge.cs (RenderScale default), MMI creators (OrganismRenderers.cs MakeMmi, AmbientGround, SoilDetail).

**P13 - Ground-cover density, triangle budget, regeneration policy** (decision, M-L, high)
- Evidence: AmbientGroundCoverRenderer.cs:99-130, 219-346: CullRadius 8 m (:25-26), 36 clusters per 25 cm cell (:261-262 `count`), disc of ~3.2k cells => ~115k candidate points per refresh, each running Domain.Contains, SubstrateAt, OpenWaterDepth, two noise gradients, rock/log bounds, `GravelCoverWeight` (Math.Pow, 2 noise evals per patch) and flora exclusion. Each cluster is 5 blades (BuildBlade simplified: 3 rows x 2 lanes x 2 tris x 5 = 60 tris, :418-430) plus broadleaf instances. Arithmetic only: ~100k clusters x 60 = ~6 M triangles per frame in the colour pass if the disc is fully covered (distFade thins the outer ring). It rebuilds whenever the camera moves >2 m (:117-119), on terrain version (:111-117), and at 0.15 s after edits (:126-137). SoilDetail runs a second similar camera-local scatter at 6 m (SoilDetailRenderer.cs:102-170) with stride 2 cells.
- Impact: high (triangle/overdraw load on iGPU, plus main-thread slices 2 ms/frame for many frames after each 2 m move, producing visible pop-in while flying).
- Remedy options: (a) cache generated instances per 2 m block keyed by (seed, terrain stamp, flora-exclusion version) and only add/remove blocks on camera move; (b) lower density with distance (fewer clusters in the 5.5-8.5 m ring, not just smaller scale); (c) 2-3 tris per blade LOD for far ring; (d) merge with SoilDetail (R2).
- Visual risk: medium (turf density is the primary "alive ground" cue); needs paired captures at 1 m, 3 m, 8 m.
- Measure: add `LastGroundcoverInstances` (exists, AmbientGroundCoverRenderer.cs:30) and `RefreshCount` to LiveDiagnostics; `primitives`; scope "AmbientGround" total.
- Files: game/Render/AmbientGroundCoverRenderer.cs (+BankFringe.cs), SoilDetailRenderer.cs, game/Shaders/ground_cover.gdshader.

**P14 - Terrain fragment shader** (decision, M, high)
- Evidence: terrain.gdshader:111-416 runs for the whole opaque island (453 lines, 41 uniforms, 23 texture calls plus helpers in surface.gdshaderinc): parallax occlusion march (24 steps scaled to 6 with distance, 4 refinements, 5 shadow taps; pom_raymarch surface.gdshaderinc:173-268) when `cam_dist < 5` and `tri_blend.y > 0.25`; `sample_untiled` (6 taps) for soil, again for damp, again x2 for slope triplanar (:215-233), leaf, moss, gravel triplanar with 3 normal-mapped cotangent frames (:287-301), silt block with `fbm` calls, 5 fbm/vnoise/terrain_noise calls for macro/aggregate/pore/ambient/gravel warp. All field lookups are `smooth_field` (extra `textureSize` + 1 tap each, 4-5 of them). Many layers are blended with early `if (w > 0.01)` branches, but occupancy/register pressure is set by the worst path.
- Impact: high at typical close-up camera heights (most of the screen is terrain within 5 m) and it is overdrawn by loam, ambient cover, coverage and litter meshes that each run their own heavy shaders.
- Remedy options: fade POM by 3 m instead of 5 m; cap `eff_steps` 24 -> 12; drop POM for `wGrav`/leaf/moss-dominant pixels earlier; precompute `soil+damp` blend in a bake texture (mip-friendly) for cam_dist > ~2 m; reduce `smooth_field` to bilinear + 1 quintic call for the two fields that need C1 (`field_tex`,`sub_tex`).
- Visual risk: medium-high (close-up soil is the hero surface).
- Measure: GPU time with the island material replaced by an unlit flat material (isolates terrain cost) at fixed near/far cameras.
- Files: game/Shaders/terrain.gdshader, surface.gdshaderinc.

**P15 - Water surface shader** (decision, M, medium-high)
- Evidence: creek_surface.gdshaderinc: `fluid()` (~:58-123: 3x3 kernel, each tap = 2 `texelFetch` state + `ground()` 4 fetches + occasional 4 more) is called in both `vertex()` (~:124-129, 96x96 grid = 9,409 verts per 16-cell tile, WaterSurfaceRenderer.cs:13 Subdivisions 6) and `fragment()` (~:132-), which then also calls `ground()` 5 more times; roughly on the order of 100+ texel fetches per water pixel (counted from source, not measured). Non-cheap path samples `hint_screen_texture` and `hint_depth_texture` (:12-15), forcing a back-buffer copy; `discard` x3 plus `depth_draw_opaque` with ALPHA. Cheap variant exists behind env `VIVARIUM_WATER_REFRACTION=substrate` (WaterSurfaceRenderer.cs:86, PrepareWaterMaterial :154-158).
- Impact: medium-high when water covers a large part of the view (streams/pond near camera).
- Remedy options: (a) have the vertex stage output head/depth/velocity/slope as varyings and do the per-pixel kernel only for pixels where depth < ~1.2 cm (shoreline) - flat pools use the interpolated value; (b) make the cheap substrate refraction the default and keep screen refraction as an explicit option; (c) share one back-buffer copy with other consumers (underwater.gdshader).
- Visual risk: medium (shoreline continuity is the stated reason for per-pixel evaluation).
- Measure: GPU time with water layer hidden vs cheap vs full, at a stream close-up.
- Files: game/Shaders/creek_surface.gdshaderinc, game/Render/WaterSurfaceRenderer.cs.

**P16 - Flora variant MMI explosion and material duplicates** (decision, M, medium-high)
- Evidence: BuildLayers/StartSpeciesBuild create `MorphCount x 3 tiers` variants per species, each with its own ArrayMesh, MultiMesh, MultiMeshInstance3D (OrganismRenderers.cs:375-469, 528-535); fruit gets `mat.Duplicate()` per variant (:459) and each duplicate registers in `NightFloraMaterials.Materials` with an O(n) `Any` scan (NightFloraMaterials.cs:~67); profile species compile two surfaces per variant (stem + leaf) = two draws (FloraVisualProfile.cs:395-420). All variants of every present species are built at load even though most morph/tier combinations may never be visible together; empty MMIs still occupy scene nodes, culling lists and GPU meshes. LiveDiagnostics logs `nodes` and `draws` but not MMI count or non-empty MMI count.
- Impact: medium-high for scene-graph cost, VRAM and load time; unquantified.
- Remedy: lazily create (or at least upload) a variant MMI the first time a refresh assigns an instance to it, building the mesh in the existing worker Task; one fruit material per species (parameters do not vary by variant); consider merging stem+leaf surfaces only if materials differ by a switch (shader branch) rather than surface. Optionally generate tiers 1-2 with `ImporterMesh.GenerateLods` instead of 3x separately generated meshes.
- Visual risk: medium (first-use build must not pop; keep a one-frame fallback to the nearest built tier/morph).
- Measure: log `ObjectNodeCount`, `ObjectResourceCount`, Σ non-empty MMIs, `video_memory_bytes`, and time to PopulationReady after world start.
- Files: game/Render/OrganismRenderers.cs, NightFloraMaterials.cs, FloraVisualProfile.cs.

**P17 - Loam patches: unique meshes, 4K litter textures** (decision, L, medium-high)
- Evidence: LoamSurfaceDetailRenderer.cs builds every 2 m patch of the island (:75-95) as 3-4 unique MeshInstance3D (CurledLeafLitter, FineTwigs, DecomposedLeafDuff via LoamLitterBuilder.cs:215-255; UndercutLoamPeds via LoamAggregateBuilder.cs:116-130), all with `CastShadow = On` at build then Off only for duff (ConfigureVisualsRecursive); targets 120 pieces/m2 nominal (LoamLitterBuilder.cs:18), caps 5000 leaves/6000 duff per board (:19-20), peds up to 180/64k tris (LoamAggregateBuilder.cs:16-18); `depth_prepass_alpha` + discard (loam_litter.gdshader:2). Built on the main thread with SurfaceTool and per-patch `GenerateTangents`, in 2 ms slices (RebuildPatchBatched :106-125); rebuilt on edits within brush radius after 0.15 s (:195-224); stamp function samples terrain and props (:227-264). Textures: ForestFloorLitter 4K x5 + Bark014 4K x3 hard-coded (LoamLitterBuilder.cs:27-53), about 171 MiB VRAM by arithmetic above, sampled on 2-8 mm cards.
- Impact: medium-high (hundreds of draws + equal shadow draws, alpha-tested overdraw, load time, VRAM).
- Remedy: instance a handful of shared leaf/twig/ped meshes with MultiMesh per patch (or per 4 m block) instead of unique geometry per patch; import ForestFloorLitter/Bark014 at 2K (`process/size_limit`) or use the existing 1K Bark014 set for twigs; evaluate dropping `CastShadow` for leaves beyond ~6 m (peds 9 m, leaves 13 m today).
- Visual risk: medium-high; litter fidelity is the "photoreal floor" cue. Paired close-up captures required.
- Measure: `draws`, `primitives`, `video_memory_bytes`, StageTimings "Forest floor"; compare with loam hidden (Quality 0 hides it, LoamSurfaceDetailRenderer.cs:61).
- Files: game/App/LoamLitterBuilder.cs, LoamAggregateBuilder.cs, game/Render/LoamSurfaceDetailRenderer.cs, game/Shaders/loam_litter.gdshader, loam_aggregate.gdshader, Textures/*.import.

**P18 - Sim wall budget and thread-pool contention** (decision, M, medium)
- Evidence: sim runs inside the main `_Process` with a fixed 6 ms wall budget (GameSession.cs:170), independent of frame time; systems yield between batches (Scheduler.cs:99-112). At a 33 ms frame (30 FPS target) that is ~18% of the frame, at 16.7 ms ~36%. Coverage and liquid use `Parallel.For`/Task.Run without a degree cap (MatRules.cs:222, SurfaceFluidDynamics.cs:203), competing with flora/coverage mesh Tasks and Godot's render thread; snapshot publish rebuilds a per-publish `Dictionary` of all liquid cells (SurfaceLiquid.cs:186-193) and clears/refills coarse water arrays (:196-201) on the main thread (CompletePending).
- Impact: medium; matters most at high sim speeds where backlog persists.
- Remedy: adapt `WallBudgetMs` to measured frame time (e.g. budget = clamp(target - last non-sim frame ms, 1, 6)); cap `MaxDegreeOfParallelism` to `ProcessorCount - 2`; reduce flora `MaxPendingVariantBuilds` after startup; pool liquid publish dictionaries.
- Visual risk: none for render; sim speed catch-up slows in worst frames.
- Measure: LiveDiagnostics `simulation_last_ms`, `backlog_sim_seconds`, `ticks_per_second`, p95/p99 frame time; add ThreadPool.PendingWorkItemCount.
- Files: game/App/GameSession.cs; src/Vivarium.Sim/Time/Scheduler.cs, Coverage/Rules/*.cs, Water/SurfaceFluidDynamics.cs, SurfaceLiquid.cs.

**P19 - Smaller items** (decision, low-medium)
- Fauna: per frame loop over all animals with `Camera.GetFrustum`, MMI `CastShadow = On` (OrganismRenderers.cs:1820-1825) while fauna.gdshader's vertex stage is 435 lines (:180-615) and runs in colour and shadow passes; `Upload` marshals the full capacity array whenever any transform changes (:2050-2062). Consider shadow-casting only fauna within the shadow range or reduced LOD in shadow pass (`LodBias` not set anywhere).
- Water tile fill: RGBAF32 18x18 textures and 200 KB ground textures per tile via `Image.SetData` + `ImageTexture.Update` (WaterSurfaceRenderer.cs:196-249); each tile has a unique ShaderMaterial. Fine at current tile counts; log `TilesUploaded`, `TextureUploadBytes`.
- Night lights: 28 OmniLights/6 shadows (OrganismRenderers.cs:145, 177); ranges 0.2-0.95 m (NightFloraMaterials.cs:14). Interacts with P2 (per-light terrain `light()` cost).
- Island textures: `UpdateFieldTexture` rewrites 5 textures every 2 s (IslandRenderer.cs:94, 477-482, 549, 587) and `UpdateCoverMask` sweeps all coverage tiles (:278-310). See R7.

---
## Part B - Architecture

### B.0 Actual boundaries today
- **Authoritative state** (src/Vivarium.Sim): `VivariumWorld`, systems registered on `Scheduler`, ticked by `SimHost.Advance` from the Godot main thread. Background threads exist inside the sim (coverage step, liquid job, autosave).
- **Client->sim** path: `SimHost.Tools` validated actions; picking uses authoritative geometry (Selection.cs), not render objects (good).
- **Sim->render**: a mixed pull model. Renderers hold `_w` and read live state directly. Only FloraRenderer builds a `RenderSnapshot`. Fauna/Guardian interpolate between two authoritative positions (`Track`, `_prevPos/_curPos`) - effectively a tiny snapshot per entity.
- **Procedural geometry**: pure managed generators in src/Vivarium.Sim/Geometry (`MeshData`, `OrganismMeshes`, `TerrainMesh`, `FloraGeometryPlacement`) are Godot-free and deterministic; `Bridge.ToArrayMesh/PrepareMeshUpload` converts to Godot arrays (managed -> `Vector3[]/Color[]` -> native). Renderer-local generators also exist in game/ (AmbientGround BuildBlade, SoilDetail BuildClod, Loam builders in game/App with SurfaceTool).
- **Asset caching**: in-memory only: `Bridge._maps` texture cache, `PreparedMeshes` ConditionalWeakTable, flora `_layers` kept per ContentLibrary, fauna `_layers` kept per content, `LoamLitterBuilder` static materials. No on-disk mesh/LOD cache. Leaf pools are baked offline (`--flora-bake`, LeafMaterialBaker).
- **Spatial queries**: sim-owned (`Terrain.Height`, `GroundHeight`, `Props.*Covers`, `Selection`, `AmbientCoverSpatialIndex` in game); renderers re-implement their own prop buckets/halo lists (R2).
- **Environmental fields**: sim `Fields` (grid 25 cm) -> IslandRenderer textures every 2 s -> shaders (R7); CPU copies sampled by flora placement every 32 ticks (`PlacementResampleTicks`).
- **Tools/UI/persistence/diagnostics**: ToolController (game/App) -> SimHost.Tools; UiRoot in a CanvasLayer; autosave via sim `AutosaveController`; diagnostics = FrameProfiler (static global) + LiveDiagnostics (opt-in) + per-class counters.

### B.1 Items

**R1 - Snapshot/dirty-region boundary is half-built; coverage reads are unsynchronised** (decision, M-L)
- Evidence: RenderSnapshot is used only by FloraRenderer (OrganismRenderers.cs:738-743, 1116, 1229); its Props/Coverage/Terrain tokens have no other consumer; `RenderDirtyRegions` is write-only (OrganismRenderers.cs:241, 543, 555; the Refresh comment at :768-769 states "had no consumer") and is referenced by no test. FloraRenderer's refresh is a main-thread iterator, yet it re-reads the live world per plant: `_w.Flora.Get` (:790, :1079, :1116), `_w.Water.SurfaceAt` (:837, :843), `_w.Fields.Light/Moisture.Sample` (:1204-1205), `_w.GroundHeight` (:997, 1187), `_w.Content.FloraOrThrow` (:803, 992, 1073), so the snapshot neither isolates threads nor guarantees a consistent frame. All other renderers read live state. CoverageRenderer builds tile meshes in `Task.Run` (CoverageRenderer.cs:565-569) reading live `tile.Occ/B/W/Dorm/Flags/D2E` (BuildTileMesh ~:861-876) and `Terrain.Height`, while the sim's async coverage step may be writing the same arrays on another pool thread (CoverageSystem.cs:309-330, `WaitPending` only on save/world edit); the renderer never checks for an in-flight step.
- Impact: correctness (torn/transient geometry) plus wasted work (snapshot capture of unused data).
- Remedy: pick one contract. Option A (recommended): delete RenderDirtyRegions and the unused snapshot members; document "renderers read the sim on the main thread only"; make Coverage tile builds copy the tile arrays on the main thread (or hold a per-tile version and discard builds whose tile version changed before commit; CoverageRenderer already compares `t.Version`); expose per-chunk versions (terrain, litter, coverage, field) as the single dirty source (see R2). Option B: build a real immutable `RenderFrame` per sim tick (flora, fauna, coverage tiles) and make every renderer consume it.
- Visual-quality risk: low for A; B is a rewrite.
- Measure: stress test running at 60x with continuous moss growth looking for tile geometry mismatches; thread-safety review; FrameProfiler "Coverage".
- Files: game/Render/RenderSnapshot.cs, RenderDirtyRegions.cs, OrganismRenderers.cs, CoverageRenderer.cs; src/Vivarium.Sim/Coverage/CoverageSystem.cs.

**R2 - Four overlapping camera-local scatter/dressing systems** (decision, L)
- Evidence: SoilDetailRenderer (6 m, fruit/blades/stalks, :102-330), AmbientGroundCoverRenderer + BankFringe (8 m, :99-346), LoamSurfaceDetailRenderer (2 m patches over the whole island), CoverageRenderer (per-tile moss/lichen), each with its own: near rock/log/gravel collection (SoilDetailRenderer.cs:152-171 vs AmbientGroundCoverRenderer.cs:182-200), flora exclusion lists/halo cells (SoilDetailRenderer.cs:173-206 vs AmbientGroundCoverRenderer.cs:202-225), `CellRng`, 4-tap `Terrain.Height` slope normals (SoilDetailRenderer.cs ~:240-246, AmbientGround :259-264), rebuild triggers (2 m, terrain version, edit delay), and 2 ms time slicing. SoilDetail already special-cases "if AmbientGroundCoverRenderer is active, suppress filler blades" (comment around :310-320). `Quality` is copied to each (GameSession.cs:110-113). Loam builders live in game/App but are render code.
- Impact: duplicated CPU, inconsistent continuity (different radii/fades/cadence per layer), and the only place where ground detail could share caches.
- Remedy: one `LocalDetailField` service owning per-cell cached terrain normal/slope, substrate, exclusion mask (props+flora) and versions; layers become pure functions of (cell cache, seed) writing into pooled MMI blocks. Move Loam*Builder into game/Render.
- Visual risk: medium (re-tuning of densities/fades).
- Measure: sum of scopes AmbientGround + SoilDetail + LoamDetail + Coverage before/after at same camera path.
- Files: game/Render/AmbientGroundCoverRenderer*.cs, SoilDetailRenderer.cs, LoamSurfaceDetailRenderer.cs, game/App/Loam*Builder.cs, GameSession.cs.

**R3 - Guardian mutates flora MMI buffers** (decision, L; top continuity constraint)
- Evidence: GuardianVegetationContact.cs:287-330 reads flora's CPU upload buffer (`TryGuardianBaseTransform`, :671-694) and writes bent transforms directly into flora MultiMesh instances via `Buffer.SetInstanceTransform`, then has to re-apply them whenever FloraRenderer re-uploads (it tracks `UploadRevision`, `LastFloraUploadRevision`). FloraRenderer's byte-compare upload cache (`PooledMultiMeshBuffer._uploaded`, OrganismRenderers.cs:1548-1572) assumes it owns the GPU copy, so the two renderers share a buffer with undocumented ordering; bends can reset on the 0.2 s/4 s flora refresh cadence.
- Impact: perf (per-instance native writes, P6 scan) and continuity (plants snap when a refresh lands).
- Remedy: move contact into the vertex stage: pass N nearest guardian contact capsules (position, radius, bend axis) as a small uniform array or data texture; flora.gdshader/leaf.gdshader displaces by distance. Deletes the cross-renderer coupling, makes bend continuous by construction, removes per-instance CPU writes. Keep the CPU "bend registry" only for audio/diagnostics if needed.
- Visual risk: medium-high (behaviour and look of contact change); needs before/after renders (ledger D5).
- Measure: scopes Guardian.Vegetation*; captured bend continuity frames across a flora refresh.
- Files: game/Render/GuardianVegetationContact.cs, OrganismRenderers.cs, game/Shaders/flora.gdshader, leaf.gdshader.

**R4 - Per-species leaf texture pools do not scale** (decision, L; blocks the photoreal overhaul)
- Evidence: only 4 of 125 species have a visual profile (content/visuals/flora: embercrown, glassfinger, lanternbrush, shadebell); each owns three 2048^2 x4-layer Texture2DArrays (color/normal/physical, 22.4 MB each on disk = ~21.3 MiB VRAM) plus vein_mask/vein_normal PNGs (FloraVisualProfile.cs:345-392, LeafMaterialBaker.cs:12, 26-41, 130-131). Extrapolation (arithmetic): ~75 MiB per species x 125 = ~9 GiB, and every profile species also gets its own `leaf.gdshader` material and two-surface meshes.
- Impact: blocks extending photoreal leaves to the whole catalog; high VRAM and disk cost.
- Remedy: share pools by leaf family (profile already has `FallbackPoolSpecies`/`PoolSpecies`, FloraVisualProfile.cs:245-247, 381-392); drop to 1024 layers for small leaves; array of per-family pools with per-instance variant index; pack normal+physical into one BC5/BC7 array.
- Visual risk: medium (species identity vs shared tissue).
- Measure: `video_memory_bytes` vs species count.
- Files: game/Render/FloraVisualProfile.cs, game/App/LeafMaterialBaker.cs, Textures/LeafPools/*.

**R5 - Switch and flag sprawl** (mixed: safe cleanup, per-item decisions, M)
- Evidence, env vars: VIVARIUM_MOSS_AGGREGATE (parsed twice: Bridge.cs:342-345, CoverageRenderer.cs:205-208), VIVARIUM_MOSS_MICRO_GROWTH (CoverageRenderer.MossGrowth.cs:25), VIVARIUM_BARK_FISSURES, VIVARIUM_LIVING_STEM (Bridge.cs:348-356), VIVARIUM_FOLIAGE_PERF_BASELINE (Bridge.cs:329), VIVARIUM_WATER_REFRACTION (WaterSurfaceRenderer.cs:86), VIVARIUM_FLUID_DIAGNOSTIC (WaterSurfaceRenderer.cs:171, BallisticFluidRenderer.cs:21), VIVARIUM_COVERAGE_DEBUG (CoverageRenderer.cs:23), VIVARIUM_DISABLE_DOF (CameraRig.cs:283, EnvironmentRig.cs:130), VIVARIUM_FORCE_NIGHT (EnvironmentRig.cs:465), VIVARIUM_GUARDIAN_SKIN_TRANSFORM_AUDIT (GuardianSkinContact.cs:138), VIVARIUM_LANDSCAPE_SCENARIO / BENCHMARK_USER_DIR (Bridge.cs:494-495). CLI flags in render hot paths: `--flora-light-cache-audit` (OrganismRenderers.cs:1364, ~25% of UpdateLuminousLights), `--guardian-constraint-cache-audit`, `--guardian-chain-cache-audit`, `--guardian-registry-cache-audit`, `--guardian-cpu-cache-audit`, `--guardian-stem-audit`, `--guardian-lowest-point-audit`, `--guardian-model-no-contact`, candidate flags `--guardian-coati-model-candidate`/`--guardian-coati-coat-candidate` (GuardianRenderer.cs:192, 204); Main.cs holds 32 more flag references. Audit methods in production classes: AuditInstanceUploadCache (OrganismRenderers.cs:1582), AuditTriangleContributions, AuditPublishedTriangles, WaterSurface AuditUniformCache/AuditPublishedTextures, GuardianVegetation spatial audit.
- Remedy: one `RenderSwitches` static (read once at startup, documented) for the keepers (DISABLE_DOF, FORCE_NIGHT, WATER_REFRACTION, COVERAGE_DEBUG); remove FOLIAGE_PERF_BASELINE and the MOSS/BARK/LIVING_STEM gain overrides unless the material defaults are re-tuned; fold audits into `Diagnostics/*.Audit.cs` partials under `#if VIVARIUM_AUDIT`; delete finished "candidate" flags after the owner picks the look.
- Visual risk: none for removal of audit/perf-only switches; decision needed for candidate flags.
- Measure: n/a (structure); confirm identical boot/review output.
- Files: game/App/Bridge.cs, Main.cs, game/Render/{CoverageRenderer*.cs, EnvironmentRig.cs, WaterSurfaceRenderer.cs, BallisticFluidRenderer.cs, Guardian*.cs, OrganismRenderers.cs}, game/Camera/CameraRig.cs.

**R6 - Dead code and stubs** (safe, S)
- Evidence (no callers in game/, src/, tests/): `OrganismRenderers.GillTrial.cs` (126 lines: PrepareGillTrial/PrepareCapRingTrial/PrepareEmbercupTrial); `BranchModuleLoader.cs` (82 lines, namespace `Vivarium.VisualLab`); `RenderDirtyRegions.cs` (225 lines, write-only); FrameProfiler APIs listed in the preamble; `GuardianContactInstances()` (GuardianVegetationContact.cs:757); WaterSurfaceRenderer stubs `AirborneSegments => 0`, `TilesCulled => 0`, `TilesResynced => 0` (:~96-99); no-op `stride = Quality >= 2 ? 1 : 1` (AmbientGroundCoverRenderer.cs:238); legacy `DistantGillStations` comment (OrganismRenderers.cs:23-24, still feeds far-tier build); `--perf-test` branches (GameSession.cs:94, Bridge.cs:494) without a runner; `ApplyQuality` cases 0 and 2 (EnvironmentRig.cs:296-309, 326-342) unreachable except the vanished perf harness (GameSession.cs:94 forces 1; memory note: single tier intentional); `Quality` property copied to 5 renderers (GameSession.cs:110-113).
- Remedy: delete after `git grep` confirmation; keep Quality 0 only if a low-end safe mode is wanted (decision).
- Visual risk: none. Measure: build + boot-test.
- Files: listed above.

**R7 - Environmental fields reach shaders as 2 s stepped textures** (decision, S-M; continuity)
- Evidence: IslandRenderer.cs:94 `FieldRefreshInterval = 2.0`; `UpdateFieldTexture` rewrites field/litter/ambient (grid res) and sub/cover (4x) images via `SetData` + `Update` (:477-482, 549, 587, 308-309) with no previous/current blend, whereas WaterSurfaceRenderer interpolates `previous_state/current_state` with `state_blend` (WaterSurfaceRenderer.cs:222-250). Moisture/light/litter-driven soil colour, moss weight and ambient cover step every 2 s; flora placement samples fields only every 32 ticks (OrganismRenderers.cs:107, 794-800).
- Impact: visible stepping/pop at fast sim speeds; cost is a full-texture upload per 2 s (low).
- Remedy: keep previous+current textures, upload on change, and lerp in terrain.gdshader via a `field_blend` uniform (as water does); alternatively upload deltas for changed cells.
- Visual risk: low (smoother).
- Measure: capture a 20 s sequence at 60x and diff soil colour discontinuities; `Island.FieldTexture` scope.
- Files: game/Render/IslandRenderer.cs, game/Shaders/terrain.gdshader (+ rock/log/strata if they sample the same fields).

**R8 - Mesh generation cache is in-memory only; triple CPU copies** (decision, M-L)
- Evidence: all flora variants regenerate every process start (OrganismRenderers.cs:396-413, 24 tasks in flight) and fauna on the main thread (:1797-1814); caches survive only across worlds in one process (`_builtContent`, `_builtFaunaContent`). Each mesh exists as `MeshData` lists, `PackedMesh` (5 arrays kept in `PreparedMeshes` ConditionalWeakTable while the MeshData lives, Bridge.cs:52, 83), then the engine copy; `StructuralSurface` indexes keep extra triangle copies for contact hosts (FloraRenderer.WoodyContact.cs:99-143).
- Impact: startup time scales with species x morphs x tiers; transient RAM spikes.
- Remedy: serialize finished ArrayMeshes (or raw vertex/index byte buffers) to user:// keyed by (content hash, generator version, species, tier, morph); build `PackedMesh` directly into native-friendly buffers (`PackedVector3Array` pooled) or use `ArrayMesh.AddSurfaceFromArrays` with `Span`-backed arrays to skip one copy.
- Visual risk: none if cache keys are correct (stale-cache risk is the real hazard).
- Measure: StageTimings Flora/Fauna, time to PopulationReady, peak `working_set_bytes`.
- Files: game/App/Bridge.cs, game/Render/OrganismRenderers.cs, FloraVisualProfile.cs.

**R9 - Diagnostics fragmented; harness absent; what is not measured** (safe/decision, M)
- Evidence of what exists: FrameProfiler (CPU wall time per named scope, max per second only is exported: LiveDiagnostics.cs:94 `scope_max_ms`; overlapping scopes, never GPU, FrameProfiler.cs:37); LiveDiagnostics 1 Hz: frame percentiles, `process_ms`, sim last ms/backlog/ticks per second, flora/fauna counts (`flora_visible`, `fauna_drawn`), `water_triangles`, memory (engine static, managed, working set, `RenderVideoMemUsed`), `RenderTotalDrawCallsInFrame`, primitives, `ObjectNodeCount`, GC counts, terrain version, log warnings (LiveDiagnostics.cs:78-95). Ad-hoc counters scattered in renderers: Flora peak*Ms strings (OrganismRenderers.cs:119-131), Luminous* (:146-162), Water UniformWrites/TextureUploadBytes, Coverage TrianglesBuilt, Guardian vegetation counters.
- Missing for the items above: (1) GPU time per frame and per pass (RenderingServer.ViewportSetMeasureRenderTime + GetMeasuredRenderTimeGpu/Cpu; Godot's frame GPU time); (2) VRAM split (`RenderTextureMemUsed`, `RenderBufferMemUsed`) and `ObjectResourceCount`/`ObjectOrphanNodeCount` (leak check); (3) counts of MMI nodes and non-empty MMIs, Σ `VisibleInstanceCount`, ground-cover instances (`LastGroundcoverInstances` exists but unlogged), loam patch/triangle count, coverage tile count, `FloraRenderer.TrianglesDrawn`, `TakeRefreshDiagnostics`; (4) managed allocation bytes per frame (`GC.GetAllocatedBytesForCurrentThread`) and gen2 pause time; (5) thread-pool queue length/busy threads; (6) scope means and totals (only max exported; the existing `TakeIntervalTotals` is unused); (7) shader/pipeline compile hitch counter; (8) per-subsystem draw attribution (do a layer-toggle sweep by hiding Visible of each renderer and recording `draws`/`primitives`/GPU ms); (9) population sizes (plants, dead plants) next to flora timings; (10) FrameProfiler's own overhead (lock + dictionaries).
- Remedy: define `IRenderDiagnostics { void Collect(DiagnosticsSink) }`, implement it on each renderer to expose the counters above, and have LiveDiagnostics iterate registered providers; add the GPU timer and allocation delta; add a `--bench <script>` runner that flies a fixed camera path and writes the JSON (restores the lost perf harness; consider also fixed-seed world load).
- Visual risk: none. Size M.
- Files: game/App/FrameProfiler.cs, LiveDiagnostics.cs, all renderers (small partials), Main.cs.

**R10 - `Bridge` god class and App/Render folder boundary** (safe, M)
- Evidence: Bridge.cs combines content source (:12-23), sim<->Godot conversion (:39-50), mesh packing/upload/tangents (:51-133, 135-315), shader factory with env-var experiments (:317-359), texture resolution + cache (:361-457), UserSettings persistence (:500-576), UserPath incl. review-mode redirect (:482-497). Loam builders, review runners (SpecimenReviewRunner 894 lines, GuardianReviewRunner, VisualReviewRunner) and LeafMaterialBaker sit in game/App with production code.
- Remedy: split into `MeshUploader`, `MaterialFactory`, `TextureCatalog`, `UserPaths`, `UserSettings`; move Loam*Builder to game/Render and review/baker tools to game/Tools (excluded from export by a `#if` or folder).
- Visual risk: none. Measure: build + boot-test.
- Files: game/App/Bridge.cs, Loam*Builder.cs, *ReviewRunner.cs, LeafMaterialBaker.cs.

**R11 - Clock domains** (decision, S; continuity)
- Evidence: three clocks: sim ticks (10 sim-s, paused with the sim); EnvironmentRig day/night uses a wall monotonic stopwatch that keeps running while paused and is neither saved nor tied to sim speed (EnvironmentRig.cs:116-123); flora `_clock`, fauna `_normalClock` accumulate render delta; water optical time follows sim speed/pause (WaterSurfaceRenderer.cs:193); shader `TIME` sways leaves/blades regardless of pause (flora.gdshader:82-117, ground_cover/leaf similar) unless review_time is set.
- Impact: inconsistent pause behaviour and day-night vs bio-time mismatch at 60x; no perf effect.
- Remedy: define a `PresentationClock` (wall seconds, paused flag, speed) injected to renderers/shaders; decide whether night follows bio days.
- Files: game/Render/EnvironmentRig.cs, OrganismRenderers.cs, WaterSurfaceRenderer.cs, shaders using TIME.

### B.2 Structural constraints that most limit continuity/performance (summary)
1. Camera-local detail is regenerated by moving windows (2 m triggers, 2 ms slices): the dominant source of pop-in and CPU churn (P13, R2). A cell/chunk-keyed cache with version stamps (R1) is the enabling change.
2. Cross-renderer buffer mutation (R3) and 0.2 s/4 s flora refresh cadence mean any dynamic contact snaps on refresh.
3. Stepped field textures (R7) and unquantised day-night writes (P1) are the two places where time is discretised unevenly.
4. Per-species texture and mesh duplication (R4, P16, R8) make the catalog-wide photoreal goal quadratic in VRAM/startup.
5. Full-screen stack (P12) plus a heavy terrain and water pixel shader (P14, P15) under a Radeon 860M budget; there is no GPU timing yet to rank them (R9).

### B.3 Suggested order of work
1. Safe group, cheapest first: P8, P7, P1, P4, P2, P5, P6, P3(a), R6, P11(4K+Masters), then P9, P10, P3(b), R9 (GPU timer + allocation delta first, so everything after can be measured).
2. Then decisions with paired captures: P12 experiments (one toggle at a time), P13/R2, P15, P14, R3, R7, R4/P16/R8.

### Safe mechanical wins vs needs design decision (recap)
- Safe: P1-P11 (P11 only for the 4K PNG/Masters filter), R6, R10, R9 counters, R5 audit/perf-only switch removal.
- Decision: P12-P19, R1-R4, R5 candidate flags, R7, R8, R11, P11 EXR.
