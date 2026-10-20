# Fauna-light Vivarium: population/performance calibration (2026-10-19)

## Goal and measured cause

Keep habitat visibly productive and increasingly lush **without** paying for thousands of separately simulated animals. Flora growth and visual lushness are separate from animal population size.

Before: 31-minute dense-default run, `build/long-runs/dense-flora-default-20261019-31m-recorded/`, had living fauna **139 → 1124** and flora **177 → 1378**. Average sampled FPS fell by five-minute windows **47.2 → 27.4 → 23.7 → 21.9 → 17.7 → 10.7**. Whole-run p99 **287.81 ms** and **24.1 mean FPS**, failing the 30FPS worst-frame floor. Other independent hotspots: `Sys.coverage.plasmodium`, `SoilDetail`, `Flora.Lights`.

## What changed

- All **17 fauna species** now have: lifespan ×2.0; maturity age ×1.4; background daily mortality ×0.5; reproduction cooldown ×2.5; maximum clutch ~55% of former (minimum one); local density threshold ~67% of old; species population caps ~32% of former, floors of 8 predators/10 others. Combined species caps **512**.
- All **16 world presets** now specify an actual `faunaBudget` **247–430** based on island diameter, parsed by `ContentLoader.ParsePreset`. Default preset cap **360**, formerly implicit **2000**. Starter founder numbers ~55% former; default **141 → 77**. All starting fauna species retained; rare predators originally founded in groups of 3+ now keep at least **3**, mitigating breeding-partner loss from early stochastic deaths.
- Reproducible versioned/idempotent changes are in `scripts/fauna/calibrate_quiet_ecosystem.py` with frozen `fauna_calibration_baseline.json`. Re-run `python scripts/fauna/calibrate_quiet_ecosystem.py --check` to validate without rewriting. Biology/physiology fields and preset fauna budgets/founders are all this script changes; **flora, materials, habitats, diet, motion, meshes and genetics are untouched**.
- The `WorldDescriptor.DefaultFaunaBudget=2000` fallback is intentionally retained for manually created fixture worlds and serialized older saves. A saved world above the new species caps stops further births but **is not force-culled**, preserving individual identity, detritus cycles and ecological continuity. Expect biological turnover, not immediate disappearance of existing saved animals.

## Native test, confirmed

`build/long-runs/fauna-quiet-default-20261019-9m/` contains full live diagnostics, initial/final `.vivsave` and 18 Godot screenshots. Real GTX 1080 Forward+ with the **user-approved prior surface-treated flora** (authored botanical hardware remains disabled).

- Fauna **76 → 89** in nine minutes; flora **177 → 426**; total live flora biomass **137.39 → 183.27** (roughly +33%).
- Sampled FPS by two-minute windows **40.7, 44.8, 43.6, 46.2, 45.1**. Full-run FPS **44.5 mean**, p50 **19.62ms**, p95 **40.42ms**, p99 **62.56ms**, worst **258.91ms**. 2458/23754 frames >33.4ms. **30FPS worst-frame floor remains unmet.**
- Run started just before the final predator founder floor +1 correction: the next default world will start with **77**, not 76.
- Not a controlled GPU A/B comparison with the earlier 31-minute run because its renderer was the experimental botanical hardware. Nine minutes **cannot prove** that the >10-minute/30-minute degradation is cured.

## Acceptance and safety

- `tests/Vivarium.Sim.Tests/FaunaDensityCalibrationTests.cs`: verifies 17 species biology, all 16 presets/retained founder species, saved over-budget individuals aren't instantly removed.
- Existing `FaunaGeneticsTests` species ecological survival/reproduction fixture was updated to leave room below the rare predator cap and use test-only bio acceleration for reasonable execution time; real lifecycle/cooldown still apply.
- Fauna + ecology test gate: **37/37 passed**, `build/botanical-rebuild/fauna-only-final-tests.log`. The separate calibrated species survival gate **17/17 passed**, `fauna-reproduction-gate.log`.
- Earlier broad mixed test selection had **69/71** passing. Two issues unrelated to fauna counts remain open: `BootstrapTests.ValidContentLoadsDeterministically` expects 48 flora definitions while content has 53, and `BioClockTests.BioClockAdvancesPerTickAndSurvivesSaveLoad` diverges after 500 ticks following a reload (coverage, fauna, fields, flora, litter, world payloads in `fauna-clock-diff-debug-normal.log`). The temporary BioClock test instrumentation was reverted. Do **not** claim the save/reload divergence is resolved or necessarily introduced by this calibration.

## Follow-up

Run a second **31-minute native density benchmark** with identical production visuals; measure late fauna counts, plant biomass and canopy cover, GPU primitives and stage timings, not just FPS. Improve apparent lushness via coverage / visual maturity / clustered vegetation without multiplying individual biological simulation. Do not restore rejected authored-geometry renderer without same-save/same-camera user approval. Continue investigating plasmodium, SoilDetail and Flora lighting independent of animal counts.

Work was done in place on existing dirty `main` without resetting unrelated edits or mutating user saves.


## Follow-up long-run started (not yet finished)

A fully recorded 31-minute native Forward+ run with the final **77-founder** calibration and previously accepted surface-treated flora was launched detached in `build/long-runs/fauna-quiet-default-final-31m-20261019/`. Manifest `run-manifest.json` lists console wrapper PID 28496, duration 1860s; actual Godot process and telemetry are under `telemetry/`. It will write a `perf/perf_report.json` and `perf/world-final.vivsave` when complete, plus continuous samples and one PNG every ~30s.

Initial world save and renderer startup were observed. **Do not report the long-term 31-minute FPS trend until the final report exists.** This is a native GPU load run, so use it only when not comparing against concurrent heavy workloads; do not falsely attribute any total FPS change to fauna alone.
