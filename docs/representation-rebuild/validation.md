# Rebuild validation and comparison protocol

The current desktop reports NVIDIA GeForce GTX 1080 and AMD Ryzen 5 1600. Record the actual adapter in every native report; these measurements do not verify the Radeon 860M target. Historical pc5 baseline on the earlier machine: real-scene GPU ~30.5 ms, ~7.05M primitives, 693 draws, ~4.1GB VRAM and ~7MB managed allocation/frame. This is historical evidence, not an identical-current-build control. The accepted INT-L comparison has its own matched GTX 1080 control.

## Commands
Run from repository root using PowerShell or a direct process API. Never feed Python via stdin/heredoc. Compile the solution and game sequentially; agents obtain the build lease before compiling shared outputs.

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
- Targeted capture correction: initial subset skipped stateful ApplyVariant calls and misaligned soil LOD shot names; now every logical transition runs before screenshot filtering. Corrected run build/overhaul/S2b/baseline-corrected generated8 selected views, log names align, exit0. Earlier S2b/baseline is invalid for state/LOD comparisons.

### Paced flora decision and restored production validation

Final native flora camera/guardian checks passed, but the later live pair rejected the1m chunk backend: candidate/control mean GPU9.2429/8.6268ms and826.55/765.45draws, with equal p50/p95 (33.2/51.8 ms) and effectively equal memory/primitive counts. Both180s/warm120 runs ended199flora/77fauna. This is a single stack comparison including the independent Coverage snapshot fix, not repeated performance acceptance or isolated flora attribution.

The previous production OrganismRenderers/GuardianVegetationContact/Main/LiveDiagnostics are restored. The exact five-file diff againstf35bee5a, two new runner sources, binary hashes, comparison JSON and analyzer are preserved under `rejected/INT-F-1m`. The patch was normalized to LF and `git apply --check --cached` passed against its recorded base before staging. Camera-manifest locking/provenance remains in VisualReviewRunner without rejected cache API references; Luna's read-only restoration/archive review found no material blocker.

Restored sequential solution/game builds passed; headless boot exited0 with `save=True load=True roundtrip=True invariants=0` and `VIVARIUM_BOOT_OK`. Logs: `build/int_f/restored-solution.log`, `restored-game.log`, `restored-boot.log`; source/binary provenance and raw performance samples remain under build/int_f. The full-suite substrate401ns timing failure is still retained. No further optimization was started, per the owner's request to pace work into bounded checkpoints.

## Coverage publication checkpoint, 2026-11-14

Final serial solution/game builds passed (`build/coverage_cost/fixed-solution.log`, `fixed-game.log`), with the existing PlantFieldDemo nullable warning in the full build. Native Forward+ `--coverage-tangent-proof` passed exact prepared/fallback upload-array equality, worker preparation, concurrent upgrade and legacy batched fallback (`final-tangent-proof.log`). Headless boot passed save/load roundtrip and invariants=0 (`final-boot.log`). Simulation source is unchanged; no new full-suite pass is claimed.

Acceptance uses the same Game/Sim DLLs for both180s/warm120/cap30 runs, fixed pose(0,5,9), rotation(-0.5,0,0), FOV62 and input lock. The control adds --soak-sync-coverage-tangents; the candidate consumes844 prepared uploads while control consumes0. Coverage main-thread CPU4.2221->1.4046ms/frame and p99111.7->55.2ms, with effectively equal primitive count, video memory and allocation/frame. Both begin178flora/77fauna and end199flora/77fauna, about3biologicaldays; ticks/backlog are not identical at the window start. These are bounded observations, not totalCPU/GPU/power or mature-world claims.

The initial control camera drifted, so earlier age-only/tangent observations cannot serve as strict matched-view comparisons. The deferred age-only candidate is archived with its tests, summaries and hashes. The controlled report is experiments/representation_rebuild/INT-C-cost.md and its result/hash JSON files. Reproduction/analysis uses analyze_coverage_cost.py with build/coverage_cost as input. Both final soaks retain one ObjectDB exit warning, not attributed here.
