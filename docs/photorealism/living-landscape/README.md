# Living landscape measurement contract

P00 scope is rendering diagnostics and reproducible visual scenario definitions. No ecological biomass, species, hydrology, scheduling, save format, approved plant topology, shader or material is changed. Authoritative simulation input and digests remain the boundary. Presentation quality, camera and light cannot feed back into biology.

Canonical visual target: Godot 4.7.1 .NET Forward+, 1600×900 at 100% internal scaling, quality 2 (high), VSync disabled, MSAA 4×, SSAO enabled, 4096 directional shadow atlas with four splits, glow disabled. Existing projected-size flora geometry tiers and hysteresis remain in place. Low uses 75% internal scaling and no shadows/AO; medium uses full scaling, FXAA and 2048 shadows/two splits. Those modes are diagnostic comparisons, not the accepted visual target.

Assets remain existing content recipes and shipped textures: `game/content/visuals/flora`, `game/Shaders`, `game/Render` and procedural `src/Vivarium.Sim/Geometry`. No asset conversions are authorized by this increment. Reference dossiers precede future geometry/PBR work.

Pinned fixture starts from `default` / Streamside Hollow, seed 20260923, with deterministic paused six-biological-day preparation in 200-tick blocks; --reference retains its historical behavior. Supplemental -Preparation initial captures tick0/zero biological days and cannot replace mature/density acceptance. Preparation records the actual tick and digest; scene queries break ties by stable entity ID or cell order. Reference libraries are version 1. See [scenarios.md](scenarios.md). Different preset/content/revision results require compatible measured world and camera conditions; old September reports are historical evidence only.

Reports use a unique UTC/GUID run directory under `build/living-landscape`, separate from historical `docs/baseline` and reference outputs. `render_report.json` is created exclusively, never overwritten. Raw frame timings, contiguous second windows, CPU scope maxima, process/VRAM samples, settings, source tree hash (includes dirty/untracked game/src), executable hash, engine/hardware/driver/OS/RAM and named images accompany provenance. CPU scope maxima overlap and cannot be added or called GPU timing; GPU timing is null until an actual GPU capture exists. Renderer counters unavailable through the present rig are explicitly listed.

The benchmark launcher redirects application storage through opt-in `Bridge.UserPath` to each run's isolated `user` directory, disables autosave/transient preferences through existing test mode and places engine logs in the run directory. It never opens a real saved world. The normal application storage and legacy perf/reference commands retain their prior behavior.

Run the source harness after building/importing the isolated checkout:

```powershell
$env:VIVARIUM_GODOT = 'E:/Vivarium/tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
./scripts/visuals/benchmark.ps1 -Scenario frozen -Preparation mature -ConfirmIsolation
python scripts/visuals/test-validate-report.py
python scripts/visuals/validate-report.py build/living-landscape/RUN/render_report.json
```

For a portable release pass `-Executable PATH -EngineVersion EXACT_BUILD -SourceRevision SHA -SourceTreeSha256 HASH`. The executable must contain this increment. Portable releases are not built by the harness. Windows CIM provides actual hardware provenance; Python is standard-library only. `-DisplayScaling` is the operator's measured Windows scaling, recorded rather than guessed. `-StartupKind cold|warm` declares the launch context; ten seconds warmup precede steady-state measurement and startup/import logs remain retained. A warm launch is an immediate repeat in the same clean session, not an assertion that the entire shader cache is hot.

For compatible reports use `validate-report.py BEFORE --compare AFTER --html comparison.html`. Images are linked and shown with frame deltas. The legacy reference comparison command routes living-landscape reports to this validator, preventing hardware/settings/camera mismatches from looking like improvement. Absolute gates apply before a 5% regression check.

A 60-second screening pass is not G0 or release approval. G0 requires actual cold/warm captures for required regimes on the target hardware, bottleneck evidence, all frame gates, a 20-minute resource soak, event/sculpt coverage, deterministic recipe/reload checks and human multi-view review. Growth/death burst, pinned worst-density fixture, exact shader wind phase and separate GPU timings remain explicit gaps. Do not proceed to visual production on a fabricated pass.

Rollback: revert the P00–P03 checkpoint; unset `VIVARIUM_LANDSCAPE_SCENARIO` to use the unchanged legacy perf runner. The presentation clock override defaults null and is never serialized.

Current verification and gate disposition are recorded in [baseline.md](baseline.md). Existing failures remain failures; no assertions are rewritten here.

The initial-world diagnostic can be requested with `-Preparation initial`. It is supplemental, never G0 maturity/density acceptance. Match actual preparation age/tick/digest and ID before A/B comparison. The real game window is visible for windowed visual/performance review; utility processes stay hidden. Source launches rebuild before capture and the runtime attests loaded game/sim assembly hashes. Portable launches require declared source lineage and attest runtime assemblies; a signed source-to-package build manifest remains follow-up.

Supplemental `-Preparation initial` selects the generated initial world for diagnosis; default `-Preparation mature` retains six biological days. Do not compare different ages or treat initial-world performance as mature/high-density acceptance. `-DisableAccessibility` records and uses the installed Godot's benchmark-only recovery flag after an AccessKit startup failure; it does not change normal game accessibility settings. Actual engine display names are normalized case-insensitively by the validator.

Managed provenance verifies loaded module MVID against the file before hashing, since Godot's byte-loaded assemblies expose no Location. Source launches use the fresh `.godot/mono/temp/bin/Debug` output. Portable managed-file layout still requires export validation; no portable compatibility pass is claimed. Raw timings are flushed before capture/replay/provenance stages can fail.
