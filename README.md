# Vivarium

A living, self-running miniature ecosystem on a hexagonal island. Moss and lichen creep outward from their
edges, slime mold hunts through the leaf litter, springtails graze, shrimp and triops work the pond, and
water moves through the ground. You shape the terrain, pour water, drop in rocks, logs and gravel, and
introduce species, then watch what happens. Nothing is scripted. Every organism follows its own habitat
preferences, competes for space and light, eats, breeds and dies.

![Windows](https://img.shields.io/badge/platform-Windows%20x64-blue) ![Godot 4.7.1](https://img.shields.io/badge/Godot-4.7.1%20mono-478cbf) ![.NET 8](https://img.shields.io/badge/.NET-8-512bd4)

## Download and play

Grab the latest `Vivarium-x.y.z-win64.zip` from the [Releases](https://github.com/bobcatchris15-eng/Vivarium/releases)
page, unzip it anywhere and run `Vivarium.exe`. No install is needed. It needs a GPU that supports Vulkan;
it was developed on an AMD Radeon 860M integrated GPU.

Saves, settings and logs live in `%APPDATA%\Vivarium\`.

## Controls

| | |
|---|---|
| **Camera** | Hold **right mouse** and drag to look · **W A S D** fly · **Space / Ctrl** up / down · **Q / E** turn · **Shift / Alt** fast / slow |
| | **Mouse wheel** zooms toward the cursor · **+ / -** flying speed · **F** focus and orbit the selection · **Home** reset view |
| **Time** | **P** pause · **, / .** slower / faster. One real hour is about one vivarium week |
| **Tool wheel** | Tap **right-click** to open it; number keys also work |
| | **1** Inspect (genome and lineage tabs for critters) · **2** Grab a critter · **3** Pick a plant · **4** Nutrients · **5** Pokin' stick |
| | **6** Rock · **7** Log (**R** rotates) · **8** Gravel, with **[ ]** or Ctrl+wheel to resize. Rocks and logs stack up to three high |
| | **9** Add flora · **0** Add fauna. Choose the species on the wheel's outer rings or in the catalog |
| | **G** Terrain: raise, lower, smooth. Dig below the water line and a pond fills in |
| | **H** Water: pour, soak up, or add or remove a spring |
| **Panels** | **C** species catalog · **T** statistics · **Ctrl+S** quick save · **F3** debug overlays · **F1** help · **Tab** hide UI |

The cursor ring turns green where an action is valid and red where it isn't. If a species dies out, you can
reintroduce it from the catalog.

## What's in the ecosystem

**Flora (28).**
- **Mosses:** carpet, cushion and bank moss, plus Sphagnum.
- **Lichens:** crust, leafy and reindeer lichen.
- **Groundcovers:**
  - Dry ground: dichondra, clover, carpobrotus and stonecrop.
  - Wet ground: creeping pennywort, creeping fig, watercress and bacopa.
- **Taller plants:** maidenhair fern, blue fescue, dwarf rush and starflower.
- **Trees & shrubs:** black locust, catalpa, tamarack, cottonwood and staghorn sumac. Tree recruitment shares an island-wide carrying limit of roughly one tree per 15 m²; shrubs use a looser structural budget.
- **Fungi and slime mold:** bonnet mushrooms, turkey tail and a many-headed slime mold.

**Fauna (7).** Springtails, pill bugs, silverfish and darkling beetles on land. Cherry shrimp, triops and
microminnows in the water. Critters carry heritable genomes (colour, markings, size and appendages) with
lineage tracking, so populations drift and adapt over generations.

Some behaviour worth knowing about:

- **Colonial growth.** Mosses, lichens and mat-forming groundcovers grow as colonies of small cells that bud
  only at the rim. The interior thickens and rises. Each cell takes a colour from its species' palette, so
  moss comes out mottled and lichen banded in rings around where the colony started. Mature interiors merge
  into fewer, larger cells to keep the simulation bounded.
- **Slime mold.** It forages as a network that follows dead matter in the soil. Veins thicken where food
  flows and change colour with age, from ochre at the old core to bright yellow at the growing front. When
  food runs out it fruits and releases spores.
- **Water.** A groundwater table and surface water both move across the terrain. Moisture wicks outward from
  wet ground. Each species has a moisture optimum and hard limits, so land mosses stay out of standing water
  while cress, bacopa and Sphagnum fill the shallows and bog margins.
- **Food web.** Grazers eat plants, biofilm and plankton. Decomposers and dead matter feed nutrients back
  into the soil.

## Building from source

**Requirements**

- Windows x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Godot 4.7.1-stable **mono**](https://godotengine.org/download/archive/4.7.1-stable/). Unpack it to
  `tools/godot/Godot_v4.7.1-stable_mono_win64/`, or point `VIVARIUM_GODOT` at its `_console.exe`. The
  `tools/` folder is gitignored.
- PowerShell 7 (`pwsh`)

**Build and run**

```powershell
dotnet build Vivarium.sln                  # simulation library, CLI and tests
dotnet build game/Vivarium.csproj          # Godot client assembly
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64.exe --path game
```

**Export a release**

```powershell
./scripts/export.ps1       # writes build/release/Vivarium/ and build/Vivarium-<version>-win64.zip
```

The version number lives in `src/Vivarium.Sim/Core/AppVersion.cs`, `game/project.godot` and
`game/export_presets.cfg`. Keep all three in step.

## Testing

`scripts/verify.ps1` runs everything and exits non-zero if any suite fails.

```powershell
./scripts/verify.ps1                          # all fast simulation suites plus Godot boot and UI smoke tests
./scripts/verify.ps1 -Suite Flora,Hydrology   # only the named suites
./scripts/verify.ps1 -Suite All -IncludeSlow  # plus the windowed screenshot tour and release packaging
```

- **Simulation suites:** Bootstrap, World, Camera, Time, Fields, Hydrology, Flora, Fauna, Genetics, Ecology,
  Tools, Persistence, Perf, Integration.
- **Godot suites:**
  - **Boot:** headless start, save and load round-trip.
  - **Smoke:** drives the UI.
  - **Render:** a camera tour that writes screenshots to `build/verify/render/`.
  - **Package:** export plus a smoke test of the release build.

The client also has a performance probe that logs fps, worst frame, per-system timings, memory and draw calls
once a second:

```powershell
Godot..._console.exe --path game -- --perf-test <out-dir>
```

It accepts these environment variables:

| Variable | Effect |
|---|---|
| `VIVARIUM_PERF_LOW=1` | Orbit at critter height |
| `VIVARIUM_PERF_SCULPT=1` | Sculpt terrain every frame |
| `VIVARIUM_PERF_SECONDS=N` | Run for N seconds instead of the default |

## Project layout

```
src/Vivarium.Sim/     Engine-free simulation (C#): world, time, fields, water, flora, fauna, genetics,
                      ecology, tools API, persistence, procedural meshes. No Godot references.
src/Vivarium.Cli/     Headless command-line front end for running and inspecting worlds
tests/                xUnit suites for the simulation
game/                 Godot 4 client: App (session, camera, tools), Render, Shaders, UI, Textures
game/content/         All species, substrates, strata, tools and presets as JSON
scripts/              verify.ps1, export.ps1, smoke-release.ps1
docs/                 Architecture, ADRs, product contract, spec and development reports
```

The simulation owns all authoritative state and is deterministic for a given seed. The Godot client only
reads it, renders it and sends validated commands through the tools API, so rendering can never change an
outcome. The render test checks this by comparing simulation digests. See
[docs/architecture/architecture.md](docs/architecture/architecture.md) for the full breakdown.

## Adding a species

Species are data. Most additions need only a JSON file.

1. Copy a similar species in `game/content/flora/` or `game/content/fauna/` and edit it:
   - `habitat`: substrates, the moisture, light and nutrient optimum and tolerance, and water-depth limits.
   - `growth` and `spread`.
   - `colony`, if it should grow as an edge-budding mat, with a colour palette and a random or banded pattern.
   - `visual`: shape, colours and height.
   - `woody`, for trees/shrubs: structural layer, canopy radius, shade opacity and same-layer spacing.
2. Register it in `game/content/index.json`, and add it to the presets in `game/content/presets/` if it should
   appear in new worlds.
3. Give it a place in the food web by adding it to a grazer's diet in `game/content/fauna/*.json`.
4. Run `./scripts/verify.ps1 -Suite Bootstrap,Flora`. The Bootstrap suite checks the species count.

A new visual shape needs a mesh in `src/Vivarium.Sim/Geometry/OrganismMeshes.cs`. A new archetype also needs
a surface mode in `game/Shaders/flora.gdshader`.

## Credits and licences

Vivarium's code, content and procedural meshes are original to this project. The release bundles the Godot
Engine and the .NET runtime (both MIT), plus CC0 photo-scanned textures from [ambientCG](https://ambientcg.com).
See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for details.

The project itself has no licence file yet, so all rights are reserved by default.
