# Vivarium 0.1.0 — first-draft implementation report

This report covers what has actually been implemented and validated (the plan itself is in `docs/VivariumSpec.txt`).
Save format: **schema v1** (`src/Vivarium.Sim/Core/AppVersion.cs`). Release candidate: `build/Vivarium-0.1.0-win64.zip`.

## Architecture in one paragraph
All authoritative state and logic live in `src/Vivarium.Sim` (pure C#/.NET 8, **no Godot reference**). The Godot 4.7.1 C# client in `game/`
is a pure presenter. It reads the sim, draws it, and sends validated commands through `SimHost` (time) and `ToolActions` (player actions).
Time is a fixed 10 s step (`SimClock`, 168 sim-hours per real hour at 1×) run by an ordered, cadenced `Scheduler` with a catch-up budget.
Determinism rests on four things: named or keyed PRNG streams, id-ordered collections, no parallelism, and a SHA-256 digest over the same
canonical payloads the save files use. See `docs/architecture/architecture.md` and ADR-001.

## Reproduce
```powershell
./scripts/verify.ps1                          # build + all fast sim suites + Godot boot + headless UI smoke/reload
./scripts/verify.ps1 -Suite All -IncludeSlow  # + multi-week soaks, windowed render tour, export + release smoke
./scripts/export.ps1                          # build/release/Vivarium/ + build/Vivarium-0.1.0-win64.zip
./scripts/smoke-release.ps1                   # boot, UI smoke, save/quit/reload against the exported exe; network audit
dotnet run --project src/Vivarium.Cli -- soak --days 28   # headless tuning/soak report
```
Prerequisites (not in git): .NET 8 SDK, Godot 4.7.1 mono in `tools/godot/` (or `$env:VIVARIUM_GODOT`), and export templates `4.7.1.stable.mono`.
On a fresh checkout `verify.ps1` performs the one-time Godot import itself.

## Feature families and content (all implemented)
| Family | What exists |
|---|---|
| World | Regular hexagon (10–20 m, default 16 m) with seeded fBm + feature terrain; the top mesh is clipped exactly to the hexagon; six banded strata walls (4 data-driven layers) plus the underside; substrate map (soil/gravel/rock/wood/water); rocks (unlimited seed variants), logs (decay classes 0–3, bark/rings/stubs), gravel patches (render-only pebble scatter); seeded placement plus a runtime place/move/remove API |
| Camera | Free-fly (WASD/QE, RMB look, Shift/Alt, wheel speed 0.02–25 m/s, persisted), focus/orbit, water-medium tracker with hysteresis |
| Render | AgX tonemap, saturated grade, sun + fill, shadows; underwater fog + screen grade; clear water with a flow-driven ripple shader and a cut face at the specimen plane; quality Low/Medium/High; flora/fauna MultiMesh with distance/frustum LOD (render-only) |
| Time | Pause, 8 speed steps (⅛×–16×), fixed step, cadenced systems, bounded catch-up |
| Fields | Scalar/categorical grids; light (terrain + prop horizon), nutrients, moisture, detritus, biofilm, plankton |
| Hydrology | Fixed water table, springs, conservative relaxation flow, ponding, boundary outflow at the cut, moisture coupling, debug overlay |
| Flora (8) | Carpet moss, cushion moss, bank moss, crust lichen, leafy lichen, creeping pennywort, dwarf rush, starflower. Each has suitability with hard refusals, logistic growth, nutrient uptake, propagation, litter shedding, competition, beneficial proximity, and death returning litter. There is also a data-driven interaction matrix |
| Fauna (4) | Springtail (terrestrial detritivore), cherry shrimp, triops (asexual), microminnow (schooling). Each has habitat, locomotion, feeding, metabolism, reproduction, aging, and death returning detritus |
| Genetics | 6 traits; offspring get the midpoint of the parents, then a 10 % chance per offspring of one Gaussian mutation, clamped to bounds; size and ornament drive phenotype via shader instance data; lineage survives death and is pruned to 6 generations |
| Ecology | One detritus → nutrient pathway, waste return, resource-limited populations (plus safety caps), reintroduction of any catalog species, statistics |
| Tools | Inspect, grab/release (habitat-validated), pick plant, nutrients, pokin' stick, place rock/log/gravel, move/remove props, introduce flora/fauna. The cursor shows validity (green/red) before commit |
| UI | Clock HUD, tool palette, inspector (details/genome/lineage), environment probe, catalog with extinct→reintroduce, stats with sparklines, new-world panel (seed, preset, diameter, relief, springs), settings, save/load, help, debug overlays |
| Save | Zip with a manifest and 6 payloads; atomic write (tmp → `File.Replace`, `.bak`); staged, validated load; v0→v1 migration; background autosave with no overlap; the running world continues on launch |
| Diagnostics | Per-subsystem timing, entity/LOD counters with mismatch detection, budget warnings, structured log in `%APPDATA%\Vivarium\logs` |
| Package | Portable Windows x64 bundle (Godot export + .NET runtime), README, THIRD_PARTY_NOTICES, engine license inventory generated from the engine |

## Requirement coverage (contract → evidence)
| Contract clause | Implementation | Verification |
|---|---|---|
| C1.1 standalone Windows app | `export.ps1`, portable bundle | `smoke-release.ps1` on the exported exe: boot, UI smoke (20 checks), reload |
| C1.2 offline | no networking APIs; content comes from the pck | the release smoke finds no network API references and observes 0 TCP connections |
| C1.3 first draft, no silent omissions | every family listed above | this table, plus the known limitations below |
| C2.1–2.2 persistent, versioned saves | SaveSystem, migrations, autosave, continue-on-launch | Persistence suite (8), Integration t-187, UI reload with an exact digest |
| C2.3 regular hexagon 10–20 m | HexDomain, descriptor validation | World suite t-013/t-014/t-016 |
| C2.4 geological cut faces | TerrainMesh walls + strata shader | t-017/t-018, render tour `cutaway_pond.png`, `strata_side.png` |
| C2.5 water ends on the cut plane | WaterMesh cut faces | t-059/060, Integration t-184, render tour |
| C3 free-fly above/under water | CameraRig, WaterMediumTracker | Camera suite t-033, UI smoke "camera crosses water surface" |
| C4.1 ≈1 real hour = 1 week | `SimClock.DefaultSimSecondsPerRealSecond = 168` | Time t-039 |
| C4.2 pause/speed, deterministic, render-independent | Scheduler, SimHost | Time suite, Fields t-050, Integration t-188/t-189, render LOD digest check |
| C5.1 non-loseable | Introduction + catalog | Ecology t-122, Integration t-186 |
| C5.2 vibrant sanitized realism | shaders, EnvironmentRig | render tour screenshots (subjective, see limitations) |
| C6.1–6.6 feature families | see the table above | World, Hydrology, Flora (21), Fauna (15), Genetics (9), Ecology (8) suites; 4-week soak |
| C7 tools reachable from UI with validity feedback | ToolController + palette | Tools suite (8), Integration t-185, UI smoke presses every tool button |
| C8 UI panels | UiRoot / Inspector / Windows | UI smoke opens catalog, stats, debug, settings, help, save/load |
| C9 quality tiers don't touch sim; logging; notices | EnvironmentRig, Log, notices | UI smoke quality-digest check; Bootstrap logging tests; bundle contents |

## Validation results
- Fast xUnit suites: **139 tests**, all passing after the post-release iteration below. *Correction:* the earlier
  claim of "130 tests, all passing" was wrong. Three fauna/genetics tests had failed since commit 8d2a121 staggered
  founder breeding cooldowns; they were found and fixed on 2026-09-23 (the fixtures now clear the stagger).
- Slow suites: all 5 pass — 4-week soak run twice with identical digest (9 m 32 s), 8-week accelerated stress with bounded entities/memory and no budget warnings (11 m 50 s), 3-week default ecology with clean invariants and active nutrient cycle (3 m 30 s), 60-day genetic drift ≥4 generations explained by lineage (1 m 15 s), save during active ecology + 3 further days deterministic (1 m 4 s).
- Godot: headless boot OK; headless UI smoke 20/20 and reload 4/4 (editor build); **exported build**: boot, smoke, and reload all pass with 0 TCP connections.
- Render tour (windowed): 16 screenshots, 0 shader errors; LOD cuts fauna triangles 87,632 → 7,420 with an unchanged digest.
- Clean clone: fresh `git clone` → `verify.ps1 -Suite Bootstrap,World,Boot,Smoke` passes (Godot import, build, headless UI smoke + reload); no untracked files produced outside ignored `build/`.
- Performance (Radeon 860M iGPU, 1600×900): ~40 fps on Medium, sim ≈0.9 ms per tick with ~400 animals.

## Known non-blocking limitations
- Determinism is guaranteed for the same binary on the same machine class; cross-CPU bit-identity is not promised.
- Several fauna populations are held in check by per-species safety caps as well as by resources. The resource response is still demonstrated (t-121).
  Springtails boom on the initial detritus stock and then settle back.
- "Vibrant sanitized realism" is judged from screenshots, not measured. Up close, flora are stylized procedural meshes rather than photoreal.
- The water cut face shows faint vertical banding where adjacent boundary cells overlap.
- The exe has no custom icon; the window title and README carry the name and version.
- A true network-unplugged run was not done: changing adapters or firewall rules is a system setting. The offline claim rests on the static audit plus zero observed connections.
- Performance was measured only on this iGPU.

## Post-release iteration (user feedback, 2026-09-23)
- Camera: the wheel zooms toward the cursor; W/A/S/D fly level; Space/Ctrl vertical; Q/E turn; flying speed on +/-.
  A settings migration repairs a flying speed that was saved at a crawl. v3 re-runs it, because the first attempt
  misread v1 files as v2.
- Grab: snaps to the nearest critter inside the cursor circle.
- New species: pill bug (roly-poly isopod). It is a terrestrial detritivore that rolls into a ball when poked.
- Terrain tools (raise, lower, smooth) and water tools (pour, soak up, add/remove springs), all through validated
  sim actions. Sculpted terrain is saved as a per-vertex delta, and digging below the water table makes a pond.
- Visuals: CC0 photo-scanned textures (ambientCG) on the ground, strata, rocks, logs and moss. The water uses
  screen-space refraction with depth-based absorption, and its cut face reads as clear aquarium glass.
  A 90 s fly-through holds 60 fps.
- Checks: fast suite 139, slow soaks 5/5, headless UI smoke 26 checks, reload 4/4, exported-build release smoke passes.

