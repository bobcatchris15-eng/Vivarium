# S2 - living soil cube (substrate renderer + material)

Files: game/Repr/Substrate/SubstrateRenderer.cs, SoilCubeDemo.cs, game/Shaders/repr_substrate.gdshader.
Captures/sheets/logs: build/overhaul/S2 (run6/run7 = timing pair, run8 = final look; sheet_*.png, timing.txt, stab.py).
Run: `<godot> --path game -- --repr-review <ABS> --repr-demo soil_cube --repr-env autoexp=0`.
**Use autoexp=0**: with auto exposure the dark wet cube is re-exposed and looks identical to the dry one.

## Method
- Renderer: 0.5 m tiles, 3 levels (0.01/0.03/0.10), coarsest level whose vertex step projects <= 9 px, 1.35x hysteresis.
  Builds on Task.Run, arrays packed on the worker, 1 commit/frame, levels kept. Own DirtyLog cursor; Geometry/SoilMaterial
  rebuilds only intersecting tiles (old mesh stays until commit); Water records upload a 20x20 RGBAH texture (sat, film mm,
  standing mm) and never rebuild geometry; wall CUSTOM0 refreshed via RefreshWater at most once / 2 s / tile.
- Shader: one shader, 4 material instances (`kind` = ground/cut/rock/stones). Hybrid: mesher displacement + world-position
  procedural structure + existing PBR sets (Ground048, Gravel022, Rock058, ScatteredLeaves007, 1K) as fine grain.
- Texture taps: typical 4-7 (water 1 + 3 per active set, usually 1-2 sets); worst case ~27 (4 sets, biplanar edge).
  Procedural: Worley 2D (9 cells) x 3 octaves (crumb D, 0.4 D, 4 cm cracks), value noise, 3D ridged fractures on rock,
  warped-stripe rootlets only on cut faces gated by root density. Octaves fade to their mean between 4 and 2 px.
- Demo: TestCube(seed 7), two simulated substrates (dry: evaporation only until dry-side top sat < 0.18, 23 x 900 s;
  wet: 2 h x 10 mm/h), roots added under two spots near the low-X wall, dark plane flush with the block base
  (spec said 1 m below; flush reads better as a specimen). Wet substrate reused for exaggerated and dug (dug last).

## Numbers (absolute; two runs, GPU spread ~0.3 ms between runs here)
Empty-scene baseline (harness_probe, same env): GPU 9.4-10.6 ms, 82-85k prims. Substrate adds roughly +3 to +5 ms at level 0.
shot: GPU ms run6 / run7, prims, draws (VRAM 2617 MB in every shot, scene-wide)
whole dry 13.6/14.1, 329k, 42 | wet 13.3/13.3 | top dry 14.7/14.6, 327k, 36 | wet 14.5/14.5
top_macro dry 12.4/12.4, 157k, 18 | wet 12.7/12.6 | wall dry 14.2/14.2, 329k, 39 | wet 14.2/14.5
wall_macro dry 13.3/13.5 | wet 13.6/13.4 | outcrop dry 14.8/14.9 | wet 15.2/14.8, 328k, 38
stone dry 13.4/13.4, 324k, 37 | wet 13.3/13.2 | top_macro_shift 12.5/12.5 (dry), 12.4/12.6 (wet), 157k, 18
exaggerated top_macro 13.1/13.2, wall 14.5/14.7, wall_macro 14.8/14.5 | seam (levels 1,0,0,0) 12.3/12.0, 260k
far (level 1 everywhere, 14k tris) 8.5/8.6, 28k, 28 | hole (dug) 14.6/14.7, 158k, 20.
Whole cube at level 0 is 109.8k tris (prims counts include shadow passes). Full table: build/overhaul/S2/timing.txt.
Cache: first build of all 4 tiles x 3 levels inline 118-122 ms (125 ms CPU in mesher), MeshBytes 6.99 MB, built=12.
Dig (12 cm bowl at x=0.20,z=0.80): rebuild requests 0->1, TilesRebuilt 0->1 of 4 tiles, 6-33 ms, MeshBytes 6.99 -> 6.79 MB,
WaterTextureUploads 1 -> 2. WallRefreshes 0 (no Water record arrived during shots; path implemented, not exercised).
Select cost reads 0 us in logs (the log line is taken outside _Process; not separately measured).
Stability: top_macro vs 1 mm translated twin: best image shift 5 px, aligned mean abs diff 2.8-3.3/255, corr 0.91-0.93
(residual = perspective, no swimming seen). Diff images stab_diff_*.png.

## Macro SDF trial
Not attempted (time spent on the hybrid material).

## Verdict per shot vs the "current look" (faceted terrain, decal-smear litter, saucer rocks, wallpaper cut face, wet == dry)
- whole: cut face now has distinct dark humus, mineral, and fractured weathered/bedrock zones with embedded stones; top reads as
  leaf litter, not smears. Wet vs dry only mildly different at this distance. Better than before, not photographic.
- top / top_macro: litter leaves + crumb pores + (dry) fine desiccation cracks; macro is plausible but leaf texture is pale and
  the pore network still looks a bit graphic. Wet is darker and sheens in hollows; dry cracks open. Modest contrast.
- wall / wall_macro: the best result: horizons, thin wavy pale rootlets crossing humus/mineral, gravelly texture, irregular
  boundary. Humus is very dark (nearly black) at fixed exposure. Bedrock loops (Rock058 cracks) look slightly cartoon.
- outcrop: still a faceted 4-column pyramid with a pale fuzzy collar (geometry from the mesher, not fixable here);
  rock texture + lamination read as rock but it is too pale and wedge-shaped.
- stone: reads as a pebble embedded in a gravelly wall (good); surface stones are mostly buried and invisible.
- hole: bowl exists and only 1 tile rebuilt, but the floor is a pale grey granular patch with blocky wetness (per-column
  saturation texture), not convincing. Worst shot.
- exaggerated: proves pores, cracks, wetness contrast exist (x3).
- seam / far: mixed levels 1/0 and far level 1: no visible cracks between tiles, so no skirts added.

## Remaining defects
1. Hole floor too pale/blocky; weathered/bedrock albedo from the mesher is light tan.
2. Wet standing water reaches only 0.06 mm in the sim after 2 h of rain, so no puddle (puddle_mm uniform lowered to 0.05
   to show anything); film max 0.14 mm.
3. Horizon boundary fingering is limited to the mesher's <= one-step gradient band.
4. Outcrop shape faceted; stones rarely break the surface; all shots chose level 0 except seam/far (MaxStepPixels 9 is fine
   quality-wise but costs ~4 ms GPU on the 860M at whole-cube distance).
5. Possible faint hatching visible in macro captures (mesh grid / normal map); not isolated.
6. A `python -` was mistakenly started once and stopped; no process left from S2.
