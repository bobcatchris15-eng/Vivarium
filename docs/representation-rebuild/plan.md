# Representation Rebuild Completion Plan

Goal: finish the approved rebuild in brief.md using the existing contracts and accepted experiments, then select and integrate measured winners.
Architecture: deterministic pure C# state in src/Vivarium.Sim/Repr; disposable Godot representation caches in game/Repr; production integration uses versioned local changes and bounded work.
Tech stack: Godot 4.7.1 .NET Forward+, .NET 8 simulation, Radeon 860M verification.
Spec: brief.md, refined by .claude/ledger.md RD1–RD16. User authorized autonomous execution and Terra workers.

## Recovery and implementation milestones
- [ ] P2g: finish Forms/catalog/attachment/growth tests and plant_forms capture; preserve plausible full canopy density at all detail levels. Own src/Vivarium.Sim/Repr/Plant and game/Repr/Plant.
- [ ] E1/E2: finish WorldSubstrate, weather, uptake, water ledger and production world hooks; prove coupled/uncoupled 90-day diversity, conservation, timestep behavior and bounded cost. Own Ecology, FloraSystem, VivariumWorld and selftest/Eco*.
- [ ] INT-L: Stock/Cached real-scene mode for fair comparison, continuously interpolated time tables, terrain/plant-fed occlusion and dirty rebake, 30 fps cap, lighting probes and real-soak comparisons. Own EnvironmentRig, settings, shaders and game/Repr/Light.
- [ ] S2b: replace rejected graphic soil look with scanned material-led relief, attached litter, natural wetness and outcrop/dig geometry; fixed-camera soil_cube captures and cost. Own Substrate state/render paths once ecology interface stable.
- [ ] C2: correct dense-interior growth and damp-rock niches, render attached moss and crust/foliose/fruticose lichen with stable optical coverage across levels; colonizing_stone demo and cost. Own Colony state/render paths.
- [ ] INT-F: connect cached chunked presentation to live production flora, handle births/growth/deaths locally, camera-only selection without population regeneration. Compare identical dense world; keep only measured winning presentation.
- [ ] INT-S/C/E: route shared authoritative substrate/colonies into actual island and remove duplicate state/rendering where replacements win.
- [ ] P4/alternatives: evaluate aggregate foliage/GPU generation against measured bottlenecks; record rejection when complexity/cost lacks benefit. Do not keep unmeasured alternatives as production options.
- [ ] REVIEW: independent adversarial tests/review, six demo captures, repeat real dense-world idle/orbit/day-cycle timings, boot and full selftests; repair regressions.
- [ ] FINAL: architecture map, experiment decisions, reproducible validation/capture commands, current ledger and clean pushed main.

Each implementation unit: inspect current code and spec; add behavioral failure probe for changed logic; implement; run target checks; return files/evidence/caveats; root reviews diff, runs integration checks, commits explicit paths, pushes. GPU capture tasks obtain root lease before launching Godot. No worker commits or touches another worker’s owned paths.
