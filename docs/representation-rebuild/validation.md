# Rebuild validation and comparison protocol

Hardware measurements belong to this Radeon 860M machine; no GTX1080 performance claim. Historical baseline: pc5 real-scene GPU ~30.5 ms, ~7.05M primitives, 693 draws, ~4.1GB VRAM and ~7MB managed allocation/frame. This is historical evidence, not an identical-current-build control.

## Commands
Run from repository root. Use cmd.exe or direct process API; default PowerShell startup can stall. Never feed Python via stdin/heredoc.

Build: dotnet build Vivarium.sln --nologo -v minimal
Boot: tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --headless --path game -- --boot-test
Boot success requires VIVARIUM_BOOT_OK, invariants=0, exit0.

Parallel selftest isolation (use unique artifacts path per process):
1. dotnet build experiments/representation_rebuild/selftest/ReprSelfTest.csproj --artifacts-path build/selftest-root --nologo -v minimal
2. dotnet build/selftest-root/bin/ReprSelfTest/debug/ReprSelfTest.dll <filter>

Do not use dotnet run --artifacts-path for proof: this environment was observed building isolated artifacts but executing stale default output. Missing test filter now must exit2 with REPR_SELFTEST_NO_MATCH. Zero selected checks is failure. Every report records test count.

GPU: one Godot window at a time. Wait for build completion, then capture a fixed scene with --repr-review ABS_DIR --repr-demo NAME. --repr-shots exact_shot_a,exact_shot_b selects a diagnostic subset; omit it for complete acceptance evidence. Diagnostic subset is not a performance acceptance run.

## Evidence rules
Keep seed, elapsed ecology time, camera, day fraction, resolution, exposure and density equal across comparisons. Discard first shader/cache warmup. Repeat at least two timings per side interleaved. Capture the real production world as well as isolated demos. Cap30 establishes steady-use GPU busy fraction; uncap both sides to compare per-frame cost. Show quality differences plainly.

Six demos: three plants and attached junctions/growth; living soil cube/dig; colonizing stone and distinct moss/lichens; dense canopy static/slow orbit; day/dusk/understory/night with responsive cached occlusion; long ecology with conservation/diversity/no backlog.

## Recovered validation
- Fresh pre-takeover solution build: exit0, 9 warnings.
- Fresh headless boot: exit0, save/load roundtrip true, invariants0.
- Missing-filter regression: old runner tests0/failed0 exit0; changed runner exact isolated DLL REPR_SELFTEST_NO_MATCH exit2.
- Initial P2g images rejected: sparse glowbriar/shrub canopy, fern crystalline overlapping pinnae, coarse paperlike silhouette. 48 baseline captures under build/overhaul/P2g/capture.
- Fresh substrate filter: 18 tests, failed0 (on recovery DLL). Timing during parallel work is correctness evidence only.
- Recovery colony filter: 12 tests, failed0; wet-route ratio remains weak (1.11), and interior/lichen appearance is queued for C2.
- Targeted capture filter: six requested candidate views generated and runner exited cleanly; default whole-set capture remains available.
