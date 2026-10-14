# Bounded erosion from actual flowing liquid

Implemented 2026-10-11 in response to the request for mild channel formation that stops.

## Behaviour

Only authoritative 3D liquid parcels touching the terrain and moving tangentially faster than 0.12 m/s can scour it. Airborne outlet water, vertical impacts without tangential motion, quiet pools, dry ground, rock, logs, pilot-tree wood footprints and ground below the standing water table do not erode. Gravel wears at one quarter of the soil rate.

Each contacted terrain triangle distributes volume-weighted flow work to its three actual vertices. A fully exposed vertex wears at no more than 0.12 mm per physical second (7.2 mm per minute), with ten-second terrain publication batches (at most 1.2 mm per edit). This avoids repeatedly rebuilding the entire island and reseating its flora for microscopic changes. There is no prescribed route: the next fluid job collides with the changed solid terrain, so a channel can guide subsequent water along the slope. Source throughput and every existing liquid retention/sink rule are retained.

There are two permanent local limits: a vertex can lose at most 30 mm to this system, and its erosion window expires 600 physical seconds after its first scour. That finite window includes intermittent or dry intervals, preventing rarely touched fringes from making tiny edits for hours. Turning a spring off stops wear; restarting it, changing its discharge, or raising the ground with the sculpt tool does not replenish a spent allowance. World pause also pauses the window's clock. A fresh world starts with a fresh allowance. Existing worlds without erosion state gain the feature when their liquid next advances.

The sparse cut/exposure budgets, pending wear, physical batch timer and removed-soil volume are serialized beside the liquid. The actual terrain delta is saved through the existing terrain mechanism. The optional payload is omitted when no erosion has occurred, preserving older save digests. All state is restored before digest verification. Biological calendar acceleration does not accelerate erosion; physical fluid speed controls and pause apply normally.

This is a game approximation of a finite erodible soil veneer, not a calibrated sediment transport model. Removed fines are counted as washed-away soil. There is no simulated suspended sediment, deposition, moving boulder, undercut bank or endlessly renewable soil supply.

## Rendering and publication

Terrain changes pass through the existing terrain version, hydrology bed and light invalidation mechanisms. An outstanding ecology tick finishes before a solid edit. The fluid worker reads an immutable terrain snapshot and only the main thread publishes erosion after a completed liquid job. Unchanged prop snapshots are reused when erosion changes only the terrain.

The terrain shader now keeps its microrelief in normal shading rather than displacing the visible bed away from collision triangles. The previous decorative displacement could conceal several millimetres of shallow water and alter the apparent channel lip; rendered and simulated bed elevations now agree. The source, stream and reconstructed pool remain meshes from actual 3D liquid parcels. Erosion budgets never provide water geometry, a water level, a depth sheet or a painted stream.

## Evidence and reproduction

Final results are recorded in `artifacts/fluid-erosion/final-window-review/`. Run the normal Godot project with user arguments `--liquid-review OUTDIR --erosion`; set `VIVARIUM_LANDSCAPE_SCENARIO=liquid-correction` and an isolated `VIVARIUM_BENCHMARK_USER_DIR`. The fixture uses a valid 5 m world, 50 mm terrain vertex spacing, a broad natural cross-slope, a 2000 L/h source and a downhill standing pond. It does not begin with an excavated stream channel.

The fixture advances twenty minutes of physical liquid time using the production water owner, solver, terrain, spring outlet, pond and liquid renderer. Biology is paused and ground-cover renderers are hidden for visibility. Its time advancement is accelerated relative to wall time, so these captures are behaviour evidence, not a real-time frame-rate benchmark. `eroded-bed.png` hides only the liquid renderer after saving, to inspect the changed solid bed without changing the liquid or saved world. The eroded world is saved and reloaded, and its canonical digest must match before the review reports success.

