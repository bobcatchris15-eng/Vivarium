# Vivarium architecture

## Principle
**Authoritative state lives only in `Vivarium.Sim`.** The Godot client (`game/`) reads the sim,
renders it, and issues commands through the sim's tool API. It never writes simulation state directly, and
nothing the client does to itself (camera, quality, LOD, UI panels) can reach the sim, because the sim has no
reference to Godot at all.

```
            +-------------------- game/ (Godot, C#) ---------------------+
 input ---> | CameraRig   ToolController   UI panels    Settings (user)   |
            |     |             | commands      ^ read-only views         |
            |     |             v               |                         |
            |  SimHost ---- Advance(realDt) --> |  Renderers (terrain,    |
            |     |                             |  walls, water, props,   |
            +-----|-----------------------------|--flora, fauna, overlays)+
                  v                             |
            +------------------ src/Vivarium.Sim ----------------------+
            | Scheduler/Clock -> systems (fixed step, ordered, cadenced) |
            | World: Terrain, Strata, Substrate, Props, Fields, Water,   |
            |        Flora, Fauna, Genomes, Lineage, Ecology tallies     |
            | Tools API | Selection raycast | Stats | Persistence | Diag |
            | Content (JSON, validated)  |  Core: Rng, Ids, Log, Version |
            +------------------------------------------------------------+
```

## Subsystems and ownership
| Module (namespace) | Owns | Reads | Written by |
|---|---|---|---|
| `Core` | version, logging facade, PRNG streams, id allocator, digests | — | — |
| `Content` | species/material/strata/preset/tool definitions + validation | JSON files via `IContentSource` | loader only |
| `World` | hex domain, descriptor, heightfield, strata, base substrate, props | content | generation, prop placement API |
| `Time` | clock (tick count, scale, speed, pause), scheduler, catch-up budget | — | SimHost |
| `Fields` | scalar/categorical grids: light, nutrients, moisture, detritus, biofilm, plankton | domain | owning systems |
| `Water` | water table, springs, surface depth, outflow tally | terrain, props | hydrology system |
| `Flora` | flora individuals/colonies, spatial index | fields, substrate, water | flora system, tools |
| `Fauna` | fauna individuals, spatial index | fields, water, terrain | fauna systems, tools |
| `Genetics` | genome bank, lineage book, phenotype mapping | species defs | offspring pipeline |
| `Ecology` | detritus pathway, resource growth, reintroduction, event tallies, stats snapshots | all | ecology systems, tools |
| `Tools` | validated player actions; selection raycast | world | client ToolController |
| `Persistence` | save manifest, payload serialization, atomic write, staged load, migrations | world | SimHost/UI |
| `Diagnostics` | per-system timing, counters, budget warnings | scheduler | scheduler |
| client `Render` | meshes, materials, MultiMesh instances, LOD, overlays | sim (read-only) | itself |
| client `Camera` | free-fly, focus/orbit, water-medium state (via `Sim.Observation`) | terrain/water queries | input |
| client `UI` | panels, HUD, dialogs, feedback | sim (read-only) + commands | input |
| client `App` | bootstrap, SimHost, autosave, smoke/boot/render test modes | — | — |
| `scripts/` | verify, export, release smoke | — | — |

## Data flow per frame
1. Input → CameraRig (client-only state) and ToolController (validated sim commands via `Tools`).
2. `SimHost.Advance(realDelta)` converts wall time into sim time (`scale × speed`, 0 when paused).
   It accumulates whole fixed ticks and runs at most `budget` ticks per frame. The backlog is carried
   over, never dropped, and reported by diagnostics when it keeps growing.
3. Scheduler runs registered systems in a fixed order, on their cadences (in ticks).
4. Renderers pull the state they need (dirty flags / versions) and rebuild meshes or instances.

## Determinism rules
- The fixed step is `FixedStepSeconds = 10` sim-seconds, and sim time = `tick × step`, never a float accumulation.
- All randomness is `Rng.Stream(worldSeed, "name")` or keyed `Rng.Keyed(seed, "name", id, counter)`.
  Stream names are hashed with FNV-1a (never `string.GetHashCode`). Keyed randomness makes results
  independent of how many values other systems consumed.
- Entity collections are id-ordered lists, with no hash-order iteration and no parallel loops.
- Digest = SHA-256 over the canonical persistence payload (without timestamps), so the save format
  and the determinism oracle can't drift apart.

## Activity LOD vs render separation
The spec asks for fauna activity LOD (t-101) and also for camera-independent digests (t-188). These are
reconciled by making **LOD purely a client concern**. Distant or off-camera fauna skip instance
updates, animation (wiggle/antennae), and high-detail meshes. The authoritative behaviour of every
individual always runs at full fidelity. The sim's cost is bounded by cadence choices and the
spatial index, not by the camera.

## Content
JSON under `game/content/`, listed in `content/index.json` so that exported builds (pck) and tests
load the same file set. Loading validates schema, bounds, and cross-references, and reports errors
as `file: $.path.to.field: message`.

## Persistence
A `.vivsave` file is a zip holding `manifest.json` plus one JSON payload per subsystem. A save is written to
`*.tmp` and swapped with `File.Replace` (keeping `.bak`). A load builds a complete staged `World`,
validates it, and only then hands it back to be swapped in. Migrations are an ordered list of
`(fromVersion → fromVersion+1)` JSON transforms.

## Locations
- Content: `res://content` (read-only, inside the pck).
- User data: `%APPDATA%\Vivarium\` (Godot `user://` with a custom user dir), holding `saves/`, `logs/`, and `settings.json`.
