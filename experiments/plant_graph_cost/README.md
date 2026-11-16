# Plant plan construction cost

This standalone runner loads the explicitly supplied `Vivarium.Sim.dll`, constructs 60 uncached plans per species with world seed `0x123456789abcdef0` and IDs 1 through 60, and hashes the serialized ordered organ fields including parent, type, vectors, sizes, timing and seed. Timing and thread allocations include signature serialization and memory stream growth; they are not a pure accommodation measurement. Run in separate processes for cold generator caches. The hash does not include shape definitions.

```powershell
dotnet build experiments/plant_graph_cost/plant_graph_cost.csproj --artifacts-path build/plant-graph-cost-20261115/runner-baseline -p:SimDll=E:/Vivarium/build/plant-graph-cost-20261115/baseline/Vivarium.Sim.dll -c Release
dotnet build/plant-graph-cost-20261115/runner-baseline/bin/plant_graph_cost/release/plant_graph_cost.dll
```

2026-11-15 single matched runs, same machine, release runner: baseline assembly MVID `b11f2a1d-dc86-4365-b761-b75cbf6a5740`, SHA256 `DB0B41F9CA81AC16CDB9E540860738D1CE2A0A01FBDDE2C33D3A81C022216319`; candidate MVID `a60279ed-333b-423a-9dd0-def69ce3e6eb`, SHA256 `232401F11742727169E2DEDA2AB9BCEE05B7C7C010D92F38D7C729A4248B98BA`. Both emitted organ hash `9D225A76874127B497EE2ECDB85A41D6A9634023F17D636B7ACA2E2E9000180D` for 98,552 organs. These are empirical checks for the tested seeds, not proof for all inputs.

| Species | Baseline ms | Candidate ms |
| --- | ---: | ---: |
| Glowbriar | 16860 | 13659 |
| Moonfern | 400 | 308 |
| Shadebell | 1628 | 1074 |
| Total | 18894 | 15046 |

Thread allocations including serialization rose from 156,323,392 to 188,722,168 bytes due to segment bounds storage. The conservative bounds test retains original candidate and penalty order; it still scans all earlier segments. Results are single measurements and may vary with machine load.

## Spatial grid iteration

The final measured candidate uses a 0.15 m grid, indexes segment bounds expanded by radius, gathers and deduplicates nearby segments, then sorts their indices before the original distance and penalty calculations. Oversized cell ranges use a full-scan fallback. The signature buffer is excluded from `plan_allocated` and `plan_ms`, which are measured only around each `PlanFor` call; the overall counters still include serialization. Build the same runner against the desired DLL by changing `SimDll` and the isolated artifacts path.

Fresh baseline: MVID `b11f2a1d-dc86-4365-b761-b75cbf6a5740`, overall 19096 ms, plan-only allocations 89,211,232 bytes. Grid candidate: MVID `5aaec925-2cab-4ee7-8017-b8c1b7987baf`, SHA256 `A577466882E273F1F2E7E2C4F80A277AD242C6F23356D5FAFADBD925AD3D9878`, overall 1714 ms, plan-only allocations 131,325,128 bytes. Both produced the same organ signature given above. These are single runs; the index substantially improves construction time at the cost of additional transient allocations. The candidate assembly may also contain concurrent changes elsewhere in the project; the tested signature specifically covers plans.

With exact segment and visit-buffer capacity, the final candidate MVID `8551bb8f-bfb7-42b4-9249-83a6cb2d4543` (SHA256 `4EA55AAA702C931C5E052CAA929378C50E68257AB7C8053168A9298F5BF7A1E9`) took 1808 ms overall and allocated 96,947,696 bytes in plan construction, with the same organ signature. This is 26% less plan allocation than the first 0.15 m grid candidate, and 9% more than the original baseline. Measurements are single runs.