The final twenty-minute run changed 64 terrain vertices. Its deepest cut was exactly 30 mm. Washed-away soil reached 2.926428 litres at 600 seconds and remained bit-for-bit unchanged at every subsequent minute through 1200 seconds, while the spring continued running. Source input was 666.666667 litres; 666.235 litres reached the standing pond, 0.431667 litres remained in the liquid/source remainder, and boundary loss was zero. The production review reported success after save/load digest verification; its stderr was empty.

The measured depth and soil-volume curves are in [bounded-erosion.pdf](../artifacts/fluid-erosion/bounded-erosion.pdf). Regenerate them with `artifacts/fluid-erosion/plot-erosion.py` (ReportLab; optional PNG export uses Poppler). Before/after scene captures and the view with the liquid hidden are under `final-window-review/`, alongside `report.json`, `terrain-cuts.json` and `eroded-world.vivsave`.

## Validation and copied-player check

The game builds with zero warnings/errors. All 68 final focused checks pass, including eight erosion cases covering dry/quiet/airborne water, protected substrates, hard depth and time limits, intermittent contact, pending-wear save/load, sculpting without budget replenishment, synchronous/asynchronous ownership, pause and water mass balance. Existing pool/overflow, older liquid saves, water tools, luminous flora, fruit recruitment and root-query checks also pass. The final receipt is `artifacts/fluid-erosion/test-results/erosion-acceptance.trx`; earlier receipts retain earlier implementation stages.

The isolated player-world review uses the existing save copy, transient settings and disabled autosave, with biology held still. It records 19.616667 physical fluid seconds in 20.072510 wall seconds, 1079 drawn frames (53.754 FPS), 253 JPG captures and 81 surface updates (4.035 Hz). The maximum local cut in that short run is 1.1965 mm. The preceding two-second edit experiment recorded 18.5 fluid seconds in 20.064091 wall seconds and 45.903 FPS; its separate `player-live/` report is retained. The final ten-second batching improves that run, while terrain and flora refreshes still introduce intermittent CPU work. The previous pre-erosion short review recorded 62.26 FPS and 5.38 surface updates per second. These are short diagnostic comparisons, not controlled whole-world or GPU performance acceptance results.

Raw final player evidence is `player-final/live-report.json`, its images and empty stderr. The motion capture is [spring-bounded-erosion.mp4](../artifacts/fluid-erosion/spring-bounded-erosion.mp4), with playback rate calculated from capture count divided by elapsed time. Neither the player's live world nor its autosave is modified: the reviewed save and the original autosave still share SHA-256 `E6070D64CE7459D32C7C4D67C512B2A711DCBB85A18FF837BFC16C1CF79DC172`. Restart the game to load the rebuilt C# implementation. No player process was forcibly restarted and no publication was performed.

Original implementation backups are under `artifacts/fluid-erosion/before/`. The initial ten-minute slope run is retained in `initial-slope-review/`; it predates the timeout and the terrain shading correction. `final-slope-review/` is the subsequent twenty-minute contact-time experiment: deep cuts stopped but microscopic fringe wear continued. Both are historical experiments, not the final acceptance receipt.

## Basis and scope

The thresholded contact-velocity reaction and particle-to-terrain vertex contributions follow the general approach described in [Skorkovská, Kolingerová and Benes, Hydraulic Erosion Modeling on a Triangular Mesh](https://www.cs.purdue.edu/cgvlab/www/resources/papers/Skorkovska-2015-Hydraulic_Erosion_Modeling_on_a_Triangular_Mesh.pdf), particularly sections 3.2–3.3. This implementation uses the existing position-based liquid solver and the game's heightfield triangles. Its numeric rates, finite depth and armouring time are gameplay choices; the paper does not establish those values or validate these measurements.

Terrain resolution bounds the channel detail: the player's current 125 mm vertex spacing produces broader cuts than the fixture's 50 mm spacing. Water itself retains the previous 5 ml parcel resolution. There is no global terrain tessellation change in this implementation.
