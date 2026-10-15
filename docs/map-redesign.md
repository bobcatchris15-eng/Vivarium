# Preset terrain and drainage

All nine presets use low rolling relief around deliberately readable water routes. Perched
spring channels have sharply defined banks, typically 10–20 cm above the bed. This is
the channel excavation depth, not a requirement for 10–20 cm of moving water: the
default discharge produces a shallow stream within the cut.

| Preset | Landscape and water route |
|---|---|
| Streamside Hollow | A meadow stream bends through a broad hollow into a small lower pond. |
| Whispering Creek | A longer winding shallow run, retaining the creek work in progress's emphasis on watchable bends. |
| Twin Meadows Creek | A continuous meandering water-table creek crosses the island and separates two large meadows. A short perched spring feeder enters it. |
| Mossveil Bayou | Two short spring-fed tributaries enter broad shallow wetlands; approximately 41% of the terrain is below the water table, typically by 3–5 cm. |
| Sunken Caldera | A short feeder enters a pond enclosed by soft, low shoulders. |
| Cobalt Inlet | A winding feeder reaches a substantial lake inlet; approximately 38% of the terrain is underwater, with a median depth near 50 cm. |
| Pebble Shoreline | A shallow spring run meets a broad lake shore; approximately 38% is underwater, with a median depth near 42 cm. |
| Rocky Rise | A compact rocky brook descends through low shoulders to the island boundary. |
| Crag & Scree | The strongest relief of the set remains gentle enough for an uninterrupted winding brook to the boundary. |

Area and depth figures above sample the default preset terrain vertices, before props and
tree shoulders. Tests also check the populated baseline's standing-water character.
The deep lake areas are intentionally deeper than the perched flow channels.

## Authoring and world creation

`bedHeight` and `toBedHeight` specify absolute upstream and downstream channel-floor
elevations. The floor grade is independent of background noise. Rounded segment end
caps extrapolate that grade so joins do not introduce flat shelves. Cuts take the minimum
height at intersections instead of adding their depths. The existing relative `amount`
features continue to work when no absolute bed is specified.

`sharp: true` retains the channel's defined banks. The authoritative triangulated heightfield
resolves those banks over its vertex spacing; they are steep bank faces rather than literal
zero-width vertical faces. Broader lake basins blend into the surrounding terrain.

Island resizing scales channel widths as well as positions and basin radii. Channel
half-widths have a minimum of 0.9 environment cells so a small island still resolves the
channel floor. Overall slope scales inversely with footprint size to retain the elevation
range. The relief slider adjusts the graded floors with the surrounding relief.

Generated rocks and logs avoid the authored drainage corridor. Automatic pilot-tree
placement chooses the least obstructing corner when its seeded corner would cover a
route with roots. Explicit player/save corners remain authoritative. Player placement
and sculpting can still deliberately obstruct or redirect the water.

## Review

Run from the repository root:

```powershell
dotnet test tests/Vivarium.Sim.Tests --filter FullyQualifiedName~MapDrainageTests
dotnet run --project scripts/map-review -- build/map-review 100
python scripts/map-review/render.py build/map-review
scripts/review-surface-water.ps1 -Scenario H -Preset creek -Mode Static
```

The console diagnostic runs the production fluid owner with real generated props and
tree roots, fixed terrain, and no biological time advance. It records terrain profiles,
water-table area/depth, placement obstructions, physical water samples, conservation,
and receiving-pond or boundary outflow at 100 physical seconds. Its atlas is a diagnostic
plan view, not a game screenshot.

Native preset review uses exactly the authored springs, retains the populated ecosystem,
and advances to 100 physical seconds. It neither injects an extra spring nor sculpts a
test hole in the map. Captures include whole-map and spring views, followed by a save
round trip. Native motion/performance review is available with `Video` / `Benchmark`.

Terrain tests cover channel connectivity and downhill perched beds at 5 m, default, and
10 m island sizes; overlap excavation; footprint scaling; descriptor round trips; shallow
bayou/deeper lakeshore area and depth; and the two substantial dry areas in Twin Meadows.

These preset changes apply to newly created worlds. Existing saves retain their stored
descriptors and terrain.
