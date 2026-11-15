# Graph plant configuration iteration

The organ-graph implementation is being developed through configuration and renderer corrections. Earlier poor parameterizations are comparison evidence, not a rejection of graph plants. Existing species appearance and save compatibility are not requirements. The retained species names are demo labels; the criterion is believable, lifelike plants with coherent growth.

## Configuration

`organic-v4.json` is the fourth visual iteration. `Glowbriar`, `Moonfern`, and `Shadebell` objects configure their generator `FormSettings`. These control axis counts, lengths, curvature, elevation, blade dimensions and flower size. Fern growth durations are configurable separately so pinnae open while the young rachis is still short. All lengths are metres and elevation angles are radians. `half-width / length` ratios determine rendered blade width; `OrganShape.LengthToWidth` is only an authoring hint.

The optional `Renderer` object controls `ChunkMetres`, `Detail0Pixels`, and `Detail1Pixels`. Chunk size is fixed when each renderer is initialized. The field comparison configurations force medium detail in both cases, varying the chunk size between 2 and 4 metres. Thus they test grouping with the same organ-shape detail; visibility/culling can still change the submitted triangle count.

Run the bundled Godot console executable from the repository root with:

```powershell
& tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --repr-review E:/Vivarium/build/plant-config-review --repr-demo plant_specimens --plant-config E:/Vivarium/experiments/representation_rebuild/plant-tuning/organic-v4.json --seed 804584441
```

`--repr-shots glowbriar_whole,moonfern_whole,shadebell_whole` narrows a trial. `--repr-camera-reference <previous plant_specimens/report.json>` reuses recorded cameras by shot name, so changing the plant cannot improve a comparison merely by moving the camera. Whole-plant iterations used the baseline cameras. Macro cameras were subsequently moved to front-side joints because the first interior-joint camera was occluded by foliage. Reports embed the configuration text and Game/Sim module IDs.

## Iterations and implementation corrections

The baseline is the graph specimen implementation at `ce0574b3`, rendered before rebuilding any changed source. Native results are under `build/plant-graph-tuning-20261115/`.

1. Candidate 1 increased foliage size and volume, corrected fern pinnae from the normal-to-frond direction to the sideways direction, and increased arch. The fern became too flat and the shrub remained too regular.
2. Candidate 2 balanced fern elevation/arch and lowered shrub branching. Parent tubes gained a tessellation ring at each emerged child attachment, keeping the actual visible curve under a junction. This improves curved joints without changing their graph positions.
3. Candidate 3 varied cane lengths, branch attachment positions and frond planes; shrub forks became independently staggered rather than matching pairs. The crown became less regular, but cane foliage remained sparse and young fern stems were mostly bare.
4. Candidate 4 raises the cane configuration to four branches and five paired leaf nodes per twig (480 nominal blades), varies and tapers blade size, narrows fern pinnae, and separates rachis and pinna growth timing. These are tuning decisions, not a requirement to retain the former species silhouette.

Common geometry fixes accompany the trials: Godot-clockwise leaf winding, outward bell normals and base-cap facing, cupped/twisted lamina normals, a shallow midrib tapering to zero at the base/tip, and curved cross-sections retained at medium/far detail. The original far blade had only two longitudinal spans and one span across its width. Full growth schedules are normalized to age one instead of cutting off late organs; the pre-fix mature shrub fixture still had 14 unfinished organs.

## Acceptance scope

These remain the graph specimen and field renderers. The live island still uses the existing production flora backend. The trials establish continued graph development and repeatable controls; they do not claim full production integration, photorealism acceptance, completed crozier unfolding, or superior whole-world performance.

The field's legacy stand-in renders top-tier whole-plant meshes and is not a fair performance control. Use the 2 m versus 4 m **graph** configurations to study batching. The native harness is capped at 30 FPS, so frame percentiles near 33.3 ms do not establish uncapped throughput. GPU timing, draw calls, submitted primitives, resource use, and visible quality must be assessed together. The harness reports the existing seven Texture RID / one ObjectDB exit warnings; these are not resolved by this tuning work.

Plant tests run through `experiments/representation_rebuild/selftest/ReprSelfTest.csproj`, using a unique `--artifacts-path` and executing its explicit DLL with filter `plant`. The new regressions cover mature crown completion and leaf/flower facing. Do not interpret determinism or mesh checks as rendered visual acceptance.

## Dense-field configuration results

Both field configurations rendered the same 180 graph plants at medium detail, seed 804584441. The same Game module `50195fe0-6e4e-471c-b68c-4cc8bc8ae258` and Sim module `a0f6062c-cd3b-4659-b887-6f4934f81ca6` produced these far-revisit observations:

| Setting | 2 m chunks | 4 m chunks |
| --- | ---: | ---: |
| Chunks | 36 | 9 |
| Draw calls | 494 | 146 |
| Submitted primitives, including render passes | 7,014,304 | 7,014,304 |
| Mean GPU time | 12.922 ms | 12.968 ms |
| Mesh storage reported by renderer | 41,412,224 bytes | 45,007,136 bytes |
| One changed chunk rebuild and blocking demo drain | 30.0 ms | 132.6 ms |

The larger grouping cuts draw calls but does not improve GPU time in this sample and makes local edits more expensive. Keep 2 m as the default; retain both configurations for further trials. Warm camera revisits published zero additional instance bytes and committed zero additional chunk builds. These are single bounded configuration observations, not repeated whole-world performance acceptance.

The probe also exposed a separate implementation problem: `PlantDemoKit.Generator` constructed a new generator on every request, bypassing its existing plan cache. Reusing one generator per configured form reduced the measured single-plant `Grow` call from 248.95 ms to 0.26 ms; the confirming 2 m run still submitted 7,014,304 primitives and the local rebuild/drain was 23.3 ms. This is a targeted warm-growth result. Initial descriptor generation remains expensive: about 17 seconds for this 180-plant mixed-age field. The full-plan accommodation/construction path is a concrete next optimization target; the graph architecture is retained.

Final source verification: `final-tests.log` reports 12 plant checks passed, including attachment, determinism, compiler/LOD geometry, crown completion and facing. `final-solution-build.log` and `final-game-build.log` pass; `final-boot.log` reports invariants=0. For the seed804584441 mature specimen, the cane form has480 blades/24flowers and no unfinished organs; its leaf-area summary is4.019m2. The fern has168leaflets in this individual, and the shrub126blades/24flowers. These are state/geometry facts, not claims of photorealism. Further leaf/flower surface refinement and faster initial graph construction remain worthwhile; live production integration stays open.
